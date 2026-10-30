using System;
using System.Globalization;
using MacExtend.Core.Updates;

namespace MacExtend.Core.CoreDevice
{
    /// <summary>
    /// The CoreDevice runtime zip this exact app build was made with. CI embeds it as assembly metadata, so the app
    /// can check a zip it downloaded, or one the user picked from a CI artifact, without asking the network. Local
    /// builds have none.
    /// </summary>
    public sealed class RuntimePackage
    {
        private RuntimePackage(string id, string file, string sha256, long size)
        {
            Id = id;
            File = file;
            Sha256 = sha256;
            Size = size;
        }

        public string Id { get; }
        public string File { get; }
        /// <summary>Lowercase hex.</summary>
        public string Sha256 { get; }
        public long Size { get; }

        public static RuntimePackage FromMetadata(Func<string, string> get)
        {
            var id = get("CoreDeviceRuntimeId");
            var file = get("CoreDeviceRuntimeFile");
            var sha = get("CoreDeviceRuntimeSha256");
            var sizeText = get("CoreDeviceRuntimeSize");
            if (string.IsNullOrWhiteSpace(id) || file == null || !UpdateManifest.IsPlainFileName(file) || sha == null || !UpdateManifest.IsSha256(sha)) return null;
            if (!long.TryParse(sizeText, NumberStyles.None, CultureInfo.InvariantCulture, out var size) || size <= 0) return null;
            return new RuntimePackage(id.Trim(), file, sha.ToLowerInvariant(), size);
        }

        public bool Matches(string sha256) => string.Equals(Sha256, sha256, StringComparison.OrdinalIgnoreCase);
    }
}
