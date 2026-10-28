// Adapted from windows-ble-hid by Abhishek Raj, MIT License, commit b33a004
// (https://github.com/abhishek-raj/windows-ble-hid). See licenses/windows-ble-hid-LICENSE.txt.

using System;
using System.Collections.Generic;

namespace MacExtend.Core.Bluetooth
{
    [Flags]
    public enum KeyModifiers : byte
    {
        None = 0x00,
        LeftControl = 0x01,
        LeftShift = 0x02,
        LeftAlt = 0x04,
        LeftGui = 0x08,
        RightControl = 0x10,
        RightShift = 0x20,
        RightAlt = 0x40,
        RightGui = 0x80,
    }

    [Flags]
    public enum MouseButtons : byte
    {
        None = 0x00,
        Left = 0x01,
        Right = 0x02,
        Middle = 0x04,
    }

    /// <summary>
    /// HID input report payloads for <see cref="HidDescriptors.ReportMapValue"/>. Over HID over GATT the report ID
    /// travels in the characteristic's Report Reference descriptor, not in the payload.
    /// </summary>
    public static class HidReports
    {
        public const int KeyboardReportLength = 8;
        public const int MouseReportLength = 6;
        public const int MaxKeys = 6;

        /// <summary>Modifier byte, reserved byte, then up to six key usages.</summary>
        public static byte[] Keyboard(KeyModifiers modifiers, IReadOnlyList<byte> usages)
        {
            var report = new byte[KeyboardReportLength];
            report[0] = (byte)modifiers;
            var count = Math.Min(usages.Count, MaxKeys);
            for (var i = 0; i < count; i++) report[2 + i] = usages[i];
            return report;
        }

        public static byte[] KeyboardRelease() => new byte[KeyboardReportLength];

        /// <summary>Buttons, 16-bit relative X and Y (little endian), 8-bit wheel.</summary>
        public static byte[] Mouse(MouseButtons buttons, int dx, int dy, int wheel)
        {
            var x = (short)Clamp(dx, -32767, 32767);
            var y = (short)Clamp(dy, -32767, 32767);
            return new[]
            {
                (byte)buttons,
                (byte)(x & 0xFF), (byte)((x >> 8) & 0xFF),
                (byte)(y & 0xFF), (byte)((y >> 8) & 0xFF),
                unchecked((byte)(sbyte)Clamp(wheel, -127, 127)),
            };
        }

        private static int Clamp(int value, int min, int max) => value < min ? min : value > max ? max : value;
    }
}
