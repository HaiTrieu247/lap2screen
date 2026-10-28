using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using MacExtend.Core;
using MacExtend.Native;
using Microsoft.Win32;

namespace MacExtend.Services
{
    internal static class DisplayInfo
    {
        /// <summary>Physical pixels and refresh rate of the primary display, independent of Windows scaling.</summary>
        public static DisplayMode GetPrimary()
        {
            var mode = new Win32.DEVMODE { dmSize = (short)Marshal.SizeOf(typeof(Win32.DEVMODE)) };
            return Win32.EnumDisplaySettings(null, Win32.ENUM_CURRENT_SETTINGS, ref mode)
                ? new DisplayMode(mode.dmPelsWidth, mode.dmPelsHeight, mode.dmDisplayFrequency)
                : new DisplayMode(0, 0, 0);
        }
    }

    internal static class ShortPath
    {
        /// <summary>
        /// uxplay opens files through the ANSI C runtime, which cannot reach paths with characters
        /// outside the code page (a user folder like C:\Users\Thịnh). The 8.3 short name of the
        /// folder is plain ASCII. Only the folder must exist; file names used here are ASCII.
        /// </summary>
        public static string Resolve(string path)
        {
            if (IsAscii(path)) return path;
            var dir = Path.GetDirectoryName(path);
            var name = Path.GetFileName(path);
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return path;
            var sb = new StringBuilder(1024);
            var len = Win32.GetShortPathName(dir, sb, sb.Capacity);
            return len > 0 && len < sb.Capacity ? Path.Combine(sb.ToString(), name) : path;
        }

        private static bool IsAscii(string s)
        {
            foreach (var c in s)
            {
                if (c > 127) return false;
            }
            return true;
        }
    }

    internal static class KeepAwake
    {
        /// <summary>Keeps the screen on while a Mac is using it. The state belongs to the calling thread: call from the UI thread only.</summary>
        public static void Set(bool on)
        {
            Win32.SetThreadExecutionState(on
                ? Win32.ExecutionState.Continuous | Win32.ExecutionState.SystemRequired | Win32.ExecutionState.DisplayRequired
                : Win32.ExecutionState.Continuous);
        }
    }

    internal static class AutoStart
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "MacExtend";

        public static string Command =>
            "\"" + System.Reflection.Assembly.GetEntryAssembly().Location + "\" --background";

        public static bool IsEnabled()
        {
            using (var key = Registry.CurrentUser.OpenSubKey(RunKey))
            {
                return string.Equals(key?.GetValue(ValueName) as string, Command, StringComparison.OrdinalIgnoreCase);
            }
        }

        public static void Set(bool on)
        {
            using (var key = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (on) key.SetValue(ValueName, Command);
                else key.DeleteValue(ValueName, false);
            }
        }
    }

    internal static class DpiCompat
    {
        private const string LayersKey = @"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers";

        /// <summary>
        /// Same as Compatibility, "Change high DPI settings", "Override high DPI scaling: Application"
        /// for uxplay.exe, so its video is drawn 1:1 instead of stretched at 125 %/150 % scaling.
        /// Applies to processes started after the change.
        /// </summary>
        /// <returns>false if the setting could not be written.</returns>
        public static bool EnsureHighDpiAware(string exePath)
        {
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(LayersKey))
                {
                    var existing = key.GetValue(exePath) as string;
                    var next = CompatLayers.WithHighDpiAware(existing);
                    if (!string.Equals(existing, next, StringComparison.Ordinal)) key.SetValue(exePath, next);
                    return true;
                }
            }
            catch (Exception e) when (e is UnauthorizedAccessException || e is System.Security.SecurityException || e is IOException)
            {
                return false;
            }
        }
    }

    internal static class WindowsTheme
    {
        private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

        /// <summary>App windows: light or dark mode chosen in Windows settings.</summary>
        public static bool AppsUseLightTheme => ReadFlag("AppsUseLightTheme", true);

        /// <summary>Taskbar and tray: often dark even when apps are light.</summary>
        public static bool SystemUsesLightTheme => ReadFlag("SystemUsesLightTheme", false);

        private static bool ReadFlag(string name, bool fallback)
        {
            using (var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey))
            {
                return key?.GetValue(name) is int value ? value != 0 : fallback;
            }
        }
    }
}
