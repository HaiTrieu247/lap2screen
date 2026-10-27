using System;
using System.IO;

namespace MacExtend
{
    /// <summary>Where MacExtend finds the bundled uxplay runtime and keeps its own data.</summary>
    internal static class AppPaths
    {
        public static string InstallDir => AppDomain.CurrentDomain.BaseDirectory;

        public static string UxPlayRoot => Path.Combine(InstallDir, "uxplay");
        public static string UxPlayBin => Path.Combine(UxPlayRoot, "bin");
        public static string UxPlayExe => Path.Combine(UxPlayBin, "uxplay.exe");
        public static string PluginDir => Path.Combine(UxPlayRoot, "lib", "gstreamer-1.0");
        public static string PluginScanner => Path.Combine(UxPlayRoot, "libexec", "gstreamer-1.0", "gst-plugin-scanner.exe");
        public static string UxPlayVersionFile => Path.Combine(UxPlayRoot, "VERSION.txt");

        /// <summary>Roaming: settings and the pairing identity that the Mac remembers.</summary>
        public static string DataDir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MacExtend");

        /// <summary>Local: logs and caches.</summary>
        public static string LocalDir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MacExtend");

        public static string SettingsFile => Path.Combine(DataDir, "settings.ini");
        public static string KeyFile => Path.Combine(DataDir, "uxplay.pem");
        public static string RegisterFile => Path.Combine(DataDir, "uxplay.register");
        public static string LogDir => Path.Combine(LocalDir, "logs");
        public static string LogFile => Path.Combine(LogDir, "uxplay.log");
        public static string GstRegistry => Path.Combine(LocalDir, "gst-registry.bin");

        public static void EnsureDirectories()
        {
            Directory.CreateDirectory(DataDir);
            Directory.CreateDirectory(LogDir);
        }
    }
}
