using System;
using System.Globalization;
using System.IO;
using System.Net.Http;

namespace MacExtend.Core.CoreDevice
{
    /// <summary>
    /// The setup window's Vietnamese status line: what it is doing, and what each outcome means for the user. Every
    /// sentence is made here: neither a system exception's text nor a command's output is ever shown, and of an outcome's
    /// "failed: reason" only the app's own fixed reasons (HelperOutcomes, <see cref="InstallFolderNotWritable"/>,
    /// <see cref="ZipNotLocal"/>) are passed on. Every "bấm X" names one of <see cref="SetupActions"/>.
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

        /// <summary>The runtime could not be written into the copy's own folder (an unelevated install).</summary>
        public const string InstallFolderNotWritable =
            "Không ghi được vào thư mục của MacExtend. Hãy cài MacExtend bằng bộ cài vào Program Files, hoặc giải nén bản portable vào một thư mục bạn có quyền ghi.";
        /// <summary>The elevated setup only takes a zip on a local drive.</summary>
        public const string ZipNotLocal = "Tệp runtime phải nằm trên ổ đĩa của máy này. Hãy chép tệp về máy, rồi bấm " + SetupActions.PickZip + " để chọn lại.";

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

        /// <summary>Getting this build's runtime zip (<paramref name="packageFile"/>) from its release failed.</summary>
        public static string DownloadFailed(Exception e, string packageFile)
        {
            // the download's two own reasons: a release without the runtime (a beta build from CI), a wrong SHA-256
            if (e is FileNotFoundException)
            {
                return "Bản phát hành này không có gói runtime trên GitHub. Nếu đây là bản beta từ CI, tải artifact của bản build, rồi bấm "
                       + SetupActions.PickZip + " và chọn tệp " + (string.IsNullOrEmpty(packageFile) ? "gói runtime" : packageFile) + ".";
            }
            if (e is InvalidDataException) return "Tệp tải về bị hỏng (sai mã SHA-256). Bấm " + SetupActions.Install + " để tải lại.";
            // HttpClient's own timeout ends as a cancellation
            if (e is HttpRequestException || e is OperationCanceledException)
                return "Không tải được thành phần. Kiểm tra Internet rồi bấm " + SetupActions.Install + ".";
            if (e is IOException || e is UnauthorizedAccessException)
                return "Không lưu được tệp tải về. Kiểm tra ổ đĩa còn chỗ trống rồi bấm " + SetupActions.Install + ".";
            return "Không tải được thành phần. Bấm " + SetupActions.Install + " để thử lại.";
        }

        /// <summary>A zip the user picked could not be used: not this build's package (CheckZip), or unreadable.</summary>
        public static string PickedZipFailed(Exception e) =>
            e is InvalidDataException
                ? "Tệp này không phải gói runtime của bản MacExtend đang chạy."
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
        /// After `mounter auto-mount` (idempotent: an image already there is a success), with what `mounter list` then said
        /// (<paramref name="detected"/>). Either one is enough: from iOS 27 the DDI is a cryptex that the image mounter may
        /// not list, so a list without it never overrules a mount that worked.
        /// </summary>
        public static string Mount(CommandResult r, bool? detected)
        {
            if (detected == true || (r != null && r.Succeeded)) return "Đã gắn ảnh DDI.";
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

        /// <summary>The reason after "failed: " when it is one of the app's own fixed sentences; null for none or for anything else.</summary>
        private static string Reason(string outcome)
        {
            if (outcome == null || !outcome.StartsWith(FailedPrefix, StringComparison.Ordinal)) return null;
            var reason = outcome.Substring(FailedPrefix.Length).Trim();
            return HelperOutcomes.IsMessage(reason) || reason == InstallFolderNotWritable || reason == ZipNotLocal ? reason : null;
        }
    }
}
