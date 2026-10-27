using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using System.Windows.Threading;
using MacExtend.Core;
using MacExtend.Native;
using Microsoft.Win32;

namespace MacExtend.Services
{
    /// <summary>
    /// Keeps uxplay running the way the user asked: starts it with arguments built from the
    /// settings, follows the connection, restarts it after crashes, sleep and network changes.
    /// All members are used on the UI thread; events from other threads are marshalled to it.
    /// </summary>
    internal sealed class ReceiverController : IDisposable
    {
        private static readonly TimeSpan StopGrace = TimeSpan.FromSeconds(3);
        private static readonly TimeSpan ReadyAfter = TimeSpan.FromSeconds(1.5);
        private static readonly TimeSpan HealthyAfter = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan ResumeDelay = TimeSpan.FromSeconds(3);

        private readonly Func<AppSettings> _settings;
        private readonly Dispatcher _ui;
        private readonly UxPlayHost _host = new UxPlayHost();
        private readonly ConnectionTracker _tracker = new ConnectionTracker();
        private readonly RestartPolicy _restarts = new RestartPolicy(() => DateTime.UtcNow);
        private readonly Debouncer _networkChange = new Debouncer(TimeSpan.FromSeconds(5), () => DateTime.UtcNow);
        private readonly DispatcherTimer _poll;
        private readonly RotatingLog _log;

        private IReadOnlyList<string> _runningArgs;
        private DateTime _startedAt;
        private bool _reportedHealthy;
        private bool _wantRunning;
        private bool _restartWhenIdle;
        private string _lastErrorLine;
        private DispatcherTimer _pendingRestart;
        private Task _transition = Task.CompletedTask;

        public ReceiverController(Func<AppSettings> settings, Dispatcher ui)
        {
            _settings = settings;
            _ui = ui;
            _log = new RotatingLog(AppPaths.LogFile, 2 * 1024 * 1024);

            _host.Output += OnOutput;
            _host.Exited += code => _ui.BeginInvoke(new Action(() => OnUnexpectedExit(code)));

            _poll = new DispatcherTimer(TimeSpan.FromSeconds(2), DispatcherPriority.Background, (_, __) => Poll(), _ui);
            _poll.Start();

            NetworkChange.NetworkAddressChanged += OnNetworkAddressChanged;
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
        }

        public ReceiverPhase Phase { get; private set; } = ReceiverPhase.Stopped;
        /// <summary>uxplay's process id while it runs, 0 otherwise.</summary>
        public int ProcessId => _host.ProcessId;
        public string ClientName => _tracker.ClientName;
        public string LastError { get; private set; }
        public string EffectiveDeviceName { get; private set; } = "";
        public RequestedVideo EffectiveVideo { get; private set; }
        public bool IsRestarting => _pendingRestart != null;

        /// <summary>The settings changed since uxplay started; they apply after a restart.</summary>
        public bool NeedsRestart
        {
            get
            {
                if (_runningArgs == null || !_host.IsRunning) return false;
                try
                {
                    return !_runningArgs.SequenceEqual(BuildArgs(out _, out _));
                }
                catch (ArgumentException)
                {
                    return false;
                }
            }
        }

        /// <summary>Raised on the UI thread whenever anything shown to the user may have changed.</summary>
        public event Action StateChanged;

        public void Start()
        {
            _wantRunning = true;
            Serialize(LaunchAsync);
        }

        public Task StopAsync()
        {
            _wantRunning = false;
            _restartWhenIdle = false;
            CancelPendingRestart();
            return Serialize(async () =>
            {
                await _host.StopAsync(StopGrace);
                _runningArgs = null;
                SetPhase(ReceiverPhase.Stopped);
            });
        }

        public Task RestartAsync()
        {
            _wantRunning = true;
            CancelPendingRestart();
            return Serialize(async () =>
            {
                Log("restarting uxplay");
                await _host.StopAsync(StopGrace);
                await LaunchAsync();
            });
        }

        /// <summary>Recomputes the name and video mode the receiver advertises, for display before it starts.</summary>
        public void RefreshPreview()
        {
            try
            {
                BuildArgs(out var name, out var video);
                EffectiveDeviceName = name;
                EffectiveVideo = video;
            }
            catch (ArgumentException)
            {
                // invalid PIN while the user is still typing; keep the last preview
            }
            StateChanged?.Invoke();
        }

        /// <summary>Stops uxplay synchronously when Windows logs off or shuts down.</summary>
        public void ShutdownNow()
        {
            _wantRunning = false;
            CancelPendingRestart();
            _host.StopNow(TimeSpan.FromSeconds(2));
            KeepAwake.Set(false);
        }

        public void Dispose()
        {
            NetworkChange.NetworkAddressChanged -= OnNetworkAddressChanged;
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            _poll.Stop();
            CancelPendingRestart();
            _host.Dispose();
            KeepAwake.Set(false);
            _log.Dispose();
        }

        // ---- lifecycle -------------------------------------------------------------------

        /// <summary>Start, stop and restart run one after another, never interleaved.</summary>
        private Task Serialize(Func<Task> step)
        {
            _transition = _transition.ContinueWith(_ => step(), TaskScheduler.FromCurrentSynchronizationContext()).Unwrap();
            return _transition;
        }

        private Task LaunchAsync()
        {
            if (!_wantRunning || _host.IsRunning) return Task.CompletedTask;

            if (!File.Exists(AppPaths.UxPlayExe))
            {
                Fail("Không tìm thấy uxplay.exe trong thư mục cài đặt. Hãy cài lại MacExtend.");
                return Task.CompletedTask;
            }

            IReadOnlyList<string> args;
            try
            {
                args = BuildArgs(out var name, out var video);
                EffectiveDeviceName = name;
                EffectiveVideo = video;
            }
            catch (ArgumentException e)
            {
                Fail(e.Message);
                return Task.CompletedTask;
            }

            _tracker.Reset();
            _lastErrorLine = null;
            LastError = null;
            if (!DpiCompat.EnsureHighDpiAware(AppPaths.UxPlayExe)) Log("could not set DPI compatibility flag; video may look blurry with Windows scaling above 100 %");
            Log("starting uxplay " + WindowsCommandLine.Join(args));
            try
            {
                _host.Start(args);
            }
            catch (Exception e) when (e is System.ComponentModel.Win32Exception || e is InvalidOperationException)
            {
                Fail("Không chạy được uxplay: " + e.Message);
                return Task.CompletedTask;
            }

            _runningArgs = args;
            _startedAt = DateTime.UtcNow;
            _reportedHealthy = false;
            SetPhase(ReceiverPhase.Starting);
            return Task.CompletedTask;
        }

        private IReadOnlyList<string> BuildArgs(out string name, out RequestedVideo video)
        {
            var s = _settings();
            var fallback = DeviceName.DefaultFor(Environment.MachineName);
            name = string.IsNullOrWhiteSpace(s.DeviceName) ? fallback : DeviceName.Sanitize(s.DeviceName, fallback);
            video = ResolutionPolicy.Resolve(s, DisplayInfo.GetPrimary());
            return UxPlayArguments.Build(s, name, video, new UxPlayPaths(AppPaths.KeyFile, AppPaths.RegisterFile), ShortPath.Resolve);
        }

        private void OnUnexpectedExit(int exitCode)
        {
            Log("uxplay exited with code " + exitCode);
            _runningArgs = null;
            _tracker.Reset();
            if (!_wantRunning)
            {
                SetPhase(ReceiverPhase.Stopped);
                return;
            }

            var delay = _restarts.NextDelay();
            if (delay == null)
            {
                _wantRunning = false;
                Fail(DescribeFailure(exitCode));
                return;
            }

            Log("restarting in " + delay.Value.TotalSeconds + " s");
            SetPhase(ReceiverPhase.Starting);
            ScheduleRestart(delay.Value, LaunchAsync);
        }

        private string DescribeFailure(int exitCode)
        {
            if (exitCode == Win32.STATUS_DLL_NOT_FOUND || exitCode == Win32.STATUS_ENTRYPOINT_NOT_FOUND)
            {
                return "uxplay thiếu thư viện đi kèm. Hãy cài lại MacExtend.";
            }
            return _lastErrorLine != null
                ? "uxplay dừng liên tục. Lỗi cuối: " + _lastErrorLine.Replace("*** ERROR:", "").Trim()
                : "uxplay dừng liên tục (mã " + exitCode + "). Xem nhật ký để biết chi tiết.";
        }

        private void ScheduleRestart(TimeSpan delay, Func<Task> action)
        {
            CancelPendingRestart();
            _pendingRestart = new DispatcherTimer(delay, DispatcherPriority.Normal, (_, __) =>
            {
                CancelPendingRestart();
                Serialize(action);
            }, _ui);
            _pendingRestart.Start();
        }

        private void CancelPendingRestart()
        {
            _pendingRestart?.Stop();
            _pendingRestart = null;
        }

        private void Fail(string message)
        {
            LastError = message;
            Log("error: " + message);
            SetPhase(ReceiverPhase.Failed);
        }

        // ---- monitoring --------------------------------------------------------------------

        private void Poll()
        {
            if (_host.IsRunning)
            {
                var now = DateTime.UtcNow;
                if (Phase == ReceiverPhase.Starting && now - _startedAt >= ReadyAfter) SetPhase(ReceiverPhase.Ready);
                if (!_reportedHealthy && now - _startedAt >= HealthyAfter)
                {
                    _reportedHealthy = true;
                    _restarts.ReportHealthy();
                }
                if (_tracker.OnTcpSample(TcpConnections.CountEstablished(_host.ProcessId))) OnConnectionChanged();
            }

            if (_networkChange.ShouldFire() && _wantRunning)
            {
                if (_tracker.IsConnected) _restartWhenIdle = true;
                else RestartAsync();
            }
        }

        private void OnOutput(string line)
        {
            _log.WriteLine(line);
            var e = UxPlayLogParser.Parse(line);
            if (e.Kind == LogEventKind.None) return;
            _ui.BeginInvoke(new Action(() =>
            {
                if (e.Kind == LogEventKind.Error) _lastErrorLine = e.Text;
                if (_tracker.OnLog(e)) OnConnectionChanged();
            }));
        }

        private void OnConnectionChanged()
        {
            if (_tracker.IsConnected)
            {
                SetPhase(ReceiverPhase.Connected);
                return;
            }
            SetPhase(_host.IsRunning ? ReceiverPhase.Ready : ReceiverPhase.Stopped);
            if (_restartWhenIdle && _wantRunning)
            {
                _restartWhenIdle = false;
                RestartAsync();
            }
        }

        private void OnNetworkAddressChanged(object sender, EventArgs e) =>
            _ui.BeginInvoke(new Action(() => _networkChange.Signal()));

        private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            _ui.BeginInvoke(new Action(() =>
            {
                if (!_wantRunning) return;
                if (e.Mode == PowerModes.Suspend)
                {
                    // uxplay's sockets and mDNS do not survive sleep; start fresh after wake
                    Log("system suspending");
                    CancelPendingRestart();
                    Serialize(async () =>
                    {
                        await _host.StopAsync(StopGrace);
                        _runningArgs = null;
                        _tracker.Reset();
                        SetPhase(ReceiverPhase.Starting);
                    });
                }
                else if (e.Mode == PowerModes.Resume)
                {
                    Log("system resumed");
                    ScheduleRestart(ResumeDelay, async () =>
                    {
                        await _host.StopAsync(StopGrace);
                        await LaunchAsync();
                    });
                }
            }));
        }

        private void SetPhase(ReceiverPhase phase)
        {
            Phase = phase;
            KeepAwake.Set(phase == ReceiverPhase.Connected);
            StateChanged?.Invoke();
        }

        private void Log(string message) => _log.WriteLine("[MacExtend] " + message);
    }
}
