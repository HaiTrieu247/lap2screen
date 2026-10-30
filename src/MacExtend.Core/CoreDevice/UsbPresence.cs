using System;
using System.Collections.Generic;
using System.Linq;

namespace MacExtend.Core.CoreDevice
{
    /// <summary>
    /// Whether a cable session's device left usbmux, from repeated `usbmux list --usb`. One missing answer is not
    /// enough (the command can fail on its own and then lists nothing); <see cref="MissesToLeave"/> in a row are.
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

        public static bool IsListed(IEnumerable<string> usbUdids, string udid) =>
            usbUdids != null && udid != null && usbUdids.Contains(udid, StringComparer.OrdinalIgnoreCase);

        /// <summary>Records one poll; true once the device has been missing <see cref="MissesToLeave"/> polls in a row.</summary>
        public bool Gone(IEnumerable<string> usbUdids)
        {
            _misses = IsListed(usbUdids, _udid) ? 0 : _misses + 1;
            return _misses >= MissesToLeave;
        }
    }
}
