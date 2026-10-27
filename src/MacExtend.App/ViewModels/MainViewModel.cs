using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using MacExtend.Core;
using MacExtend.Services;

namespace MacExtend.ViewModels
{
    internal enum CheckState
    {
        Checking,
        Ok,
        Problem,
        Unknown,
    }

    internal sealed class MainViewModel : ObservableObject
    {
        private static readonly Random Rng = new Random();
        private static readonly string[] SettingNames =
        {
            nameof(DeviceNameInput), nameof(IsAutoResolution), nameof(IsCustomResolution), nameof(CustomWidthText),
            nameof(CustomHeightText), nameof(CustomRefreshText), nameof(CustomH265), nameof(IsFps30), nameof(IsFps60),
            nameof(AudioEnabled), nameof(PinEnabled), nameof(Pin), nameof(PinDisplay), nameof(StartWithWindows),
        };
        private static readonly string[] StatusNames =
        {
            nameof(Phase), nameof(StatusTitle), nameof(StatusDetail), nameof(ClientName), nameof(EffectiveDeviceName),
            nameof(ModeText), nameof(AutoResolutionDetail), nameof(IsActive), nameof(CanStart), nameof(NeedsRestart),
            nameof(RestartNotice), nameof(ShowPin), nameof(PinOff), nameof(StepChooseDevice),
        };

        private readonly ReceiverController _controller;
        private readonly SettingsService _settings;
        private CheckState _firewallState = CheckState.Checking;
        private CheckState _networkState = CheckState.Checking;
        private string _firewallText = "Đang kiểm tra";
        private string _networkText = "Đang kiểm tra";
        private string _systemMessage;
        private NetworkConnection _publicNetwork;
        private bool _checking;
        private bool _fixing;

        public MainViewModel(ReceiverController controller, SettingsService settings)
        {
            _controller = controller;
            _settings = settings;
            _controller.StateChanged += () =>
            {
                Raise(StatusNames);
                // state changes without user input do not re-query commands on their own
                CommandManager.InvalidateRequerySuggested();
            };

            StartCommand = new RelayCommand(() => _controller.Start(), () => CanStart);
            StopCommand = new RelayCommand(() => _controller.StopAsync(), () => IsActive);
            RestartCommand = new RelayCommand(() => _controller.RestartAsync(), () => IsActive || CanStart);
            NewPinCommand = new RelayCommand(() => Update(s => s.Pin = Core.Pin.Generate(Rng)), () => PinEnabled);
            ForgetMacsCommand = new RelayCommand(ForgetMacs);
            FixFirewallCommand = new RelayCommand(() => _ = FixFirewallAsync(), () => FirewallState != CheckState.Ok && !_fixing);
            MakePrivateCommand = new RelayCommand(() => _ = MakePrivateAsync(), () => _publicNetwork != null && !_fixing);
            OpenLogsCommand = new RelayCommand(() => Process.Start("explorer.exe", "\"" + AppPaths.LogDir + "\""));

            VersionText = BuildVersionText();
        }

        public ICommand StartCommand { get; }
        public ICommand StopCommand { get; }
        public ICommand RestartCommand { get; }
        public ICommand NewPinCommand { get; }
        public ICommand ForgetMacsCommand { get; }
        public ICommand FixFirewallCommand { get; }
        public ICommand MakePrivateCommand { get; }
        public ICommand OpenLogsCommand { get; }

        // ---- receiver status ----------------------------------------------------------------

        public ReceiverPhase Phase => _controller.Phase;
        public bool IsActive => Phase == ReceiverPhase.Starting || Phase == ReceiverPhase.Ready || Phase == ReceiverPhase.Connected;
        public bool CanStart => Phase == ReceiverPhase.Stopped || Phase == ReceiverPhase.Failed;
        public string ClientName => _controller.ClientName ?? "Mac";
        public string EffectiveDeviceName => _controller.EffectiveDeviceName;
        public bool ShowPin => PinEnabled;
        public bool PinOff => !PinEnabled;

        public string StatusTitle
        {
            get
            {
                switch (Phase)
                {
                    case ReceiverPhase.Starting: return _controller.IsRestarting ? "Đang khởi động lại" : "Đang khởi động";
                    case ReceiverPhase.Ready: return "Sẵn sàng kết nối";
                    case ReceiverPhase.Connected: return "Đang là màn hình phụ";
                    case ReceiverPhase.Failed: return "Không chạy được";
                    default: return "Đã dừng";
                }
            }
        }

        public string StatusDetail
        {
            get
            {
                switch (Phase)
                {
                    case ReceiverPhase.Starting: return "Đang chuẩn bị nhận kết nối từ Mac.";
                    case ReceiverPhase.Ready: return "Mac trong cùng mạng đã thấy " + EffectiveDeviceName + ".";
                    case ReceiverPhase.Connected:
                        return ClientName + " đang dùng màn hình này. Màn hình sẽ luôn sáng cho đến khi ngắt kết nối.";
                    case ReceiverPhase.Failed: return _controller.LastError ?? "Xem nhật ký để biết chi tiết.";
                    default: return "Bấm Bắt đầu để Mac thấy máy này trong Phản chiếu màn hình.";
                }
            }
        }

        public string StepChooseDevice => "Chọn " + EffectiveDeviceName;

        public string ModeText => FormatMode(_controller.EffectiveVideo);

        public string AutoResolutionDetail =>
            "Theo màn hình này: " + FormatMode(ResolutionPolicy.Resolve(new AppSettings(), DisplayInfo.GetPrimary())) + ".";

        public bool NeedsRestart => _controller.NeedsRestart;

        public string RestartNotice => Phase == ReceiverPhase.Connected
            ? "Thay đổi có hiệu lực sau khi khởi động lại. Mac đang kết nối sẽ bị ngắt."
            : "Thay đổi có hiệu lực sau khi khởi động lại bộ nhận.";

        public string VersionText { get; }

        // ---- settings -----------------------------------------------------------------------

        public string DeviceNameInput
        {
            get => _settings.Current.DeviceName;
            set => Update(s => s.DeviceName = value ?? "");
        }

        public string DeviceNamePlaceholder => DeviceName.DefaultFor(Environment.MachineName);

        public bool IsAutoResolution
        {
            get => _settings.Current.ResolutionMode == ResolutionMode.Auto;
            set { if (value) Update(s => s.ResolutionMode = ResolutionMode.Auto); }
        }

        public bool IsCustomResolution
        {
            get => _settings.Current.ResolutionMode == ResolutionMode.Custom;
            set { if (value) Update(s => s.ResolutionMode = ResolutionMode.Custom); }
        }

        public string CustomWidthText
        {
            get => _settings.Current.CustomWidth.ToString(CultureInfo.InvariantCulture);
            set => UpdateNumber(value, (s, n) => s.CustomWidth = n);
        }

        public string CustomHeightText
        {
            get => _settings.Current.CustomHeight.ToString(CultureInfo.InvariantCulture);
            set => UpdateNumber(value, (s, n) => s.CustomHeight = n);
        }

        public string CustomRefreshText
        {
            get => _settings.Current.CustomRefresh.ToString(CultureInfo.InvariantCulture);
            set => UpdateNumber(value, (s, n) => s.CustomRefresh = n);
        }

        public bool CustomH265
        {
            get => _settings.Current.CustomH265;
            set => Update(s => s.CustomH265 = value);
        }

        public bool IsFps30
        {
            get => _settings.Current.Fps == 30;
            set { if (value) Update(s => s.Fps = 30); }
        }

        public bool IsFps60
        {
            get => _settings.Current.Fps == 60;
            set { if (value) Update(s => s.Fps = 60); }
        }

        public bool AudioEnabled
        {
            get => _settings.Current.AudioEnabled;
            set => Update(s => s.AudioEnabled = value);
        }

        public bool PinEnabled
        {
            get => _settings.Current.PinEnabled;
            set => Update(s => s.PinEnabled = value);
        }

        public string Pin => _settings.Current.Pin;

        /// <summary>The PIN with thin spaces between digits, easier to read off the screen.</summary>
        public string PinDisplay => string.Join("\u2009", Pin.Select(c => c.ToString()));

        public bool StartWithWindows
        {
            get => _settings.Current.StartWithWindows;
            set
            {
                try
                {
                    AutoStart.Set(value);
                }
                catch (Exception e) when (e is UnauthorizedAccessException || e is System.Security.SecurityException)
                {
                    SystemMessage = "Không ghi được cài đặt khởi động cùng Windows.";
                    Raise(nameof(StartWithWindows));
                    return;
                }
                Update(s => s.StartWithWindows = value);
            }
        }

        /// <summary>Re-applies the start-with-Windows entry, for example after the app moved to another folder.</summary>
        public void SyncAutoStart()
        {
            try
            {
                if (_settings.Current.StartWithWindows != AutoStart.IsEnabled()) AutoStart.Set(_settings.Current.StartWithWindows);
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        // ---- system checks ------------------------------------------------------------------

        public CheckState FirewallState
        {
            get => _firewallState;
            private set => Set(ref _firewallState, value);
        }

        public string FirewallText
        {
            get => _firewallText;
            private set => Set(ref _firewallText, value);
        }

        public CheckState NetworkState
        {
            get => _networkState;
            private set => Set(ref _networkState, value);
        }

        public string NetworkText
        {
            get => _networkText;
            private set => Set(ref _networkText, value);
        }

        public bool CanMakePrivate => _publicNetwork != null;

        /// <summary>Result of the last action on this screen (fix firewall, forget Macs and so on).</summary>
        public string SystemMessage
        {
            get => _systemMessage;
            private set => Set(ref _systemMessage, value);
        }

        public async Task RefreshSystemStatusAsync()
        {
            if (_checking) return;
            _checking = true;
            try
            {
                var firewall = await Firewall.GetStatusAsync();
                if (!firewall.IsKnown)
                {
                    FirewallState = CheckState.Unknown;
                    FirewallText = "Không kiểm tra được tường lửa. Nếu Mac không thấy máy này, hãy bấm Cho phép.";
                }
                else if (firewall.Ok)
                {
                    FirewallState = CheckState.Ok;
                    FirewallText = "MacExtend được phép nhận kết nối trong mạng riêng tư.";
                }
                else
                {
                    FirewallState = CheckState.Problem;
                    FirewallText = firewall.HasBlock
                        ? "Một quy tắc tường lửa đang chặn MacExtend, nên Mac không kết nối được."
                        : "MacExtend chưa được phép nhận kết nối, nên Mac sẽ không thấy máy này.";
                }

                var networks = await NetworkProfile.GetAsync();
                _publicNetwork = networks.FirstOrDefault(n => n.IsPublic);
                if (networks.Count == 0)
                {
                    NetworkState = CheckState.Problem;
                    NetworkText = "Máy này chưa kết nối mạng. Hãy vào cùng Wi-Fi với Mac.";
                }
                else if (_publicNetwork != null)
                {
                    NetworkState = CheckState.Problem;
                    NetworkText = _publicNetwork.Alias + " đang là mạng Công cộng. Windows ẩn máy này với các thiết bị khác.";
                }
                else
                {
                    NetworkState = CheckState.Ok;
                    NetworkText = string.Join(", ", networks.Select(n => n.Alias)) + ": mạng Riêng tư.";
                }
                Raise(nameof(CanMakePrivate));
            }
            finally
            {
                _checking = false;
                CommandManager.InvalidateRequerySuggested();
            }
        }

        private async Task FixFirewallAsync()
        {
            _fixing = true;
            SystemMessage = null;
            try
            {
                var result = await Firewall.FixAsync();
                if (result.Cancelled) SystemMessage = "Cần quyền quản trị để cho phép MacExtend qua tường lửa.";
                else if (!result.Succeeded) SystemMessage = "Không thêm được quy tắc tường lửa. Thử lại hoặc thêm thủ công cho uxplay.exe.";
                else SystemMessage = "Đã cho phép MacExtend qua tường lửa.";
            }
            finally
            {
                _fixing = false;
            }
            await RefreshSystemStatusAsync();
        }

        private async Task MakePrivateAsync()
        {
            var network = _publicNetwork;
            if (network == null) return;
            _fixing = true;
            SystemMessage = null;
            try
            {
                var result = await NetworkProfile.MakePrivateAsync(network.InterfaceIndex);
                if (result.Cancelled) SystemMessage = "Cần quyền quản trị để đổi loại mạng.";
                else if (!result.Succeeded) SystemMessage = "Không đổi được loại mạng. Vào Cài đặt Windows, mục Mạng để đổi sang Riêng tư.";
                else SystemMessage = network.Alias + " đã chuyển sang mạng Riêng tư.";
            }
            finally
            {
                _fixing = false;
            }
            await RefreshSystemStatusAsync();
        }

        private void ForgetMacs()
        {
            try
            {
                if (File.Exists(AppPaths.RegisterFile)) File.Delete(AppPaths.RegisterFile);
            }
            catch (IOException)
            {
                SystemMessage = "Không xóa được danh sách Mac đã ghép nối.";
                return;
            }
            if (Phase == ReceiverPhase.Ready || Phase == ReceiverPhase.Starting)
            {
                _controller.RestartAsync();
                SystemMessage = "Đã quên các Mac đã ghép nối. Lần tới Mac sẽ phải nhập mã PIN.";
            }
            else
            {
                SystemMessage = Phase == ReceiverPhase.Connected
                    ? "Đã quên các Mac đã ghép nối. Có hiệu lực sau khi khởi động lại bộ nhận."
                    : "Đã quên các Mac đã ghép nối. Lần tới Mac sẽ phải nhập mã PIN.";
            }
        }

        // ---- helpers -------------------------------------------------------------------------

        private void Update(Action<AppSettings> change)
        {
            change(_settings.Current);
            _settings.Current.Clamp();
            _settings.ScheduleSave();
            _controller.RefreshPreview();
            Raise(SettingNames);
            Raise(StatusNames);
        }

        private void UpdateNumber(string text, Action<AppSettings, int> apply)
        {
            if (int.TryParse((text ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)) Update(s => apply(s, n));
            else Raise(SettingNames); // put the last valid value back into the box
        }

        private static string FormatMode(RequestedVideo video)
        {
            if (video == null) return "";
            var m = video.Mode;
            return string.Format(CultureInfo.InvariantCulture, "{0} × {1}, {2} Hz{3}", m.Width, m.Height, m.RefreshHz, video.H265 ? ", H.265" : "");
        }

        private static string BuildVersionText()
        {
            var version = typeof(MainViewModel).Assembly.GetName().Version;
            var text = "MacExtend " + version.ToString(3);
            try
            {
                if (File.Exists(AppPaths.UxPlayVersionFile))
                {
                    var parts = File.ReadAllLines(AppPaths.UxPlayVersionFile).Where(l => l.Trim().Length > 0).Select(l => l.Trim());
                    text += ", " + string.Join(", ", parts);
                }
            }
            catch (IOException)
            {
            }
            return text + ". Mã nguồn mở theo giấy phép GPLv3.";
        }
    }
}
