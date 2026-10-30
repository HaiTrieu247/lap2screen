using System;
using System.Collections.Generic;
using System.Linq;

namespace MacExtend.Core.CoreDevice
{
    /// <summary>
    /// Whether a cable session's device left usbmux, from repeated device listings. Only an answer that lists the cable's
    /// devices without this one is a miss; <see cref="MissesToLeave"/> misses in a row mean it left. An unknown answer
    /// (null: the service did not answer, or answered oddly) is never a miss and leaves the count as it is.
    /// Not thread-safe: one watcher owns it.
    /// </summary>
    public sealed class UsbPresence
    {
        public const int MissesToLeave = 2;

        private readonly string _udid;
        private int _misses;

        public UsbPresence(string udid)
        {
            _udid = udid ?? throw new ArgumentNullException(nameof(udid));
        }

        /// <summary>Whatever the case, and with or without the dash of an A12-or-later UDID: usbmuxd may drop it.</summary>
        public static bool IsListed(IEnumerable<string> usbUdids, string udid) =>
            usbUdids != null && udid != null && usbUdids.Any(u => SameDevice(u, udid));

        /// <summary>Records one listing (null: unknown); true once the device has been missing <see cref="MissesToLeave"/> listings in a row.</summary>
        public bool Gone(IEnumerable<string> usbUdids)
        {
            if (usbUdids != null) _misses = IsListed(usbUdids, _udid) ? 0 : _misses + 1;
            return _misses >= MissesToLeave;
        }

        private static bool SameDevice(string a, string b) =>
            a != null && string.Equals(a.Replace("-", ""), b.Replace("-", ""), StringComparison.OrdinalIgnoreCase);
    }
}
