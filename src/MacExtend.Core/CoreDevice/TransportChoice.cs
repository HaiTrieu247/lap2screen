using System;
using System.Collections.Generic;
using System.Linq;

namespace MacExtend.Core.CoreDevice
{
    public enum CoreDeviceRoute
    {
        None,
        /// <summary>The cable, through serve-web's in-process userspace tunnel: no administrator rights, no firewall.</summary>
        Usb,
        /// <summary>RemotePairing over Wi-Fi through tunneld, which runs elevated.</summary>
        Wifi,
    }

    public static class TransportChoice
    {
        /// <summary>The cable wins. Wi-Fi only for a device set up for it, and only when Windows' part is in place.</summary>
        public static CoreDeviceRoute Choose(string udid, IEnumerable<string> usbUdids, IEnumerable<string> wifiUdids, bool wifiAvailable)
        {
            if (udid == null) return CoreDeviceRoute.None;
            if (Contains(usbUdids, udid)) return CoreDeviceRoute.Usb;
            if (wifiAvailable && Contains(wifiUdids, udid)) return CoreDeviceRoute.Wifi;
            return CoreDeviceRoute.None;
        }

        /// <summary>Every device a session could use now, cable devices first.</summary>
        public static IReadOnlyList<string> Candidates(IEnumerable<string> usbUdids, IEnumerable<string> wifiUdids, bool wifiAvailable) =>
            (usbUdids ?? Enumerable.Empty<string>())
                .Concat(wifiAvailable ? wifiUdids ?? Enumerable.Empty<string>() : Enumerable.Empty<string>())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

        /// <summary>
        /// The device to open without asking: the last one used if it is a candidate, else the only candidate. null when
        /// the user has to choose (several) or there is none.
        /// </summary>
        public static string PickDevice(IEnumerable<string> usbUdids, IEnumerable<string> wifiUdids, bool wifiAvailable, string lastUsed)
        {
            var candidates = Candidates(usbUdids, wifiUdids, wifiAvailable);
            var last = candidates.FirstOrDefault(u => string.Equals(u, lastUsed, StringComparison.OrdinalIgnoreCase));
            if (last != null) return last;
            return candidates.Count == 1 ? candidates[0] : null;
        }

        private static bool Contains(IEnumerable<string> list, string udid) => list != null && list.Contains(udid, StringComparer.OrdinalIgnoreCase);
    }

    public enum TunnelStep
    {
        UseExisting,
        WaitForExisting,
        StartTunneld,
        /// <summary>The port listens but is not a tunneld listing: another program owns it.</summary>
        PortBusy,
    }

    /// <summary>
    /// A tunneld that already answers on 127.0.0.1:49151 belongs to someone else (the user, or one left from an earlier
    /// run): it is used, never started a second time, and never shut down by this session.
    /// </summary>
    public static class TunnelPlan
    {
        public static TunnelStep First(bool tunneldAnswers, bool hasTunnelForDevice) =>
            hasTunnelForDevice ? TunnelStep.UseExisting : tunneldAnswers ? TunnelStep.WaitForExisting : TunnelStep.StartTunneld;

        public static TunnelStep First(bool tunneldAnswers, bool hasTunnelForDevice, bool portListens)
        {
            var step = First(tunneldAnswers, hasTunnelForDevice);
            return step == TunnelStep.StartTunneld && portListens ? TunnelStep.PortBusy : step;
        }

        /// <summary>Rules a copy without the task added itself are re-added only when its own start timed out.</summary>
        public static bool ReaddRulesAfter(TunnelOutcome outcome, bool startedByUs) => startedByUs && outcome == TunnelOutcome.Timeout;

        public static bool ShutdownAfter(TunnelStep first) => first == TunnelStep.StartTunneld;
    }
}
