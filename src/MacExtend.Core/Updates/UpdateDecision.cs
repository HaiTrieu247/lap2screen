using System;

namespace MacExtend.Core.Updates
{
    public sealed class UpdateDecision
    {
        private UpdateDecision(bool available, bool needsRuntime, bool needsCoreDevice, long downloadBytes)
        {
            Available = available;
            NeedsRuntime = needsRuntime;
            NeedsCoreDevice = needsCoreDevice;
            DownloadBytes = downloadBytes;
        }

        public bool Available { get; }
        /// <summary>The installed uxplay/GStreamer runtime differs from the release's and must be replaced too.</summary>
        public bool NeedsRuntime { get; }
        /// <summary>The CoreDevice runtime is installed and differs from the release's.</summary>
        public bool NeedsCoreDevice { get; }
        public long DownloadBytes { get; }

        public static UpdateDecision For(Version current, string installedRuntimeId, UpdateManifest manifest, string installedCoreDeviceId = null)
        {
            if (manifest == null || manifest.Version <= AppVersion.Normalize(current)) return new UpdateDecision(false, false, false, 0);
            var runtime = manifest.Runtime != null && !string.Equals(installedRuntimeId, manifest.RuntimeId, StringComparison.Ordinal);
            // optional: never downloaded for a machine that did not install it
            var coreDevice = manifest.CoreDevice != null && !string.IsNullOrEmpty(installedCoreDeviceId)
                             && !string.Equals(installedCoreDeviceId, manifest.CoreDeviceId, StringComparison.Ordinal);
            var bytes = manifest.App.Size + (runtime ? manifest.Runtime.Size : 0) + (coreDevice ? manifest.CoreDevice.Size : 0);
            return new UpdateDecision(true, runtime, coreDevice, bytes);
        }
    }
}
