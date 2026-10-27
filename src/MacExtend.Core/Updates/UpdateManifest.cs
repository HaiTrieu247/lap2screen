using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace MacExtend.Core.Updates
{
    /// <summary>One downloadable zip of a release.</summary>
    public sealed class UpdatePackage
    {
        public UpdatePackage(string file, string sha256, long size)
        {
            File = file;
            Sha256 = sha256;
            Size = size;
        }

        /// <summary>Asset name inside the release (no folders).</summary>
        public string File { get; }
        /// <summary>Lowercase hex SHA-256 of the zip.</summary>
        public string Sha256 { get; }
        public long Size { get; }
    }

    /// <summary>
    /// update.ini published with every release: which version it is, the app zip (always needed)
    /// and the runtime zip (uxplay + GStreamer, needed only when its fingerprint changed).
    /// </summary>
    public sealed class UpdateManifest
    {
        public UpdateManifest(Version version, UpdatePackage app, UpdatePackage runtime, string runtimeId)
        {
            Version = AppVersion.Normalize(version);
            App = app;
            Runtime = runtime;
            RuntimeId = runtime == null ? null : runtimeId;
        }

        public Version Version { get; }
        public UpdatePackage App { get; }
        public UpdatePackage Runtime { get; }
        public string RuntimeId { get; }

        /// <returns>null when the text is not a usable manifest (for example a GitHub error page).</returns>
        public static UpdateManifest Parse(string text)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var raw in (text ?? "").Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                var eq = line.IndexOf('=');
                if (eq <= 0) continue;
                values[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
            }

            if (!values.TryGetValue("version", out var v) || !AppVersion.TryParse(v, out var version)) return null;
            var app = Package(values, "app");
            if (app == null) return null;
            var runtime = Package(values, "runtime");
            values.TryGetValue("runtime.id", out var runtimeId);
            if (string.IsNullOrEmpty(runtimeId)) runtime = null;
            return new UpdateManifest(version, app, runtime, runtimeId);
        }

        public static string Serialize(UpdateManifest m)
        {
            var sb = new StringBuilder("# MacExtend update manifest\n");
            sb.Append("version=").Append(m.Version.ToString(3)).Append('\n');
            Write(sb, "app", m.App);
            if (m.Runtime != null)
            {
                sb.Append("runtime.id=").Append(m.RuntimeId).Append('\n');
                Write(sb, "runtime", m.Runtime);
            }
            return sb.ToString();
        }

        private static UpdatePackage Package(Dictionary<string, string> values, string prefix)
        {
            if (!values.TryGetValue(prefix + ".file", out var file) || !IsPlainFileName(file)) return null;
            if (!values.TryGetValue(prefix + ".sha256", out var hash) || !IsSha256(hash)) return null;
            values.TryGetValue(prefix + ".size", out var sizeText);
            long.TryParse(sizeText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var size);
            return new UpdatePackage(file, hash.ToLowerInvariant(), Math.Max(0, size));
        }

        private static void Write(StringBuilder sb, string prefix, UpdatePackage p)
        {
            sb.Append(prefix).Append(".file=").Append(p.File).Append('\n');
            sb.Append(prefix).Append(".sha256=").Append(p.Sha256).Append('\n');
            sb.Append(prefix).Append(".size=").Append(p.Size.ToString(CultureInfo.InvariantCulture)).Append('\n');
        }

        // asset names become part of a URL and a local path: no folders, no parent references
        private static bool IsPlainFileName(string name) =>
            name.Length > 0 && name.IndexOfAny(new[] { '/', '\\', ':' }) < 0 && name != "." && !name.Contains("..");

        private static bool IsSha256(string hash)
        {
            if (hash.Length != 64) return false;
            foreach (var c in hash)
            {
                var hex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
                if (!hex) return false;
            }
            return true;
        }
    }
}
