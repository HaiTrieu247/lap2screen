// Adapted from windows-ble-hid by Abhishek Raj, MIT License, commit b33a004
// (https://github.com/abhishek-raj/windows-ble-hid). See licenses/windows-ble-hid-LICENSE.txt.

namespace MacExtend.Core.Bluetooth
{
    /// <summary>Windows virtual-key codes (as a low-level keyboard hook reports them) to HID keyboard usages.</summary>
    public static class VirtualKeyMap
    {
        /// <summary>
        /// Resolves the physical key. A real keyboard reports the key and lets the host apply its own Num Lock,
        /// while Windows has already applied the local Num Lock to the virtual-key code, so the numpad is read
        /// from the scan code.
        /// </summary>
        public static bool TryGetUsage(int virtualKey, uint scanCode, bool extended, out byte usage)
        {
            if (TryGetNumpadUsage(scanCode, extended, out usage)) return true;

            // Keypad Enter shares VK_RETURN with the main Enter; only the extended flag tells them apart
            if (virtualKey == 0x0D && extended)
            {
                usage = 0x58;
                return true;
            }

            return TryGetUsage(virtualKey, out usage);
        }

        /// <summary>The modifier bit of a left/right-specific modifier key, or None.</summary>
        public static KeyModifiers ModifierOf(int virtualKey)
        {
            switch (virtualKey)
            {
                case 0xA0: return KeyModifiers.LeftShift;
                case 0xA1: return KeyModifiers.RightShift;
                case 0xA2: return KeyModifiers.LeftControl;
                case 0xA3: return KeyModifiers.RightControl;
                case 0xA4: return KeyModifiers.LeftAlt;
                case 0xA5: return KeyModifiers.RightAlt;
                case 0x5B: return KeyModifiers.LeftGui;
                case 0x5C: return KeyModifiers.RightGui;
                default: return KeyModifiers.None;
            }
        }

        /// <summary>
        /// The numpad keys whose virtual-key code changes with Num Lock. The navigation cluster shares their scan
        /// codes and is told apart only by the extended flag.
        /// </summary>
        private static bool TryGetNumpadUsage(uint scanCode, bool extended, out byte usage)
        {
            usage = 0;
            if (extended) return false;
            switch (scanCode)
            {
                case 0x47: usage = 0x5F; break; // Keypad 7
                case 0x48: usage = 0x60; break; // Keypad 8
                case 0x49: usage = 0x61; break; // Keypad 9
                case 0x4B: usage = 0x5C; break; // Keypad 4
                case 0x4C: usage = 0x5D; break; // Keypad 5
                case 0x4D: usage = 0x5E; break; // Keypad 6
                case 0x4F: usage = 0x59; break; // Keypad 1
                case 0x50: usage = 0x5A; break; // Keypad 2
                case 0x51: usage = 0x5B; break; // Keypad 3
                case 0x52: usage = 0x62; break; // Keypad 0
                case 0x53: usage = 0x63; break; // Keypad .
            }
            return usage != 0;
        }

        private static bool TryGetUsage(int virtualKey, out byte usage)
        {
            if (virtualKey >= 0x41 && virtualKey <= 0x5A) usage = (byte)(0x04 + virtualKey - 0x41);      // A-Z
            else if (virtualKey >= 0x31 && virtualKey <= 0x39) usage = (byte)(0x1E + virtualKey - 0x31); // 1-9
            else if (virtualKey >= 0x70 && virtualKey <= 0x7B) usage = (byte)(0x3A + virtualKey - 0x70); // F1-F12
            else if (virtualKey >= 0x61 && virtualKey <= 0x69) usage = (byte)(0x59 + virtualKey - 0x61); // Numpad 1-9
            else usage = Single(virtualKey);
            return usage != 0;
        }

        private static byte Single(int virtualKey)
        {
            switch (virtualKey)
            {
                case 0x30: return 0x27; // 0
                case 0x0D: return 0x28; // Enter
                case 0x1B: return 0x29; // Escape
                case 0x08: return 0x2A; // Backspace
                case 0x09: return 0x2B; // Tab
                case 0x20: return 0x2C; // Space
                case 0xBD: return 0x2D; // -
                case 0xBB: return 0x2E; // =
                case 0xDB: return 0x2F; // [
                case 0xDD: return 0x30; // ]
                case 0xDC: return 0x31; // \
                case 0xBA: return 0x33; // ;
                case 0xDE: return 0x34; // '
                case 0xC0: return 0x35; // `
                case 0xBC: return 0x36; // ,
                case 0xBE: return 0x37; // .
                case 0xBF: return 0x38; // /
                case 0x14: return 0x39; // Caps Lock
                case 0x2C: return 0x46; // Print Screen
                case 0x91: return 0x47; // Scroll Lock
                case 0x13: return 0x48; // Pause
                case 0x2D: return 0x49; // Insert
                case 0x24: return 0x4A; // Home
                case 0x21: return 0x4B; // Page Up
                case 0x2E: return 0x4C; // Delete
                case 0x23: return 0x4D; // End
                case 0x22: return 0x4E; // Page Down
                case 0x27: return 0x4F; // Right
                case 0x25: return 0x50; // Left
                case 0x28: return 0x51; // Down
                case 0x26: return 0x52; // Up
                case 0x90: return 0x53; // Num Lock
                case 0x6F: return 0x54; // Numpad /
                case 0x6A: return 0x55; // Numpad *
                case 0x6D: return 0x56; // Numpad -
                case 0x6B: return 0x57; // Numpad +
                case 0x60: return 0x62; // Numpad 0
                case 0x6E: return 0x63; // Numpad .
                case 0x5D: return 0x65; // Application (menu key)
                default: return 0;
            }
        }
    }
}
