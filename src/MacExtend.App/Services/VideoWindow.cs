using System;
using System.Runtime.InteropServices;
using MacExtend.Core;
using MacExtend.Native;

namespace MacExtend.Services
{
    /// <summary>
    /// The window GStreamer's d3d11videosink opens inside uxplay while a device streams: a normal window
    /// with a title bar and resize frame, or full screen (Alt+Enter switches). Closing it ends the session;
    /// uxplay keeps running and opens a new window for the next one.
    /// MacExtend is per-monitor DPI aware, so every rectangle here is in physical pixels, whatever DPI
    /// awareness uxplay runs with.
    /// </summary>
    internal static class VideoWindow
    {
        /// <summary>Full screen may sit a pixel or two off the monitor edges.</summary>
        private const int FullscreenSlack = 2;

        /// <returns>The largest visible top-level window of the process, or IntPtr.Zero.</returns>
        public static IntPtr Find(int processId)
        {
            if (processId <= 0) return IntPtr.Zero;
            var best = IntPtr.Zero;
            long bestArea = 0;
            Win32.EnumWindows((hwnd, _) =>
            {
                Win32.GetWindowThreadProcessId(hwnd, out var pid);
                if (pid != processId || !Win32.IsWindowVisible(hwnd) || !Win32.GetWindowRect(hwnd, out var r)) return true;
                long area = (long)r.Width * r.Height;
                if (area > bestArea)
                {
                    bestArea = area;
                    best = hwnd;
                }
                return true;
            }, IntPtr.Zero);
            return best;
        }

        public static bool IsAlive(IntPtr hwnd) => hwnd != IntPtr.Zero && Win32.IsWindow(hwnd) && Win32.IsWindowVisible(hwnd);

        public static bool IsInFront(IntPtr hwnd) => hwnd != IntPtr.Zero && Win32.GetForegroundWindow() == hwnd && !Win32.IsIconic(hwnd);

        /// <summary>
        /// The window has no title bar or resize frame and covers its whole monitor, the way d3d11videosink
        /// leaves it in full screen. The sink also maximizes it there, so a maximized flag proves nothing
        /// either way, and an ordinary maximized window keeps its title bar. Anything that cannot be read
        /// counts as not full screen.
        /// </summary>
        public static bool IsFullscreen(IntPtr hwnd)
        {
            if (!IsAlive(hwnd) || Win32.IsIconic(hwnd)) return false;
            var style = Win32.GetWindowLongPtr(hwnd, Win32.GWL_STYLE).ToInt64();
            // a visible window always has WS_VISIBLE set, so 0 means the read failed
            if (style == 0 || (style & (Win32.WS_CAPTION | Win32.WS_THICKFRAME)) != 0) return false;
            if (!Win32.GetWindowRect(hwnd, out var r) || !TryGetMonitor(hwnd, out var info)) return false;
            var m = info.rcMonitor;
            return r.Left <= m.Left + FullscreenSlack && r.Top <= m.Top + FullscreenSlack &&
                   r.Right >= m.Right - FullscreenSlack && r.Bottom >= m.Bottom - FullscreenSlack;
        }

        /// <summary>
        /// Bounds of a window the user can move and resize: visible, not minimized, maximized or full screen,
        /// with a title bar or resize frame.
        /// </summary>
        public static bool TryGetNormalBounds(IntPtr hwnd, out PixelRect bounds)
        {
            bounds = default(PixelRect);
            if (!IsAlive(hwnd) || Win32.IsIconic(hwnd) || Win32.IsZoomed(hwnd)) return false;
            var style = Win32.GetWindowLongPtr(hwnd, Win32.GWL_STYLE).ToInt64();
            var framed = (style & Win32.WS_CAPTION) == Win32.WS_CAPTION || (style & Win32.WS_THICKFRAME) != 0;
            if (!framed || !Win32.GetWindowRect(hwnd, out var r)) return false;
            bounds = new PixelRect(r.Left, r.Top, r.Right, r.Bottom);
            return true;
        }

        /// <summary>Work area (monitor minus taskbar) of the monitor showing the window.</summary>
        public static bool TryGetWorkArea(IntPtr hwnd, out PixelRect work)
        {
            work = default(PixelRect);
            if (!TryGetMonitor(hwnd, out var info)) return false;
            var w = info.rcWork;
            work = new PixelRect(w.Left, w.Top, w.Right, w.Bottom);
            return true;
        }

        /// <summary>
        /// Moves and resizes the window to <paramref name="target"/> unless it changed since
        /// <paramref name="expected"/> was read: another window, another process, or the user (or Alt+Enter)
        /// already moved, resized, maximized or minimized it. Neither activates it nor changes its z-order.
        /// </summary>
        /// <returns>true if the window was moved.</returns>
        public static bool MoveIfUnchanged(IntPtr hwnd, int processId, PixelRect expected, PixelRect target)
        {
            if (processId <= 0) return false;
            Win32.GetWindowThreadProcessId(hwnd, out var pid);
            if (pid != processId || !TryGetNormalBounds(hwnd, out var now) || now != expected) return false;
            return Win32.SetWindowPos(hwnd, IntPtr.Zero, target.Left, target.Top, target.Width, target.Height,
                                      Win32.SWP_NOACTIVATE | Win32.SWP_NOZORDER);
        }

        public static bool IsTopmost(IntPtr hwnd) =>
            IsAlive(hwnd) && (Win32.GetWindowLongPtr(hwnd, Win32.GWL_EXSTYLE).ToInt64() & Win32.WS_EX_TOPMOST) != 0;

        /// <summary>Pins the window above all non-topmost windows, or drops the pin, without activating it.</summary>
        /// <returns>true if Windows accepted the change.</returns>
        public static bool SetTopmost(IntPtr hwnd, bool topmost)
        {
            if (!IsAlive(hwnd)) return false;
            return Win32.SetWindowPos(hwnd, topmost ? Win32.HWND_TOPMOST : Win32.HWND_NOTOPMOST, 0, 0, 0, 0,
                                      Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE);
        }

        /// <summary>
        /// Windows refuses SetForegroundWindow from a process the user is not using. Sharing input with the
        /// current foreground thread is usually enough. Only when Windows still refuses, Alt is held while
        /// switching (released only after the switch, so the old app never sees a lone Alt and opens its menu):
        /// a lone Alt reaching the video window's title bar could leave it waiting in menu mode.
        /// The topmost flip raises the window even if the switch is still refused. It also drops an
        /// always-on-top pin, so a caller that keeps one puts it back afterwards.
        /// </summary>
        public static void BringToFront(IntPtr hwnd)
        {
            if (!IsAlive(hwnd)) return;
            if (Win32.IsIconic(hwnd)) Win32.ShowWindow(hwnd, Win32.SW_RESTORE);

            var self = Win32.GetCurrentThreadId();
            var foregroundThread = Win32.GetWindowThreadProcessId(Win32.GetForegroundWindow(), out _);
            var targetThread = Win32.GetWindowThreadProcessId(hwnd, out _);
            var attachedForeground = foregroundThread != 0 && foregroundThread != self && Win32.AttachThreadInput(self, foregroundThread, true);
            var attachedTarget = targetThread != 0 && targetThread != self && Win32.AttachThreadInput(self, targetThread, true);
            try
            {
                Win32.SetWindowPos(hwnd, Win32.HWND_TOPMOST, 0, 0, 0, 0, Win32.SWP_NOMOVE | Win32.SWP_NOSIZE);
                Win32.SetWindowPos(hwnd, Win32.HWND_NOTOPMOST, 0, 0, 0, 0, Win32.SWP_NOMOVE | Win32.SWP_NOSIZE);
                Win32.BringWindowToTop(hwnd);
                if (Win32.SetForegroundWindow(hwnd) && Win32.GetForegroundWindow() == hwnd) return;

                Win32.keybd_event(Win32.VK_MENU, 0, Win32.KEYEVENTF_EXTENDEDKEY, UIntPtr.Zero);
                try
                {
                    Win32.SetForegroundWindow(hwnd);
                }
                finally
                {
                    Win32.keybd_event(Win32.VK_MENU, 0, Win32.KEYEVENTF_EXTENDEDKEY | Win32.KEYEVENTF_KEYUP, UIntPtr.Zero);
                }
            }
            finally
            {
                if (attachedTarget) Win32.AttachThreadInput(self, targetThread, false);
                if (attachedForeground) Win32.AttachThreadInput(self, foregroundThread, false);
            }
        }

        /// <summary>Physical-pixel bounds of the monitor showing the window.</summary>
        public static Win32.RECT MonitorBounds(IntPtr hwnd)
        {
            if (TryGetMonitor(hwnd, out var info)) return info.rcMonitor;
            Win32.GetWindowRect(hwnd, out var r);
            return r;
        }

        private static bool TryGetMonitor(IntPtr hwnd, out Win32.MONITORINFO info)
        {
            info = new Win32.MONITORINFO { cbSize = Marshal.SizeOf(typeof(Win32.MONITORINFO)) };
            var monitor = Win32.MonitorFromWindow(hwnd, Win32.MONITOR_DEFAULTTONEAREST);
            return monitor != IntPtr.Zero && Win32.GetMonitorInfo(monitor, ref info);
        }
    }
}
