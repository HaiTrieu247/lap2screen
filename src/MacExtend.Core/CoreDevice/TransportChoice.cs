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

    public enum ShutdownAction
    {
        SendShutdown,
        Wait,
        EndTask,
        Done,
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

        /// <summary>How long a shutdown of a tunneld this session started may take, counting from when it was asked.</summary>
        public static readonly TimeSpan ShutdownBound = TimeSpan.FromSeconds(30);

        /// <summary>In task mode, how long after the first /shutdown a still listening port makes the task be ended.</summary>
        public static readonly TimeSpan ShutdownGrace = TimeSpan.FromSeconds(10);

        /// <summary>
        /// One poll of stopping a tunneld this session started: it may bind its port only after the stop was asked, so
        /// /shutdown is sent on every poll while the port listens, and the port counts as stopped once it closed after
        /// having listened. Task mode ends the task if the port never came up within the bound or still listens after the grace.
        /// </summary>
        /// <param name="sinceFirstShutdown">Null until a /shutdown was sent.</param>
        public static ShutdownAction NextShutdown(bool seenListening, bool listening, TimeSpan elapsed, TimeSpan? sinceFirstShutdown, bool taskMode)
        {
            if (seenListening && !listening) return ShutdownAction.Done;
            if (listening)
            {
                if (taskMode && sinceFirstShutdown >= ShutdownGrace) return ShutdownAction.EndTask;
                return elapsed >= ShutdownBound ? ShutdownAction.Done : ShutdownAction.SendShutdown;
            }
            if (elapsed < ShutdownBound) return ShutdownAction.Wait;
            return taskMode ? ShutdownAction.EndTask : ShutdownAction.Done;
        }

        public static bool ShutdownAfter(TunnelStep first) => first == TunnelStep.StartTunneld;
    }
}
