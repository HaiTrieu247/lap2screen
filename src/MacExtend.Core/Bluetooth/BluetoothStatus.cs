namespace MacExtend.Core.Bluetooth
{
    /// <summary>The laptop's Bluetooth keyboard and mouse service.</summary>
    public enum PeripheralState
    {
        /// <summary>Not running, and the last check found nothing wrong.</summary>
        Idle,
        Starting,
        /// <summary>Advertising: devices can pair and connect.</summary>
        Ready,
        NoAdapter,
        /// <summary>The adapter cannot take the LE peripheral role, so it cannot act as a keyboard.</summary>
        NoPeripheral,
        RadioOff,
        /// <summary>Disabled by policy or by the user in Windows privacy settings.</summary>
        Blocked,
        Failed,
    }

    /// <summary>What the Bluetooth control row in the app shows.</summary>
    public enum BluetoothState
    {
        Off,
        WaitingForMirroring,
        Starting,
        NoAdapter,
        NoPeripheral,
        RadioOff,
        Blocked,
        Failed,
        WaitingForDevice,
        /// <summary>Several devices are connected and none of them matches the one mirroring.</summary>
        ChooseDevice,
        Connected,
        Controlling,
    }

    public static class BluetoothStatus
    {
        /// <param name="running">The service should be up: a device mirrors, or did a moment ago.</param>
        /// <param name="target">Name of the device that would be controlled, null if none is chosen.</param>
        public static BluetoothState Derive(bool enabled, bool running, PeripheralState peripheral, int connected,
                                            string target, bool controlling)
        {
            if (!enabled) return BluetoothState.Off;
            switch (peripheral)
            {
                case PeripheralState.NoAdapter: return BluetoothState.NoAdapter;
                case PeripheralState.NoPeripheral: return BluetoothState.NoPeripheral;
                case PeripheralState.RadioOff: return BluetoothState.RadioOff;
                case PeripheralState.Blocked: return BluetoothState.Blocked;
                case PeripheralState.Failed: return BluetoothState.Failed;
            }
            if (!running) return BluetoothState.WaitingForMirroring;
            if (peripheral != PeripheralState.Ready) return BluetoothState.Starting;
            if (controlling) return BluetoothState.Controlling;
            if (connected == 0) return BluetoothState.WaitingForDevice;
            return target == null ? BluetoothState.ChooseDevice : BluetoothState.Connected;
        }

        public static string Describe(BluetoothState state, string target, string failure)
        {
            switch (state)
            {
                case BluetoothState.Off: return "Đang tắt.";
                case BluetoothState.WaitingForMirroring: return "Sẵn sàng. Bluetooth bật khi có thiết bị phản chiếu.";
                case BluetoothState.Starting: return "Đang bật Bluetooth…";
                case BluetoothState.NoAdapter: return "Không tìm thấy Bluetooth trên máy này.";
                case BluetoothState.NoPeripheral: return "Bluetooth của máy này không đóng vai bàn phím và chuột được.";
                case BluetoothState.RadioOff: return "Bluetooth của Windows đang tắt. Bật trong Cài đặt > Bluetooth và thiết bị.";
                case BluetoothState.Blocked: return "Windows đang chặn ứng dụng dùng Bluetooth (chính sách hoặc quyền riêng tư).";
                case BluetoothState.Failed: return "Không bật được Bluetooth: " + failure;
                case BluetoothState.WaitingForDevice: return "Chờ thiết bị kết nối Bluetooth.";
                case BluetoothState.ChooseDevice: return "Có nhiều thiết bị đang kết nối. Chọn thiết bị để điều khiển.";
                case BluetoothState.Connected: return "Đã kết nối " + target + ". Bấm vào hình để điều khiển.";
                case BluetoothState.Controlling: return "Đang điều khiển " + target + ". Nhấn Ctrl+Alt+Q để trả lại Windows.";
                default: return state.ToString();
            }
        }
    }
}
