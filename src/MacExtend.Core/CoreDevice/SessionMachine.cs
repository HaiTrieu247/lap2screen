using System;

namespace MacExtend.Core.CoreDevice
{
    public enum SessionPhase
    {
        Idle,
        StartingTunnel,
        Preparing,
        Starting,
        Ready,
        Reconnecting,
        WaitingForDevice,
        Stopping,
        Stopped,
        Failed,
    }

    public enum SessionFailure
    {
        None,
        MediaInUse,
        DeviceNotFound,
        NeedsTrust,
        DeviceLocked,
        DeveloperSetup,
        NoInternet,
        NoAppleService,
        StartTimeout,
        TunnelTimeout,
        WindowsSetupMissing,
        AdminDeclined,
        PortInUse,
        NeedsAdminAccount,
        KeepsExiting,
        Exited,
        Other,
    }

    public enum SessionAction
    {
        None,
        StartTunnel,
        MountImage,
        StartServeWeb,
        StopServeWeb,
        WatchUsb,
    }

    public enum TunnelOutcome
    {
        Ready,
        Timeout,
        SetupMissing,
        AdminDeclined,
        /// <summary>127.0.0.1:49151 is taken by something that is not a tunneld listing.</summary>
        PortInUse,
        /// <summary>Wi-Fi control needs an administrator account.</summary>
        NeedsAdminAccount,
    }

    /// <summary>
    /// One "Điều khiển trực tiếp" session as a pure state machine: the service tells it what happened and does the
    /// action it returns. Wi-Fi reconnects are serve-web's own (through tunneld). A cable session that loses the device
    /// stops serve-web and waits for the device to come back, restarting at most <see cref="MaxRestarts"/> times
    /// without reaching Ready. A restart needs both that the device is back and that the old serve-web has exited,
    /// in either order. After <see cref="Stop"/> nothing restarts.
    /// Thread-safe: every member takes one private lock; the returned action is decided atomically, the caller performs it outside the lock.
    /// </summary>
    public sealed class SessionMachine
    {
        public const int MaxRestarts = 3;

        private readonly object _gate = new object();
        private SessionPhase _phase = SessionPhase.Idle;
        private SessionFailure _failure;
        private bool _stopRequested;
        private int _restarts;
        private bool _oldServeWebExited;
        private bool _deviceBack;

        public SessionMachine(CoreDeviceRoute route)
        {
            if (route == CoreDeviceRoute.None) throw new ArgumentException("a session needs a route", nameof(route));
            Route = route;
        }

        public CoreDeviceRoute Route { get; }

        public SessionPhase Phase
        {
            get { lock (_gate) return _phase; }
        }

        public SessionFailure Failure
        {
            get { lock (_gate) return _failure; }
        }

        public bool StopRequested
        {
            get { lock (_gate) return _stopRequested; }
        }

        public bool IsActive
        {
            get { lock (_gate) return IsActiveLocked(); }
        }

        /// <summary>serve-web may still run, so a stop has to go through its cleanup.</summary>
        public bool ServeWebMayRun
        {
            get { lock (_gate) return ServeWebMayRunLocked(); }
        }

        /// <summary>Failed, Stopping or Stopped: nothing in this session starts again, so the service tears it down.</summary>
        public bool IsOver
        {
            get { lock (_gate) return _phase == SessionPhase.Failed || _phase == SessionPhase.Stopping || _phase == SessionPhase.Stopped; }
        }

        public SessionAction Start()
        {
            lock (_gate)
            {
                if (_phase != SessionPhase.Idle) return SessionAction.None;
                if (Route == CoreDeviceRoute.Wifi)
                {
                    _phase = SessionPhase.StartingTunnel;
                    return SessionAction.StartTunnel;
                }
                _phase = SessionPhase.Preparing;
                return SessionAction.MountImage;
            }
        }

        public SessionAction TunnelReady(TunnelOutcome outcome)
        {
            lock (_gate)
            {
                if (_phase != SessionPhase.StartingTunnel || _stopRequested) return SessionAction.None;
                switch (outcome)
                {
                    case TunnelOutcome.Ready:
                        _phase = SessionPhase.Preparing;
                        return SessionAction.MountImage;
                    case TunnelOutcome.SetupMissing:
                        return Fail(SessionFailure.WindowsSetupMissing);
                    case TunnelOutcome.AdminDeclined:
                        return Fail(SessionFailure.AdminDeclined);
                    case TunnelOutcome.PortInUse:
                        return Fail(SessionFailure.PortInUse);
                    case TunnelOutcome.NeedsAdminAccount:
                        return Fail(SessionFailure.NeedsAdminAccount);
                    default:
                        return Fail(SessionFailure.TunnelTimeout);
                }
            }
        }

        public SessionAction ImageMounted(bool ok, CliError error)
        {
            lock (_gate)
            {
                if (_phase != SessionPhase.Preparing || _stopRequested) return SessionAction.None;
                if (!ok) return Fail(FromCli(error));
                _phase = SessionPhase.Starting;
                return SessionAction.StartServeWeb;
            }
        }

        public SessionAction ServeWebReady()
        {
            lock (_gate)
            {
                if (_phase != SessionPhase.Starting || _stopRequested) return SessionAction.None;
                _phase = SessionPhase.Ready;
                _restarts = 0;
                return SessionAction.None;
            }
        }

        /// <summary>serve-web did not become ready; its job still has to be stopped.</summary>
        public SessionAction ServeWebFailed(ServeWebLogKind lastFatal, bool timedOut)
        {
            lock (_gate)
            {
                if (_phase != SessionPhase.Starting || _stopRequested) return SessionAction.None;
                return Fail(timedOut ? SessionFailure.StartTimeout : FromServeWeb(lastFatal), SessionAction.StopServeWeb);
            }
        }

        public SessionAction Log(ServeWebLogKind kind)
        {
            lock (_gate)
            {
                if (_stopRequested) return SessionAction.None;
                if (kind == ServeWebLogKind.DeviceDisconnected && _phase == SessionPhase.Ready) _phase = SessionPhase.Reconnecting;
                else if (kind == ServeWebLogKind.DeviceReconnected && _phase == SessionPhase.Reconnecting) _phase = SessionPhase.Ready;
                return SessionAction.None;
            }
        }

        /// <summary>The cable session's device left usbmux while serve-web was starting or running.</summary>
        public SessionAction UsbDeviceGone()
        {
            lock (_gate)
            {
                if (Route != CoreDeviceRoute.Usb || _stopRequested) return SessionAction.None;
                if (_phase != SessionPhase.Starting && _phase != SessionPhase.Ready) return SessionAction.None;
                EnterWaitingForDevice();
                return SessionAction.StopServeWeb;
            }
        }

        /// <summary>serve-web's job is empty: it exited on its own, or a stop this machine asked for finished.</summary>
        public SessionAction ServeWebExited(ServeWebLogKind lastFatal)
        {
            lock (_gate)
            {
                if (_stopRequested) return SessionAction.None;
                if (_phase == SessionPhase.WaitingForDevice)
                {
                    _oldServeWebExited = true;
                    return _deviceBack ? Restart() : SessionAction.WatchUsb;
                }
                if (_phase != SessionPhase.Ready && _phase != SessionPhase.Reconnecting) return SessionAction.None;
                if (lastFatal == ServeWebLogKind.MediaInUse) return Fail(SessionFailure.MediaInUse);
                if (Route == CoreDeviceRoute.Usb)
                {
                    EnterWaitingForDevice();
                    _oldServeWebExited = true;
                    return SessionAction.WatchUsb;
                }
                return Fail(SessionFailure.Exited);
            }
        }

        public SessionAction UsbDeviceBack()
        {
            lock (_gate)
            {
                if (_phase != SessionPhase.WaitingForDevice || _stopRequested) return SessionAction.None;
                _deviceBack = true;
                return _oldServeWebExited ? Restart() : SessionAction.None;
            }
        }

        public SessionAction Stop()
        {
            lock (_gate)
            {
                if (!IsActiveLocked()) return SessionAction.None;
                var serveWeb = ServeWebMayRunLocked();
                _stopRequested = true;
                _phase = SessionPhase.Stopping;
                return serveWeb ? SessionAction.StopServeWeb : SessionAction.None;
            }
        }

        /// <summary>The service hit an error it has no other input for: fail with Other; serve-web, if it may run, still has to be stopped.</summary>
        public SessionAction Fault()
        {
            lock (_gate)
            {
                if (!IsActiveLocked() || _stopRequested) return SessionAction.None;
                return Fail(SessionFailure.Other, ServeWebMayRunLocked() ? SessionAction.StopServeWeb : SessionAction.None);
            }
        }

        public void StopFinished()
        {
            lock (_gate)
            {
                if (_phase == SessionPhase.Stopping) _phase = SessionPhase.Stopped;
            }
        }

        private bool IsActiveLocked() =>
            _phase != SessionPhase.Idle && _phase != SessionPhase.Stopped && _phase != SessionPhase.Failed;

        private bool ServeWebMayRunLocked() =>
            _phase == SessionPhase.Starting || _phase == SessionPhase.Ready || _phase == SessionPhase.Reconnecting || _phase == SessionPhase.WaitingForDevice;

        private void EnterWaitingForDevice()
        {
            _phase = SessionPhase.WaitingForDevice;
            _oldServeWebExited = false;
            _deviceBack = false;
        }

        /// <summary>Both the device is back and the old serve-web has exited: count the restart and mount again.</summary>
        private SessionAction Restart()
        {
            if (++_restarts > MaxRestarts) return Fail(SessionFailure.KeepsExiting);
            _phase = SessionPhase.Preparing;
            _oldServeWebExited = false;
            _deviceBack = false;
            return SessionAction.MountImage;
        }

        private SessionAction Fail(SessionFailure failure, SessionAction action = SessionAction.None)
        {
            _failure = failure;
            _phase = SessionPhase.Failed;
            _oldServeWebExited = false;
            _deviceBack = false;
            return action;
        }

        public static SessionFailure FromCli(CliError error)
        {
            switch (error)
            {
                case CliError.NotConnected: return SessionFailure.DeviceNotFound;
                case CliError.NeedsTrust:
                case CliError.TrustDenied: return SessionFailure.NeedsTrust;
                case CliError.DeviceLocked: return SessionFailure.DeviceLocked;
                case CliError.DeveloperModeOff:
                case CliError.ServiceUnavailable: return SessionFailure.DeveloperSetup;
                case CliError.NoInternet: return SessionFailure.NoInternet;
                case CliError.NoUsbmux: return SessionFailure.NoAppleService;
                default: return SessionFailure.Other;
            }
        }

        internal static SessionFailure FromServeWeb(ServeWebLogKind kind)
        {
            switch (kind)
            {
                case ServeWebLogKind.MediaInUse: return SessionFailure.MediaInUse;
                case ServeWebLogKind.ServiceUnavailable: return SessionFailure.DeveloperSetup;
                case ServeWebLogKind.DeviceNotFound: return SessionFailure.DeviceNotFound;
                default: return SessionFailure.Other;
            }
        }
    }
}
