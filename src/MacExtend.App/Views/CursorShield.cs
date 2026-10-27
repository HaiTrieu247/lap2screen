using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using MacExtend.Native;

namespace MacExtend.Views
{
    /// <summary>
    /// An invisible window laid over the Mac display so the Windows pointer disappears there.
    /// Windows shows the cursor of the window under the mouse: this one has none. Alpha 1 instead
    /// of 0 keeps it hit-testable (fully transparent pixels let the mouse fall through). It never
    /// takes keyboard focus, so shortcuts keep reaching Windows and the app in front.
    /// </summary>
    internal sealed class CursorShield : Window
    {
        private Win32.RECT _bounds;
        private bool _covering;

        public CursorShield()
        {
            Title = "MacExtend";
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true;
            Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = false;
            Focusable = false;
            Cursor = Cursors.None;
            SourceInitialized += (_, __) =>
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                var style = Win32.GetWindowLongPtr(hwnd, Win32.GWL_EXSTYLE).ToInt64();
                Win32.SetWindowLongPtr(hwnd, Win32.GWL_EXSTYLE, new IntPtr(style | Win32.WS_EX_NOACTIVATE | Win32.WS_EX_TOOLWINDOW));
            };
        }

        /// <param name="bounds">Physical pixels of the monitor showing the Mac display.</param>
        public void Cover(Win32.RECT bounds)
        {
            var wasCovering = _covering;
            if (!IsVisible) Show();
            if (!wasCovering || !Same(bounds, _bounds))
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                Win32.SetWindowPos(hwnd, Win32.HWND_TOPMOST, bounds.Left, bounds.Top, bounds.Width, bounds.Height,
                                   Win32.SWP_NOACTIVATE | Win32.SWP_SHOWWINDOW);
                _bounds = bounds;
            }
            _covering = true;
            // Windows updates the pointer image on the next mouse move; nudge it in place once
            if (!wasCovering && Win32.GetCursorPos(out var p)) Win32.SetCursorPos(p.X, p.Y);
        }

        public void Uncover()
        {
            if (!_covering) return;
            _covering = false;
            Hide();
            if (Win32.GetCursorPos(out var p)) Win32.SetCursorPos(p.X, p.Y);
        }

        private static bool Same(Win32.RECT a, Win32.RECT b) =>
            a.Left == b.Left && a.Top == b.Top && a.Right == b.Right && a.Bottom == b.Bottom;
    }
}
