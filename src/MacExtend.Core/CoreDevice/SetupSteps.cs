using System.Collections.Generic;
using System.Linq;

namespace MacExtend.Core.CoreDevice
{
    public enum SetupStepId
    {
        Components,
        AppleService,
        Device,
        Trust,
        DeveloperMode,
        DiskImage,
        Wifi,
    }

    public enum SetupStatus
    {
        Done,
        Todo,
        /// <summary>Needs an earlier step first.</summary>
        Waiting,
        Optional,
    }

    /// <summary>What the setup window found; each step is evaluated from these alone.</summary>
    public sealed class SetupFacts
    {
        public bool RuntimeInstalled;
        /// <summary>Scheduled Task and firewall rules in place (always true for a portable copy, which uses neither).</summary>
        public bool WindowsSetupDone;
        public bool AppleServiceReachable;
        /// <summary>The cable device being set up; null when none is plugged in.</summary>
        public string UsbUdid;
        public bool? Trusted;
        public bool? DeveloperMode;
        public bool DiskImageMounted;
        public bool WifiWanted;
        public bool WifiPaired;
    }

    public sealed class SetupStep
    {
        public SetupStep(SetupStepId id, SetupStatus status, string title, string detail)
        {
            Id = id;
            Status = status;
            Title = title;
            Detail = detail;
        }

        public SetupStepId Id { get; }
        public SetupStatus Status { get; }
        public string Title { get; }
        public string Detail { get; }
    }

    public static class SetupSteps
    {
        public static IReadOnlyList<SetupStep> Evaluate(SetupFacts f)
        {
            var components = f.RuntimeInstalled && f.WindowsSetupDone;
            var canSeeDevices = f.RuntimeInstalled && f.AppleServiceReachable;
            var device = canSeeDevices && f.UsbUdid != null;
            var trusted = device && f.Trusted == true;
            var developer = trusted && f.DeveloperMode == true;

            return new[]
            {
                new SetupStep(SetupStepId.Components, components ? SetupStatus.Done : SetupStatus.Todo, "Thành phần điều khiển trực tiếp",
                    components ? "Đã cài." : "Tải khoảng 100 MB và cài một lần. Windows hỏi quyền quản trị đúng một lần."),
                new SetupStep(SetupStepId.AppleService, f.AppleServiceReachable ? SetupStatus.Done : SetupStatus.Todo, "Dịch vụ thiết bị của Apple",
                    f.AppleServiceReachable ? "Windows nói chuyện được với iPhone, iPad qua cáp."
                        : "Cài iTunes (apple.com) hoặc Apple Devices (Microsoft Store), rồi bấm Kiểm tra lại."),
                new SetupStep(SetupStepId.Device, !canSeeDevices ? SetupStatus.Waiting : device ? SetupStatus.Done : SetupStatus.Todo, "Cắm thiết bị",
                    device ? "Đã thấy thiết bị qua cáp." : "Cắm iPhone hoặc iPad bằng cáp và mở khóa màn hình."),
                new SetupStep(SetupStepId.Trust, !device ? SetupStatus.Waiting : trusted ? SetupStatus.Done : SetupStatus.Todo, "Tin cậy máy tính này",
                    trusted ? "Thiết bị đã tin cậy laptop." : "Bấm Kết nối, rồi chọn Tin cậy trên thiết bị và nhập mật mã."),
                new SetupStep(SetupStepId.DeveloperMode, !trusted ? SetupStatus.Waiting : developer ? SetupStatus.Done : SetupStatus.Todo, "Developer Mode",
                    developer ? "Đã bật." : "Bấm Hiện tùy chọn, rồi vào Cài đặt > Quyền riêng tư & Bảo mật > Chế độ nhà phát triển và bật lên. Thiết bị khởi động lại và hỏi xác nhận. Apple cảnh báo khi bật vì chế độ này nới một số lớp bảo vệ."),
                new SetupStep(SetupStepId.DiskImage, !developer ? SetupStatus.Waiting : f.DiskImageMounted ? SetupStatus.Done : SetupStatus.Todo, "Ảnh DDI",
                    f.DiskImageMounted ? "Đã gắn. App tự gắn lại trước mỗi phiên." : "Bấm Gắn ảnh. Lần đầu cần Internet."),
                new SetupStep(SetupStepId.Wifi,
                    !f.WifiWanted ? SetupStatus.Optional : !trusted ? SetupStatus.Waiting : f.WifiPaired ? SetupStatus.Done : SetupStatus.Todo,
                    "Điều khiển qua Wi-Fi",
                    !f.WifiWanted ? "Tùy chọn: bật để điều khiển không cần cáp, khi thiết bị và laptop cùng mạng Wi-Fi loại Riêng tư."
                        : f.WifiPaired ? "Đã ghép nối Wi-Fi." : "Bấm Ghép nối khi đang cắm cáp. Thiết bị không hiện hộp thoại nào."),
            };
        }

        public static bool Ready(IEnumerable<SetupStep> steps) => steps.All(s => s.Status == SetupStatus.Done || s.Status == SetupStatus.Optional);
    }
}
