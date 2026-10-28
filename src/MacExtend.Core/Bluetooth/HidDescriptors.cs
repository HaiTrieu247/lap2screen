// Adapted from windows-ble-hid by Abhishek Raj, MIT License, commit b33a004
// (https://github.com/abhishek-raj/windows-ble-hid). See licenses/windows-ble-hid-LICENSE.txt.

using System;

namespace MacExtend.Core.Bluetooth
{
    /// <summary>Bluetooth SIG UUIDs for HID over GATT and the composite keyboard plus mouse report descriptor.</summary>
    public static class HidDescriptors
    {
        public static Guid Uuid16(ushort id) => new Guid("0000" + id.ToString("x4") + "-0000-1000-8000-00805f9b34fb");

        public static readonly Guid HidService = Uuid16(0x1812);
        public static readonly Guid HidInformation = Uuid16(0x2A4A);
        public static readonly Guid ReportMap = Uuid16(0x2A4B);
        public static readonly Guid HidControlPoint = Uuid16(0x2A4C);
        public static readonly Guid Report = Uuid16(0x2A4D);
        public static readonly Guid ProtocolMode = Uuid16(0x2A4E);
        public static readonly Guid ReportReference = Uuid16(0x2908);
        public static readonly Guid BatteryService = Uuid16(0x180F);
        public static readonly Guid BatteryLevel = Uuid16(0x2A19);

        public const byte KeyboardReportId = 1;
        public const byte MouseReportId = 2;

        public const byte ReportTypeInput = 1;
        public const byte ReportTypeOutput = 2;

        /// <summary>bcdHID 1.11, country code 0, flags RemoteWake | NormallyConnectable.</summary>
        public static readonly byte[] HidInformationValue = { 0x11, 0x01, 0x00, 0x03 };

        /// <summary>Keyboard on report ID 1 (with the LED output report hosts write), mouse on report ID 2.</summary>
        public static readonly byte[] ReportMapValue =
        {
            // keyboard
            0x05, 0x01,       // Usage Page (Generic Desktop)
            0x09, 0x06,       // Usage (Keyboard)
            0xA1, 0x01,       // Collection (Application)
            0x85, KeyboardReportId,
            0x05, 0x07,       //   Usage Page (Keyboard/Keypad)
            0x19, 0xE0,       //   Usage Minimum (Left Control)
            0x29, 0xE7,       //   Usage Maximum (Right GUI)
            0x15, 0x00,       //   Logical Minimum (0)
            0x25, 0x01,       //   Logical Maximum (1)
            0x75, 0x01,       //   Report Size (1)
            0x95, 0x08,       //   Report Count (8)
            0x81, 0x02,       //   Input (Data, Var, Abs): modifier byte
            0x95, 0x01,       //   Report Count (1)
            0x75, 0x08,       //   Report Size (8)
            0x81, 0x01,       //   Input (Const): reserved byte
            // LED block where the boot keyboard descriptor puts it, between the reserved byte and the keys
            0x15, 0x00,       //   Logical Minimum (0)
            0x25, 0x01,       //   Logical Maximum (1)
            0x95, 0x05,       //   Report Count (5)
            0x75, 0x01,       //   Report Size (1)
            0x05, 0x08,       //   Usage Page (LEDs)
            0x19, 0x01,       //   Usage Minimum (Num Lock)
            0x29, 0x05,       //   Usage Maximum (Kana)
            0x91, 0x02,       //   Output (Data, Var, Abs): 5 LED bits
            0x95, 0x01,       //   Report Count (1)
            0x75, 0x03,       //   Report Size (3)
            0x91, 0x01,       //   Output (Const): padding
            0x95, 0x06,       //   Report Count (6)
            0x75, 0x08,       //   Report Size (8)
            0x15, 0x00,       //   Logical Minimum (0)
            0x25, 0x65,       //   Logical Maximum (101)
            0x05, 0x07,       //   Usage Page (Keyboard/Keypad)
            0x19, 0x00,       //   Usage Minimum (0)
            0x29, 0x65,       //   Usage Maximum (101)
            0x81, 0x00,       //   Input (Data, Array): 6 keys
            0xC0,             // End Collection

            // mouse
            0x05, 0x01,       // Usage Page (Generic Desktop)
            0x09, 0x02,       // Usage (Mouse)
            0xA1, 0x01,       // Collection (Application)
            0x85, MouseReportId,
            0x09, 0x01,       //   Usage (Pointer)
            0xA1, 0x00,       //   Collection (Physical)
            0x05, 0x09,       //     Usage Page (Button)
            0x19, 0x01,       //     Usage Minimum (1)
            0x29, 0x03,       //     Usage Maximum (3)
            0x15, 0x00,       //     Logical Minimum (0)
            0x25, 0x01,       //     Logical Maximum (1)
            0x95, 0x03,       //     Report Count (3)
            0x75, 0x01,       //     Report Size (1)
            0x81, 0x02,       //     Input (Data, Var, Abs): buttons
            0x95, 0x01,       //     Report Count (1)
            0x75, 0x05,       //     Report Size (5)
            0x81, 0x01,       //     Input (Const): padding
            0x05, 0x01,       //     Usage Page (Generic Desktop)
            0x09, 0x30,       //     Usage (X)
            0x09, 0x31,       //     Usage (Y)
            0x16, 0x01, 0x80, //     Logical Minimum (-32767)
            0x26, 0xFF, 0x7F, //     Logical Maximum (32767)
            0x75, 0x10,       //     Report Size (16)
            0x95, 0x02,       //     Report Count (2)
            0x81, 0x06,       //     Input (Data, Var, Rel): dx, dy
            0x09, 0x38,       //     Usage (Wheel)
            0x15, 0x81,       //     Logical Minimum (-127)
            0x25, 0x7F,       //     Logical Maximum (127)
            0x75, 0x08,       //     Report Size (8)
            0x95, 0x01,       //     Report Count (1)
            0x81, 0x06,       //     Input (Data, Var, Rel): wheel
            0xC0,             //   End Collection
            0xC0,             // End Collection
        };
    }
}
