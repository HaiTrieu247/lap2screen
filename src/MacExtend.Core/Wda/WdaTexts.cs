using System;
using System.Globalization;

namespace MacExtend.Core.Wda
{
    /// <summary>Where a WDA session is: what the settings page, the tray and the overlay show.</summary>
    public enum WdaPhase
    {
        /// <summary>The switch is off.</summary>
        Off,
        /// <summary>On, waiting for an iPhone or iPad to mirror.</summary>
        Waiting,
        /// <summary>Several devices on the cable and none chosen for the one mirroring.</summary>
        Choose,
        Starting,
        Ready,
        Stopping,
        Failed,
    }

    /// <summary>The steps of a start, in order; the status line names the one running.</summary>
    public enum WdaStep
    {
        FindingDevice,
        CheckingDevice,
        Mounting,
        FindingWda,
        Launching,
        Connecting,
    }

    /// <summary>The Vietnamese text of WDA control.</summary>
    public static class WdaTexts
    {
        public const string Title = "Điều khiển bằng WDA";
        public const string Caption = "Tự bật khi iPhone hoặc iPad đang phản chiếu và có WebDriverAgent. Bấm vào hình để chạm, kéo để vuốt, lăn chuột để cuộn, gõ phím để nhập chữ, nhấp chuột phải là Home.";
        public const string Retry = "Thử lại";
        public const string ChooseDevice = "Chọn thiết bị để điều khiển";
        public const string SetupButton = "Thiết lập...";

        public static string Step(WdaStep step)
        {
            switch (step)
            {
                case WdaStep.FindingDevice: return "Đang tìm thiết bị qua cáp...";
                case WdaStep.CheckingDevice: return "Đang kiểm tra thiết bị...";
                case WdaStep.Mounting: return "Đang gắn ảnh DDI (lần đầu có thể mất vài phút)...";
                case WdaStep.FindingWda: return "Đang tìm WebDriverAgent trên thiết bị...";
                case WdaStep.Launching: return "Đang khởi chạy WebDriverAgent...";
                default: return "Đang kết nối tới WebDriverAgent...";
            }
        }

        public static string Failure(WdaFailure failure)
        {
            switch (failure)
            {
                case WdaFailure.RuntimeMissing: return "Chưa cài thành phần điều khiển. Bấm Thiết lập và làm bước đầu tiên.";
                case WdaFailure.NoAppleService: return "Thiếu dịch vụ thiết bị của Apple. Cài iTunes hoặc Apple Devices, rồi bấm Thử lại.";
                case WdaFailure.NoDevice: return "Không thấy iPhone hoặc iPad qua cáp. Cắm cáp, mở khóa màn hình rồi bấm Thử lại.";
                case WdaFailure.NotTrusted: return "Thiết bị chưa tin cậy laptop. Mở khóa thiết bị, bấm Thiết lập và làm bước Tin cậy.";
                case WdaFailure.Locked: return "Thiết bị đang khóa. Mở khóa rồi bấm Thử lại.";
                case WdaFailure.DeveloperModeOff: return "Developer Mode đang tắt. Bấm Thiết lập và làm bước Developer Mode.";
                case WdaFailure.NoInternet: return "Cần Internet để tải ảnh DDI lần đầu. Kiểm tra mạng rồi bấm Thử lại.";
                case WdaFailure.DiskImageFailed: return "Không gắn được ảnh DDI. Mở khóa thiết bị rồi bấm Thử lại.";
                case WdaFailure.NotInstalled: return "Chưa có WebDriverAgent trên thiết bị. Bấm Thiết lập và làm bước WebDriverAgent.";
                case WdaFailure.Expired: return "Chữ ký WebDriverAgent đã hết hạn. Cài lại WebDriverAgentRunner.ipa bằng Sideloadly rồi bấm Thử lại.";
                case WdaFailure.UntrustedDeveloper: return "iOS chưa cho chạy WebDriverAgent. Trên thiết bị vào Cài đặt > Cài đặt chung > Quản lý VPN & thiết bị, chọn Apple ID của bạn rồi chọn Tin cậy. Nếu đã tin cậy, chữ ký có thể đã hết hạn: cài lại bằng Sideloadly.";
                case WdaFailure.OldIos: return "iOS 17.0 đến 17.3 không điều khiển được qua cáp mà không cần quyền quản trị. Cập nhật iOS rồi bấm Thử lại.";
                case WdaFailure.RunnerFailed: return "WebDriverAgent không khởi chạy được. Mở khóa thiết bị, kiểm tra đã Tin cậy nhà phát triển, rồi bấm Thử lại.";
                case WdaFailure.NotAnswering: return "WebDriverAgent không trả lời trong 60 giây. Mở khóa thiết bị rồi bấm Thử lại.";
                case WdaFailure.Lost: return "Mất kết nối với WebDriverAgent. Bấm Thử lại.";
                case WdaFailure.ForwardFailed: return "Không mở được kết nối tới WebDriverAgent qua cáp. Rút cáp, cắm lại rồi bấm Thử lại.";
                case WdaFailure.Other: return "Không bật được điều khiển bằng WDA. Chi tiết ở nhật ký wda.log.";
                default: return "";
            }
        }

        /// <summary>Failures the setup window fixes: the settings page offers its button next to Thử lại.</summary>
        public static bool NeedsSetup(WdaFailure failure) =>
            failure == WdaFailure.RuntimeMissing || failure == WdaFailure.NotTrusted || failure == WdaFailure.DeveloperModeOff
            || failure == WdaFailure.NotInstalled;

        /// <summary>The status line on the settings page.</summary>
        public static string Status(WdaView view, DateTime now)
        {
            switch (view.Phase)
            {
                case WdaPhase.Off: return "";
                case WdaPhase.Waiting: return "Chờ iPhone hoặc iPad phản chiếu.";
                case WdaPhase.Choose: return "Có nhiều thiết bị qua cáp. Bấm " + ChooseDevice + ".";
                case WdaPhase.Starting: return Step(view.Step);
                case WdaPhase.Ready: return "Sẵn sàng qua cáp. Bấm vào hình để điều khiển." + Expiry(view.ExpiresUtc, now);
                case WdaPhase.Stopping: return "Đang dừng...";
                default: return Failure(view.Failure);
            }
        }

        /// <summary>" Chữ ký hết hạn ngày dd/MM." (local date), a warning under a day, nothing when unknown.</summary>
        public static string Expiry(DateTime? expiresUtc, DateTime nowUtc)
        {
            if (expiresUtc == null) return "";
            var left = expiresUtc.Value - nowUtc;
            if (left <= TimeSpan.Zero) return " Chữ ký đã hết hạn.";
            var date = expiresUtc.Value.ToLocalTime().ToString("dd/MM", CultureInfo.InvariantCulture);
            if (left < ProvisionProfiles.WarnBefore) return " Chữ ký hết hạn trong vòng 24 giờ (" + date + "): cài lại bằng Sideloadly.";
            return " Chữ ký hết hạn ngày " + date + ".";
        }

        /// <summary>The tray's status line while WDA control is ready; null otherwise.</summary>
        public static string TrayStatus(WdaView view, string device) =>
            view.Phase == WdaPhase.Ready ? "Điều khiển " + (string.IsNullOrEmpty(device) ? "thiết bị" : device) + " bằng WDA" : null;
    }
}
