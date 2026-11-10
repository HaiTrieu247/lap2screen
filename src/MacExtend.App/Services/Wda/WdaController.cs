using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using MacExtend.Core;
using MacExtend.Core.CoreDevice;
using MacExtend.Core.Wda;
using MacExtend.CoreDeviceHost;
using MacExtend.Services.CoreDevice;
using MacExtend.Views;

namespace MacExtend.Services.Wda
{
    /// <summary>
    /// "Điều khiển bằng WDA": while the switch is on and an iPhone or iPad mirrors, a session starts by itself over the
    /// cable: Developer Mode, the Developer Disk Image, WebDriverAgent found and launched as an XCUITest, a usbmux
    /// forward to its port 8100, a WDA session; then the AirPlay picture takes touches through <see cref="WdaOverlay"/>.
    /// It ends when mirroring has been over for <see cref="WdaLifetime.Linger"/>, the switch goes off, the cable leaves,
    /// WDA stops answering, another device takes over the mirroring, or the app exits. A failure is shown and not retried
    /// in a loop: the next mirroring session, "Thử lại", or the cable coming back after it left starts again.
    /// AirPlay never depends on any of this. All members are used on the UI thread; the start runs on the thread pool and
    /// every result it posts back is dropped unless its session is still the current one.
    /// </summary>
    internal sealed class WdaController : IDisposable
    {
        private static readonly TimeSpan ReadyTimeout = TimeSpan.FromSeconds(60);
        private static readonly TimeSpan ReadyPoll = TimeSpan.FromMilliseconds(500);
        private static readonly TimeSpan PresencePoll = TimeSpan.FromSeconds(3);
        // the previous session's runner gets one Ctrl+C and 10 s; a new start waits for it at most this long
        private static readonly TimeSpan TeardownBound = TimeSpan.FromSeconds(20);

        private readonly Func<AppSettings> _settings;
        private readonly Action _saveSettings;
        private readonly ReceiverController _receiver;
        private readonly AirPlayPresenter _presenter;
        private readonly Dispatcher _ui;
        private readonly RotatingLog _log;
        private readonly WdaLifetime _lifetime = new WdaLifetime(() => DateTime.UtcNow);
        private readonly DispatcherTimer _recheck;
        private readonly DispatcherTimer _presence;
        private WdaOverlay _overlay;
        private Session _session;
        private Task _teardown = Task.CompletedTask;
        private WdaView _view = WdaView.Off;
        private IReadOnlyList<string> _choices = new string[0];
        private bool _wasMobile;
        private string _lastClient;
        private bool _presenceBusy;
        private int _generation;
        private bool _disposed;
        private bool _halted;

        public WdaController(Func<AppSettings> settings, Action saveSettings, ReceiverController receiver, AirPlayPresenter presenter, Dispatcher ui)
        {
            _settings = settings;
            _saveSettings = saveSettings;
            _receiver = receiver;
            _presenter = presenter;
            _ui = ui;
            _log = new RotatingLog(AppPaths.WdaLog, 512 * 1024);
            _recheck = new DispatcherTimer(WdaLifetime.Linger + TimeSpan.FromMilliseconds(500), DispatcherPriority.Normal, (_, __) =>
            {
                _recheck.Stop();
                Refresh();
            }, ui);
            _recheck.Stop();
            _presence = new DispatcherTimer(PresencePoll, DispatcherPriority.Background, (_, __) => _ = CheckPresenceAsync(), ui);
            _presence.Stop();
            _receiver.StateChanged += Refresh;
            _presenter.VideoChanged += Refresh;
        }

        /// <summary>Raised on the UI thread whenever anything shown to the user may have changed.</summary>
        public event Action StateChanged;

        public WdaView View => _view;

        public string StatusText => WdaTexts.Status(_view, DateTime.UtcNow);

        /// <summary>The AirPlay video window takes touches now; IntPtr.Zero otherwise.</summary>
        public IntPtr TouchVideo => _view.IsReady ? _presenter.Video : IntPtr.Zero;

        /// <summary>The devices to pick from while <see cref="WdaPhase.Choose"/>.</summary>
        public IReadOnlyList<string> Choices => _choices;

        public bool CanRetry => _session == null && (_view.Phase == WdaPhase.Failed || _view.Phase == WdaPhase.Choose) && Mobile;

        public bool NeedsSetup => _view.Phase == WdaPhase.Failed && WdaTexts.NeedsSetup(_view.Failure);

        private bool Enabled => _settings().WdaControl;

        private bool Mobile => _receiver.Phase == ReceiverPhase.Connected && WdaLifetime.IsMobileModel(_receiver.ClientModel);

        /// <summary>Re-evaluates everything after a change: the setting, the receiver, the video window.</summary>
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
                StopSession();
                SetView(WdaView.Off);
            }
            else if (!run)
            {
                StopSession();
                SetView(new WdaView(WdaPhase.Waiting, WdaStep.FindingDevice, WdaFailure.None, null, null));
            }
            else
            {
                if (!mobile && !_recheck.IsEnabled) _recheck.Start();
                // another device took over the mirroring: the session controls the wrong one
                if (_session != null && mobile && !string.Equals(_session.AirPlayName, client, StringComparison.Ordinal))
                {
                    Log("another device mirrors now; starting again");
                    StopSession();
                    newMirroring = true;
                }
                var phase = _view.Phase;
                if (_session == null && mobile
                    && (phase == WdaPhase.Off || phase == WdaPhase.Waiting || newMirroring))
                {
                    Begin();
                }
            }

            if (_view.IsReady && _session?.Driver != null)
            {
                var video = _presenter.Video;
                _presenter.SetTouch(video);
                if (video == IntPtr.Zero) _overlay?.Detach();
                else Overlay().Attach(video, _session.Driver, _receiver.ClientName);
            }
            UpdatePresence();
            StateChanged?.Invoke();
        }

        /// <summary>"Thử lại" after a failure, or a new look once the user has fixed what the failure said.</summary>
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
            var s = _settings();
            s.WdaDevices = WdaChoices.Remember(s.WdaDevices, _receiver.ClientName, udid);
            _saveSettings();
            if (_session == null && Mobile) Begin();
        }

        /// <summary>
        /// The app exits or updates: the session ends, its runner gets its Ctrl+C, and nothing starts again until
        /// <see cref="Resume"/> (an update replaces the runtime's files). The task ends when the runner is gone.
        /// </summary>
        public Task StopAsync()
        {
            _halted = true;
            StopSession();
            return _teardown;
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
            StopSession();
            _disposed = true;
            _receiver.StateChanged -= Refresh;
            _presenter.VideoChanged -= Refresh;
            _recheck.Stop();
            _presence.Stop();
            _overlay?.Close();
            _overlay = null;
            _log.Dispose();
        }

        // ---- the session -----------------------------------------------------------------

        private void Begin()
        {
            if (_session != null || _disposed || _halted) return;
            var s = new Session(++_generation, _receiver.ClientName);
            _session = s;
            _choices = new string[0];
            Log("#" + s.Generation + " start (" + (_receiver.ClientModel ?? "?") + ")");
            SetView(new WdaView(WdaPhase.Starting, WdaStep.FindingDevice, WdaFailure.None, null, null));
            var previous = _teardown;
            var remembered = WdaChoices.Remembered(_settings().WdaDevices, s.AirPlayName);
            s.Run = Task.Run(() => RunAsync(s, previous, remembered));
        }

        private async Task RunAsync(Session s, Task previous, string remembered)
        {
            var token = s.Cts.Token;
            try
            {
                if (!previous.IsCompleted)
                {
                    Log(s, "waiting for the previous session to stop");
                    await Task.WhenAny(previous, Task.Delay(TeardownBound)).ConfigureAwait(false);
                }
                if (!CoreDeviceRuntime.IsCurrent)
                {
                    Fail(s, WdaFailure.RuntimeMissing);
                    return;
                }
                var usb = await CoreDeviceCommands.UsbmuxUsbDevicesAsync().ConfigureAwait(false);
                if (usb == null)
                {
                    Fail(s, WdaFailure.NoAppleService);
                    return;
                }
                var target = WdaTarget.Choose(usb, remembered);
                Log(s, usb.Count + " device(s) on the cable, " + target.Kind);
                if (target.Kind == WdaTargetKind.None)
                {
                    Fail(s, WdaFailure.NoDevice);
                    return;
                }
                if (target.Kind == WdaTargetKind.Pick)
                {
                    Post(s, () => AskToChoose(s, target.Choices));
                    return;
                }
                var udid = target.Udid;
                s.Udid = udid;

                Step(s, WdaStep.CheckingDevice);
                var developer = await Command(() => CoreDeviceCommands.DeveloperModeAsync(udid)).ConfigureAwait(false);
                if (token.IsCancellationRequested) return;
                Log(s, "developer mode status: " + Describe(developer));
                if (!developer.Succeeded)
                {
                    Fail(s, WdaLog.FromCommand(developer, DeviceQueries.Trusted(developer) == false ? WdaFailure.NotTrusted : WdaFailure.Other));
                    return;
                }
                if (DeviceQueries.ParseBool(developer.Stdout) == false)
                {
                    Fail(s, WdaFailure.DeveloperModeOff);
                    return;
                }

                Step(s, WdaStep.Mounting);
                var mount = await Command(() => CoreDeviceCommands.MountAsync(udid, CoreDeviceRoute.Usb)).ConfigureAwait(false);
                if (token.IsCancellationRequested) return;
                Log(s, "image mount: " + Describe(mount));
                if (!mount.Succeeded)
                {
                    Fail(s, WdaLog.FromCommand(mount, WdaFailure.DiskImageFailed));
                    return;
                }

                Step(s, WdaStep.FindingWda);
                var apps = await Command(() => CoreDeviceCommands.UserAppsAsync(udid)).ConfigureAwait(false);
                if (token.IsCancellationRequested) return;
                if (!apps.Succeeded)
                {
                    Log(s, "app list: " + Describe(apps));
                    Fail(s, WdaLog.FromCommand(apps, WdaFailure.Other));
                    return;
                }
                var bundle = WdaBundles.Pick(apps.Stdout);
                if (bundle == null)
                {
                    Fail(s, WdaFailure.NotInstalled);
                    return;
                }
                var profiles = await Command(() => CoreDeviceCommands.ProfilesAsync(udid)).ConfigureAwait(false);
                if (token.IsCancellationRequested) return;
                s.Expires = profiles.Succeeded ? ProvisionProfiles.Expiry(profiles.Stdout, bundle) : null;
                Log(s, "WDA found; profile " + (s.Expires == null ? "unknown" : "expires " + s.Expires.Value.ToString("u")));
                if (ProvisionProfiles.IsExpired(s.Expires, DateTime.UtcNow))
                {
                    Fail(s, WdaFailure.Expired);
                    return;
                }

                Step(s, WdaStep.Launching);
                s.Runner = new WdaRunner(line => Log(s, line));
                s.Runner.Start(udid, bundle);
                s.Forwarder = new UsbmuxForwarder(UsbmuxForwarder.AppleService, udid, WdaClient.DevicePort);
                s.Forwarder.Log += line => Log(s, "forward: " + line);
                s.Forwarder.Start();
                s.Client = new WdaClient(new Uri("http://127.0.0.1:" + s.Forwarder.LocalPort + "/"));
                s.Client.Logged += line => Log(s, "client: " + line);

                Step(s, WdaStep.Connecting);
                var watch = Stopwatch.StartNew();
                while (true)
                {
                    if (token.IsCancellationRequested) return;
                    if (s.Runner.HasExited)
                    {
                        s.Runner.SettleOutput();
                        Fail(s, WdaLog.Pick(s.Runner.Failure, WdaFailure.RunnerFailed));
                        return;
                    }
                    if (await s.Client.IsReadyAsync(TimeSpan.FromSeconds(2), token).ConfigureAwait(false)) break;
                    if (watch.Elapsed > ReadyTimeout)
                    {
                        Fail(s, WdaLog.Pick(s.Runner.Failure, WdaFailure.NotAnswering));
                        return;
                    }
                    await Task.Delay(ReadyPoll, token).ConfigureAwait(false);
                }
                Log(s, "WDA answers after " + watch.Elapsed.TotalSeconds.ToString("0.0") + " s");

                s.Driver = new WdaDriver(s.Client, () => DateTime.UtcNow);
                s.Driver.Logged += line => Log(s, "driver: " + line);
                s.Driver.Lost += failure => Fail(s, failure);
                s.Driver.SizeChanged += () => Post(s, () => _overlay?.Relayout());
                await s.Driver.StartAsync(token).ConfigureAwait(false);
                Log(s, "ready, screen " + s.Driver.Size);
                Post(s, () => BecomeReady(s));
            }
            catch (OperationCanceledException)
            {
                // stopped meanwhile
            }
            catch (WdaException e)
            {
                Log(s, "WDA refused the session: " + e.Message);
                Fail(s, e.NoAnswer ? WdaFailure.NotAnswering : WdaFailure.Other);
            }
            catch (CoreDeviceHostException e)
            {
                Log(s, "the runner could not start: " + e.Message);
                Fail(s, WdaFailure.Other);
            }
            catch (Exception e) when (e is SocketException || e is IOException || e is InvalidOperationException || e is ArgumentException)
            {
                Log(s, "start failed: " + e.GetType().Name);
                Fail(s, WdaFailure.Other);
            }
        }

        private void BecomeReady(Session s)
        {
            SetView(new WdaView(WdaPhase.Ready, WdaStep.Connecting, WdaFailure.None, s.Udid, s.Expires));
            Refresh();
        }

        private void AskToChoose(Session s, IReadOnlyList<string> choices)
        {
            EndSession(s);
            _choices = choices;
            SetView(new WdaView(WdaPhase.Choose, WdaStep.FindingDevice, WdaFailure.None, null, null));
            Refresh();
        }

        /// <summary>Any thread: the session ends with <paramref name="failure"/>, unless it is already over.</summary>
        private void Fail(Session s, WdaFailure failure)
        {
            Log(s, "failed: " + failure);
            Post(s, () =>
            {
                EndSession(s);
                SetView(new WdaView(WdaPhase.Failed, WdaStep.FindingDevice, failure, s.Udid, s.Expires));
                Refresh();
            });
        }

        private void StopSession()
        {
            if (_session == null) return;
            Log(_session, "stop");
            EndSession(_session);
        }

        /// <summary>UI thread: the session is no longer current; its parts are let go of on the thread pool.</summary>
        private void EndSession(Session s)
        {
            if (_session != s) return;
            _session = null;
            _overlay?.Detach();
            _presenter.SetTouch(IntPtr.Zero);
            s.Cts.Cancel();
            var previous = _teardown;
            _teardown = Task.Run(async () =>
            {
                await previous.ConfigureAwait(false);
                await TeardownAsync(s).ConfigureAwait(false);
            });
        }

        private async Task TeardownAsync(Session s)
        {
            try
            {
                // the start creates the parts on the thread pool: once it has returned (it notices the cancellation within
                // half a second, or when a short command ends), every part it made is in the session and nothing comes after
                try
                {
                    if (s.Run != null) await s.Run.ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    Log(s, "start ended with " + e.GetType().Name);
                }
                s.Driver?.Dispose();
                s.Driver?.WaitStopped(TimeSpan.FromSeconds(3));
                s.Forwarder?.Dispose();
                if (s.Runner != null) await s.Runner.StopAsync().ConfigureAwait(false);
            }
            catch (Exception e) when (e is CoreDeviceHostException || e is InvalidOperationException || e is IOException)
            {
                Log(s, "teardown: " + e.GetType().Name);
            }
            finally
            {
                s.Runner?.Dispose();
                s.Client?.Dispose();
                Log(s, "session over");
            }
        }

        /// <summary>The cable watch: the session's device leaving ends it; after that failure, a device coming back starts again.</summary>
        private async Task CheckPresenceAsync()
        {
            if (_presenceBusy || _disposed) return;
            _presenceBusy = true;
            try
            {
                var usb = await CoreDeviceCommands.UsbmuxUsbDevicesAsync();
                if (usb == null || _disposed) return;
                var s = _session;
                if (s?.Udid != null)
                {
                    if (!usb.Contains(s.Udid, StringComparer.OrdinalIgnoreCase)) Fail(s, WdaFailure.NoDevice);
                }
                else if (s == null && _view.Phase == WdaPhase.Failed && _view.Failure == WdaFailure.NoDevice && Mobile && Enabled && usb.Count > 0)
                {
                    Log("a device is on the cable again");
                    Begin();
                }
            }
            finally
            {
                _presenceBusy = false;
            }
        }

        private void UpdatePresence()
        {
            var watch = _session != null || (_view.Phase == WdaPhase.Failed && _view.Failure == WdaFailure.NoDevice && Mobile);
            if (watch && !_presence.IsEnabled) _presence.Start();
            else if (!watch && _presence.IsEnabled) _presence.Stop();
        }

        private WdaOverlay Overlay() => _overlay ?? (_overlay = new WdaOverlay());

        private void Step(Session s, WdaStep step) =>
            Post(s, () => SetView(new WdaView(WdaPhase.Starting, step, WdaFailure.None, s.Udid, s.Expires)));

        /// <summary>Runs <paramref name="action"/> on the UI thread if <paramref name="s"/> is still the current session.</summary>
        private void Post(Session s, Action action)
        {
            _ui.BeginInvoke(new Action(() =>
            {
                if (_disposed || _session != s) return;
                action();
            }));
        }

        private void SetView(WdaView view)
        {
            var changed = view.Phase != _view.Phase || view.Step != _view.Step || view.Failure != _view.Failure;
            _view = view;
            if (changed) StateChanged?.Invoke();
        }

        /// <summary>A short command; one that could not even start (python.exe missing, no job) counts as failed.</summary>
        private static async Task<CommandResult> Command(Func<Task<CommandResult>> command)
        {
            try
            {
                return await command().ConfigureAwait(false);
            }
            catch (CoreDeviceHostException)
            {
                return new CommandResult(-1, "", CliError.Other, false);
            }
        }

        private static string Describe(CommandResult r) => r.Succeeded ? "ok" : r.TimedOut ? "timed out" : "failed (" + r.Error + ")";

        private void Log(Session s, string line) => Log("#" + s.Generation + " " + line);

        private void Log(string line)
        {
            try
            {
                _log.WriteLine(line);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ObjectDisposedException)
            {
                // a log that cannot be written never stops control
            }
        }

        private sealed class Session
        {
            public Session(int generation, string airPlayName)
            {
                Generation = generation;
                AirPlayName = airPlayName;
            }

            public int Generation { get; }
            /// <summary>The AirPlay name mirroring when the session started.</summary>
            public string AirPlayName { get; }
            public CancellationTokenSource Cts { get; } = new CancellationTokenSource();
            /// <summary>The start on the thread pool; the teardown waits for it.</summary>
            public Task Run;
            // written by the start on the thread pool before it posts anything that reads them
            public volatile string Udid;
            public DateTime? Expires;
            public WdaRunner Runner;
            public UsbmuxForwarder Forwarder;
            public WdaClient Client;
            public WdaDriver Driver;
        }
    }
}
