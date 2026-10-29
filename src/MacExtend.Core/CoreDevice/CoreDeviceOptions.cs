using System;
using System.Text.RegularExpressions;

namespace MacExtend.Core.CoreDevice
{
    /// <summary>How serve-web reaches the device's iOS 17+ tunnel. Exactly one is passed to the child.</summary>
    public enum CoreDeviceTransport
    {
        /// <summary><c>--userspace</c>: in-process tunnel, no admin rights, iOS 17.4+ over USB.</summary>
        Userspace,

        /// <summary><c>--tunnel UDID</c>: a tunneld the user started with admin rights (needed for iOS 17.0-17.3.1).</summary>
        Tunnel,
    }

    /// <summary>One serve-web session of the CoreDevice probe: which interpreter, which device, which port.</summary>
    public sealed class CoreDeviceOptions
    {
        /// <summary>The pymobiledevice3 release the probe was checked against (docs/research/2026-10-29-coredevice-probe.md).</summary>
        public const string RequiredVersion = "11.47.0";

        public const int DefaultHttpPort = 8080;
        public const int MinHttpPort = 1024;
        public const int MaxHttpPort = 65535;

        // 40 hex digits (older devices) or 8-16 hex digits with a dash (A12 and later)
        private static readonly Regex UdidPattern = new Regex(@"\A(?:[0-9A-Fa-f]{40}|[0-9A-Fa-f]{8}-[0-9A-Fa-f]{16})\z", RegexOptions.CultureInvariant);

        public CoreDeviceOptions(string pythonPath, string udid, CoreDeviceTransport transport, int httpPort)
        {
            if (!IsWindowsAbsolutePath(pythonPath))
            {
                throw new ArgumentException("the Python interpreter must be given as an absolute Windows path (for example C:\\...\\.venv\\Scripts\\python.exe)", nameof(pythonPath));
            }
            if (!IsValidUdid(udid))
            {
                throw new ArgumentException("the device UDID must be 40 hex digits or 8-16 hex digits joined by a dash", nameof(udid));
            }
            if (transport != CoreDeviceTransport.Userspace && transport != CoreDeviceTransport.Tunnel)
            {
                throw new ArgumentException("the transport must be userspace or tunnel", nameof(transport));
            }
            if (httpPort < MinHttpPort || httpPort > MaxHttpPort)
            {
                throw new ArgumentException($"the HTTP port must be between {MinHttpPort} and {MaxHttpPort}", nameof(httpPort));
            }
            PythonPath = pythonPath;
            Udid = udid;
            Transport = transport;
            HttpPort = httpPort;
        }

        public string PythonPath { get; }
        public string Udid { get; }
        public CoreDeviceTransport Transport { get; }
        public int HttpPort { get; }

        public string ViewerUrl => $"http://{CoreDeviceArguments.BindAddress}:{HttpPort}/";

        /// <summary>
        /// A drive-rooted (C:\ or C:/) or UNC path, checked by hand because Path.IsPathRooted follows the host OS
        /// and the tests run on macOS too.
        /// </summary>
        public static bool IsWindowsAbsolutePath(string path)
        {
            if (string.IsNullOrEmpty(path) || path.IndexOfAny(new[] { '"', '\0', '\r', '\n', '<', '>', '|', '*', '?' }) >= 0) return false;
            if (path.Length >= 3 && IsAsciiLetter(path[0]) && path[1] == ':' && (path[2] == '\\' || path[2] == '/')) return path.IndexOf(':', 2) < 0;
            if (path.StartsWith(@"\\", StringComparison.Ordinal))
            {
                var parts = path.Substring(2).Split('\\');
                return parts.Length >= 3 && parts[0].Length > 0 && parts[1].Length > 0 && path.IndexOf(':') < 0;
            }
            return false;
        }

        public static bool IsValidUdid(string udid) => udid != null && UdidPattern.IsMatch(udid);

        public static bool TryParseTransport(string text, out CoreDeviceTransport transport)
        {
            switch ((text ?? "").ToLowerInvariant())
            {
                case "userspace":
                    transport = CoreDeviceTransport.Userspace;
                    return true;
                case "tunnel":
                    transport = CoreDeviceTransport.Tunnel;
                    return true;
                default:
                    transport = CoreDeviceTransport.Userspace;
                    return false;
            }
        }

        private static bool IsAsciiLetter(char c) => (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z');
    }
}
