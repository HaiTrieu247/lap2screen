using System;
using System.Globalization;
using System.IO;
using System.Net.Http;

namespace MacExtend.Core.CoreDevice
{
    /// <summary>
    /// The setup window's Vietnamese status line: what it is doing, and what each outcome means for the user. Neither a
    /// system exception's own text nor a command's output is ever shown; only the fixed Vietnamese reasons the app's own
    /// code gives (a release without the runtime, a zip with the wrong SHA-256, an elevated helper's exit code) are
    /// passed on as they are. Every "bấm X" names one of <see cref="SetupActions"/>.
    /// </summary>
    public static class SetupMessages
    {
        // what CoreDeviceRuntime.SetupAsync and AddFirewallRulesAsync return
        private const string Ok = "ok";
        private const string Cancelled = "cancelled";
        private const string FailedPrefix = "failed: ";

        public const string Checking = "Đang kiểm tra...";
        /// <summary>`amfi developer-mode-status` pairs first, so the device may show the Trust dialog meanwhile.</summary>
        public const string ReadingDevice = "Đang đọc thiết bị. Nếu thiết bị hỏi có tin cậy máy tính này không, chọn Tin cậy và nhập mật mã.";
        public const string CheckingZip = "Đang kiểm tra tệp runtime...";
        public const string StoppingSession = "Đang dừng phiên điều khiển để thay thành phần...";
        public const string Mounting = "Đang gắn ảnh DDI. Lần đầu cần tải ảnh nên có thể mất vài phút.";
        public const string Revealing = "Đang hiện tùy chọn Developer Mode trên thiết bị...";
        public const string Pairing = "Đang ghép nối Wi-Fi...";
        public const string AddingRules = "Windows sẽ hỏi quyền quản trị để mở tường lửa cho Wi-Fi.";
        public const string WindowsCheckTimedOut = "Windows không trả lời khi kiểm tra tường lửa và tác vụ. Bấm " + SetupActions.Refresh + ".";
        public const string Ready = "Đã thiết lập xong. Mở Điều khiển trực tiếp từ cửa sổ chính hoặc menu khay.";

        /// <summary>While the runtime is installed: behind UAC for a copy with the task, in its own folder otherwise.</summary>
        public static string Installing(bool elevated) => elevated ? "Windows sẽ hỏi quyền quản trị một lần." : "Đang cài thành phần...";

        /// <param name="fraction">0..1 of the zip.</param>
        public static string Downloading(double fraction)
        {
            if (double.IsNaN(fraction)) fraction = 0;
            var percent = (int)Math.Floor(Math.Max(0, Math.Min(1, fraction)) * 100);
            return "Đang tải thành phần... " + percent.ToString(CultureInfo.InvariantCulture) + "%";
        }

        /// <summary>CoreDeviceRuntime.SetupAsync's or AddFirewallRulesAsync's answer was "ok".</summary>
        public static bool IsOk(string outcome) => outcome == Ok;

        /// <summary>CoreDeviceRuntime.SetupAsync's "ok", "cancelled" or "failed: " + a fixed reason.</summary>
        public static string Installed(string outcome)
        {
            if (outcome == Ok) return "Đã cài thành phần.";
            if (outcome == Cancelled) return "Đã hủy. Cài thành phần cần quyền quản trị: thử lại và chọn Có khi Windows hỏi.";
            var reason = Reason(outcome);
            return "Cài thành phần không thành công." + (reason == null ? "" : " " + reason);
        }

        /// <summary>Getting this build's runtime zip from its release failed.</summary>
        public static string DownloadFailed(Exception e)
        {
            // the two reasons the download gives itself, already in Vietnamese: no runtime on the release, a corrupt file
            if (e is FileNotFoundException || e is InvalidDataException) return e.Message;
            // HttpClient's own timeout ends as a cancellation
            if (e is HttpRequestException || e is OperationCanceledException)
                return "Không tải được thành phần. Kiểm tra Internet rồi bấm " + SetupActions.Install + ".";
            if (e is IOException || e is UnauthorizedAccessException)
                return "Không lưu được tệp tải về. Kiểm tra ổ đĩa còn chỗ trống rồi bấm " + SetupActions.Install + ".";
            return "Không tải được thành phần. Bấm " + SetupActions.Install + " để thử lại.";
        }

        /// <summary>A zip the user picked could not be used: not this build's package (CheckZip's fixed reason), or unreadable.</summary>
        public static string PickedZipFailed(Exception e) =>
            e is InvalidDataException
                ? e.Message
                : "Không đọc được tệp đã chọn. Chép tệp về ổ đĩa của máy này, rồi bấm " + SetupActions.PickZip + " để chọn lại.";

        /// <summary>What to do after `amfi developer-mode-status` failed; null when it answered (the device trusts the laptop).</summary>
        public static string Trust(CommandResult r)
        {
            if (r == null || r.Succeeded) return null;
            if (r.TimedOut) return "Thiết bị chưa trả lời câu hỏi Tin cậy. Mở khóa thiết bị, chọn Tin cậy và nhập mật mã, rồi bấm " + SetupActions.Connect + ".";
            switch (r.Error)
            {
                case CliError.NeedsTrust:
                    return "Thiết bị đang hỏi có tin cậy laptop không. Chọn Tin cậy và nhập mật mã, rồi bấm " + SetupActions.Connect + ".";
                case CliError.TrustDenied:
                    return "Thiết bị đã chọn Không tin cậy. Rút cáp, cắm lại, rồi bấm " + SetupActions.Connect + " và chọn Tin cậy.";
                default:
                    return DeviceProblem(r, SetupActions.Connect);
            }
        }

        /// <summary>After `amfi reveal-developer-mode`: where the switch is now, or what went wrong.</summary>
        public static string Reveal(CommandResult r) =>
            r != null && r.Succeeded
                ? "Trên thiết bị, vào Cài đặt > Quyền riêng tư & Bảo mật > Chế độ nhà phát triển và bật lên. Thiết bị khởi động lại; mở khóa, chọn Bật khi được hỏi, rồi bấm "
                  + SetupActions.Refresh + "."
                : DeviceProblem(r, SetupActions.Reveal);

        /// <summary>
        /// After `mounter auto-mount`, with what `mounter list` then said (<paramref name="detected"/>, null when it could
        /// not say). The device's own list wins over the command's exit code.
        /// </summary>
        public static string Mount(CommandResult r, bool? detected)
        {
            var ran = r != null && r.Succeeded;
            if (detected == true || (ran && detected == null)) return "Đã gắn ảnh DDI.";
            if (ran) return "Thiết bị chưa báo có ảnh DDI. Mở khóa thiết bị, rồi bấm " + SetupActions.Mount + ".";
            if (r != null && r.TimedOut) return "Gắn ảnh quá 3 phút. Kiểm tra Internet, mở khóa thiết bị, rồi bấm " + SetupActions.Mount + ".";
            return DeviceProblem(r, SetupActions.Mount);
        }

        /// <summary>
        /// After `lockdown remotepairing --pair` (<paramref name="paired"/>: its record is there now) and, for a copy without
        /// the task, the firewall rules (<paramref name="rulesOutcome"/>: AddFirewallRulesAsync's answer; null when not asked).
        /// </summary>
        public static string Pair(CommandResult r, bool paired, string rulesOutcome)
        {
            if (!paired)
            {
                return r != null && r.Succeeded
                    ? "Ghép nối chưa xong. Mở khóa thiết bị, rồi bấm " + SetupActions.Pair + "."
                    : DeviceProblem(r, SetupActions.Pair);
            }
            if (rulesOutcome == null || rulesOutcome == Ok) return "Đã ghép nối. Khi không cắm cáp, thiết bị và laptop cần cùng mạng Wi-Fi loại Riêng tư.";
            if (rulesOutcome == Cancelled) return "Đã ghép nối, nhưng Windows chưa mở tường lửa cho Wi-Fi vì chưa có quyền quản trị. Phiên Wi-Fi đầu tiên sẽ hỏi lại.";
            var reason = Reason(rulesOutcome);
            return "Đã ghép nối, nhưng chưa mở được tường lửa cho Wi-Fi." + (reason == null ? "" : " " + reason) + " Phiên Wi-Fi đầu tiên sẽ thử lại.";
        }

        /// <summary>The line under "Cho phép điều khiển qua Wi-Fi": whom the switch is for, or why it is off.</summary>
        public static string WifiSwitchHint(bool standardAccount, string udid)
        {
            if (standardAccount) return "Chỉ dùng được với tài khoản Windows có quyền quản trị.";
            if (string.IsNullOrEmpty(udid)) return "Cắm iPhone hoặc iPad bằng cáp để bật hoặc tắt cho thiết bị đó.";
            // the last digits tell two devices apart without showing the whole UDID
            return "Áp dụng cho thiết bị đang cắm cáp (…" + (udid.Length <= 6 ? udid : udid.Substring(udid.Length - 6)) + ").";
        }

        /// <summary>A short device command failed: what to do, and which button to press after.</summary>
        private static string DeviceProblem(CommandResult r, string retry)
        {
            if (r != null && r.TimedOut) return "Thiết bị không trả lời. Mở khóa thiết bị, rồi bấm " + retry + ".";
            switch (r?.Error ?? CliError.Other)
            {
                case CliError.DeviceLocked:
                    return "Thiết bị đang khóa. Mở khóa màn hình, rồi bấm " + retry + ".";
                case CliError.NeedsTrust:
                case CliError.TrustDenied:
                    return "Thiết bị chưa tin cậy laptop. Bấm " + SetupActions.Refresh + ", rồi làm bước Tin cậy.";
                case CliError.NotConnected:
                    return "Không thấy thiết bị. Cắm lại cáp, rồi bấm " + SetupActions.Refresh + ".";
                case CliError.DeveloperModeOff:
                    return "Developer Mode chưa bật trên thiết bị. Bấm " + SetupActions.Refresh + ", rồi làm bước Developer Mode.";
                case CliError.NoUsbmux:
                    return CoreDeviceMessages.Failure(SessionFailure.NoAppleService);
                case CliError.NoInternet:
                    return CoreDeviceMessages.Failure(SessionFailure.NoInternet);
                default:
                    return "Không làm được bước này. Mở khóa thiết bị, rồi bấm " + retry + ".";
            }
        }

        /// <summary>The fixed reason after "failed: "; null when there is none.</summary>
        private static string Reason(string outcome)
        {
            if (outcome == null || !outcome.StartsWith(FailedPrefix, StringComparison.Ordinal)) return null;
            var reason = outcome.Substring(FailedPrefix.Length).Trim();
            return reason.Length == 0 ? null : reason;
        }
    }
}
