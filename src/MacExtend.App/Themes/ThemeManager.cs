using System;
using System.Linq;
using System.Windows;
using System.Windows.Interop;
using MacExtend.Native;
using MacExtend.Services;
using Microsoft.Win32;

namespace MacExtend.Themes
{
    /// <summary>Follows the Windows light/dark setting by swapping the palette dictionary at runtime.</summary>
    internal static class ThemeManager
    {
        private static readonly Uri LightUri = new Uri("pack://application:,,,/MacExtend;component/Themes/Colors.Light.xaml");
        private static readonly Uri DarkUri = new Uri("pack://application:,,,/MacExtend;component/Themes/Colors.Dark.xaml");

        public static bool IsDark { get; private set; }

        /// <summary>The app palette or the taskbar's light/dark setting changed. Raised on the UI thread.</summary>
        public static event Action Changed;

        public static void Initialize(Application app)
        {
            Apply(app, !WindowsTheme.AppsUseLightTheme);
            SystemEvents.UserPreferenceChanged += (_, e) =>
            {
                if (e.Category != UserPreferenceCategory.General && e.Category != UserPreferenceCategory.VisualStyle) return;
                app.Dispatcher.BeginInvoke(new Action(() =>
                {
                    Apply(app, !WindowsTheme.AppsUseLightTheme);
                    Changed?.Invoke();
                }));
            };
        }

        /// <summary>Rounded corners on Windows 11 and a title-bar frame that matches the palette.</summary>
        public static void ApplyWindowFrame(Window window)
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;
            var dark = IsDark ? 1 : 0;
            Win32.DwmSetWindowAttribute(hwnd, Win32.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
            var round = Win32.DWMWCP_ROUND;
            Win32.DwmSetWindowAttribute(hwnd, Win32.DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));
            if (window.TryFindResource("Sheet.Border.Color") is System.Windows.Media.Color c)
            {
                var colorRef = c.R | (c.G << 8) | (c.B << 16); // COLORREF is 0x00BBGGRR
                Win32.DwmSetWindowAttribute(hwnd, Win32.DWMWA_BORDER_COLOR, ref colorRef, sizeof(int));
            }
        }

        private static void Apply(Application app, bool dark)
        {
            var dictionaries = app.Resources.MergedDictionaries;
            var current = dictionaries.FirstOrDefault(d => d.Source == LightUri || d.Source == DarkUri);
            var next = new ResourceDictionary { Source = dark ? DarkUri : LightUri };
            if (current != null) dictionaries[dictionaries.IndexOf(current)] = next;
            else dictionaries.Insert(0, next);
            IsDark = dark;
            foreach (Window w in app.Windows) ApplyWindowFrame(w);
        }
    }
}
