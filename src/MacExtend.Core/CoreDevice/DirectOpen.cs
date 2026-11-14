using System;
using System.Collections.Generic;
using System.Linq;

namespace MacExtend.Core.CoreDevice
{
    public enum DirectOpenStep
    {
        /// <summary>The setup window instead: nothing could reach a device yet.</summary>
        Setup,
        /// <summary>No session can start now; <see cref="DirectOpenPlan.Failure"/> says why.</summary>
        Problem,
        /// <summary>Several devices: the user picks one of <see cref="DirectOpenPlan.Choices"/>.</summary>
        Pick,
        /// <summary>A session to <see cref="DirectOpenPlan.Udid"/> over <see cref="DirectOpenPlan.Route"/>.</summary>
        Open,
    }

    /// <summary>A device the picker offers, with the route a session to it would take.</summary>
    public sealed class DeviceChoice
    {
        public DeviceChoice(string udid, CoreDeviceRoute route)
        {
            Udid = udid ?? throw new ArgumentNullException(nameof(udid));
            Route = route;
        }

        public string Udid { get; }
        public CoreDeviceRoute Route { get; }

        /// <summary>
        /// A device name would take a lockdown session per device; the last six characters of the UDID are enough to
        /// tell two devices apart.
        /// </summary>
        public string Name => "Thiết bị …" + (Udid.Length > 6 ? Udid.Substring(Udid.Length - 6) : Udid);

        public string RouteText => CoreDeviceMessages.Route(Route);
    }

    /// <summary>What a direct-control session starts on next. Compared by what it would do, so two plans can be told apart.</summary>
    public sealed class DirectOpenPlan : IEquatable<DirectOpenPlan>
    {
        public static readonly DirectOpenPlan Setup = new DirectOpenPlan(DirectOpenStep.Setup, SessionFailure.None, null, CoreDeviceRoute.None, new DeviceChoice[0]);

        private DirectOpenPlan(DirectOpenStep step, SessionFailure failure, string udid, CoreDeviceRoute route, IReadOnlyList<DeviceChoice> choices)
        {
            Step = step;
            Failure = failure;
            Udid = udid;
            Route = route;
            Choices = choices;
        }

        public static DirectOpenPlan Problem(SessionFailure failure) =>
            new DirectOpenPlan(DirectOpenStep.Problem, failure, null, CoreDeviceRoute.None, new DeviceChoice[0]);

        public static DirectOpenPlan Pick(IReadOnlyList<DeviceChoice> choices) =>
            new DirectOpenPlan(DirectOpenStep.Pick, SessionFailure.None, null, CoreDeviceRoute.None, choices ?? throw new ArgumentNullException(nameof(choices)));

        public static DirectOpenPlan Open(string udid, CoreDeviceRoute route) =>
            new DirectOpenPlan(DirectOpenStep.Open, SessionFailure.None, udid ?? throw new ArgumentNullException(nameof(udid)), route, new DeviceChoice[0]);

        public DirectOpenStep Step { get; }
        /// <summary>None unless <see cref="Step"/> is Problem.</summary>
        public SessionFailure Failure { get; }
        public string Udid { get; }
        public CoreDeviceRoute Route { get; }
        /// <summary>Cable devices first; empty unless <see cref="Step"/> is Pick.</summary>
        public IReadOnlyList<DeviceChoice> Choices { get; }

        public bool Equals(DirectOpenPlan other) =>
            other != null && Step == other.Step && Failure == other.Failure && Route == other.Route
            && string.Equals(Udid, other.Udid, StringComparison.OrdinalIgnoreCase)
            && Choices.Select(c => c.Udid.ToUpperInvariant() + "/" + c.Route).SequenceEqual(other.Choices.Select(c => c.Udid.ToUpperInvariant() + "/" + c.Route));

        public override bool Equals(object obj) => Equals(obj as DirectOpenPlan);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = (int)Step;
                hash = hash * 31 + (int)Failure;
                hash = hash * 31 + (int)Route;
                return hash * 31 + (Udid == null ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode(Udid));
            }
        }
    }

    /// <summary>
    /// The devices a direct-control session can start on: the setup window while nothing could work yet; otherwise the
    /// only device, or the user's pick among several, and its route, the cable first.
    /// </summary>
    public static class DirectOpen
    {
        /// <summary>The current runtime is missing, or neither the cable (no Apple service) nor Wi-Fi (no device set up for it) can reach a device.</summary>
        public static bool NeedsSetupFirst(bool runtimeCurrent, bool appleServiceReachable, int wifiDeviceCount) =>
            !runtimeCurrent || (!appleServiceReachable && wifiDeviceCount == 0);

        /// <param name="usb">The devices on a cable.</param>
        /// <param name="wifi">The devices set up for Wi-Fi whose pairing record is there.</param>
        /// <param name="windowsSetupDone">Windows' part of Wi-Fi (the firewall rules, the task) is in place.</param>
        /// <param name="lastUsed">The device of the last session; kept for caller compatibility, never bypasses the picker.</param>
        public static DirectOpenPlan Plan(IReadOnlyList<string> usb, IReadOnlyList<string> wifi, bool windowsSetupDone, string lastUsed)
        {
            usb = usb ?? new string[0];
            wifi = wifi ?? new string[0];
            var wifiAvailable = wifi.Count > 0 && windowsSetupDone;
            var candidates = TransportChoice.Candidates(usb, wifi, wifiAvailable);
            if (candidates.Count == 1)
            {
                var udid = candidates[0];
                return DirectOpenPlan.Open(udid, TransportChoice.Choose(udid, usb, wifi, wifiAvailable));
            }
            if (candidates.Count > 1)
            {
                return DirectOpenPlan.Pick(candidates.Select(u => new DeviceChoice(u, TransportChoice.Choose(u, usb, wifi, wifiAvailable))).ToList());
            }
            // N12: a device set up for Wi-Fi, none on the cable, and Windows' part of Wi-Fi is missing: offer to set it up again
            return DirectOpenPlan.Problem(wifi.Count > 0 && !windowsSetupDone ? SessionFailure.WindowsSetupMissing : SessionFailure.DeviceNotFound);
        }

        /// <summary>
        /// Whether Windows' part of Wi-Fi could change the plan. It is read with PowerShell (seconds), only when it
        /// changes the available devices or their routes.
        /// </summary>
        public static bool NeedsWindowsCheck(IReadOnlyList<string> usb, IReadOnlyList<string> wifi, string lastUsed) =>
            wifi != null && wifi.Count > 0 && !Plan(usb, wifi, true, lastUsed).Equals(Plan(usb, wifi, false, lastUsed));
    }
}
