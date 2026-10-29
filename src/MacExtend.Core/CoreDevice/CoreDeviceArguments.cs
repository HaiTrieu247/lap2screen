using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace MacExtend.Core.CoreDevice
{
    /// <summary>
    /// The argv and environment of the probe's Python children, checked against pymobiledevice3 11.47.0
    /// (docs/research/2026-10-29-coredevice-probe.md).
    /// </summary>
    public static class CoreDeviceArguments
    {
        public const string BindAddress = "127.0.0.1";
        public const string UdidVariable = "PYMOBILEDEVICE3_UDID";

        private const string UpstreamPrefix = "PYMOBILEDEVICE3_";

        // -I: ignore PYTHON* variables, the user site and the current directory; -X utf8: UTF-8 pipes; -u: unbuffered
        private static readonly string[] InterpreterFlags = { "-I", "-X", "utf8", "-u" };

        /// <summary>
        /// serve-web on loopback over one transport. serve-web has no --udid option: --userspace reads the device
        /// from PYMOBILEDEVICE3_UDID, and --tunnel names it. <paramref name="script"/> replaces "-m pymobiledevice3"
        /// with a stand-in for the smoke tests; everything after it stays the same.
        /// </summary>
        public static IReadOnlyList<string> ServeWeb(CoreDeviceOptions options, string script = null)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            var args = new List<string>(InterpreterFlags);
            if (script == null) args.AddRange(new[] { "-m", "pymobiledevice3" });
            else args.Add(script);
            args.AddRange(new[]
            {
                "developer", "core-device", "display", "serve-web",
                "--bind", BindAddress,
                "--http-port", options.HttpPort.ToString(CultureInfo.InvariantCulture),
                // the device-side audio session is opened either way; this only stops the viewer from asking for
                // /audio.bin, which serve-web cannot decode outside macOS
                "--no-audio",
            });
            if (options.Transport == CoreDeviceTransport.Tunnel) args.AddRange(new[] { "--tunnel", options.Udid });
            else args.Add("--userspace");
            return args;
        }

        /// <summary>Prints the installed pymobiledevice3 version, using the same interpreter flags as serve-web.</summary>
        public static IReadOnlyList<string> VersionCheck() =>
            new List<string>(InterpreterFlags) { "-c", "import importlib.metadata as m; print(m.version('pymobiledevice3'))" };

        public static string CommandLine(string executable, IEnumerable<string> args) =>
            WindowsCommandLine.Join(new[] { executable }.Concat(args));

        /// <summary>
        /// The inherited environment with every PYMOBILEDEVICE3_* variable removed (any of them could pick another
        /// transport or device), then the UDID set. Colors are turned off so status lines parse.
        /// </summary>
        public static IDictionary<string, string> Environment(IDictionary inherited, CoreDeviceOptions options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            var env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (inherited != null)
            {
                foreach (DictionaryEntry entry in inherited)
                {
                    var name = entry.Key as string;
                    var value = entry.Value as string ?? "";
                    if (string.IsNullOrEmpty(name) || name.IndexOf('=', 1) >= 0 || name.IndexOf('\0') >= 0 || value.IndexOf('\0') >= 0) continue;
                    if (name.StartsWith(UpstreamPrefix, StringComparison.OrdinalIgnoreCase)) continue;
                    if (string.Equals(name, "FORCE_COLOR", StringComparison.OrdinalIgnoreCase)) continue;
                    env[name] = value;
                }
            }
            env[UdidVariable] = options.Udid;
            env["NO_COLOR"] = "1";
            return env;
        }

        /// <summary>A CREATE_UNICODE_ENVIRONMENT block: NAME=value entries sorted case-insensitively, then an empty one.</summary>
        public static string EnvironmentBlock(IDictionary<string, string> env)
        {
            if (env == null) throw new ArgumentNullException(nameof(env));
            var sb = new StringBuilder();
            foreach (var pair in env.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
            {
                sb.Append(pair.Key).Append('=').Append(pair.Value).Append('\0');
            }
            if (sb.Length == 0) sb.Append('\0');
            return sb.Append('\0').ToString();
        }
    }
}
