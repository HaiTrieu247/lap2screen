using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using MacExtend.Core;
using MacExtend.Core.CoreDevice;
using MacExtend.CoreDeviceHost;
using MacExtend.ViewModels;
using MacExtend.Views;

namespace MacExtend.Services.CoreDevice
{
    /// <summary>
    /// "Điều khiển trực tiếp": while the switch is on and an iPhone or iPad mirrors, a CoreDevice session (serve-web, no
    /// window of its own) starts by itself for that device, and the AirPlay picture takes the mouse and the keyboard
    /// through <see cref="DirectOverlay"/>. The device is the only one reachable, the one picked before for this AirPlay
    /// name, the only one on the cable named or built like it, or the user's pick. The session ends when mirroring has
    /// been over for <see cref="DirectLifetime.Linger"/>, the switch goes off, another device takes over the mirroring,
    /// or the app exits; the cable leaving and coming back is the session's own business. A failure is shown and not
    /// retried in a loop: the next mirroring session, "Thử lại", or the setup window closing starts again. AirPlay never
    /// depends on any of this. All members are used on the UI thread; the device look-up runs on the thread pool and
    /// what it posts back is dropped unless it is still the current one.
    /// </summary>
    internal sealed class DirectControlController : IDisposable
    {
        // PowerShellRunner has no deadline of its own (Get-NetFirewallRule, Get-ScheduledTask)
        private static readonly TimeSpan WindowsCheckTimeout = TimeSpan.FromSeconds(30);
        /// <summary>A press on a landscape picture asks again how the device is turned once the last answer is this old.</summary>
        private static readonly TimeSpan OrientationAge = TimeSpan.FromSeconds(5);

        private readonly SettingsService _settings;
        private readonly ReceiverController _receiver;
        private readonly AirPlayPresenter _presenter;
        private readonly CoreDeviceService _service;
        private readonly Dispatcher _ui;
        private readonly DirectLifetime _lifetime = new DirectLifetime(() => DateTime.UtcNow);
        private readonly DispatcherTimer _recheck;
        private DirectView _view = DirectView.Off;
        private IReadOnlyList<DeviceChoice> _choices = new DeviceChoice[0];
        private DirectOverlay _overlay;
        private DirectInput _input;
        private CoreDeviceSetupWindow _setup;
        private bool _wasMobile;
        private string _lastClient;
        // the look-up in progress, so a late answer of an old one is dropped
        private int _generation;
        private bool _finding;
        // the AirPlay name the look-up in progress is for
        private string _lastFindingFor;
        // the AirPlay name the running session was started for; null while none runs
        private string _sessionFor;
        private ScreenOrientation _reported = ScreenOrientation.Unknown;
        private DateTime _reportedAt;
        private PictureShape _shape = PictureShape.Unknown;
        private bool _asking;
        private bool _askAgain;
        private bool _halted;
        private bool _disposed;

        public DirectControlController(SettingsService settings, ReceiverController receiver, AirPlayPresenter presenter, CoreDeviceService service, Dispatcher ui)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _receiver = receiver ?? throw new ArgumentNullException(nameof(receiver));
            _presenter = presenter ?? throw new ArgumentNullException(nameof(presenter));
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _ui = ui ?? throw new ArgumentNullException(nameof(ui));
            _recheck = new DispatcherTimer(DirectLifetime.Linger + TimeSpan.FromMilliseconds(500), DispatcherPriority.Normal, (_, __) =>
            {
                _recheck.Stop();
                Refresh();
            }, ui);
            _recheck.Stop();
            _receiver.StateChanged += Refresh;
            _presenter.VideoChanged += Refresh;
            _service.StateChanged += OnServiceChanged;
        }

        /// <summary>Raised on the UI thread whenever anything shown to the user may have changed.</summary>
        public event Action StateChanged;

        public DirectView View => _view;

        public SessionView Session => _service.View;

        public string StatusText => DirectControlTexts.Status(_view, _service.View, CoreDeviceRuntime.IsCurrent);

        /// <summary>The AirPlay video window takes touches now, or will once the session is up; IntPtr.Zero otherwise.</summary>
        public IntPtr TouchVideo => Touching || Connecting ? _presenter.Video : IntPtr.Zero;

        /// <summary>The devices to pick from while <see cref="DirectPhase.Choose"/>.</summary>
        public IReadOnlyList<DeviceChoice> Choices => _choices;

        public bool CanRetry => !_finding && _sessionFor == null && Mobile && (_view.Phase == DirectPhase.Failed || _view.Phase == DirectPhase.Choose);

        private bool Enabled => _settings.Current.DirectControl;

        private bool Mobile => _receiver.Phase == ReceiverPhase.Connected && DirectLifetime.IsMobileModel(_receiver.ClientModel);

        private bool Touching => _view.Phase == DirectPhase.Running && _service.Phase == SessionPhase.Ready && _service.Port != 0;

        /// <summary>The device mirroring is being looked for, or its session is starting: the picture says so.</summary>
        private bool Connecting => !Touching && Enabled && Mobile && (_finding || (_view.Phase == DirectPhase.Running && _sessionFor != null));

        /// <summary>Re-evaluates everything after a change: the setting, the receiver, the video window, the session.</summary>
        public void Refresh()
        {
            if (_disposed) return;
            var enabled = Enabled;
            var mobile = Mobile;
            var run = _lifetime.ShouldRun(enabled, mobile);
            var client = mobile ? _receiver.ClientName : null;
            // a new mirroring session, or another device taking it over (-nohold) without a gap in between
            var newMirroring = mobile && (!_wasMobile || !string.Equals(client, _lastClient, StringComparison.Ordinal));
            _wasMobile = mobile;
            if (mobile) _lastClient = client;

            if (!enabled)
            {
                StopSession("switched off");
                SetView(DirectView.Off);
            }
            else if (!run)
            {
                StopSession("mirroring is over");
                SetView(DirectView.Of(DirectPhase.Waiting));
            }
            else
            {
                if (!mobile && !_recheck.IsEnabled) _recheck.Start();
                // another device took over the mirroring: the session controls the wrong one
                var current = _sessionFor ?? (_finding ? _lastFindingFor : null);
                if (current != null && mobile && !string.Equals(current, client, StringComparison.Ordinal))
                {
                    StopSession("another device mirrors now");
                    newMirroring = true;
                }
                var phase = _view.Phase;
                if (!_finding && _sessionFor == null && mobile && (phase == DirectPhase.Off || phase == DirectPhase.Waiting || newMirroring))
                {
                    Begin();
                }
            }

            UpdateShape();
            UpdateOverlay();
            StateChanged?.Invoke();
        }

        /// <summary>"Thử lại" after a failure, or a new look once the user has done what the failure said.</summary>
        public void Retry()
        {
            if (!CanRetry) return;
            Log("retry asked for");
            Begin();
        }

        /// <summary>The user picked <paramref name="udid"/> for the device mirroring; remembered for its AirPlay name.</summary>
        public void Choose(string udid)
        {
            if (_disposed || !CoreDeviceOptions.IsValidUdid(udid)) return;
            var s = _settings.Current;
            s.DirectDevices = RememberedDevices.Remember(s.DirectDevices, _receiver.ClientName, udid);
            _settings.ScheduleSave();
            if (!_finding && _sessionFor == null && Mobile) Begin();
        }

        /// <summary>The one-time setup window; brought forward when it is open already.</summary>
        public void OpenSetup()
        {
            if (_disposed || _halted) return;
            if (_setup != null)
            {
                if (_setup.WindowState == WindowState.Minimized) SystemCommands.RestoreWindow(_setup);
                _setup.Activate();
                return;
            }
            var setup = new CoreDeviceSetupWindow(new CoreDeviceSetupViewModel(_settings, _service));
            setup.Closed += (_, __) =>
            {
                if (_setup == setup) _setup = null;
                // what the setup fixed may let a session start now
                if (CanRetry) Begin();
                StateChanged?.Invoke();
            };
            _setup = setup;
            setup.Show();
        }

        /// <summary>
        /// The app exits or updates: the session stops and nothing starts again until <see cref="Resume"/> (an update
        /// replaces the runtime's files). The task ends when serve-web and a tunneld the session started are gone.
        /// </summary>
        public Task StopAsync()
        {
            _halted = true;
            _setup?.Close();
            StopSession("the app stops");
            return _service.StopAsync();
        }

        /// <summary>The update did not go ahead after <see cref="StopAsync"/>: starts are allowed again.</summary>
        public void Resume()
        {
            if (!_halted || _disposed) return;
            _halted = false;
            Refresh();
        }

        public void Dispose()
        {
            if (_disposed) return;
            StopSession("the app exits");
            _disposed = true;
            _receiver.StateChanged -= Refresh;
            _presenter.VideoChanged -= Refresh;
            _service.StateChanged -= OnServiceChanged;
            _recheck.Stop();
            _overlay?.Close();
            _overlay = null;
        }

        // ---- finding the device ------------------------------------------------------------

        private void Begin()
        {
            if (_disposed || _halted || _finding || _sessionFor != null) return;
            var generation = ++_generation;
            _finding = true;
            _lastFindingFor = _receiver.ClientName;
            _choices = new DeviceChoice[0];
            var name = _receiver.ClientName;
            var model = _receiver.ClientModel;
            var s = _settings.Current;
            var wifi = s.WifiDevices();
            var remembered = RememberedDevices.Remembered(s.DirectDevices, name);
            Log("#" + generation + " looking for the device mirroring (" + (model ?? "?") + ")");
            SetView(DirectView.Of(DirectPhase.Finding));
            _ = FindAsync(generation, wifi, remembered, name, model);
        }

        private async Task FindAsync(int generation, IReadOnlyList<string> configuredWifi, string remembered, string name, string model)
        {
            DirectOpenPlan plan;
            bool runtime;
            try
            {
                plan = await Task.Run(() => PlanAsync(configuredWifi, remembered, name, model));
                runtime = CoreDeviceRuntime.IsCurrent;
            }
            catch (Exception e)
            {
                // a bug or a system failure: logged by type, never left as an unobserved task
                Log("#" + generation + " look-up failed: unexpected " + e.GetType().Name);
                plan = DirectOpenPlan.Problem(SessionFailure.Other);
                runtime = true;
            }
            if (_disposed || generation != _generation) return;
            _finding = false;
            if (_halted || !Enabled || !Mobile)
            {
                Refresh();
                return;
            }
            switch (plan.Step)
            {
                case DirectOpenStep.Setup:
                    // the status says "Chưa thiết lập" while the runtime is missing; otherwise the Apple service is
                    Log("#" + generation + " nothing can reach a device yet (" + (runtime ? "no Apple service, no Wi-Fi device" : "no runtime") + ")");
                    SetView(DirectView.Failed(SessionFailure.NoAppleService));
                    break;
                case DirectOpenStep.Problem:
                    Log("#" + generation + " " + plan.Failure);
                    SetView(DirectView.Failed(plan.Failure));
                    break;
                case DirectOpenStep.Pick:
                    Log("#" + generation + " " + plan.Choices.Count + " devices and none known for this one: the user picks");
                    _choices = plan.Choices;
                    SetView(DirectView.Of(DirectPhase.Choose));
                    break;
                default:
                    StartSession(generation, plan.Udid, plan.Route);
                    break;
            }
            Refresh();
        }

        /// <summary>The devices now, then the device mirroring among them. On the thread pool; a few seconds at most, except PowerShell.</summary>
        private static async Task<DirectOpenPlan> PlanAsync(IReadOnlyList<string> configuredWifi, string remembered, string name, string model)
        {
            // synchronous, up to 500 ms
            var appleService = CoreDeviceCommands.AppleServiceReachable();
            if (DirectOpen.NeedsSetupFirst(CoreDeviceRuntime.IsCurrent, appleService, configuredWifi.Count)) return DirectOpenPlan.Setup;
            var usb = appleService ? await UsbDevicesAsync().ConfigureAwait(false) : new string[0];
            var wifi = configuredWifi.Where(CoreDeviceCommands.RemotePaired).ToList();
            // the PowerShell check only when its answer could change what opens
            var windowsDone = !DirectOpen.NeedsWindowsCheck(usb, wifi, null) || await WindowsSetupDoneAsync().ConfigureAwait(false);
            var plan = DirectOpen.Plan(usb, wifi, windowsDone, null);
            if (plan.Step != DirectOpenStep.Pick) return plan;
            plan = DirectTarget.Resolve(plan, remembered, null, name, model);
            // what the cable devices are called: one lockdown query per device, only when it can settle the choice
            if (plan.Step == DirectOpenStep.Pick && usb.Count > 0) plan = DirectTarget.Resolve(plan, null, await NamedAsync().ConfigureAwait(false), name, model);
            return plan;
        }

        private static async Task<IReadOnlyList<string>> UsbDevicesAsync()
        {
            try
            {
                return await CoreDeviceCommands.UsbDevicesAsync().ConfigureAwait(false);
            }
            catch (CoreDeviceHostException)
            {
                // the runtime's python.exe could not be started in a job: no device on the cable as far as we can tell
                return new string[0];
            }
        }

        private static async Task<IReadOnlyList<UsbDevice>> NamedAsync()
        {
            try
            {
                return await CoreDeviceCommands.NamedUsbDevicesAsync().ConfigureAwait(false);
            }
            catch (CoreDeviceHostException)
            {
                return new UsbDevice[0];
            }
        }

        /// <summary>
        /// Windows' part of Wi-Fi. No answer in time counts as in place: the session then tries Wi-Fi and says itself
        /// what is missing, instead of a prompt to set up what may well be there.
        /// </summary>
        private static async Task<bool> WindowsSetupDoneAsync()
        {
            var check = Task.Run(() => WindowsSetupState.IsDoneAsync());
            if (await Task.WhenAny(check, Task.Delay(WindowsCheckTimeout)).ConfigureAwait(false) != check)
            {
                // it goes on until PowerShell exits; whatever it ends with must not reach the crash log
                _ = check.ContinueWith(t => { var ignored = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
                return true;
            }
            return await check.ConfigureAwait(false);
        }

        // ---- the session -------------------------------------------------------------------

        private void StartSession(int generation, string udid, CoreDeviceRoute route)
        {
            Log("#" + generation + " device …" + udid.Substring(udid.Length - 4) + " " + route);
            _sessionFor = _receiver.ClientName ?? "";
            _settings.Current.DirectLastDevice = udid;
            _settings.ScheduleSave();
            _reported = ScreenOrientation.Unknown;
            SetView(DirectView.Of(DirectPhase.Running));
            _service.Start(udid, route);
        }

        private void StopSession(string why)
        {
            if (_finding)
            {
                // its answer is dropped
                _generation++;
                _finding = false;
            }
            DropInput();
            if (_sessionFor == null) return;
            Log("stop: " + why);
            _sessionFor = null;
            _ = _service.StopAsync();
        }

        private void OnServiceChanged()
        {
            if (_disposed) return;
            if (_sessionFor != null)
            {
                var phase = _service.Phase;
                if (phase == SessionPhase.Failed || (!_service.IsActive && phase != SessionPhase.Idle))
                {
                    // over by itself: what it ended with is shown, nothing starts again until a retry or a new mirroring
                    var failure = phase == SessionPhase.Failed ? _service.Failure : SessionFailure.Exited;
                    Log("session over: " + failure);
                    _sessionFor = null;
                    DropInput();
                    SetView(DirectView.Failed(failure == SessionFailure.None ? SessionFailure.Exited : failure));
                }
                else if (phase == SessionPhase.Ready && _reported == ScreenOrientation.Unknown)
                {
                    AskOrientation();
                }
            }
            UpdateOverlay();
            StateChanged?.Invoke();
        }

        // ---- the picture -------------------------------------------------------------------

        private void UpdateShape()
        {
            var shape = TouchMap.Shape(_receiver.PictureWidth, _receiver.PictureHeight);
            if (shape == _shape) return;
            var turned = _shape != PictureShape.Unknown && shape != PictureShape.Unknown;
            _shape = shape;
            // the picture turned between portrait and landscape: which way it went is SpringBoard's to say
            if (turned && Touching) AskOrientation();
        }

        private void UpdateOverlay()
        {
            if (_disposed) return;
            var video = _presenter.Video;
            var touching = Touching;
            if (!touching) DropInput();
            if (video == IntPtr.Zero || (!touching && !Connecting))
            {
                _overlay?.Detach();
                _presenter.SetTouch(IntPtr.Zero);
                return;
            }
            if (touching && (_input == null || _input.Port != _service.Port))
            {
                DropInput();
                var input = new DirectInput(_service.Port, () => TouchMap.Effective(_reported, _shape), line => Log("input: " + line));
                input.MediaBusy += () => _ui.BeginInvoke(new Action(() =>
                {
                    if (_input != input) return;
                    Log("the device's camera or microphone is in use: input waits");
                    _overlay?.ShowNotice(DirectControlTexts.MediaBusy);
                }));
                _input = input;
            }
            _presenter.SetTouch(video);
            var overlay = Overlay();
            overlay.SetPicture(_receiver.PictureWidth, _receiver.PictureHeight);
            // while the session starts the picture says so and takes nothing
            overlay.Attach(video, touching ? _input : null, _receiver.ClientName);
        }

        private void DropInput()
        {
            if (_input == null) return;
            _overlay?.Detach();
            _input.Dispose();
            _input = null;
        }

        private DirectOverlay Overlay()
        {
            if (_overlay != null) return _overlay;
            _overlay = new DirectOverlay();
            _overlay.Pressed += () =>
            {
                // a turn from one landscape to the other keeps the picture's size: a press asks again once in a while
                if (_shape != PictureShape.Portrait && DateTime.UtcNow - _reportedAt > OrientationAge) AskOrientation();
            };
            return _overlay;
        }

        /// <summary>One SpringBoard question at a time; a request meanwhile asks once more after it.</summary>
        private void AskOrientation()
        {
            if (_asking)
            {
                _askAgain = true;
                return;
            }
            var udid = _service.Udid;
            var route = _service.Route;
            if (udid == null || route == CoreDeviceRoute.None) return;
            _asking = true;
            _ = AskOrientationAsync(udid, route);
        }

        private async Task AskOrientationAsync(string udid, CoreDeviceRoute route)
        {
            var answer = ScreenOrientation.Unknown;
            try
            {
                var r = await CoreDeviceCommands.OrientationAsync(udid, route);
                answer = r.Succeeded ? TouchMap.ParseOrientation(r.Stdout) : ScreenOrientation.Unknown;
                if (!r.Succeeded) Log("orientation: " + (r.TimedOut ? "timed out" : "failed (" + r.Error + ")"));
            }
            catch (CoreDeviceHostException)
            {
                Log("orientation: the query could not run");
            }
            _asking = false;
            if (_disposed) return;
            if (answer != ScreenOrientation.Unknown && string.Equals(udid, _service.Udid, StringComparison.OrdinalIgnoreCase))
            {
                if (answer != _reported) Log("orientation " + answer);
                _reported = answer;
                _reportedAt = DateTime.UtcNow;
            }
            if (_askAgain && Touching)
            {
                _askAgain = false;
                AskOrientation();
            }
            _askAgain = false;
        }

        private void SetView(DirectView view)
        {
            var changed = view.Phase != _view.Phase || view.Failure != _view.Failure;
            _view = view;
            if (changed) StateChanged?.Invoke();
        }

        /// <summary>coredevice.log, opened per line like the session's writer of the same file. Never a full UDID, never a key.</summary>
        private static void Log(string line)
        {
            try
            {
                using (var log = new RotatingLog(AppPaths.CoreDeviceLog, 512 * 1024)) log.WriteLine("[direct] " + line);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                // a log that cannot be written never stops control
            }
        }
    }
}
