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
    }

    /// <summary>
    /// One "Điều khiển trực tiếp" session as a pure state machine: the service tells it what happened and does the
    /// action it returns. Wi-Fi reconnects are serve-web's own (through tunneld). A cable session that loses the device
    /// stops serve-web and waits for the device to come back, restarting at most <see cref="MaxRestarts"/> times
    /// without reaching Ready. After <see cref="Stop"/> nothing restarts.
    /// </summary>
    public sealed class SessionMachine
    {
        public const int MaxRestarts = 3;

        private int _restarts;

        public SessionMachine(CoreDeviceRoute route)
        {
            if (route == CoreDeviceRoute.None) throw new ArgumentException("a session needs a route", nameof(route));
            Route = route;
        }

        public CoreDeviceRoute Route { get; }
        public SessionPhase Phase { get; private set; } = SessionPhase.Idle;
        public SessionFailure Failure { get; private set; }
        public bool StopRequested { get; private set; }

        public bool IsActive => Phase != SessionPhase.Idle && Phase != SessionPhase.Stopped && Phase != SessionPhase.Failed;

        /// <summary>serve-web may still run, so a stop has to go through its cleanup.</summary>
        public bool ServeWebMayRun =>
            Phase == SessionPhase.Starting || Phase == SessionPhase.Ready || Phase == SessionPhase.Reconnecting || Phase == SessionPhase.WaitingForDevice;

        public SessionAction Start()
        {
            if (Phase != SessionPhase.Idle) return SessionAction.None;
            if (Route == CoreDeviceRoute.Wifi)
            {
                Phase = SessionPhase.StartingTunnel;
                return SessionAction.StartTunnel;
            }
            Phase = SessionPhase.Preparing;
            return SessionAction.MountImage;
        }

        public SessionAction TunnelReady(TunnelOutcome outcome)
        {
            if (Phase != SessionPhase.StartingTunnel || StopRequested) return SessionAction.None;
            switch (outcome)
            {
                case TunnelOutcome.Ready:
                    Phase = SessionPhase.Preparing;
                    return SessionAction.MountImage;
                case TunnelOutcome.SetupMissing:
                    return Fail(SessionFailure.WindowsSetupMissing);
                case TunnelOutcome.AdminDeclined:
                    return Fail(SessionFailure.AdminDeclined);
                default:
                    return Fail(SessionFailure.TunnelTimeout);
            }
        }

        public SessionAction ImageMounted(bool ok, CliError error)
        {
            if (Phase != SessionPhase.Preparing || StopRequested) return SessionAction.None;
            if (!ok) return Fail(FromCli(error));
            Phase = SessionPhase.Starting;
            return SessionAction.StartServeWeb;
        }

        public SessionAction ServeWebReady()
        {
            if (Phase != SessionPhase.Starting || StopRequested) return SessionAction.None;
            Phase = SessionPhase.Ready;
            _restarts = 0;
            return SessionAction.None;
        }

        /// <summary>serve-web did not become ready; its job still has to be stopped.</summary>
        public SessionAction ServeWebFailed(ServeWebLogKind lastFatal, bool timedOut)
        {
            if (Phase != SessionPhase.Starting || StopRequested) return SessionAction.None;
            return Fail(timedOut ? SessionFailure.StartTimeout : FromServeWeb(lastFatal), SessionAction.StopServeWeb);
        }

        public SessionAction Log(ServeWebLogKind kind)
        {
            if (StopRequested) return SessionAction.None;
            if (kind == ServeWebLogKind.DeviceDisconnected && Phase == SessionPhase.Ready) Phase = SessionPhase.Reconnecting;
            else if (kind == ServeWebLogKind.DeviceReconnected && Phase == SessionPhase.Reconnecting) Phase = SessionPhase.Ready;
            return SessionAction.None;
        }

        /// <summary>The cable session's device left usbmux while serve-web was starting or running.</summary>
        public SessionAction UsbDeviceGone()
        {
            if (Route != CoreDeviceRoute.Usb || StopRequested) return SessionAction.None;
            if (Phase != SessionPhase.Starting && Phase != SessionPhase.Ready) return SessionAction.None;
            Phase = SessionPhase.WaitingForDevice;
            return SessionAction.StopServeWeb;
        }

        /// <summary>serve-web's job is empty: it exited on its own, or a stop this machine asked for finished.</summary>
        public SessionAction ServeWebExited(ServeWebLogKind lastFatal)
        {
            if (StopRequested) return SessionAction.None;
            if (Phase == SessionPhase.WaitingForDevice) return SessionAction.WatchUsb;
            if (Phase != SessionPhase.Ready && Phase != SessionPhase.Reconnecting) return SessionAction.None;
            if (lastFatal == ServeWebLogKind.MediaInUse) return Fail(SessionFailure.MediaInUse);
            if (Route == CoreDeviceRoute.Usb)
            {
                Phase = SessionPhase.WaitingForDevice;
                return SessionAction.WatchUsb;
            }
            return Fail(SessionFailure.Exited);
        }

        public SessionAction UsbDeviceBack()
        {
            if (Phase != SessionPhase.WaitingForDevice || StopRequested) return SessionAction.None;
            if (++_restarts > MaxRestarts) return Fail(SessionFailure.KeepsExiting);
            Phase = SessionPhase.Preparing;
            return SessionAction.MountImage;
        }

        public SessionAction Stop()
        {
            if (!IsActive) return SessionAction.None;
            var serveWeb = ServeWebMayRun;
            StopRequested = true;
            Phase = SessionPhase.Stopping;
            return serveWeb ? SessionAction.StopServeWeb : SessionAction.None;
        }

        public void StopFinished()
        {
            if (Phase == SessionPhase.Stopping) Phase = SessionPhase.Stopped;
        }

        private SessionAction Fail(SessionFailure failure, SessionAction action = SessionAction.None)
        {
            Failure = failure;
            Phase = SessionPhase.Failed;
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
