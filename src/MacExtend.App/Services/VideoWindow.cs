using System;
using MacExtend.Native;

namespace MacExtend.Services
{
    /// <summary>The full-screen window GStreamer's d3d11videosink opens inside uxplay while a Mac streams.</summary>
    internal static class VideoWindow
    {
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
        /// Windows refuses SetForegroundWindow from a process the user is not using. Two documented
        /// ways around it: share input with the current foreground thread, and hold Alt while switching
        /// (Alt is released only after the switch, so the old app never sees a lone Alt and opens its menu).
        /// The topmost flip raises the window even if the switch is still refused.
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
                Win32.keybd_event(Win32.VK_MENU, 0, Win32.KEYEVENTF_EXTENDEDKEY, UIntPtr.Zero);
                Win32.SetWindowPos(hwnd, Win32.HWND_TOPMOST, 0, 0, 0, 0, Win32.SWP_NOMOVE | Win32.SWP_NOSIZE);
                Win32.SetWindowPos(hwnd, Win32.HWND_NOTOPMOST, 0, 0, 0, 0, Win32.SWP_NOMOVE | Win32.SWP_NOSIZE);
                Win32.BringWindowToTop(hwnd);
                Win32.SetForegroundWindow(hwnd);
            }
            finally
            {
                Win32.keybd_event(Win32.VK_MENU, 0, Win32.KEYEVENTF_EXTENDEDKEY | Win32.KEYEVENTF_KEYUP, UIntPtr.Zero);
                if (attachedTarget) Win32.AttachThreadInput(self, targetThread, false);
                if (attachedForeground) Win32.AttachThreadInput(self, foregroundThread, false);
            }
        }

        /// <summary>Physical-pixel bounds of the monitor showing the window.</summary>
        public static Win32.RECT MonitorBounds(IntPtr hwnd)
        {
            var info = new Win32.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(Win32.MONITORINFO)) };
            var monitor = Win32.MonitorFromWindow(hwnd, Win32.MONITOR_DEFAULTTONEAREST);
            if (monitor != IntPtr.Zero && Win32.GetMonitorInfo(monitor, ref info)) return info.rcMonitor;
            Win32.GetWindowRect(hwnd, out var r);
            return r;
        }
    }
}
