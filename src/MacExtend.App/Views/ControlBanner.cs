using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using MacExtend.Core;
using MacExtend.Native;

namespace MacExtend.Views
{
    /// <summary>
    /// A short notice at the top of the AirPlay picture when the laptop starts controlling the device: which
    /// device, and how to get the keyboard and mouse back. Clicks fall through it, it never takes focus, and it
    /// fades out after a few seconds. Dark in both themes: it sits on top of the device's own picture.
    /// </summary>
    internal sealed class ControlBanner : Window
    {
        private static readonly TimeSpan Visible = TimeSpan.FromSeconds(3);
        private static readonly TimeSpan Fade = TimeSpan.FromMilliseconds(400);

        private readonly TextBlock _title;
        private readonly DispatcherTimer _timer;
        private int _shown;

        public ControlBanner()
        {
            Title = "MacExtend";
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = false;
            Focusable = false;
            IsHitTestVisible = false;
            SizeToContent = SizeToContent.WidthAndHeight;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = -10000;
            Top = -10000;

            var font = TryFindResource("Font.Sans") as FontFamily;
            _title = new TextBlock { Foreground = Brushes.White, FontSize = 14, FontWeight = FontWeights.SemiBold };
            var hint = new TextBlock
            {
                Text = "Nhấn Ctrl+Alt+Q để trả chuột và bàn phím cho Windows.",
                Foreground = new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)),
                FontSize = 12.5,
                Margin = new Thickness(0, 3, 0, 0),
            };
            if (font != null)
            {
                _title.FontFamily = font;
                hint.FontFamily = font;
            }
            var text = new StackPanel();
            text.Children.Add(_title);
            text.Children.Add(hint);
            Content = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0xEB, 0x1C, 0x1C, 0x1E)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x29, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(18, 11, 18, 12),
                Child = text,
            };

            SourceInitialized += (_, __) =>
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                var style = Win32.GetWindowLongPtr(hwnd, Win32.GWL_EXSTYLE).ToInt64();
                Win32.SetWindowLongPtr(hwnd, Win32.GWL_EXSTYLE,
                                       new IntPtr(style | Win32.WS_EX_NOACTIVATE | Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_TRANSPARENT));
            };
            _timer = new DispatcherTimer { Interval = Visible };
            _timer.Tick += (_, __) =>
            {
                _timer.Stop();
                FadeOut();
            };
        }

        /// <param name="picture">Physical pixels of the AirPlay picture.</param>
        public void ShowOver(PixelRect picture, string device)
        {
            _title.Text = "Đang điều khiển " + device;
            _shown++;
            BeginAnimation(OpacityProperty, null);
            Opacity = 1;
            if (!IsVisible) Show();
            UpdateLayout();
            Place(picture);
            // on a monitor with other scaling WPF resizes the window once it lands there: centre it again
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => Place(picture)));
            _timer.Stop();
            _timer.Start();
        }

        public void HideNow()
        {
            _timer.Stop();
            _shown++;
            BeginAnimation(OpacityProperty, null);
            Hide();
        }

        private void Place(PixelRect picture)
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (!IsVisible || !Win32.GetWindowRect(hwnd, out var r)) return;
            var x = picture.CenterX - r.Width / 2;
            var y = picture.Top + Math.Max(16, picture.Height / 24);
            Win32.SetWindowPos(hwnd, Win32.HWND_TOPMOST, x, y, 0, 0, Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE | Win32.SWP_SHOWWINDOW);
        }

        private void FadeOut()
        {
            var shown = _shown;
            var fade = new DoubleAnimation(0, Fade);
            // shown again meanwhile: that showing owns the window now
            fade.Completed += (_, __) =>
            {
                if (shown == _shown) Hide();
            };
            BeginAnimation(OpacityProperty, fade);
        }
    }
}
