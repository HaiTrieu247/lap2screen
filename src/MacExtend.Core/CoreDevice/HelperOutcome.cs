using System.Globalization;

namespace MacExtend.Core.CoreDevice
{
    /// <summary>
    /// Exit codes of MacExtend.exe's elevated helper modes (--coredevice-setup, --coredevice-rules, --coredevice-remove).
    /// The number is all a helper reports: it writes nothing outside the install folder, so no caller-chosen path can
    /// receive text from an elevated process. The app turns the number into a fixed message (HelperOutcomes).
    /// </summary>
    public enum HelperOutcome
    {
        Ok = 0,
        /// <summary>Something no other code names.</summary>
        Failed = 1,
        BadArguments = 2,
        NotElevated = 3,
        /// <summary>A runtime is staged with administrator rights only inside Program Files or Program Files (x86).</summary>
        NotProtectedLocation = 4,
        /// <summary>A local build: no embedded runtime package to check the zip against.</summary>
        PackageMissing = 5,
        /// <summary>"-" (keep the installed runtime), but there is none.</summary>
        RuntimeMissing = 6,
        ZipNotFound = 7,
        HashMismatch = 8,
        WrongRuntimeId = 9,
        IncompleteRuntime = 10,
        /// <summary>The right bytes, but a zip that cannot be extracted safely.</summary>
        BadPackage = 11,
        /// <summary>The runtime folder could not be replaced (files in use, disk full); the previous one is kept.</summary>
        InstallFailed = 12,
        FirewallFailed = 13,
        TaskFailed = 14,
        VerifyFailed = 15,
        RemoveFailed = 16,
    }

    public static class HelperOutcomes
    {
        /// <summary>
        /// A fixed Vietnamese sentence for an exit code: no exception text, no paths. Any other number (a crash, an exit
        /// code from Windows) gets the generic sentence with the number.
        /// </summary>
        public static string Message(int exitCode)
        {
            switch ((HelperOutcome)exitCode)
            {
                case HelperOutcome.Ok: return "Đã xong.";
                case HelperOutcome.Failed: return "Bước cài đặt có quyền quản trị gặp lỗi không lường trước.";
                case HelperOutcome.BadArguments: return "Lệnh cài đặt không hợp lệ.";
                case HelperOutcome.NotElevated: return "Bước cài đặt không có quyền quản trị.";
                case HelperOutcome.NotProtectedLocation:
                    return "Chỉ cài thành phần với quyền quản trị cho bản MacExtend cài trong Program Files hoặc Program Files (x86).";
                case HelperOutcome.PackageMissing: return "Bản build cục bộ không có thông tin runtime. Hãy dùng bản do CI build.";
                case HelperOutcome.RuntimeMissing: return "Chưa có thành phần Điều khiển trực tiếp trong thư mục MacExtend.";
                case HelperOutcome.ZipNotFound: return "Không mở được tệp runtime. Hãy tải lại hoặc chọn lại tệp.";
                case HelperOutcome.HashMismatch: return "Gói runtime không phải của bản MacExtend này (sai mã SHA-256).";
                case HelperOutcome.WrongRuntimeId: return "Gói runtime có RUNTIME_ID khác với bản MacExtend này.";
                case HelperOutcome.IncompleteRuntime: return "Gói runtime thiếu python.exe hoặc pythonw.exe.";
                case HelperOutcome.BadPackage: return "Gói runtime bị hỏng.";
                case HelperOutcome.InstallFailed:
                    return "Không thay được thành phần trong thư mục MacExtend, thành phần cũ vẫn được giữ. Hãy đóng phiên Điều khiển trực tiếp rồi thử lại.";
                case HelperOutcome.FirewallFailed: return "Không tạo được luật tường lửa.";
                case HelperOutcome.TaskFailed: return "Không tạo được tác vụ Scheduled Task.";
                case HelperOutcome.VerifyFailed: return "Windows chưa có đủ luật tường lửa hoặc tác vụ sau khi thiết lập.";
                case HelperOutcome.RemoveFailed: return "Không xóa hết được tác vụ và luật tường lửa.";
                default:
                    return "Bước cài đặt có quyền quản trị không thành công (mã " + exitCode.ToString(CultureInfo.InvariantCulture) + ").";
            }
        }

        public static HelperOutcome For(RuntimeProblem problem)
        {
            switch (problem)
            {
                case RuntimeProblem.NotThePackage: return HelperOutcome.HashMismatch;
                case RuntimeProblem.WrongRuntimeId: return HelperOutcome.WrongRuntimeId;
                case RuntimeProblem.Incomplete: return HelperOutcome.IncompleteRuntime;
                default: return HelperOutcome.BadPackage;
            }
        }
    }
}
