namespace MacExtend.Core.CoreDevice
{
    /// <summary>The Vietnamese text the window, the tray and the settings page show for a session.</summary>
    public static class CoreDeviceMessages
    {
        public static string Route(CoreDeviceRoute route) =>
            route == CoreDeviceRoute.Usb ? "Qua cáp" : route == CoreDeviceRoute.Wifi ? "Qua Wi-Fi" : "";

        public static string Phase(SessionPhase phase)
        {
            switch (phase)
            {
                case SessionPhase.Idle: return "Chưa chạy";
                case SessionPhase.StartingTunnel: return "Đang tìm thiết bị qua Wi-Fi...";
                case SessionPhase.Preparing: return "Đang chuẩn bị thiết bị...";
                case SessionPhase.Starting: return "Đang kết nối...";
                case SessionPhase.Ready: return "Đang điều khiển";
                case SessionPhase.Reconnecting: return "Mất kết nối Wi-Fi, đang kết nối lại...";
                case SessionPhase.WaitingForDevice: return "Mất kết nối. Cắm lại cáp để tiếp tục.";
                case SessionPhase.Stopping: return "Đang dừng...";
                case SessionPhase.Stopped: return "Đã dừng";
                case SessionPhase.Failed: return "Không mở được phiên điều khiển";
                default: return "";
            }
        }

        public static string Failure(SessionFailure failure)
        {
            switch (failure)
            {
                case SessionFailure.MediaInUse: return "Camera, micro hoặc một cuộc gọi đang được dùng trên thiết bị. Đóng Camera, Ghi âm hoặc cuộc gọi rồi thử lại.";
                case SessionFailure.DeviceNotFound: return "Không thấy thiết bị. Cắm cáp, hoặc để thiết bị cùng mạng Wi-Fi và mở khóa màn hình.";
                case SessionFailure.NeedsTrust: return "Thiết bị chưa tin cậy laptop. Mở khóa thiết bị, mở Thiết lập và làm bước Tin cậy.";
                case SessionFailure.DeviceLocked: return "Thiết bị đang khóa. Mở khóa thiết bị rồi thử lại.";
                case SessionFailure.DeveloperSetup: return "Thiết bị chưa sẵn sàng (Developer Mode hoặc ảnh DDI). Mở Thiết lập để làm lại bước này.";
                case SessionFailure.NoInternet: return "Cần Internet để tải ảnh DDI lần đầu. Kiểm tra mạng rồi thử lại.";
                case SessionFailure.NoAppleService: return "Thiếu dịch vụ thiết bị của Apple. Cài iTunes hoặc Apple Devices, rồi thử lại.";
                case SessionFailure.StartTimeout: return "Thiết bị không phản hồi trong 120 giây. Mở khóa thiết bị rồi thử lại.";
                case SessionFailure.TunnelTimeout: return "Không tìm thấy thiết bị qua Wi-Fi trong 30 giây. Kiểm tra: cùng mạng Wi-Fi, mạng là loại Riêng tư, màn hình thiết bị đang mở.";
                case SessionFailure.WindowsSetupMissing: return "Thiếu phần thiết lập Windows cho Wi-Fi. Mở Thiết lập và cài lại thành phần.";
                case SessionFailure.AdminDeclined: return "Cần quyền quản trị để điều khiển qua Wi-Fi. Thử lại và chọn Có khi Windows hỏi, hoặc cắm cáp.";
                case SessionFailure.PortInUse: return "Cổng 49151 đang được một chương trình khác dùng nên không bật được kênh Wi-Fi. Đóng chương trình đó hoặc dùng cáp.";
                case SessionFailure.NeedsAdminAccount: return "Điều khiển qua Wi-Fi cần tài khoản Windows có quyền quản trị; tài khoản này chỉ dùng được qua cáp.";
                case SessionFailure.KeepsExiting: return "Phiên điều khiển dừng liên tục. Rút cáp, mở khóa thiết bị, cắm lại rồi thử lại.";
                case SessionFailure.Exited: return "Phiên điều khiển dừng bất ngờ. Thử lại.";
                case SessionFailure.Other: return "Không mở được phiên điều khiển. Chi tiết ở nhật ký coredevice.log.";
                default: return "";
            }
        }

        /// <summary>Failures the setup window can fix; the window offers "Mở Thiết lập" for them.</summary>
        public static bool NeedsSetup(SessionFailure failure) =>
            failure == SessionFailure.NeedsTrust || failure == SessionFailure.DeveloperSetup
            || failure == SessionFailure.WindowsSetupMissing || failure == SessionFailure.NoAppleService;
    }
}
