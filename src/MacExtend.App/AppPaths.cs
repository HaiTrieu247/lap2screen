using System;
using System.IO;

namespace MacExtend
{
    /// <summary>Where MacExtend finds the bundled uxplay runtime and keeps its own data.</summary>
    internal static class AppPaths
    {
        public static string InstallDir => AppDomain.CurrentDomain.BaseDirectory;
        public static string SelfExe => Path.Combine(InstallDir, "MacExtend.exe");

        public static string UxPlayRoot => Path.Combine(InstallDir, "uxplay");
        public static string UxPlayBin => Path.Combine(UxPlayRoot, "bin");
        public static string UxPlayExe => Path.Combine(UxPlayBin, "uxplay.exe");
        public static string PluginDir => Path.Combine(UxPlayRoot, "lib", "gstreamer-1.0");
        public static string PluginScanner => Path.Combine(UxPlayRoot, "libexec", "gstreamer-1.0", "gst-plugin-scanner.exe");
        public static string UxPlayVersionFile => Path.Combine(UxPlayRoot, "VERSION.txt");
        /// <summary>Fingerprint of the bundled uxplay/GStreamer build, compared with update.ini's runtime.id.</summary>
        public static string RuntimeIdFile => Path.Combine(UxPlayRoot, "RUNTIME_ID");

        /// <summary>Python + pymobiledevice3 for "Điều khiển trực tiếp"; installed by the first setup, never by the installer.</summary>
        public static string CoreDeviceRoot => Path.Combine(InstallDir, "coredevice");
        /// <summary>The interpreter folder: the firewall rules' programs and the Scheduled Task's working directory.</summary>
        public static string CoreDevicePythonDir => Path.Combine(CoreDeviceRoot, "python");
        /// <summary>serve-web and the short pymobiledevice3 commands.</summary>
        public static string CoreDevicePython => Path.Combine(CoreDevicePythonDir, "python.exe");
        /// <summary>tunneld for Wi-Fi: the Scheduled Task's program (or started with UAC by a portable copy).</summary>
        public static string CoreDevicePythonw => Path.Combine(CoreDevicePythonDir, "pythonw.exe");
        public static string CoreDeviceRuntimeIdFile => Path.Combine(CoreDeviceRoot, "RUNTIME_ID");

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
        /// <summary>Bluetooth state changes only; never what was typed.</summary>
        public static string BluetoothLog => Path.Combine(LogDir, "bluetooth.log");
        public static string GstRegistry => Path.Combine(LocalDir, "gst-registry.bin");
        public static string UpdatesDir => Path.Combine(LocalDir, "updates");
        public static string UpdateLog => Path.Combine(LogDir, "update.log");
        /// <summary>Classified session and setup status lines only; never keys, clipboard, full UDIDs or raw Python output.</summary>
        public static string CoreDeviceLog => Path.Combine(LogDir, "coredevice.log");
        /// <summary>pymobiledevice3's own folder (pair records, RemotePairing records, DDI cache): Path.home() on Windows.</summary>
        public static string PymobiledeviceHome =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".pymobiledevice3");

        public static void EnsureDirectories()
        {
            Directory.CreateDirectory(DataDir);
            Directory.CreateDirectory(LogDir);
        }
    }
}
