namespace MacExtend.Core.CoreDevice
{
    /// <summary>The Vietnamese text the settings page, the tray and the AirPlay picture show for "Điều khiển trực tiếp".</summary>
    public static class DirectControlTexts
    {
        public const string Title = "Điều khiển trực tiếp";
        public const string Caption = "Khi iPhone hoặc iPad phản chiếu, bấm thẳng lên hình AirPlay để chạm, giữ để nhấn giữ, kéo để vuốt, lăn chuột để cuộn và gõ phím để nhập. Nhấp chuột phải là Home. Qua cáp, hoặc Wi-Fi nếu đã bật trong Thiết lập.";
        public const string NotSetUp = "Chưa thiết lập. Bấm Thiết lập: cần Developer Mode trên thiết bị, và iTunes hoặc Apple Devices trên Windows.";
        public const string ChooseDevice = "Chọn thiết bị để điều khiển";
        public const string Retry = "Thử lại";

        /// <summary>Over the picture while the session starts: it takes no input yet.</summary>
        public const string Connecting = "Đang kết nối điều khiển...";

        /// <summary>Over the picture when the device refused the media stream input needs.</summary>
        public const string MediaBusy = "Camera hoặc micro đang được dùng trên thiết bị. Đóng ứng dụng đó để điều khiển.";

        /// <summary>The one line over the picture when control begins.</summary>
        public const string Hint = "Nhấp chuột phải: Home. Ctrl+V: dán chữ từ laptop. Ctrl+H: Home. Ctrl+mũi tên lên/xuống: âm lượng.";

        public const string ForcedStopTitle = "Điều khiển trực tiếp đã dừng";
        public const string ForcedStopText = "Phiên phải dừng cưỡng bức. Mở Camera trên thiết bị để chắc camera và micro đã trở lại.";

        /// <summary>The status line under the switch on the settings page; empty while the switch is off.</summary>
        /// <param name="session">The CoreDevice session, which says how far a running one is.</param>
        /// <param name="runtimeCurrent">This build's CoreDevice runtime is installed.</param>
        public static string Status(DirectView view, SessionView session, bool runtimeCurrent)
        {
            switch (view.Phase)
            {
                case DirectPhase.Off:
                    return "";
                case DirectPhase.Waiting:
                    return runtimeCurrent ? "Đang chờ iPhone hoặc iPad phản chiếu." : NotSetUp;
                case DirectPhase.Finding:
                    return "Đang tìm thiết bị...";
                case DirectPhase.Choose:
                    return "Có nhiều thiết bị. Bấm " + ChooseDevice + " và chọn thiết bị đang phản chiếu.";
                case DirectPhase.Running:
                    if (session.Phase != SessionPhase.Ready) return session.StatusText;
                    return "Đang điều khiển " + Lower(CoreDeviceMessages.Route(session.Route)) + ". Bấm lên hình AirPlay để chạm, nhấp chuột phải là Home.";
                default:
                    // nothing can start before the runtime is there, whatever else failed
                    return runtimeCurrent ? CoreDeviceMessages.Failure(view.Failure) : NotSetUp;
            }
        }

        /// <summary>The tray's status line while a device is controlled or a choice waits; null otherwise (the receiver's own status stays).</summary>
        public static string TrayStatus(DirectView view, SessionView session, string device)
        {
            if (view.Phase == DirectPhase.Choose) return ChooseDevice;
            if (view.Phase != DirectPhase.Running || session.Phase != SessionPhase.Ready) return null;
            return "Đang điều khiển " + (string.IsNullOrWhiteSpace(device) ? "thiết bị" : device);
        }

        /// <summary>The session is over and its stop had to kill serve-web: camera and microphone may not be back.</summary>
        public static bool WarnForcedStop(SessionView view) => !view.IsActive && view.LastStopWasForced;

        /// <summary>"Qua cáp" becomes "qua cáp", "Qua Wi-Fi" stays "qua Wi-Fi".</summary>
        private static string Lower(string route) => route.Length > 0 ? char.ToLowerInvariant(route[0]) + route.Substring(1) : route;
    }
}
