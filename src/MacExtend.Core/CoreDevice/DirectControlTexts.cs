namespace MacExtend.Core.CoreDevice
{
    /// <summary>The Vietnamese text the settings page, the tray and the open's prompts show for "Điều khiển trực tiếp".</summary>
    public static class DirectControlTexts
    {
        public const string Opening = "Đang tìm thiết bị...";
        public const string SetUp = "Đã thiết lập. Cắm cáp, hoặc dùng Wi-Fi nếu đã bật, rồi bấm Mở điều khiển.";
        public const string NotSetUp = "Chưa thiết lập. Cần Developer Mode trên thiết bị, và iTunes hoặc Apple Devices trên Windows.";

        /// <summary>The prompt's button that runs the one-time Windows setup again (one UAC).</summary>
        public const string Resetup = "Thiết lập lại";
        public const string SetupBusy = "Một bước thiết lập khác đang chạy. Đợi bước đó xong rồi bấm " + Resetup + ".";

        public const string ForcedStopTitle = "Điều khiển trực tiếp đã dừng";
        public const string ForcedStopText = "Phiên phải dừng cưỡng bức. Mở Camera trên thiết bị để chắc camera và micro đã trở lại.";

        /// <summary>The status line under "Điều khiển trực tiếp" on the settings page.</summary>
        /// <param name="runtimeCurrent">This build's CoreDevice runtime is installed.</param>
        /// <param name="opening">The devices are being looked at for a new session.</param>
        public static string Settings(SessionView view, bool runtimeCurrent, bool opening) =>
            view.IsActive ? view.StatusText
            : opening ? Opening
            : runtimeCurrent ? SetUp
            : NotSetUp;

        /// <summary>The tray menu item: start, stop, or wait for the stop.</summary>
        public static string TrayItem(SessionView view) =>
            view.Phase == SessionPhase.Stopping ? "Đang dừng điều khiển trực tiếp..."
            : view.IsActive ? "Dừng điều khiển trực tiếp"
            : "Điều khiển trực tiếp...";

        public static bool TrayItemEnabled(SessionView view) => view.Phase != SessionPhase.Stopping;

        /// <summary>The tray's status line while the device is shown; null otherwise (the receiver's own status stays).</summary>
        public static string TrayStatus(SessionView view)
        {
            if (view.Phase != SessionPhase.Ready) return null;
            var route = CoreDeviceMessages.Route(view.Route);
            // "Qua cáp" becomes "qua cáp", "Qua Wi-Fi" stays "qua Wi-Fi"
            if (route.Length > 0) route = char.ToLowerInvariant(route[0]) + route.Substring(1);
            return "Đang điều khiển trực tiếp (" + route + ")";
        }

        /// <summary>The session is over and its stop had to kill serve-web: camera and microphone may not be back.</summary>
        public static bool WarnForcedStop(SessionView view) => !view.IsActive && view.LastStopWasForced;

        /// <summary>The title of the prompt when "Mở điều khiển" cannot start a session.</summary>
        public static string ProblemTitle(SessionFailure failure) =>
            failure == SessionFailure.WindowsSetupMissing ? "Wi-Fi chưa sẵn sàng trên Windows" : "Không thấy thiết bị";

        /// <summary>
        /// The prompt's text. Windows' part of Wi-Fi gets its own sentence: that prompt has the <see cref="Resetup"/>
        /// button itself, while the session's own message sends the user to the setup window.
        /// </summary>
        public static string ProblemDetail(SessionFailure failure) =>
            failure == SessionFailure.WindowsSetupMissing
                ? "Windows còn thiếu luật tường lửa hoặc tác vụ cho Wi-Fi. Bấm " + Resetup + ", Windows hỏi quyền quản trị một lần. Hoặc cắm cáp để điều khiển qua cáp."
                : CoreDeviceMessages.Failure(failure);
    }
}
