using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using MacExtend.Core;
using MacExtend.Core.Wda;
using MacExtend.Native;
using MacExtend.Services;

namespace MacExtend.Views
{
    /// <summary>
    /// A nearly transparent window laid exactly over the picture area of uxplay's video window while WDA control is ready.
    /// It sits right above the video in the z-order (never owned by it: an owner in another process would tie the two
    /// processes' input together) and follows it as it moves, resizes, goes full screen or is minimized; the video's own
    /// title bar and frame stay uncovered. Clicks become taps, holding becomes a long press, a drag is replayed on the
    /// device when the button comes up, the wheel scrolls, typed text (UniKey's included) is typed on the device, and
    /// a right click is Home. Alt+Enter goes to the video window. Nothing typed is kept or logged.
    /// </summary>
    internal sealed class WdaOverlay : Window
    {
        private static readonly TimeSpan LongPress = TimeSpan.FromMilliseconds(500);
        private static readonly TimeSpan WheelBatch = TimeSpan.FromMilliseconds(100);
        private static readonly TimeSpan HintVisible = TimeSpan.FromSeconds(3);
        /// <summary>Physical pixels the pointer may wander before a press becomes a drag.</summary>
        private const int DragSlop = 8;

        private readonly Canvas _effects = new Canvas { IsHitTestVisible = false };
        private readonly Border _hint;
        private readonly TextBlock _hintText;
        private readonly DispatcherTimer _longPress;
        private readonly DispatcherTimer _wheel;
        private readonly DispatcherTimer _follow;
        private readonly DispatcherTimer _hintTimer;
        private readonly Stopwatch _clock = new Stopwatch();
        // kept in fields: the native hooks call them for as long as they live
        private readonly Win32.WinEventProc _onLocation;
        private readonly Win32.WinEventProc _onSystem;
        private IntPtr _locationHook;
        private IntPtr _systemHook;
        private IntPtr _video;
        private WdaDriver _driver;
        private IntPtr _hwnd;

        // the press in progress
        private bool _pressing;
        private bool _dragging;
        private bool _longPressed;
        private Win32.POINT _downAt;
        private readonly List<PathSample> _samples = new List<PathSample>();
        private Polyline _trail;
        private double _wheelV;
        private double _wheelH;
        private double _wheelX;
        private double _wheelY;

        public WdaOverlay()
        {
            Title = "MacExtend";
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true;
            // alpha 1 instead of 0 keeps it hit-testable: fully transparent pixels let the mouse fall through
            Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
            ShowInTaskbar = false;
            ShowActivated = false;
            Focusable = true;
            Cursor = Cursors.Arrow;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = -10000;
            Top = -10000;
            Width = 1;
            Height = 1;

            var font = TryFindResource("Font.Sans") as FontFamily;
            _hintText = new TextBlock
            {
                Text = "Điều khiển bằng WDA. Nhấp chuột phải: Home. Ctrl+V: dán chữ.",
                Foreground = Brushes.White,
                FontSize = 12.5,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
            };
            if (font != null) _hintText.FontFamily = font;
            _hint = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0xEB, 0x1C, 0x1C, 0x1E)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x29, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14, 8, 14, 9),
                Margin = new Thickness(16, 16, 16, 0),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                IsHitTestVisible = false,
                Opacity = 0,
                Child = _hintText,
            };
            var root = new Grid();
            root.Children.Add(_effects);
            root.Children.Add(_hint);
            Content = root;

            _longPress = new DispatcherTimer(LongPress, DispatcherPriority.Input, (_, __) => OnLongPress(), Dispatcher);
            _longPress.Stop();
            _wheel = new DispatcherTimer(WheelBatch, DispatcherPriority.Input, (_, __) => FlushWheel(), Dispatcher);
            _wheel.Stop();
            // the hooks catch every move; this catches what they cannot see (a style change, a topmost pin)
            _follow = new DispatcherTimer(TimeSpan.FromMilliseconds(500), DispatcherPriority.Background, (_, __) => Sync(), Dispatcher);
            _follow.Stop();
            _hintTimer = new DispatcherTimer(HintVisible, DispatcherPriority.Background, (_, __) => FadeHint(), Dispatcher);
            _hintTimer.Stop();

            _onLocation = (hook, eventType, hwnd, idObject, idChild, thread, time) =>
            {
                if (hwnd == _video && idObject == Win32.OBJID_WINDOW) Sync();
            };
            _onSystem = (hook, eventType, hwnd, idObject, idChild, thread, time) =>
            {
                if (hwnd == _video || hwnd == _hwnd) Sync();
            };

            SourceInitialized += (_, __) =>
            {
                _hwnd = new WindowInteropHelper(this).Handle;
                var style = Win32.GetWindowLongPtr(_hwnd, Win32.GWL_EXSTYLE).ToInt64();
                // out of Alt+Tab: the video window is the one to switch to
                Win32.SetWindowLongPtr(_hwnd, Win32.GWL_EXSTYLE, new IntPtr(style | Win32.WS_EX_TOOLWINDOW));
                HwndSource.FromHwnd(_hwnd)?.AddHook(WndProc);
            };
            Activated += (_, __) => Keyboard.Focus(this);
        }

        /// <summary>Lays the overlay over <paramref name="video"/> and sends its gestures to <paramref name="driver"/>.</summary>
        public void Attach(IntPtr video, WdaDriver driver, string device)
        {
            var again = video == _video && driver == _driver;
            _driver = driver;
            if (!again)
            {
                Unhook();
                _video = video;
                Win32.GetWindowThreadProcessId(video, out var pid);
                _locationHook = Win32.SetWinEventHook(Win32.EVENT_OBJECT_LOCATIONCHANGE, Win32.EVENT_OBJECT_LOCATIONCHANGE, IntPtr.Zero,
                                                      _onLocation, pid, 0, Win32.WINEVENT_OUTOFCONTEXT);
                _systemHook = Win32.SetWinEventHook(Win32.EVENT_SYSTEM_FOREGROUND, Win32.EVENT_SYSTEM_MINIMIZEEND, IntPtr.Zero,
                                                    _onSystem, 0, 0, Win32.WINEVENT_OUTOFCONTEXT);
                _hintText.Text = "Điều khiển " + (string.IsNullOrEmpty(device) ? "thiết bị" : device)
                                 + " bằng WDA. Nhấp chuột phải: Home. Ctrl+V: dán chữ.";
                ShowHint();
            }
            _follow.Start();
            Sync();
        }

        public void Detach()
        {
            CancelPress();
            Unhook();
            _follow.Stop();
            _wheel.Stop();
            _wheelV = _wheelH = 0;
            _video = IntPtr.Zero;
            _driver = null;
            if (IsVisible) Hide();
        }

        /// <summary>The screen size changed (the device rotated): the picture inside the window moved.</summary>
        public void Relayout() => Sync();

        protected override void OnClosed(EventArgs e)
        {
            Unhook();
            base.OnClosed(e);
        }

        // ---- following the video window ------------------------------------------------------

        private void Sync()
        {
            if (_video == IntPtr.Zero || !VideoWindow.IsAlive(_video) || Win32.IsIconic(_video) || !VideoWindow.TryGetClientBounds(_video, out var client))
            {
                CancelPress();
                if (IsVisible) Hide();
                return;
            }
            if (!IsVisible)
            {
                Show();
                _hwnd = new WindowInteropHelper(this).Handle;
            }
            var topmost = VideoWindow.IsTopmost(_video);
            var mine = (Win32.GetWindowLongPtr(_hwnd, Win32.GWL_EXSTYLE).ToInt64() & Win32.WS_EX_TOPMOST) != 0;
            if (topmost != mine)
            {
                Win32.SetWindowPos(_hwnd, topmost ? Win32.HWND_TOPMOST : Win32.HWND_NOTOPMOST, 0, 0, 0, 0,
                                   Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE);
            }
            // right above the video: just under the window above it, or at the top of the video's band (topmost or not)
            // when nothing of that band is above it
            var above = Win32.GetWindow(_video, Win32.GW_HWNDPREV);
            var flags = Win32.SWP_NOACTIVATE | Win32.SWP_SHOWWINDOW;
            IntPtr insertAfter;
            if (above == _hwnd)
            {
                insertAfter = IntPtr.Zero;
                flags |= Win32.SWP_NOZORDER;
            }
            else if (above == IntPtr.Zero || (!topmost && HasTopmostStyle(above)))
            {
                insertAfter = topmost ? Win32.HWND_TOPMOST : Win32.HWND_TOP;
            }
            else
            {
                insertAfter = above;
            }
            Win32.SetWindowPos(_hwnd, insertAfter, client.Left, client.Top, client.Width, client.Height, flags);
        }

        private static bool HasTopmostStyle(IntPtr hwnd) =>
            (Win32.GetWindowLongPtr(hwnd, Win32.GWL_EXSTYLE).ToInt64() & Win32.WS_EX_TOPMOST) != 0;

        private void Unhook()
        {
            if (_locationHook != IntPtr.Zero) Win32.UnhookWinEvent(_locationHook);
            if (_systemHook != IntPtr.Zero) Win32.UnhookWinEvent(_systemHook);
            _locationHook = _systemHook = IntPtr.Zero;
        }

        /// <summary>The device screen inside the video window, in physical pixels.</summary>
        private bool TryPicture(out PixelRect picture)
        {
            picture = default(PixelRect);
            if (_driver == null || !VideoWindow.TryGetClientBounds(_video, out var client)) return false;
            var size = _driver.Size;
            picture = PictureMap.Picture(client, size.Width, size.Height);
            return true;
        }

        private static Win32.POINT CursorNow()
        {
            Win32.GetCursorPos(out var p);
            return p;
        }

        // ---- mouse ---------------------------------------------------------------------------

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            e.Handled = true;
            Activate();
            if (_driver == null || !TryPicture(out var picture)) return;
            var at = CursorNow();
            if (!PictureMap.ToFraction(picture, at.X, at.Y, out var xf, out var yf)) return;
            _pressing = true;
            _dragging = false;
            _longPressed = false;
            _downAt = at;
            _samples.Clear();
            _clock.Restart();
            _samples.Add(new PathSample(0, xf, yf));
            CaptureMouse();
            _longPress.Stop();
            _longPress.Start();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!_pressing || _longPressed || !TryPicture(out var picture)) return;
            var at = CursorNow();
            PictureMap.ToFraction(picture, at.X, at.Y, out var xf, out var yf);
            _samples.Add(new PathSample((int)_clock.ElapsedMilliseconds, xf, yf));
            if (!_dragging && (Math.Abs(at.X - _downAt.X) > DragSlop || Math.Abs(at.Y - _downAt.Y) > DragSlop))
            {
                _dragging = true;
                _longPress.Stop();
                _trail = Trail(e.GetPosition(this));
            }
            if (_dragging && _trail != null) _trail.Points.Add(e.GetPosition(this));
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonUp(e);
            e.Handled = true;
            if (!_pressing) return;
            var released = (int)_clock.ElapsedMilliseconds;
            var first = _samples[0];
            var dragging = _dragging;
            var longPressed = _longPressed;
            var samples = new List<PathSample>(_samples);
            var trail = _trail;
            EndPress();
            if (_driver == null || longPressed) return;
            if (dragging)
            {
                _driver.Drag(samples, released);
                if (trail != null) FadeOut(trail, 450);
                return;
            }
            _driver.Tap(first.X, first.Y);
            Ripple(e.GetPosition(this), false);
        }

        protected override void OnLostMouseCapture(MouseEventArgs e)
        {
            base.OnLostMouseCapture(e);
            // Alt+Tab, a dialog, the window hidden: nothing half done is sent
            if (_pressing) CancelPress();
        }

        protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseRightButtonUp(e);
            e.Handled = true;
            _driver?.Home();
        }

        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            base.OnMouseWheel(e);
            e.Handled = true;
            AddWheel(e.Delta, false, CursorNow());
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == Win32.WM_MOUSEHWHEEL)
            {
                var delta = (short)((wParam.ToInt64() >> 16) & 0xFFFF);
                var point = new Win32.POINT { X = (short)(lParam.ToInt64() & 0xFFFF), Y = (short)((lParam.ToInt64() >> 16) & 0xFFFF) };
                AddWheel(delta, true, point);
                handled = true;
                return new IntPtr(1);
            }
            return IntPtr.Zero;
        }

        private void AddWheel(int delta, bool horizontal, Win32.POINT at)
        {
            if (_driver == null || _pressing || !TryPicture(out var picture)) return;
            if (!PictureMap.ToFraction(picture, at.X, at.Y, out var xf, out var yf)) return;
            // the Windows setting: lines per notch (3 by default; a page counts as 3)
            var lines = SystemParameters.WheelScrollLines;
            var factor = lines <= 0 ? 1.0 : Math.Min(3.0, Math.Max(1.0 / 3, lines / 3.0));
            var notches = delta / 120.0 * factor;
            if (horizontal) _wheelH += notches;
            else _wheelV += notches;
            _wheelX = xf;
            _wheelY = yf;
            if (!_wheel.IsEnabled) _wheel.Start();
        }

        private void FlushWheel()
        {
            _wheel.Stop();
            if (_driver == null) return;
            if (_wheelV != 0) _driver.Scroll(_wheelX, _wheelY, _wheelV, false);
            if (_wheelH != 0) _driver.Scroll(_wheelX, _wheelY, _wheelH, true);
            _wheelV = _wheelH = 0;
        }

        private void OnLongPress()
        {
            _longPress.Stop();
            if (!_pressing || _dragging || _driver == null) return;
            _longPressed = true;
            var first = _samples[0];
            _driver.LongPress(first.X, first.Y);
            Ripple(Mouse.GetPosition(this), true);
        }

        private void EndPress()
        {
            _pressing = false;
            _dragging = false;
            _longPressed = false;
            _longPress.Stop();
            _trail = null;
            if (IsMouseCaptured) ReleaseMouseCapture();
        }

        private void CancelPress()
        {
            var trail = _trail;
            EndPress();
            if (trail != null) _effects.Children.Remove(trail);
        }

        // ---- keyboard ------------------------------------------------------------------------

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            base.OnPreviewKeyDown(e);
            if (_driver == null) return;
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            var mods = Keyboard.Modifiers;
            if (key == Key.Enter && mods == ModifierKeys.Alt)
            {
                e.Handled = true;
                FullscreenToggle();
                return;
            }
            if (mods == ModifierKeys.Control)
            {
                switch (key)
                {
                    case Key.V:
                        e.Handled = true;
                        _driver.Paste(ClipboardText());
                        return;
                    case Key.H:
                        e.Handled = true;
                        _driver.Home();
                        return;
                    case Key.J:
                        e.Handled = true;
                        _driver.AppSwitcher();
                        return;
                    case Key.Up:
                        e.Handled = true;
                        _driver.Volume(true);
                        return;
                    case Key.Down:
                        e.Handled = true;
                        _driver.Volume(false);
                        return;
                }
                return;
            }
            if (mods != ModifierKeys.None && mods != ModifierKeys.Shift) return;
            switch (key)
            {
                case Key.Back:
                    e.Handled = true;
                    _driver.Type("\b");
                    break;
                case Key.Enter:
                    e.Handled = true;
                    _driver.Type("\n");
                    break;
                case Key.Tab:
                    e.Handled = true;
                    _driver.Type("\t");
                    break;
            }
        }

        protected override void OnTextInput(TextCompositionEventArgs e)
        {
            base.OnTextInput(e);
            if (_driver == null || string.IsNullOrEmpty(e.Text)) return;
            // control characters (Enter, Backspace, Ctrl+letter) are handled as keys above, or not at all
            var text = new System.Text.StringBuilder(e.Text.Length);
            foreach (var c in e.Text)
            {
                if (c >= 0x20 && c != 0x7F) text.Append(c);
            }
            if (text.Length == 0) return;
            e.Handled = true;
            _driver.Type(text.ToString());
        }

        private static string ClipboardText()
        {
            try
            {
                return Clipboard.ContainsText() ? Clipboard.GetText() : "";
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                // another app holds the clipboard
                return "";
            }
        }

        /// <summary>d3d11videosink switches on Alt+Enter it sees itself: the video window comes to the front and gets one.</summary>
        private void FullscreenToggle()
        {
            if (!VideoWindow.IsAlive(_video)) return;
            VideoWindow.BringToFront(_video);
            if (!VideoWindow.IsInFront(_video)) return;
            Win32.keybd_event(Win32.VK_MENU, 0, Win32.KEYEVENTF_EXTENDEDKEY, UIntPtr.Zero);
            Win32.keybd_event(Win32.VK_RETURN, 0, 0, UIntPtr.Zero);
            Win32.keybd_event(Win32.VK_RETURN, 0, Win32.KEYEVENTF_KEYUP, UIntPtr.Zero);
            Win32.keybd_event(Win32.VK_MENU, 0, Win32.KEYEVENTF_EXTENDEDKEY | Win32.KEYEVENTF_KEYUP, UIntPtr.Zero);
        }

        // ---- feedback ------------------------------------------------------------------------

        private void Ripple(Point at, bool large)
        {
            var size = large ? 56.0 : 40.0;
            var ring = new Ellipse
            {
                Width = size,
                Height = size,
                Fill = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)),
                Stroke = new SolidColorBrush(Color.FromArgb(0xF2, 0xFF, 0xFF, 0xFF)),
                StrokeThickness = 2,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new ScaleTransform(0.6, 0.6),
                Effect = null,
            };
            var halo = new Ellipse
            {
                Width = size + 4,
                Height = size + 4,
                Stroke = new SolidColorBrush(Color.FromArgb(0x59, 0, 0, 0)),
                StrokeThickness = 2,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new ScaleTransform(0.6, 0.6),
            };
            Place(halo, at, size + 4);
            Place(ring, at, size);
            foreach (var e in new[] { halo, ring })
            {
                var grow = new DoubleAnimation(0.6, 1.0, TimeSpan.FromMilliseconds(large ? 500 : 320)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
                ((ScaleTransform)e.RenderTransform).BeginAnimation(ScaleTransform.ScaleXProperty, grow);
                ((ScaleTransform)e.RenderTransform).BeginAnimation(ScaleTransform.ScaleYProperty, grow);
                FadeOut(e, large ? 600 : 380);
            }
        }

        private void Place(FrameworkElement e, Point at, double size)
        {
            Canvas.SetLeft(e, at.X - size / 2);
            Canvas.SetTop(e, at.Y - size / 2);
            _effects.Children.Add(e);
        }

        private Polyline Trail(Point start)
        {
            var line = new Polyline
            {
                Stroke = new SolidColorBrush(Color.FromArgb(0xD9, 0xFF, 0xFF, 0xFF)),
                StrokeThickness = 4,
                StrokeLineJoin = PenLineJoin.Round,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                Opacity = 0.9,
            };
            line.Points.Add(start);
            _effects.Children.Add(line);
            return line;
        }

        private void FadeOut(UIElement element, int ms)
        {
            var fade = new DoubleAnimation(element.Opacity, 0, TimeSpan.FromMilliseconds(ms));
            fade.Completed += (_, __) => _effects.Children.Remove(element);
            element.BeginAnimation(OpacityProperty, fade);
        }

        private void ShowHint()
        {
            _hint.BeginAnimation(OpacityProperty, null);
            _hint.Opacity = 1;
            _hintTimer.Stop();
            _hintTimer.Start();
        }

        private void FadeHint()
        {
            _hintTimer.Stop();
            _hint.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(400)));
        }
    }
}
