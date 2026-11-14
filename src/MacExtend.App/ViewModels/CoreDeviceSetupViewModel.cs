using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Threading;
using MacExtend.Core;
using MacExtend.Core.CoreDevice;
using MacExtend.CoreDeviceHost;
using MacExtend.Native;
using MacExtend.Services.CoreDevice;
using Microsoft.Win32;

namespace MacExtend.ViewModels
{
    /// <summary>The chip and marker of a step row.</summary>
    internal enum SetupChip
    {
        Done,
        Todo,
        Running,
        Waiting,
        Optional,
    }

    /// <summary>
    /// One row of the setup window. Made once per step and updated in place (<see cref="Update"/>), so a refresh keeps the
    /// rows, their layout and the keyboard focus.
    /// </summary>
    internal sealed class SetupStepItem : ObservableObject
    {
        public SetupStepItem(int number, SetupStepId id, ICommand action)
        {
            Number = number;
            Id = id;
            Action = action;
        }

        public int Number { get; }
        public SetupStepId Id { get; }
        /// <summary>What the step's button does; enabled while nothing else runs.</summary>
        public ICommand Action { get; }
        public bool ShowDivider => Number > 1;

        public SetupStep Step { get; private set; }
        public string Title => Step?.Title ?? "";
        public string Detail => Step?.Detail ?? "";
        public SetupStatus Status => Step?.Status ?? SetupStatus.Waiting;
        /// <summary>The step's own action runs now ("Đang làm").</summary>
        public bool IsRunning { get; private set; }
        /// <summary>The window has read the facts once; before that a row has no chip and no button, only "Đang kiểm tra...".</summary>
        public bool Known { get; private set; }
        public string StatusText => SetupSteps.StatusText(Status, IsRunning);
        public bool ShowChip => Known || IsRunning;

        public SetupChip Chip
        {
            get
            {
                if (IsRunning) return SetupChip.Running;
                if (!Known) return SetupChip.Waiting;
                switch (Status)
                {
                    case SetupStatus.Done: return SetupChip.Done;
                    case SetupStatus.Todo: return SetupChip.Todo;
                    case SetupStatus.Optional: return SetupChip.Optional;
                    default: return SetupChip.Waiting;
                }
            }
        }

        /// <summary>The button's label, from Core (SetupActions), so the step's text names exactly this button.</summary>
        public string ActionText => Step?.ActionText ?? "";
        public bool HasAction => Known && Step?.ActionText != null && !IsRunning;
        /// <summary>The first step still to do: its button is the primary one.</summary>
        public bool IsNext { get; private set; }
        public bool ShowPrimaryAction => HasAction && IsNext;
        public bool ShowSecondaryAction => HasAction && !IsNext;

        public void Update(SetupStep step, bool running, bool next, bool known)
        {
            Step = step;
            IsRunning = running;
            IsNext = next;
            Known = known;
            Raise(nameof(Step), nameof(Title), nameof(Detail), nameof(Status), nameof(IsRunning), nameof(Known), nameof(StatusText), nameof(ShowChip),
                  nameof(Chip), nameof(ActionText), nameof(HasAction), nameof(IsNext), nameof(ShowPrimaryAction), nameof(ShowSecondaryAction));
        }
    }

    /// <summary>
    /// "Thiết lập điều khiển trực tiếp": the one-time steps of <see cref="SetupSteps"/>, each read again on every refresh
    /// (the Developer Disk Image too, N21). One action at a time in the whole app (<see cref="Gate"/>): a window opened
    /// while a closed one's action still runs shows that step busy with its message, and checks again once it is over.
    /// Everything here runs on the UI thread; the checks and commands run on the thread pool and are awaited. Device
    /// commands come back as classified results only, and the elevated setup as its fixed outcome: the window shows
    /// Core's Vietnamese sentences (SetupMessages), never an exception's or a command's own text.
    /// </summary>
    internal sealed class CoreDeviceSetupViewModel : ObservableObject
    {
        // PowerShellRunner has no deadline of its own (Get-NetFirewallRule, Get-ScheduledTask)
        private static readonly TimeSpan WindowsCheckTimeout = TimeSpan.FromSeconds(30);
        // a session's stop: 2 s release, 40 s cleanup, then a tunneld this session started (TunnelController bounds itself)
        private static readonly TimeSpan SessionStopBound = TimeSpan.FromSeconds(120);

        /// <summary>
        /// Shared by every setup window of this process, and a Wi-Fi session adding
        /// its firewall rules: install, rules, pairing, mount and the checks never overlap, even when a window was closed
        /// while its action still runs. The windows take it on the UI thread, the session on the thread pool.
        /// </summary>
        private static SetupGate Gate => CoreDeviceRuntime.SetupGate;

        private readonly Dispatcher _ui;
        private readonly SettingsService _settings;
        private readonly CoreDeviceService _service;
        // cancelled when the window closes: a download in progress stops, and no UAC prompt follows for a closed window
        private readonly CancellationTokenSource _closed = new CancellationTokenSource();
        private SetupFacts _facts = new SetupFacts();
        private bool _checked;
        // the device the switch was turned on for in this window, while it is not paired yet
        private string _wifiOptIn;
        // another window's action holds the gate: check again once it is over, and keep its last message
        private bool _waitingForOther;
        private string _otherMessage = "";
        private double _progress;
        private bool _showProgress;
        private string _message = "";

        public CoreDeviceSetupViewModel(SettingsService settings, CoreDeviceService service)
        {
            _ui = Dispatcher.CurrentDispatcher;
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _service = service ?? throw new ArgumentNullException(nameof(service));
            RefreshCommand = new RelayCommand(() => _ = RefreshAsync(), () => !Busy);
            PickZipCommand = new RelayCommand(() => _ = PickZipAsync(), () => !Busy);
            var number = 0;
            foreach (var step in SetupSteps.Evaluate(_facts)) Steps.Add(new SetupStepItem(++number, step.Id, ActionFor(step.Id)));
            Gate.Changed += OnGateChangedAnyThread;
            if (Gate.IsHeld)
            {
                // a window closed meanwhile still runs its action: show it, and check once it is over
                _waitingForOther = true;
                _otherMessage = Gate.Message;
            }
            Rebuild();
        }

        public ObservableCollection<SetupStepItem> Steps { get; } = new ObservableCollection<SetupStepItem>();
        public ICommand RefreshCommand { get; }
        public ICommand PickZipCommand { get; }

        /// <summary>A setup action runs, in this window or in another (one closed meanwhile).</summary>
        public bool Busy => Gate.IsHeld;

        /// <summary>Fraction 0..1 of the runtime download.</summary>
        public double Progress { get => _progress; private set => Set(ref _progress, value); }

        public bool ShowProgress
        {
            get => _showProgress;
            private set
            {
                if (Set(ref _showProgress, value)) Raise(nameof(ShowSpinner));
            }
        }

        public bool ShowSpinner => Busy && !ShowProgress;

        /// <summary>The status line: this window's, or the message of another window's action while that one runs.</summary>
        public string Message
        {
            get => IsOtherRunning ? Gate.Message : _message;
            private set
            {
                _message = value ?? "";
                Gate.SetMessage(this, _message);
                Raise(nameof(Message), nameof(HasMessage));
            }
        }

        public bool HasMessage => Message.Length > 0;

        private bool IsOtherRunning
        {
            get
            {
                var owner = Gate.Owner;
                return owner != null && !ReferenceEquals(owner, this);
            }
        }

        public bool Ready => SetupSteps.Ready(SetupSteps.Evaluate(_facts));
        public bool ShowReady => _checked && !Busy && Ready;
        public string ReadyText => SetupMessages.Ready;

        /// <summary>"Cho phép điều khiển qua Wi-Fi" for the device on the cable. Off forgets the device for Wi-Fi (its pairing stays).</summary>
        public bool WifiWanted
        {
            get => _facts.WifiWanted && !_facts.StandardAccount;
            set
            {
                var udid = _facts.UsbUdid;
                if (value == WifiWanted || Busy || !SetupSteps.WifiSwitchEnabled(_facts))
                {
                    // refused: the switch shows what is true again
                    Raise(nameof(WifiWanted));
                    return;
                }
                _facts.WifiWanted = value;
                _wifiOptIn = value ? udid : null;
                // on counts at once for a device paired before; otherwise once Ghép nối has paired it
                if (!value || _facts.WifiPaired)
                {
                    _settings.Current.SetWifiDevice(udid, value);
                    _settings.ScheduleSave();
                }
                Rebuild();
            }
        }

        public bool WifiSwitchEnabled => !Busy && SetupSteps.WifiSwitchEnabled(_facts);
        public string WifiHint => SetupMessages.WifiSwitchHint(_facts.StandardAccount, _facts.UsbUdid);

        /// <summary>Reads every step again. Ignored while an action runs.</summary>
        public Task RefreshAsync() => Run(null, SetupMessages.Checking, async () => Message = await CheckAsync());

        /// <summary>
        /// The window closed: a download in progress stops, and no UAC prompt follows. A command or the elevated setup
        /// already started finishes on its own and holds the gate until then.
        /// </summary>
        public void CancelWork()
        {
            _closed.Cancel();
            Gate.Changed -= OnGateChangedAnyThread;
        }

        /// <summary>A Wi-Fi session takes the gate on the thread pool (its firewall rules): this window hears of it on the UI thread.</summary>
        private void OnGateChangedAnyThread()
        {
            if (_ui.CheckAccess()) OnGateChanged();
            else _ = _ui.BeginInvoke(new Action(OnGateChanged));
        }

        /// <summary>Another window's action started, said something or ended. Its own changes this window makes itself.</summary>
        private void OnGateChanged()
        {
            var owner = Gate.Owner;
            if (ReferenceEquals(owner, this)) return;
            if (owner != null)
            {
                _waitingForOther = true;
                _otherMessage = Gate.Message;
            }
            RaiseBusy();
            Rebuild();
            if (owner == null && _waitingForOther && !_closed.IsCancellationRequested)
            {
                _waitingForOther = false;
                _ = CheckAfterOtherAsync(_otherMessage);
            }
        }

        /// <summary>The other window's action is over: read every step again; with nothing new to say, keep its last message.</summary>
        private async Task CheckAfterOtherAsync(string otherMessage)
        {
            // out of the gate's Changed call: the gate was let go inside the other window's own clean-up
            await Task.Yield();
            await Run(null, SetupMessages.Checking, async () =>
            {
                var hint = await CheckAsync();
                Message = hint.Length > 0 ? hint : otherMessage;
            });
        }

        private ICommand ActionFor(SetupStepId id)
        {
            switch (id)
            {
                case SetupStepId.Components: return Command(DownloadAndInstallAsync);
                case SetupStepId.Trust: return Command(ConnectAsync);
                case SetupStepId.DeveloperMode: return Command(RevealAsync);
                case SetupStepId.DiskImage: return Command(MountAsync);
                case SetupStepId.Wifi: return Command(PairWifiAsync);
                // the Apple service and the cable only need a new look
                default: return RefreshCommand;
            }
        }

        private ICommand Command(Func<Task> action) => new RelayCommand(() => _ = action(), () => !Busy);

        // ---- the checks ------------------------------------------------------------------

        /// <summary>
        /// Every fact read again, the image too (N21). The rows keep showing what was known about the same device until its
        /// new answer is in. Returns what the status line should say once done ("" for nothing).
        /// </summary>
        private async Task<string> CheckAsync()
        {
            var hint = "";
            var local = await Task.Run(() => new
            {
                Current = CoreDeviceRuntime.IsCurrent,
                WithoutTask = !CoreDeviceRuntime.TaskAllowed,
                Admin = Elevation.IsAdminAccount,
                // a TCP connect of up to 500 ms
                Apple = CoreDeviceCommands.AppleServiceReachable(),
            });
            var next = new SetupFacts
            {
                RuntimeInstalled = local.Current,
                WithoutTask = local.WithoutTask,
                StandardAccount = !local.Admin,
                AppleServiceReachable = local.Apple,
            };
            if (next.RuntimeInstalled)
            {
                var windows = await WindowsSetupDoneAsync();
                next.WindowsSetupDone = windows == true;
                if (windows == null) hint = SetupMessages.WindowsCheckTimedOut;
            }
            if (next.RuntimeInstalled && next.AppleServiceReachable)
            {
                // the Python listing, like the session's device choice, so the UDID is written the same way everywhere
                var usb = await UsbDevicesAsync();
                next.UsbUdid = TransportChoice.PickDevice(usb, null, false, _settings.Current.DirectLastDevice) ?? usb.FirstOrDefault();
            }

            var udid = next.UsbUdid;
            if (udid != null)
            {
                var sameDevice = string.Equals(udid, _facts.UsbUdid, StringComparison.OrdinalIgnoreCase);
                if (sameDevice)
                {
                    next.Trusted = _facts.Trusted;
                    next.DeveloperMode = _facts.DeveloperMode;
                    next.DiskImageMounted = _facts.DiskImageMounted;
                    next.WifiPaired = _facts.WifiPaired;
                }
                next.WifiWanted = WifiWantedFor(udid);
                // what comes before the device is known now
                _facts = next;
                _checked = true;
                // developer-mode-status pairs first: the Trust dialog may be on the device now, waited for up to 120 s
                if (next.Trusted != true) Gate.SetStep(this, SetupStepId.Trust);
                Message = SetupMessages.ReadingDevice;
                Rebuild();

                // found or mounted earlier in this window, for this device
                var imageSeen = next.DiskImageMounted;
                var status = await RunCommandAsync(() => CoreDeviceCommands.DeveloperModeAsync(udid));
                next.Trusted = DeviceQueries.Trusted(status);
                next.DeveloperMode = status.Succeeded ? DeviceQueries.ParseBool(status.Stdout) : null;
                if (next.Trusted == true && next.DeveloperMode == true)
                {
                    // the list may only turn the step done: from iOS 27 the image mounter may not list the cryptex DDI,
                    // so a list without it never undoes a mount that worked in this window
                    var listed = await DetectImageAsync(udid);
                    if (imageSeen && listed == false) Log("image list does not show the image this window saw or mounted; the step stays done");
                    next.DiskImageMounted = listed == true || imageSeen;
                }
                else
                {
                    next.DiskImageMounted = false;
                }
                next.WifiPaired = CoreDeviceCommands.RemotePaired(udid);
                Log("developer mode status: " + Describe(status));
                if (hint.Length == 0) hint = SetupMessages.Trust(status) ?? "";
            }
            _facts = next;
            _checked = true;
            Log("check: " + string.Join(", ", SetupSteps.Evaluate(next).Select(s => s.Id + " " + s.Status)));
            return hint;
        }

        /// <summary>The task and the firewall rules; null when PowerShell did not answer in time.</summary>
        private static async Task<bool?> WindowsSetupDoneAsync()
        {
            var check = Task.Run(() => WindowsSetupState.IsDoneAsync());
            if (await Task.WhenAny(check, Task.Delay(WindowsCheckTimeout)) != check)
            {
                // it goes on until PowerShell exits; whatever it ends with must not reach the crash log
                _ = check.ContinueWith(t => { var ignored = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
                Log("Windows part: no answer from PowerShell");
                return null;
            }
            try
            {
                return await check;
            }
            catch (Exception e) when (e is InvalidOperationException || e is IOException || e is Win32Exception)
            {
                return false;
            }
        }

        private static async Task<IReadOnlyList<string>> UsbDevicesAsync()
        {
            try
            {
                return await CoreDeviceCommands.UsbDevicesAsync();
            }
            catch (CoreDeviceHostException)
            {
                // the runtime's python.exe could not be started: nothing listed
                return new string[0];
            }
        }

        /// <summary>`mounter list`: true when the developer disk image is mounted; null when the device could not say.</summary>
        private static async Task<bool?> DetectImageAsync(string udid)
        {
            var listed = await RunCommandAsync(() => CoreDeviceCommands.MountedImagesAsync(udid));
            var mounted = listed.Succeeded ? DeviceQueries.ParseDeveloperImageMounted(listed.Stdout) : null;
            Log("image list: " + Describe(listed) + ", mounted " + (mounted == null ? "unknown" : mounted.Value ? "yes" : "no"));
            return mounted;
        }

        /// <summary>A short command; one that could not even start (python.exe missing, no job) counts as failed.</summary>
        private static async Task<CommandResult> RunCommandAsync(Func<Task<CommandResult>> command)
        {
            try
            {
                return await command();
            }
            catch (CoreDeviceHostException)
            {
                return new CommandResult(-1, "", CliError.Other, false);
            }
        }

        private bool WifiWantedFor(string udid) =>
            _settings.Current.WifiDevices().Contains(udid, StringComparer.OrdinalIgnoreCase)
            || string.Equals(_wifiOptIn, udid, StringComparison.OrdinalIgnoreCase);

        // ---- the actions -----------------------------------------------------------------

        /// <summary>The Trust step: developer-mode-status shows the Trust dialog and waits for it, so a new check is the action.</summary>
        private Task ConnectAsync() => Run(SetupStepId.Trust, SetupMessages.ReadingDevice, async () => Message = await CheckAsync());

        private Task DownloadAndInstallAsync() => Run(SetupStepId.Components, SetupMessages.Checking, async () =>
        {
            var state = await Task.Run(() => new { Current = CoreDeviceRuntime.IsCurrent, HasPackage = CoreDeviceRuntime.Package != null });
            string zip = null;
            if (!state.Current)
            {
                if (!state.HasPackage)
                {
                    // a local build knows no runtime zip to fetch
                    Message = CoreDeviceRuntime.NoMetadataMessage;
                    return;
                }
                Progress = 0;
                ShowProgress = true;
                Message = SetupMessages.Downloading(0);
                // made here, so its reports come back to this (UI) thread; a late report after the download must not
                // overwrite the next message
                var progress = new Progress<double>(p =>
                {
                    if (!ShowProgress) return;
                    Progress = p;
                    Message = SetupMessages.Downloading(p);
                });
                try
                {
                    // off the UI thread: the download first hashes a zip already there (about 100 MB), before its first await
                    zip = await Task.Run(() => CoreDeviceRuntime.DownloadAsync(progress, _closed.Token));
                }
                catch (Exception e) when (e is HttpRequestException || e is IOException || e is InvalidDataException || e is UnauthorizedAccessException
                                          || e is OperationCanceledException || e is InvalidOperationException)
                {
                    Log("download failed: " + e.GetType().Name);
                    Message = SetupMessages.DownloadFailed(e, CoreDeviceRuntime.Package?.File);
                    return;
                }
                finally
                {
                    ShowProgress = false;
                }
            }
            await InstallAsync(zip);
        });

        /// <summary>A runtime zip from a CI artifact (a beta build has no release): checked against this build before any UAC.</summary>
        private async Task PickZipAsync()
        {
            if (Busy) return;
            var package = CoreDeviceRuntime.Package;
            if (package == null)
            {
                Message = CoreDeviceRuntime.NoMetadataMessage;
                return;
            }
            var dialog = new OpenFileDialog
            {
                Title = "Chọn gói runtime " + package.File,
                Filter = "Gói runtime (*-coredevice-runtime.zip)|*-coredevice-runtime.zip|Tệp zip (*.zip)|*.zip",
                CheckFileExists = true,
            };
            // owned by the active window: the setup window, whose button opened it
            if (dialog.ShowDialog() != true) return;
            var path = dialog.FileName;
            await Run(SetupStepId.Components, SetupMessages.CheckingZip, async () =>
            {
                try
                {
                    // hashes about 100 MB
                    await Task.Run(() => CoreDeviceRuntime.CheckZip(path));
                }
                catch (Exception e) when (e is InvalidDataException || e is IOException || e is UnauthorizedAccessException || e is ArgumentException
                                          || e is NotSupportedException || e is InvalidOperationException)
                {
                    Log("picked zip refused: " + e.GetType().Name);
                    Message = SetupMessages.PickedZipFailed(e);
                    return;
                }
                await InstallAsync(path);
            });
        }

        /// <summary>
        /// The one-time setup (CoreDeviceRuntime.SetupAsync): one UAC for a copy in Program Files, none for any other. A
        /// new runtime replaces files a session holds (serve-web's python.exe, a tunneld it started), so a running
        /// session is stopped first; its stop also shuts down a tunneld this app started, by the task or by UAC. A tunneld
        /// the app did not start is never stopped (Review Focus 3): if it holds the files, the install fails safely and the
        /// previous runtime stays.
        /// </summary>
        /// <param name="zip">null keeps the installed runtime (only Windows' part is made again).</param>
        private async Task InstallAsync(string zip)
        {
            if (_closed.IsCancellationRequested) return;
            if (zip != null && _service.IsActive)
            {
                Message = SetupMessages.StoppingSession;
                await Task.WhenAny(_service.StopAsync(), Task.Delay(SessionStopBound));
            }
            var elevated = await Task.Run(() => CoreDeviceRuntime.TaskAllowed);
            // closed during the session's stop: no UAC for a window that is gone
            if (_closed.IsCancellationRequested) return;
            Message = SetupMessages.Installing(elevated);
            // its outcome is logged by SetupAsync itself
            var outcome = await CoreDeviceRuntime.SetupAsync(zip);
            Message = SetupMessages.Installed(outcome);
            if (!SetupMessages.IsOk(outcome)) return;
            var hint = await CheckAsync();
            if (hint.Length > 0) Message = hint;
        }

        private Task RevealAsync() => Run(SetupStepId.DeveloperMode, SetupMessages.Revealing, async () =>
        {
            var udid = _facts.UsbUdid;
            if (udid == null) return;
            var r = await RunCommandAsync(() => CoreDeviceCommands.RevealDeveloperModeAsync(udid));
            Log("reveal developer mode: " + Describe(r));
            Message = SetupMessages.Reveal(r);
        });

        /// <summary>
        /// auto-mount over the cable's userspace tunnel, then N21: the device's list is read again. Either one is enough,
        /// since auto-mount succeeds for an image already there and from iOS 27 the list may not show the cryptex DDI.
        /// </summary>
        private Task MountAsync() => Run(SetupStepId.DiskImage, SetupMessages.Mounting, async () =>
        {
            var udid = _facts.UsbUdid;
            if (udid == null) return;
            var r = await RunCommandAsync(() => CoreDeviceCommands.MountAsync(udid, CoreDeviceRoute.Usb));
            Log("mount: " + Describe(r));
            var detected = await DetectImageAsync(udid);
            if (r.Succeeded && detected == false) Log("mount ok, but the image list does not show the image");
            if (!r.Succeeded && detected == true) Log("mount failed, but the image list shows the image");
            _facts.DiskImageMounted = detected == true || r.Succeeded;
            Message = SetupMessages.Mount(r, detected);
        });

        /// <summary>
        /// RemotePairing over the cable; the device is then on the Wi-Fi list. A copy without the task also gets its four
        /// firewall rules now (one UAC); its first Wi-Fi session of each run adds them again by itself (TunnelController).
        /// </summary>
        private Task PairWifiAsync() => Run(SetupStepId.Wifi, SetupMessages.Pairing, async () =>
        {
            var udid = _facts.UsbUdid;
            if (udid == null) return;
            var r = await RunCommandAsync(() => CoreDeviceCommands.RemotePairAsync(udid));
            var paired = CoreDeviceCommands.RemotePaired(udid);
            Log("Wi-Fi pairing: " + Describe(r) + ", record " + (paired ? "present" : "missing"));
            _facts.WifiPaired = paired;
            string rules = null;
            if (paired)
            {
                _settings.Current.SetWifiDevice(udid, true);
                _settings.ScheduleSave();
                if (_facts.WithoutTask && !_closed.IsCancellationRequested)
                {
                    Message = SetupMessages.AddingRules;
                    // logged by AddFirewallRulesAsync itself
                    rules = await CoreDeviceRuntime.AddFirewallRulesAsync();
                }
            }
            Message = SetupMessages.Pair(r, paired, rules);
        });

        // ---- plumbing --------------------------------------------------------------------

        /// <summary>
        /// One action at a time in the whole app (<see cref="Gate"/>): the buttons of every setup window are disabled until
        /// it is over, and its row shows "Đang làm". Ignored while another action runs.
        /// </summary>
        private async Task Run(SetupStepId? step, string message, Func<Task> work)
        {
            if (!Gate.TryEnter(this, step, message)) return;
            Message = message;
            RaiseBusy();
            Rebuild();
            try
            {
                await work();
            }
            finally
            {
                ShowProgress = false;
                Gate.Exit(this);
                RaiseBusy();
                Rebuild();
            }
        }

        private void RaiseBusy()
        {
            Raise(nameof(Busy), nameof(ShowSpinner), nameof(ShowReady), nameof(WifiSwitchEnabled), nameof(Message), nameof(HasMessage));
            CommandManager.InvalidateRequerySuggested();
        }

        private void Rebuild()
        {
            var steps = SetupSteps.Evaluate(_facts);
            var next = steps.FirstOrDefault(s => s.ActionText != null)?.Id;
            // the running step is the gate's, whichever window runs it
            var running = Gate.Step;
            foreach (var item in Steps) item.Update(steps.Single(s => s.Id == item.Id), running == item.Id, next == item.Id, _checked);
            Raise(nameof(Ready), nameof(ShowReady), nameof(WifiWanted), nameof(WifiSwitchEnabled), nameof(WifiHint));
        }

        /// <summary>A command's classified outcome for the log: never its output.</summary>
        private static string Describe(CommandResult r) => r.Succeeded ? "ok" : r.TimedOut ? "timed out" : "failed (" + r.Error + ")";

        /// <summary>coredevice.log, opened per line like the session's and the runtime's writers of the same file. No UDID.</summary>
        private static void Log(string line)
        {
            try
            {
                using (var log = new RotatingLog(AppPaths.CoreDeviceLog, 512 * 1024)) log.WriteLine("[setup] " + line);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                // a log that cannot be written never fails the setup
            }
        }
    }
}
