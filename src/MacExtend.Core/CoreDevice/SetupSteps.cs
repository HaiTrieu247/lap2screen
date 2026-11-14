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
        /// <summary>
        /// A copy that never gets the Scheduled Task (portable, or installed outside Program Files): its runtime is
        /// installed without UAC, its firewall rules are added with Wi-Fi pairing, and every Wi-Fi session asks UAC.
        /// </summary>
        public bool WithoutTask;
        /// <summary>
        /// The Windows account is not an administrator: no Wi-Fi (UAC would run tunneld as another account, whose profile
        /// has no RemotePairing records), the cable only.
        /// </summary>
        public bool StandardAccount;
    }

    public sealed class SetupStep
    {
        public SetupStep(SetupStepId id, SetupStatus status, string title, string detail, string actionText = null)
        {
            Id = id;
            Status = status;
            Title = title;
            Detail = detail;
            ActionText = actionText;
        }

        public SetupStepId Id { get; }
        public SetupStatus Status { get; }
        public string Title { get; }
        public string Detail { get; }
        /// <summary>The label of the step's button (one of <see cref="SetupActions"/>); null when there is nothing to press.</summary>
        public string ActionText { get; }
    }

    /// <summary>
    /// The setup window's button labels. Texts that say "bấm X" build X from these, and the window shows these, so a
    /// text never names a button that is not there.
    /// </summary>
    public static class SetupActions
    {
        public const string Install = "Tải và cài";
        /// <summary>The runtime is there, but Windows' part (firewall rules, task) is missing.</summary>
        public const string Reinstall = "Cài lại";
        /// <summary>The window's own refresh button, and the button of the steps that only need a new look.</summary>
        public const string Refresh = "Kiểm tra lại";
        public const string Connect = "Kết nối";
        public const string Reveal = "Hiện tùy chọn";
        public const string Mount = "Gắn ảnh";
        public const string Pair = "Ghép nối";
        /// <summary>A runtime zip from a CI artifact, for a beta build without a release.</summary>
        public const string PickZip = "Chọn tệp runtime...";

        public static IReadOnlyList<string> All { get; } = new[] { Install, Reinstall, Refresh, Connect, Reveal, Mount, Pair, PickZip };
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
            var wifiWanted = f.WifiWanted && !f.StandardAccount;

            return new[]
            {
                Make(SetupStepId.Components, components ? SetupStatus.Done : SetupStatus.Todo, "Thành phần điều khiển trực tiếp",
                    components ? "Đã cài."
                        : f.RuntimeInstalled ? "Windows còn thiếu luật tường lửa hoặc tác vụ cho Wi-Fi. Bấm " + SetupActions.Reinstall + ", Windows hỏi quyền quản trị một lần."
                        : f.WithoutTask ? "Khoảng 100 MB, cài một lần vào thư mục của MacExtend, không cần quyền quản trị. Bấm " + SetupActions.Install + "."
                        : "Khoảng 100 MB, cài một lần. Bấm " + SetupActions.Install + "; Windows hỏi quyền quản trị đúng một lần.",
                    f.RuntimeInstalled ? SetupActions.Reinstall : SetupActions.Install),
                Make(SetupStepId.AppleService, f.AppleServiceReachable ? SetupStatus.Done : SetupStatus.Todo, "Dịch vụ thiết bị của Apple",
                    f.AppleServiceReachable ? "Windows nói chuyện được với iPhone, iPad qua cáp."
                        : "Cài iTunes (apple.com) hoặc Apple Devices (Microsoft Store), rồi bấm " + SetupActions.Refresh + ".",
                    SetupActions.Refresh),
                Make(SetupStepId.Device, !canSeeDevices ? SetupStatus.Waiting : device ? SetupStatus.Done : SetupStatus.Todo, "Cắm thiết bị",
                    device ? "Đã thấy thiết bị qua cáp." : "Cắm iPhone hoặc iPad bằng cáp và mở khóa màn hình, rồi bấm " + SetupActions.Refresh + ".",
                    SetupActions.Refresh),
                Make(SetupStepId.Trust, !device ? SetupStatus.Waiting : trusted ? SetupStatus.Done : SetupStatus.Todo, "Tin cậy máy tính này",
                    trusted ? "Thiết bị đã tin cậy laptop." : "Bấm " + SetupActions.Connect + ", rồi chọn Tin cậy trên thiết bị và nhập mật mã.",
                    SetupActions.Connect),
                Make(SetupStepId.DeveloperMode, !trusted ? SetupStatus.Waiting : developer ? SetupStatus.Done : SetupStatus.Todo, "Developer Mode",
                    developer ? "Đã bật." : "Bấm " + SetupActions.Reveal + ", rồi vào Cài đặt > Quyền riêng tư & Bảo mật > Chế độ nhà phát triển và bật lên. Thiết bị khởi động lại và hỏi xác nhận. Apple cảnh báo khi bật vì chế độ này nới một số lớp bảo vệ.",
                    SetupActions.Reveal),
                Make(SetupStepId.DiskImage, !developer ? SetupStatus.Waiting : f.DiskImageMounted ? SetupStatus.Done : SetupStatus.Todo, "Ảnh DDI",
                    f.DiskImageMounted ? "Đã gắn. App tự gắn lại trước mỗi phiên."
                        : "Bấm " + SetupActions.Mount + " để gắn ngay. App cũng tự gắn trước mỗi phiên; lần đầu cần Internet.",
                    SetupActions.Mount),
                Make(SetupStepId.Wifi,
                    !wifiWanted ? SetupStatus.Optional : !trusted ? SetupStatus.Waiting : f.WifiPaired ? SetupStatus.Done : SetupStatus.Todo,
                    "Điều khiển qua Wi-Fi",
                    f.StandardAccount ? CoreDeviceMessages.Failure(SessionFailure.NeedsAdminAccount)
                        : !wifiWanted ? "Tùy chọn: bật để điều khiển không cần cáp, khi thiết bị và laptop cùng mạng Wi-Fi loại Riêng tư."
                        : f.WifiPaired ? "Đã ghép nối Wi-Fi." + (f.WithoutTask ? " Mỗi phiên qua Wi-Fi, Windows hỏi quyền quản trị một lần." : "")
                        : "Bấm " + SetupActions.Pair + " khi đang cắm cáp. Thiết bị không hiện hộp thoại nào."
                          + (f.WithoutTask ? " Windows hỏi quyền quản trị một lần để mở tường lửa cho Wi-Fi." : ""),
                    SetupActions.Pair),
            };
        }

        /// <summary>
        /// Every step done or optional. The DDI step does not hold readiness back once Developer Mode is on (it is to do
        /// only then): every session runs auto-mount before serve-web, and from iOS 27 the image mounter may not list the
        /// cryptex DDI at all.
        /// </summary>
        public static bool Ready(IEnumerable<SetupStep> steps) =>
            steps.All(s => s.Status == SetupStatus.Done || s.Status == SetupStatus.Optional
                           || (s.Id == SetupStepId.DiskImage && s.Status == SetupStatus.Todo));

        /// <summary>The chip beside a step's title; a step whose action runs now is "Đang làm" whatever its status.</summary>
        public static string StatusText(SetupStatus status, bool running)
        {
            if (running) return "Đang làm";
            switch (status)
            {
                case SetupStatus.Done: return "Đã xong";
                case SetupStatus.Todo: return "Cần làm";
                case SetupStatus.Optional: return "Tùy chọn";
                default: return "Chờ bước trước";
            }
        }

        /// <summary>
        /// "Cho phép điều khiển qua Wi-Fi" is set per device, for the one on the cable (pairing needs the cable), and only
        /// on an administrator account.
        /// </summary>
        public static bool WifiSwitchEnabled(SetupFacts f) => !f.StandardAccount && f.UsbUdid != null;

        /// <summary>A step's button exists only while the step is to do.</summary>
        private static SetupStep Make(SetupStepId id, SetupStatus status, string title, string detail, string action) =>
            new SetupStep(id, status, title, detail, status == SetupStatus.Todo ? action : null);
    }
}
