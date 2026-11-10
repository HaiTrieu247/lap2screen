using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Threading;
using MacExtend.Core;
using MacExtend.Core.Bluetooth;
using MacExtend.Native;
using MacExtend.Views;
using Microsoft.Win32;

namespace MacExtend.Services.Bluetooth
{
    /// <summary>
    /// Makes the laptop a Bluetooth keyboard and mouse for the device mirroring, while the user wants that and a
    /// device mirrors (<see cref="BluetoothLifetime"/>), and runs control sessions: a click on the AirPlay picture
    /// starts one; Ctrl+Alt+Q, Alt+Tab, another window in front, a locked session or a lost connection ends it.
    /// AirPlay never depends on any of this: nothing Bluetooth is created before the user switches it on, and
    /// whatever fails in here is logged and shown, never thrown. All members are used on the UI thread.
    /// </summary>
    internal sealed class BluetoothController : IDisposable
    {
        private static readonly TimeSpan RecheckSlack = TimeSpan.FromMilliseconds(500);

        private readonly Func<AppSettings> _settings;
        private readonly ReceiverController _receiver;
        private readonly AirPlayPresenter _presenter;
        private readonly Dispatcher _ui;
        private readonly RotatingLog _log;
        private readonly BluetoothLifetime _lifetime = new BluetoothLifetime(() => DateTime.UtcNow);
        private readonly ControlTarget _target = new ControlTarget();
        private readonly ReportQueue _reports = new ReportQueue();
        private readonly DispatcherTimer _recheck;
        // created the first time the user switches Bluetooth control on
        private HidPeripheral _peripheral;
        private ReportPump _pump;
        private InputCapture _capture;
        // kept in a field: the native hook calls it for as long as the hook lives
        private readonly Win32.WinEventProc _onDesktopSwitch;
        private IntPtr _desktopHook;
        private ControlBanner _banner;

        private PeripheralState _peripheralState = PeripheralState.Idle;
        private string _failure;
        private bool _wasEnabled;
        private bool _partsFailed;
        private bool _checking;
        private bool _starting;
        private bool _running;
        private IReadOnlyList<PairedHost> _hosts = new PairedHost[0];
        private bool _controlling;
        private int _session;
        private IntPtr _controlledVideo;
        private bool _disposed;

        public BluetoothController(Func<AppSettings> settings, ReceiverController receiver, AirPlayPresenter presenter, Dispatcher ui)
        {
            _settings = settings;
            _receiver = receiver;
            _presenter = presenter;
            _ui = ui;
            _log = new RotatingLog(AppPaths.BluetoothLog, 512 * 1024);
            _recheck = new DispatcherTimer(BluetoothLifetime.Linger + RecheckSlack, DispatcherPriority.Normal, (_, __) =>
            {
                _recheck.Stop();
                Refresh();
            }, ui);
            _recheck.Stop();

            _receiver.StateChanged += Refresh;
            _presenter.VideoChanged += Refresh;
            _presenter.ForegroundChanged += OnForegroundChanged;
            SystemEvents.SessionSwitch += OnSessionSwitch;
            // Ctrl+Alt+Del, UAC and the lock screen run on another desktop, where no hook sees the keys go up
            _onDesktopSwitch = (hook, eventType, hwnd, idObject, idChild, thread, time) => End(StopReason.Focus);
            _desktopHook = Win32.SetWinEventHook(Win32.EVENT_SYSTEM_DESKTOPSWITCH, Win32.EVENT_SYSTEM_DESKTOPSWITCH, IntPtr.Zero,
                                                 _onDesktopSwitch, 0, 0, Win32.WINEVENT_OUTOFCONTEXT);
        }

        /// <summary>Raised on the UI thread whenever anything shown to the user may have changed.</summary>
        public event Action StateChanged;

        public BluetoothState State => BluetoothStatus.Derive(Enabled, _running, _peripheralState, _hosts.Count, TargetName, _controlling);

        public string StatusText => BluetoothStatus.Describe(State, TargetName, _failure);

        /// <summary>Devices connected over Bluetooth right now.</summary>
        public IReadOnlyList<PairedHost> Hosts => _hosts;

        public string TargetId => _target.HostId;

        /// <summary>Name of the connected device that would be controlled, null if none is.</summary>
        public string TargetName => _hosts.FirstOrDefault(h => string.Equals(h.Id, _target.HostId, StringComparison.OrdinalIgnoreCase))?.Display;

        public bool IsControlling => _controlling;

        /// <summary>A click on the picture, or the tray menu, would start controlling now.</summary>
        public bool CanControl => !_controlling && State == BluetoothState.Connected && _presenter.HasVideoWindow && !TouchControlled;

        /// <summary>
        /// The video window currently taking touches through WDA control, or IntPtr.Zero. A click there is a tap: it never
        /// starts a Bluetooth session, and one running ends when WDA takes over.
        /// </summary>
        public Func<IntPtr> TouchVideo { get; set; }

        private bool TouchControlled
        {
            get
            {
                var touch = TouchVideo?.Invoke() ?? IntPtr.Zero;
                return touch != IntPtr.Zero && touch == _presenter.Video;
            }
        }

        private bool Enabled => _settings().BluetoothControl;

        /// <summary>Re-evaluates everything after a change: the setting, the receiver, the video, the devices.</summary>
        public void Refresh()
        {
            if (_disposed) return;
            var enabled = Enabled;
            var mirroring = _receiver.Phase == ReceiverPhase.Connected;
            var run = _lifetime.ShouldRun(enabled, mirroring);
            if (enabled && _peripheral == null && !CreateParts())
            {
                StateChanged?.Invoke();
                return;
            }

            if (enabled != _wasEnabled)
            {
                _wasEnabled = enabled;
                Log(enabled ? "Bluetooth control switched on" : "Bluetooth control switched off");
                _peripheralState = PeripheralState.Idle;
                _failure = null;
                // tell the user about a missing or switched-off adapter before any device mirrors
                if (enabled) Check();
            }
            // a failed start is retried with the next mirroring session, not in a loop
            if (!run && _peripheralState == PeripheralState.Failed) _peripheralState = PeripheralState.Idle;

            // _starting: a stop deferred until the last start finished; never two starts at once
            if (run && !_running && !_starting && _peripheralState == PeripheralState.Idle && !_checking) StartPeripheral();
            else if (!run && _running) StopPeripheral();
            if (run && !mirroring && !_recheck.IsEnabled) _recheck.Start();

            _target.Resolve(_hosts, mirroring ? _receiver.ClientName : null);
            if (_peripheral != null) _peripheral.TargetId = _target.HostId;

            if (_controlling && (!mirroring || TargetName == null || _presenter.Video != _controlledVideo)) End(StopReason.Disconnected);
            else if (_controlling && TouchControlled) End(StopReason.Request);
            _capture?.Watch(CanControl ? _presenter.Video : IntPtr.Zero);
            StateChanged?.Invoke();
        }

        /// <summary>From the tray menu: control without clicking the picture.</summary>
        public void StartControl()
        {
            if (!CanControl) return;
            var video = _presenter.Video;
            VideoWindow.BringToFront(video);
            if (!VideoWindow.IsInFront(video))
            {
                Log("could not bring the video to the front; control not started");
                return;
            }
            Begin(video);
        }

        /// <summary>The user picked the device to control.</summary>
        public void SelectHost(string hostId)
        {
            if (hostId == _target.HostId) return;
            _target.Select(hostId, _receiver.Phase == ReceiverPhase.Connected ? _receiver.ClientName : null);
            if (_controlling) End(StopReason.Request);
            Refresh();
        }

        /// <summary>
        /// Gives the keyboard and mouse back to Windows from any thread, touching nothing else: for the crash
        /// handler, which may run on a thread that must not touch WPF windows.
        /// </summary>
        public void ReleaseInput() => _capture?.End(StopReason.Error);

        public void End(StopReason reason)
        {
            if (!_controlling) return;
            _capture?.End(reason);
            // without waiting for the hook thread: the pointer shield and the notice go at once
            OnStopped(reason);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _receiver.StateChanged -= Refresh;
            _presenter.VideoChanged -= Refresh;
            _presenter.ForegroundChanged -= OnForegroundChanged;
            SystemEvents.SessionSwitch -= OnSessionSwitch;
            if (_desktopHook != IntPtr.Zero) Win32.UnhookWinEvent(_desktopHook);
            _desktopHook = IntPtr.Zero;
            _recheck.Stop();
            _controlling = false;
            if (_capture != null)
            {
                _capture.Dispose();
                // the all-up reports the capture queued on its way out get a moment to leave
                Task.Delay(100).Wait();
            }
            _pump?.Dispose();
            try
            {
                _peripheral?.Dispose();
            }
            catch (Exception e)
            {
                Log("stopping Bluetooth: " + e.Message);
            }
            _banner?.Close();
            _log.Dispose();
        }

        // ---- peripheral --------------------------------------------------------------------

        private bool CreateParts()
        {
            // once is enough: what failed will fail again until Windows changes
            if (_partsFailed) return false;
            try
            {
                _peripheral = new HidPeripheral();
                _peripheral.Log += Log;
                _peripheral.HostsChanged += () => Post(UpdateHosts);
                _peripheral.RadioChanged += () => Post(Check);
                _pump = new ReportPump(_peripheral, _reports);
                _pump.Failed += error => Post(() =>
                {
                    Log("could not send to the device: " + error.Message);
                    End(StopReason.Error);
                });
                _capture = new InputCapture(_reports, _pump.Wake);
                _capture.Log += Log;
                _capture.VideoClicked += (x, y) => Post(() => OnVideoClicked(x, y));
                _capture.Stopped += (reason, session) => Post(() =>
                {
                    if (session == _session) OnStopped(reason);
                });
                return true;
            }
            catch (Exception e)
            {
                // WinRT missing or broken on this Windows: say so, keep AirPlay going
                Log("Bluetooth unavailable: " + e);
                _peripheral = null;
                _partsFailed = true;
                _peripheralState = PeripheralState.Failed;
                _failure = e.Message;
                return false;
            }
        }

        private async void Check()
        {
            if (_disposed || _checking || !Enabled || _peripheral == null) return;
            _checking = true;
            PeripheralCheck check;
            try
            {
                // on the thread pool, as in the reference: WinRT objects made on the STA UI thread could route
                // their calls through it and stall notifications whenever the UI is busy
                check = await Task.Run(() => _peripheral.CheckAsync());
            }
            catch (Exception e)
            {
                Log("Bluetooth check failed: " + e);
                check = new PeripheralCheck(PeripheralState.Failed, e.Message);
            }
            _checking = false;
            if (_disposed) return;

            if (check.State != PeripheralState.Idle)
            {
                if (_peripheralState != check.State) Log("Bluetooth not available: " + check.State);
                if (_running) StopPeripheral();
                _peripheralState = check.State;
                _failure = check.Detail;
            }
            else if (!_running)
            {
                // Bluetooth came back on in Windows
                _peripheralState = PeripheralState.Idle;
                _failure = null;
            }
            Refresh();
        }

        private async void StartPeripheral()
        {
            _running = true;
            _starting = true;
            _peripheralState = PeripheralState.Starting;
            Log("starting the Bluetooth keyboard and mouse");
            PeripheralCheck result;
            try
            {
                result = await Task.Run(() => _peripheral.StartAsync());
            }
            catch (Exception e)
            {
                Log("Bluetooth start failed: " + e);
                result = new PeripheralCheck(PeripheralState.Failed, e.Message);
            }
            _starting = false;
            if (_disposed) return;

            if (!_running)
            {
                // mirroring ended or the switch went off while starting
                SafeStop();
                _peripheralState = PeripheralState.Idle;
            }
            else if (result.State != PeripheralState.Ready)
            {
                SafeStop();
                _running = false;
                _peripheralState = result.State;
                _failure = result.Detail;
                Log("Bluetooth did not start: " + result.State + (result.Detail != null ? " (" + result.Detail + ")" : ""));
            }
            else
            {
                _peripheralState = PeripheralState.Ready;
            }
            UpdateHosts();
        }

        private void StopPeripheral()
        {
            _running = false;
            End(StopReason.Disconnected);
            if (_starting) return; // StartPeripheral stops it once StartAsync is done
            SafeStop();
            if (_peripheralState == PeripheralState.Ready || _peripheralState == PeripheralState.Starting) _peripheralState = PeripheralState.Idle;
            _hosts = new PairedHost[0];
            Log("Bluetooth keyboard and mouse stopped");
        }

        private void SafeStop()
        {
            try
            {
                _peripheral.Stop();
            }
            catch (Exception e)
            {
                Log("stopping Bluetooth: " + e);
            }
        }

        private void UpdateHosts()
        {
            if (_disposed) return;
            try
            {
                _hosts = _running && !_starting ? _peripheral.Hosts() : new PairedHost[0];
            }
            catch (Exception e)
            {
                Log("reading connected devices: " + e.Message);
                _hosts = new PairedHost[0];
            }
            Refresh();
        }

        // ---- control sessions -----------------------------------------------------------------

        private void OnVideoClicked(int x, int y)
        {
            if (!CanControl) return;
            var video = _presenter.Video;
            // the click must have landed on the picture itself, not on a window over it
            var hit = Win32.GetAncestor(Win32.WindowFromPoint(new Win32.POINT { X = x, Y = y }), Win32.GA_ROOT);
            if (hit != video && (hit == IntPtr.Zero || hit != _presenter.Shield)) return;
            if (!VideoWindow.IsInFront(video)) VideoWindow.BringToFront(video);
            Begin(video);
        }

        private void Begin(IntPtr video)
        {
            _controlling = true;
            _session++;
            _controlledVideo = video;
            _peripheral.TargetId = _target.HostId;
            _capture.Begin(video, _session);
            _presenter.SetControlled(video);
            if (VideoWindow.TryGetClientBounds(video, out var picture))
            {
                if (_banner == null) _banner = new ControlBanner();
                _banner.ShowOver(picture, TargetName);
            }
            Log("control started: " + TargetName);
            StateChanged?.Invoke();
        }

        private void OnStopped(StopReason reason)
        {
            if (!_controlling) return;
            _controlling = false;
            _controlledVideo = IntPtr.Zero;
            _presenter.SetControlled(IntPtr.Zero);
            _banner?.HideNow();
            Log("control ended: " + reason);
            Refresh();
        }

        private void OnForegroundChanged()
        {
            if (_controlling && Win32.GetForegroundWindow() != _controlledVideo) End(StopReason.Focus);
        }

        private void OnSessionSwitch(object sender, SessionSwitchEventArgs e) => Post(() => End(StopReason.Focus));

        private void Post(Action action) => _ui.BeginInvoke(new Action(() =>
        {
            if (!_disposed) action();
        }));

        private void Log(string message) => _log.WriteLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + message);
    }
}
