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
            nameof(AutoCheckUpdates), nameof(FocusOnConnect), nameof(HideCursor), nameof(Fullscreen), nameof(AlwaysOnTop),
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
        private bool _phoneGuide;

        public MainViewModel(ReceiverController controller, SettingsService settings, Action<string> exitForUpdate)
        {
            _controller = controller;
            _settings = settings;
            Update = new UpdateViewModel(() => _settings.Current.AutoCheckUpdates,
                                         () => _controller.Phase == ReceiverPhase.Connected, exitForUpdate);
            _controller.StateChanged += () =>
            {
                Raise(StatusNames);
                Update.Refresh();
                // state changes without user input do not re-query commands on their own
                CommandManager.InvalidateRequerySuggested();
            };

            StartCommand = new RelayCommand(() => _controller.Start(), () => CanStart);
            StopCommand = new RelayCommand(() => _controller.StopAsync(), () => IsActive);
            RestartCommand = new RelayCommand(() => _controller.RestartAsync(), () => IsActive || CanStart);
            NewPinCommand = new RelayCommand(() => Change(s => s.Pin = Core.Pin.Generate(Rng)), () => PinEnabled);
            ForgetDevicesCommand = new RelayCommand(ForgetDevices);
            FixFirewallCommand = new RelayCommand(() => _ = FixFirewallAsync(), () => FirewallState != CheckState.Ok && !_fixing);
            MakePrivateCommand = new RelayCommand(() => _ = MakePrivateAsync(), () => _publicNetwork != null && !_fixing);
            OpenLogsCommand = new RelayCommand(() => Process.Start("explorer.exe", "\"" + AppPaths.LogDir + "\""));

            VersionText = BuildVersionText();
        }

        public ICommand StartCommand { get; }
        public ICommand StopCommand { get; }
        public ICommand RestartCommand { get; }
        public ICommand NewPinCommand { get; }
        public ICommand ForgetDevicesCommand { get; }
        public ICommand FixFirewallCommand { get; }
        public ICommand MakePrivateCommand { get; }
        public ICommand OpenLogsCommand { get; }

        public UpdateViewModel Update { get; }

        // ---- receiver status ----------------------------------------------------------------

        public ReceiverPhase Phase => _controller.Phase;
        public bool IsActive => Phase == ReceiverPhase.Starting || Phase == ReceiverPhase.Ready || Phase == ReceiverPhase.Connected;
        public bool CanStart => Phase == ReceiverPhase.Stopped || Phase == ReceiverPhase.Failed;
        public string ClientName => _controller.ClientName ?? "Thiết bị";
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
                    case ReceiverPhase.Connected: return "Đang nhận AirPlay";
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
                    case ReceiverPhase.Starting: return "Đang chuẩn bị nhận kết nối AirPlay.";
                    case ReceiverPhase.Ready:
                        return "Trên thiết bị Apple cùng mạng, mở Phản chiếu màn hình và chọn " + EffectiveDeviceName + ".";
                    case ReceiverPhase.Connected:
                        return "Nguồn phát: " + ClientName + ". Màn hình Windows sẽ luôn sáng cho đến khi ngắt kết nối.";
                    case ReceiverPhase.Failed: return _controller.LastError ?? "Xem nhật ký để biết chi tiết.";
                    default: return "Bấm Bắt đầu để nhận kết nối trong Phản chiếu màn hình.";
                }
            }
        }

        public string StepChooseDevice => "Chọn " + EffectiveDeviceName;

        public string ModeText => FormatMode(_controller.EffectiveVideo);

        public string AutoResolutionDetail =>
            "Theo màn hình này: " + FormatMode(ResolutionPolicy.Resolve(new AppSettings(), DisplayInfo.GetPrimary())) + ".";

        public bool NeedsRestart => _controller.NeedsRestart;

        public string RestartNotice => Phase == ReceiverPhase.Connected
            ? "Thay đổi có hiệu lực sau khi khởi động lại. Thiết bị đang kết nối sẽ bị ngắt."
            : "Thay đổi có hiệu lực sau khi khởi động lại bộ nhận.";

        public string VersionText { get; }

        // ---- connect guide ------------------------------------------------------------------

        public bool IsMacGuide
        {
            get => !_phoneGuide;
            set { if (value) ShowPhoneGuide(false); }
        }

        /// <summary>
        /// The connect steps describe an iPhone or iPad instead of a Mac. Only the steps on screen change:
        /// nothing is saved and the receiver is not touched, so switching during a session is harmless.
        /// </summary>
        public bool IsPhoneGuide
        {
            get => _phoneGuide;
            set { if (value) ShowPhoneGuide(true); }
        }

        private void ShowPhoneGuide(bool phone)
        {
            if (Set(ref _phoneGuide, phone, nameof(IsPhoneGuide))) Raise(nameof(IsMacGuide));
        }

        // ---- settings -----------------------------------------------------------------------

        public string DeviceNameInput
        {
            get => _settings.Current.DeviceName;
            set => Change(s => s.DeviceName = value ?? "");
        }

        public string DeviceNamePlaceholder => DeviceName.DefaultFor(Environment.MachineName);

        public bool IsAutoResolution
        {
            get => _settings.Current.ResolutionMode == ResolutionMode.Auto;
            set { if (value) Change(s => s.ResolutionMode = ResolutionMode.Auto); }
        }

        public bool IsCustomResolution
        {
            get => _settings.Current.ResolutionMode == ResolutionMode.Custom;
            set { if (value) Change(s => s.ResolutionMode = ResolutionMode.Custom); }
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
            set => Change(s => s.CustomH265 = value);
        }

        public bool IsFps30
        {
            get => _settings.Current.Fps == 30;
            set { if (value) Change(s => s.Fps = 30); }
        }

        public bool IsFps60
        {
            get => _settings.Current.Fps == 60;
            set { if (value) Change(s => s.Fps = 60); }
        }

        public bool AudioEnabled
        {
            get => _settings.Current.AudioEnabled;
            set => Change(s => s.AudioEnabled = value);
        }

        public bool PinEnabled
        {
            get => _settings.Current.PinEnabled;
            set => Change(s => s.PinEnabled = value);
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
                Change(s => s.StartWithWindows = value);
            }
        }

        public bool FocusOnConnect
        {
            get => _settings.Current.FocusOnConnect;
            set => Change(s => s.FocusOnConnect = value);
        }

        public bool HideCursor
        {
            get => _settings.Current.HideCursor;
            set => Change(s => s.HideCursor = value);
        }

        /// <summary>
        /// How the next session's video window opens. Part of uxplay's arguments, so a change shows the
        /// restart notice and leaves a running session alone. Alt+Enter in the video only switches that
        /// session and is not saved.
        /// </summary>
        public bool Fullscreen
        {
            get => _settings.Current.Fullscreen;
            set => Change(s => s.Fullscreen = value);
        }

        /// <summary>
        /// MacExtend pins the open video window itself, so a change applies at once, also from the tray menu,
        /// and needs no restart. Ignored while the video is full screen.
        /// </summary>
        public bool AlwaysOnTop
        {
            get => _settings.Current.AlwaysOnTop;
            set => Change(s => s.AlwaysOnTop = value);
        }

        public bool AutoCheckUpdates
        {
            get => _settings.Current.AutoCheckUpdates;
            set => Change(s => s.AutoCheckUpdates = value);
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

        /// <summary>Result of the last action on this screen (fix firewall, forget devices and so on).</summary>
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
                    FirewallText = "Không kiểm tra được tường lửa. Nếu thiết bị không thấy máy này, hãy bấm Cho phép.";
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
                        ? "Một quy tắc tường lửa đang chặn MacExtend, nên thiết bị không kết nối được."
                        : "MacExtend chưa được phép nhận kết nối, nên thiết bị có thể không thấy máy này.";
                }

                var networks = NetworkProfileParser.Relevant(await NetworkProfile.GetAsync());
                _publicNetwork = networks.FirstOrDefault(n => n.IsPublic);
                if (networks.Count == 0)
                {
                    NetworkState = CheckState.Problem;
                    NetworkText = "Máy này chưa kết nối mạng. Hãy kết nối cùng mạng với thiết bị phát.";
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

        private void ForgetDevices()
        {
            const string forgotten = "Đã quên các thiết bị đã ghép nối. Nếu bật PIN, thiết bị sẽ phải nhập mã khi ghép nối lại.";
            try
            {
                if (File.Exists(AppPaths.RegisterFile)) File.Delete(AppPaths.RegisterFile);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                SystemMessage = "Không xóa được danh sách thiết bị đã ghép nối.";
                return;
            }
            if (Phase == ReceiverPhase.Ready || Phase == ReceiverPhase.Starting)
            {
                _controller.RestartAsync();
                SystemMessage = forgotten;
            }
            else
            {
                SystemMessage = Phase == ReceiverPhase.Connected
                    ? "Đã quên các thiết bị đã ghép nối. Có hiệu lực sau khi khởi động lại bộ nhận."
                    : forgotten;
            }
        }

        // ---- helpers -------------------------------------------------------------------------

        private void Change(Action<AppSettings> change)
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
            if (int.TryParse((text ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)) Change(s => apply(s, n));
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
