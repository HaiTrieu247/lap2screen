using System;
using MacExtend.Core.CoreDevice;

namespace MacExtend.Core.Wda
{
    /// <summary>Why WDA control did not start, or stopped.</summary>
    public enum WdaFailure
    {
        None,
        /// <summary>The CoreDevice runtime (Python + pymobiledevice3) is not installed.</summary>
        RuntimeMissing,
        /// <summary>No Apple Mobile Device Service to reach a device on the cable.</summary>
        NoAppleService,
        /// <summary>No iPhone or iPad on the cable.</summary>
        NoDevice,
        NotTrusted,
        Locked,
        DeveloperModeOff,
        /// <summary>auto-mount could not reach Apple for the Developer Disk Image.</summary>
        NoInternet,
        DiskImageFailed,
        /// <summary>No app whose bundle, name or executable says WebDriverAgent.</summary>
        NotInstalled,
        /// <summary>The provisioning profile WDA was signed with has expired (7 days with a free Apple ID).</summary>
        Expired,
        /// <summary>iOS refused to launch WDA: the developer is not trusted, or the signature is not valid any more.</summary>
        UntrustedDeveloper,
        /// <summary>iOS 17.0 to 17.3 has no userspace tunnel over the cable.</summary>
        OldIos,
        /// <summary>The XCUITest runner exited, or never connected back.</summary>
        RunnerFailed,
        /// <summary>WDA did not answer within the start deadline.</summary>
        NotAnswering,
        /// <summary>WDA was working and stopped answering.</summary>
        Lost,
        /// <summary>The cable's port forward to WDA could not be opened.</summary>
        ForwardFailed,
        Other,
    }

    /// <summary>
    /// Classifies lines the XCUITest runner (pymobiledevice3 developer dvt xcuitest) writes, and the short commands'
    /// classified errors, into a <see cref="WdaFailure"/>. Lines carry the UDID and the device's name, so nothing else of
    /// them is ever kept.
    /// </summary>
    public static class WdaLog
    {
        public static WdaFailure Classify(string line)
        {
            if (string.IsNullOrEmpty(line)) return WdaFailure.None;
            bool Has(string s) => line.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0;
            if (Has("has expired") || Has("profile has expired") || Has("certificate has expired")) return WdaFailure.Expired;
            if (Has("explicitly trusted") || Has("untrusted developer") || Has("invalid code signature") || Has("inadequate entitlements")
                || Has("could not be verified")) return WdaFailure.UntrustedDeveloper;
            if (Has("DeveloperModeIsNotEnabled") || Has("Developer Mode is disabled")) return WdaFailure.DeveloperModeOff;
            if (Has("PasswordRequired") || Has("password protected") || Has("device is locked") || Has("Unlock the device")) return WdaFailure.Locked;
            if (Has("needs iOS 17.4") || Has("iOS < 17.4") || Has("CoreDeviceProxy unavailable")) return WdaFailure.OldIos;
            if (Has("PairingDialogResponsePending") || Has("UserDeniedPairing") || Has("NotPaired") || Has("not paired")) return WdaFailure.NotTrusted;
            if (Has("NoDeviceConnected") || Has("DeviceNotFound") || Has("Device is not connected")) return WdaFailure.NoDevice;
            if (Has("XCTestDriverInterface") || Has("runner did not connect") || Has("Test runner exited")) return WdaFailure.RunnerFailed;
            return WdaFailure.None;
        }

        /// <summary>The first specific failure wins over a later generic one.</summary>
        public static WdaFailure Pick(WdaFailure current, WdaFailure next) =>
            current != WdaFailure.None && current != WdaFailure.RunnerFailed && current != WdaFailure.Other ? current
            : next != WdaFailure.None ? next : current;

        /// <summary>A short command's classified error, as the failure WDA control reports.</summary>
        public static WdaFailure FromCommand(CommandResult result, WdaFailure otherwise)
        {
            if (result == null) return otherwise;
            switch (result.Error)
            {
                case CliError.NotConnected: return WdaFailure.NoDevice;
                case CliError.NeedsTrust:
                case CliError.TrustDenied: return WdaFailure.NotTrusted;
                case CliError.DeviceLocked: return WdaFailure.Locked;
                case CliError.DeveloperModeOff: return WdaFailure.DeveloperModeOff;
                case CliError.NoUsbmux: return WdaFailure.NoAppleService;
                case CliError.NoInternet: return WdaFailure.NoInternet;
                default: return result.TimedOut ? WdaFailure.Locked : otherwise;
            }
        }
    }
}
