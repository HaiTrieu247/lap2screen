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
    }
}
