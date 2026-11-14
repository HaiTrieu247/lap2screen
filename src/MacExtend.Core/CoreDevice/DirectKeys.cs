using MacExtend.Core.Bluetooth;

namespace MacExtend.Core.CoreDevice
{
    /// <summary>
    /// The keys typed on the AirPlay picture as the HID keyboard usages serve-web's /key takes. The picture's window gets
    /// WM_KEYDOWN, whose Shift, Ctrl and Alt are the generic codes; the scan code or the extended flag tells the side.
    /// Ctrl is sent as Command, the key iOS shortcuts use, so Ctrl+C, Ctrl+V and Ctrl+Z do what they do on Windows; Alt
    /// is Option.
    /// </summary>
    public static class DirectKeys
    {
        public static bool TryGetUsage(int virtualKey, uint scanCode, bool extended, out int usage)
        {
            switch (virtualKey)
            {
                case 0x10: usage = scanCode == 0x36 ? 0xE5 : 0xE1; return true;
                case 0xA0: usage = 0xE1; return true;
                case 0xA1: usage = 0xE5; return true;
                case 0x11: usage = extended ? 0xE7 : 0xE3; return true;
                case 0xA2: usage = 0xE3; return true;
                case 0xA3: usage = 0xE7; return true;
                case 0x12: usage = extended ? 0xE6 : 0xE2; return true;
                case 0xA4: usage = 0xE2; return true;
                case 0xA5: usage = 0xE6; return true;
            }
            if (VirtualKeyMap.TryGetUsage(virtualKey, scanCode, extended, out var b))
            {
                usage = b;
                return true;
            }
            usage = 0;
            return false;
        }
    }
}
