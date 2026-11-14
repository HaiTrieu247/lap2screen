using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using MacExtend.Core;
using MacExtend.Core.CoreDevice;
using MacExtend.Native;
using MacExtend.Services;
using MacExtend.Services.CoreDevice;

namespace MacExtend.Views
{
    /// <summary>
    /// A nearly transparent window laid exactly over the picture area of uxplay's video window while direct control is
    /// ready. It sits right above the video in the z-order (never owned by it: an owner in another process would tie the
    /// two processes' input together) and follows it as it moves, resizes, goes full screen or is minimized; the video's
    /// own title bar and frame stay uncovered. The mouse is a finger, live: press, hold, drag and lift go to the device as
    /// they happen. The wheel scrolls, keys are the device's keyboard (Ctrl as Command), a right click is Home. Alt+Enter
    /// and Alt+F4 go to the video window. Nothing typed is kept or logged.
    /// </summary>
    internal sealed class DirectOverlay : Window
    {
        private static readonly TimeSpan WheelBatch = TimeSpan.FromMilliseconds(100);
        private static readonly TimeSpan HintVisible = TimeSpan.FromSeconds(4);
        /// <summary>Physical pixels the pointer may wander before a press shows its trail.</summary>
        private const int DragSlop = 8;
        private const int CommandLeft = 0xE3;
        private const int CommandRight = 0xE7;
        private const int OptionLeft = 0xE2;
        private const int OptionRight = 0xE6;
        private const int VkV = 0x56;
        private const int VkH = 0x48;
        private const int VkUp = 0x26;
        private const int VkDown = 0x28;

        private readonly Canvas _effects = new Canvas { IsHitTestVisible = false };
        private readonly Border _hint;
        private readonly TextBlock _hintText;
        private readonly DispatcherTimer _wheel;
        private readonly DispatcherTimer _follow;
        private readonly DispatcherTimer _hintTimer;
        // kept in fields: the native hooks call them for as long as they live
        private readonly Win32.WinEventProc _onLocation;
        private readonly Win32.WinEventProc _onSystem;
        private readonly HashSet<int> _held = new HashSet<int>();
        // virtual keys whose press became a shortcut: their release is not the device's either
        private readonly HashSet<int> _shortcut = new HashSet<int>();
        private IntPtr _locationHook;
        private IntPtr _systemHook;
        private IntPtr _video;
        private DirectInput _input;
        private IntPtr _hwnd;
        private int _pictureWidth;
        private int _pictureHeight;

        // the press in progress
        private bool _pressing;
        private bool _dragging;
        private Win32.POINT _downAt;
        private Polyline _trail;
        private double _wheelV;
        private double _wheelH;
        private double _wheelX;
        private double _wheelY;

        public DirectOverlay()
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
            Deactivated += (_, __) => LetGo();
        }

        /// <summary>A press began on the picture; the controller may ask the device how it is turned.</summary>
        public event Action Pressed;

        /// <summary>Lays the overlay over <paramref name="video"/> and sends what happens on it to <paramref name="input"/>.</summary>
        public void Attach(IntPtr video, DirectInput input, string device)
        {
            var again = video == _video && input == _input;
            if (input != _input) LetGo();
            _input = input;
            if (!again)
            {
                Unhook();
                _video = video;
                Win32.GetWindowThreadProcessId(video, out var pid);
                _locationHook = Win32.SetWinEventHook(Win32.EVENT_OBJECT_LOCATIONCHANGE, Win32.EVENT_OBJECT_LOCATIONCHANGE, IntPtr.Zero,
                                                      _onLocation, pid, 0, Win32.WINEVENT_OUTOFCONTEXT);
                _systemHook = Win32.SetWinEventHook(Win32.EVENT_SYSTEM_FOREGROUND, Win32.EVENT_SYSTEM_MINIMIZEEND, IntPtr.Zero,
                                                    _onSystem, 0, 0, Win32.WINEVENT_OUTOFCONTEXT);
                _hintText.Text = "Điều khiển " + (string.IsNullOrEmpty(device) ? "thiết bị" : device) + ". " + DirectControlTexts.Hint;
                ShowHint();
            }
            _follow.Start();
            Sync();
        }

        public void Detach()
        {
            LetGo();
            Unhook();
            _follow.Stop();
            _wheel.Stop();
            _wheelV = _wheelH = 0;
            _video = IntPtr.Zero;
            _input = null;
            if (IsVisible) Hide();
        }

        /// <summary>The picture's size uxplay logged (0 while unknown): where the device screen is inside the window.</summary>
        public void SetPicture(int width, int height)
        {
            if (width == _pictureWidth && height == _pictureHeight) return;
            _pictureWidth = width;
            _pictureHeight = height;
            Sync();
        }

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
                LetGo();
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
            if (_input == null || !VideoWindow.TryGetClientBounds(_video, out var client)) return false;
            picture = PictureMap.Picture(client, _pictureWidth, _pictureHeight);
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
            if (_input == null || _pressing || !TryPicture(out var picture)) return;
            var at = CursorNow();
            if (!PictureMap.ToFraction(picture, at.X, at.Y, out var xf, out var yf)) return;
            _pressing = true;
            _dragging = false;
            _downAt = at;
            CaptureMouse();
            _input.Down(xf, yf);
            Ripple(e.GetPosition(this));
            Pressed?.Invoke();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!_pressing || _input == null || !TryPicture(out var picture)) return;
            var at = CursorNow();
            // a drag that leaves the picture goes on along its edge
            PictureMap.ToFraction(picture, at.X, at.Y, out var xf, out var yf);
            _input.Move(xf, yf);
            if (!_dragging && (Math.Abs(at.X - _downAt.X) > DragSlop || Math.Abs(at.Y - _downAt.Y) > DragSlop))
            {
                _dragging = true;
                _trail = Trail(e.GetPosition(this));
            }
            if (_dragging && _trail != null) _trail.Points.Add(e.GetPosition(this));
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonUp(e);
            e.Handled = true;
            if (!_pressing) return;
            if (_input != null && TryPicture(out var picture))
            {
                var at = CursorNow();
                PictureMap.ToFraction(picture, at.X, at.Y, out var xf, out var yf);
                _input.Up(xf, yf);
            }
            else
            {
                _input?.Cancel();
            }
            var trail = _trail;
            EndPress();
            if (trail != null) FadeOut(trail, 450);
        }

        protected override void OnLostMouseCapture(MouseEventArgs e)
        {
            base.OnLostMouseCapture(e);
            // Alt+Tab, a dialog, the window hidden: the finger lifts where it was
            if (_pressing) CancelPress();
        }

        protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseRightButtonUp(e);
            e.Handled = true;
            if (!_pressing) _input?.Press(DeviceButton.Home);
        }

        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            base.OnMouseWheel(e);
            e.Handled = true;
            AddWheel(e.Delta, false, CursorNow());
        }

        private void AddWheel(int delta, bool horizontal, Win32.POINT at)
        {
            if (_input == null || _pressing || !TryPicture(out var picture)) return;
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
            if (_input == null) return;
            if (_wheelV != 0) _input.Scroll(_wheelX, _wheelY, _wheelV, false);
            if (_wheelH != 0) _input.Scroll(_wheelX, _wheelY, _wheelH, true);
            _wheelV = _wheelH = 0;
        }

        private void EndPress()
        {
            _pressing = false;
            _dragging = false;
            _trail = null;
            if (IsMouseCaptured) ReleaseMouseCapture();
        }

        private void CancelPress()
        {
            _input?.Cancel();
            var trail = _trail;
            EndPress();
            if (trail != null) _effects.Children.Remove(trail);
        }

        /// <summary>The overlay loses the keyboard or the picture: no finger and no key stays down on the device.</summary>
        private void LetGo()
        {
            if (_pressing) CancelPress();
            _shortcut.Clear();
            if (_held.Count == 0) return;
            _held.Clear();
            _input?.Keys(_held);
        }

        // ---- keyboard ------------------------------------------------------------------------

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            switch (msg)
            {
                case Win32.WM_MOUSEHWHEEL:
                {
                    var delta = (short)((wParam.ToInt64() >> 16) & 0xFFFF);
                    var point = new Win32.POINT { X = (short)(lParam.ToInt64() & 0xFFFF), Y = (short)((lParam.ToInt64() >> 16) & 0xFFFF) };
                    AddWheel(delta, true, point);
                    handled = true;
                    return new IntPtr(1);
                }
                case Win32.WM_KEYDOWN:
                case Win32.WM_SYSKEYDOWN:
                    handled = true;
                    OnKeyMessageDown((int)wParam.ToInt64(), lParam.ToInt64());
                    return IntPtr.Zero;
                case Win32.WM_KEYUP:
                case Win32.WM_SYSKEYUP:
                    handled = true;
                    OnKeyMessageUp((int)wParam.ToInt64(), lParam.ToInt64());
                    return IntPtr.Zero;
                case Win32.WM_KILLFOCUS:
                    LetGo();
                    return IntPtr.Zero;
            }
            return IntPtr.Zero;
        }

        private void OnKeyMessageDown(int vk, long lParam)
        {
            if (_input == null) return;
            var scan = (uint)((lParam >> 16) & 0xFF);
            var extended = ((lParam >> 24) & 1) != 0;
            var repeat = ((lParam >> 30) & 1) != 0;
            var command = _held.Contains(CommandLeft) || _held.Contains(CommandRight);
            var option = _held.Contains(OptionLeft) || _held.Contains(OptionRight);

            // the video window's own keys: full screen, and closing it
            if (option && !command && (vk == Win32.VK_RETURN || vk == Win32.VK_F4))
            {
                if (!repeat)
                {
                    LetGo();
                    if (vk == Win32.VK_RETURN) FullscreenToggle();
                    else Win32.PostMessage(_video, Win32.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                }
                return;
            }
            if (command && (vk == VkV || vk == VkH || vk == VkUp || vk == VkDown))
            {
                if (!repeat || vk == VkUp || vk == VkDown) Shortcut(vk);
                _shortcut.Add(vk);
                return;
            }
            // the device repeats a held key itself
            if (repeat) return;
            if (DirectKeys.TryGetUsage(vk, scan, extended, out var usage) && _held.Add(usage)) _input.Keys(_held);
        }

        private void OnKeyMessageUp(int vk, long lParam)
        {
            if (_shortcut.Remove(vk) || _input == null) return;
            var scan = (uint)((lParam >> 16) & 0xFF);
            var extended = ((lParam >> 24) & 1) != 0;
            if (DirectKeys.TryGetUsage(vk, scan, extended, out var usage) && _held.Remove(usage)) _input.Keys(_held);
        }

        private void Shortcut(int vk)
        {
            switch (vk)
            {
                case VkV:
                    _input.Paste(ClipboardText(), _held.ToList());
                    break;
                case VkH:
                    _input.Press(DeviceButton.Home);
                    break;
                case VkUp:
                    _input.Press(DeviceButton.VolumeUp);
                    break;
                case VkDown:
                    _input.Press(DeviceButton.VolumeDown);
                    break;
            }
        }

        private static string ClipboardText()
        {
            try
            {
                return Clipboard.ContainsText() ? Clipboard.GetText() : "";
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                // another app holds the clipboard: the device's own clipboard is pasted
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

        private void Ripple(Point at)
        {
            const double size = 40.0;
            var ring = new Ellipse
            {
                Width = size,
                Height = size,
                Fill = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)),
                Stroke = new SolidColorBrush(Color.FromArgb(0xF2, 0xFF, 0xFF, 0xFF)),
                StrokeThickness = 2,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new ScaleTransform(0.6, 0.6),
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
                var grow = new DoubleAnimation(0.6, 1.0, TimeSpan.FromMilliseconds(320)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
                ((ScaleTransform)e.RenderTransform).BeginAnimation(ScaleTransform.ScaleXProperty, grow);
                ((ScaleTransform)e.RenderTransform).BeginAnimation(ScaleTransform.ScaleYProperty, grow);
                FadeOut(e, 380);
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
