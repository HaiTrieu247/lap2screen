using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace MacExtend.Core.CoreDevice
{
    public enum CliError
    {
        None,
        NotConnected,
        /// <summary>The Trust dialog is waiting on the device.</summary>
        NeedsTrust,
        TrustDenied,
        /// <summary>Locked with a passcode: unlock and retry.</summary>
        DeviceLocked,
        DeveloperModeOff,
        /// <summary>No Apple Mobile Device Service (usbmuxd) to talk to.</summary>
        NoUsbmux,
        /// <summary>auto-mount could not reach Apple to fetch the Developer Disk Image.</summary>
        NoInternet,
        ServiceUnavailable,
        Other,
    }

    /// <summary>
    /// Classifies the ERROR lines pymobiledevice3's CLI writes for a failed command,
    /// "&lt;date&gt; &lt;time&gt; &lt;host&gt; __main__[&lt;pid&gt;] ERROR &lt;message&gt;". The message is never kept.
    /// </summary>
    public static class CliLog
    {
        private static readonly Regex Ansi = new Regex(@"\x1b\[[0-9;]*m", RegexOptions.CultureInvariant);

        private static readonly Regex Error = new Regex(LogPrefix.Build(LogPrefix.Main, "ERROR") + "(.*)\\z", RegexOptions.CultureInvariant);

        public static CliError Parse(string line)
        {
            if (string.IsNullOrEmpty(line)) return CliError.None;
            var m = Error.Match(Ansi.Replace(line, ""));
            return m.Success ? Classify(m.Groups[1].Value) : CliError.None;
        }

        /// <summary>The error to tell the user about: the first specific one, else Other if any error was seen.</summary>
        public static CliError Pick(IEnumerable<CliError> seen)
        {
            var list = (seen ?? Enumerable.Empty<CliError>()).Where(e => e != CliError.None).ToList();
            var specific = list.FirstOrDefault(e => e != CliError.Other);
            if (specific != CliError.None) return specific;
            return list.Count > 0 ? CliError.Other : CliError.None;
        }

        private static CliError Classify(string message)
        {
            bool Has(string s) => message.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0;
            if (Has("PairingDialogResponsePending") || Has("answer the Trust dialog")) return CliError.NeedsTrust;
            if (Has("UserDeniedPairing") || Has("refused to trust")) return CliError.TrustDenied;
            if (Has("PasswordRequired") || Has("password protected")) return CliError.DeviceLocked;
            if (Has("DeveloperModeIsNotEnabled") || Has("Developer Mode is disabled")) return CliError.DeveloperModeOff;
            if (Has("ConnectionFailedToUsbmuxd") || Has("usbmuxd socket")) return CliError.NoUsbmux;
            if (Has("failed to query DeveloperDiskImage versions")) return CliError.NoInternet;
            if (Has("Failed to start service")) return CliError.ServiceUnavailable;
            if (Has("NoDeviceConnected") || Has("Device is not connected") || Has("DeviceNotFound") || Has("No device")) return CliError.NotConnected;
            return CliError.Other;
        }
    }
}
