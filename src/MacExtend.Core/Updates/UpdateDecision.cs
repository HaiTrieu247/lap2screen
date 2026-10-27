using System;

namespace MacExtend.Core.Updates
{
    public sealed class UpdateDecision
    {
        private UpdateDecision(bool available, bool needsRuntime, long downloadBytes)
        {
            Available = available;
            NeedsRuntime = needsRuntime;
            DownloadBytes = downloadBytes;
        }

        public bool Available { get; }
        /// <summary>The installed uxplay/GStreamer runtime differs from the release's and must be replaced too.</summary>
        public bool NeedsRuntime { get; }
        public long DownloadBytes { get; }

        public static UpdateDecision For(Version current, string installedRuntimeId, UpdateManifest manifest)
        {
            if (manifest == null || manifest.Version <= AppVersion.Normalize(current)) return new UpdateDecision(false, false, 0);
            var runtime = manifest.Runtime != null && !string.Equals(installedRuntimeId, manifest.RuntimeId, StringComparison.Ordinal);
            return new UpdateDecision(true, runtime, manifest.App.Size + (runtime ? manifest.Runtime.Size : 0));
        }
    }
}
