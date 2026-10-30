using System;
using System.Collections.Generic;
using System.Linq;

namespace MacExtend.Core.CoreDevice
{
    /// <summary>Reads the stdout of the short pymobiledevice3 commands the app runs; all of them print with print_json.</summary>
    public static class DeviceQueries
    {
        /// <summary>`usbmux list --usb --simple`: a JSON array of UDIDs. Anything that is not a valid UDID is dropped.</summary>
        public static IReadOnlyList<string> ParseUdids(string stdout)
        {
            if (!MiniJson.TryParse((stdout ?? "").Trim(), out var value) || !(value is List<object> list)) return new string[0];
            return list.OfType<string>().Where(CoreDeviceOptions.IsValidUdid).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>`amfi developer-mode-status`: JSON true or false; null when the output is neither.</summary>
        public static bool? ParseBool(string stdout)
        {
            if (!MiniJson.TryParse((stdout ?? "").Trim(), out var value)) return null;
            return value is bool b ? b : (bool?)null;
        }

        /// <summary>
        /// `mounter list`: the image mounter's entries, one object per mounted image. The developer disk image is the one
        /// whose DiskImageType is "Developer" (below iOS 17) or "Personalized" (iOS 17 and later, the cryptex one of iOS 27
        /// included): pymobiledevice3's own is_image_mounted asks the same of the same list. null when the output is not
        /// such a list.
        /// </summary>
        public static bool? ParseDeveloperImageMounted(string stdout)
        {
            if (!MiniJson.TryParse((stdout ?? "").Trim(), out var value) || !(value is List<object> list)) return null;
            return list.OfType<Dictionary<string, object>>().Any(image =>
                image.TryGetValue("DiskImageType", out var type) && (type as string == "Developer" || type as string == "Personalized"));
        }

        /// <summary>
        /// Whether the device trusts this laptop, from `amfi developer-mode-status`, which pairs first (the Trust dialog)
        /// and waits for the answer: answered means trusted; a trust problem, a locked device or no answer before the
        /// deadline means not trusted; anything else (no device, an odd error) is unknown.
        /// </summary>
        public static bool? Trusted(CommandResult developerModeStatus)
        {
            if (developerModeStatus == null) return null;
            if (developerModeStatus.Succeeded) return true;
            if (developerModeStatus.TimedOut) return false;
            switch (developerModeStatus.Error)
            {
                case CliError.NeedsTrust:
                case CliError.TrustDenied:
                case CliError.DeviceLocked:
                    return false;
                default:
                    return null;
            }
        }
    }
}
