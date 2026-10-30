using System.Threading.Tasks;
using MacExtend.Core.CoreDevice;

namespace MacExtend.Core.Tests.CoreDevice;

public class SessionMachineTests
{
    private static SessionMachine ReadyUsb()
    {
        var m = new SessionMachine(CoreDeviceRoute.Usb);
        m.Start();
        m.ImageMounted(true, CliError.None);
        m.ServeWebReady();
        return m;
    }

    [Fact]
    public void UsbMountsThenStartsServeWeb()
    {
        var m = new SessionMachine(CoreDeviceRoute.Usb);

        Assert.Equal(SessionAction.MountImage, m.Start());
        Assert.Equal(SessionPhase.Preparing, m.Phase);
        Assert.Equal(SessionAction.StartServeWeb, m.ImageMounted(true, CliError.None));
        Assert.Equal(SessionAction.None, m.ServeWebReady());
        Assert.Equal(SessionPhase.Ready, m.Phase);
        Assert.Equal(SessionAction.StopServeWeb, m.Stop());
        Assert.Equal(SessionPhase.Stopping, m.Phase);
        m.StopFinished();
        Assert.Equal(SessionPhase.Stopped, m.Phase);
        Assert.False(m.IsActive);
    }

    [Fact]
    public void WifiNeedsTheTunnelBeforeTheImage()
    {
        var m = new SessionMachine(CoreDeviceRoute.Wifi);

        Assert.Equal(SessionAction.StartTunnel, m.Start());
        Assert.Equal(SessionAction.MountImage, m.TunnelReady(TunnelOutcome.Ready));
        Assert.Equal(SessionAction.StartServeWeb, m.ImageMounted(true, CliError.None));
    }

    [Theory]
    [InlineData(TunnelOutcome.Timeout, SessionFailure.TunnelTimeout)]
    [InlineData(TunnelOutcome.SetupMissing, SessionFailure.WindowsSetupMissing)]
    [InlineData(TunnelOutcome.AdminDeclined, SessionFailure.AdminDeclined)]
    public void TunnelProblemsFailTheSession(TunnelOutcome outcome, SessionFailure expected)
    {
        var m = new SessionMachine(CoreDeviceRoute.Wifi);
        m.Start();

        Assert.Equal(SessionAction.None, m.TunnelReady(outcome));
        Assert.Equal(SessionPhase.Failed, m.Phase);
        Assert.Equal(expected, m.Failure);
    }

    [Theory]
    [InlineData(CliError.NotConnected, SessionFailure.DeviceNotFound)]
    [InlineData(CliError.NeedsTrust, SessionFailure.NeedsTrust)]
    [InlineData(CliError.TrustDenied, SessionFailure.NeedsTrust)]
    [InlineData(CliError.DeviceLocked, SessionFailure.DeviceLocked)]
    [InlineData(CliError.DeveloperModeOff, SessionFailure.DeveloperSetup)]
    [InlineData(CliError.ServiceUnavailable, SessionFailure.DeveloperSetup)]
    [InlineData(CliError.NoInternet, SessionFailure.NoInternet)]
    [InlineData(CliError.NoUsbmux, SessionFailure.NoAppleService)]
    [InlineData(CliError.Other, SessionFailure.Other)]
    public void MountFailuresAreExplained(CliError error, SessionFailure expected)
    {
        var m = new SessionMachine(CoreDeviceRoute.Usb);
        m.Start();

        m.ImageMounted(false, error);

        Assert.Equal(expected, m.Failure);
    }

    [Fact]
    public void MediaInUseAtStartIsReported()
    {
        var m = new SessionMachine(CoreDeviceRoute.Usb);
        m.Start();
        m.ImageMounted(true, CliError.None);

        Assert.Equal(SessionAction.StopServeWeb, m.ServeWebFailed(ServeWebLogKind.MediaInUse, timedOut: false));
        Assert.Equal(SessionPhase.Failed, m.Phase);
        Assert.Equal(SessionFailure.MediaInUse, m.Failure);
    }

    [Fact]
    public void AStartTimeoutIsNotTheLastLogLine()
    {
        var m = new SessionMachine(CoreDeviceRoute.Usb);
        m.Start();
        m.ImageMounted(true, CliError.None);

        m.ServeWebFailed(ServeWebLogKind.Error, timedOut: true);

        Assert.Equal(SessionFailure.StartTimeout, m.Failure);
    }

    [Fact]
    public void WifiDisconnectsAndReconnectsThroughTheLog()
    {
        var m = new SessionMachine(CoreDeviceRoute.Wifi);
        m.Start();
        m.TunnelReady(TunnelOutcome.Ready);
        m.ImageMounted(true, CliError.None);
        m.ServeWebReady();

        m.Log(ServeWebLogKind.DeviceDisconnected);
        Assert.Equal(SessionPhase.Reconnecting, m.Phase);
        m.Log(ServeWebLogKind.DeviceReconnected);
        Assert.Equal(SessionPhase.Ready, m.Phase);
    }

    [Fact]
    public void AnUnpluggedCableWaitsThenRestarts()
    {
        var m = ReadyUsb();

        Assert.Equal(SessionAction.StopServeWeb, m.UsbDeviceGone());
        Assert.Equal(SessionPhase.WaitingForDevice, m.Phase);
        Assert.Equal(SessionAction.WatchUsb, m.ServeWebExited(ServeWebLogKind.Other));
        Assert.Equal(SessionAction.MountImage, m.UsbDeviceBack());
        Assert.Equal(SessionPhase.Preparing, m.Phase);
    }

    [Fact]
    public void ServeWebExitingOnACableSessionWaitsForTheDevice()
    {
        var m = ReadyUsb();

        Assert.Equal(SessionAction.WatchUsb, m.ServeWebExited(ServeWebLogKind.Other));
        Assert.Equal(SessionPhase.WaitingForDevice, m.Phase);
    }

    [Fact]
    public void ServeWebExitingOverWifiFails()
    {
        var m = new SessionMachine(CoreDeviceRoute.Wifi);
        m.Start();
        m.TunnelReady(TunnelOutcome.Ready);
        m.ImageMounted(true, CliError.None);
        m.ServeWebReady();

        m.ServeWebExited(ServeWebLogKind.Other);

        Assert.Equal(SessionFailure.Exited, m.Failure);
    }

    [Fact]
    public void RestartsAreBounded()
    {
        var m = ReadyUsb();
        m.UsbDeviceGone();

        for (var i = 0; i < SessionMachine.MaxRestarts; i++)
        {
            m.ServeWebExited(ServeWebLogKind.Other);
            Assert.Equal(SessionAction.MountImage, m.UsbDeviceBack());
            m.ImageMounted(true, CliError.None);
            m.UsbDeviceGone();
        }

        m.ServeWebExited(ServeWebLogKind.Other);
        Assert.Equal(SessionAction.None, m.UsbDeviceBack());
        Assert.Equal(SessionFailure.KeepsExiting, m.Failure);
    }

    [Fact]
    public void ReachingReadyResetsTheRestartCount()
    {
        var m = ReadyUsb();
        for (var i = 0; i < SessionMachine.MaxRestarts + 2; i++)
        {
            m.UsbDeviceGone();
            m.ServeWebExited(ServeWebLogKind.Other);
            Assert.Equal(SessionAction.MountImage, m.UsbDeviceBack());
            m.ImageMounted(true, CliError.None);
            m.ServeWebReady();
        }

        Assert.Equal(SessionPhase.Ready, m.Phase);
    }

    [Fact]
    public void StopWhileWaitingNeverRestarts()
    {
        var m = ReadyUsb();
        m.UsbDeviceGone();

        Assert.Equal(SessionAction.StopServeWeb, m.Stop());
        Assert.Equal(SessionAction.None, m.UsbDeviceBack());
        Assert.Equal(SessionAction.None, m.ServeWebExited(ServeWebLogKind.Other));
        Assert.Equal(SessionPhase.Stopping, m.Phase);
    }

    [Fact]
    public void StopBeforeServeWebNeedsNoServeWebStop()
    {
        var m = new SessionMachine(CoreDeviceRoute.Wifi);
        m.Start();

        Assert.Equal(SessionAction.None, m.Stop());
        Assert.Equal(SessionAction.None, m.TunnelReady(TunnelOutcome.Ready));
        Assert.Equal(SessionPhase.Stopping, m.Phase);
    }

    [Fact]
    public void LateEventsAfterAFailureChangeNothing()
    {
        var m = new SessionMachine(CoreDeviceRoute.Usb);
        m.Start();
        m.ImageMounted(false, CliError.NotConnected);

        Assert.Equal(SessionAction.None, m.ServeWebReady());
        Assert.Equal(SessionAction.None, m.Stop());
        Assert.Equal(SessionPhase.Failed, m.Phase);
    }

    [Fact]
    public void UsbBackBeforeTheOldServeWebExitsWaitsForIt()
    {
        var m = ReadyUsb();
        m.UsbDeviceGone();

        Assert.Equal(SessionAction.None, m.UsbDeviceBack());
        Assert.Equal(SessionPhase.WaitingForDevice, m.Phase);

        Assert.Equal(SessionAction.MountImage, m.ServeWebExited(ServeWebLogKind.Other));
        Assert.Equal(SessionPhase.Preparing, m.Phase);
    }

    [Fact]
    public void ServeWebExitingBeforeTheDeviceIsBackRestartsWhenItReturns()
    {
        var m = ReadyUsb();
        m.UsbDeviceGone();

        Assert.Equal(SessionAction.WatchUsb, m.ServeWebExited(ServeWebLogKind.Other));
        Assert.Equal(SessionPhase.WaitingForDevice, m.Phase);

        Assert.Equal(SessionAction.MountImage, m.UsbDeviceBack());
        Assert.Equal(SessionPhase.Preparing, m.Phase);
    }

    [Fact]
    public void StopThenADisconnectLogLineLeavesTheSessionStopping()
    {
        var m = new SessionMachine(CoreDeviceRoute.Wifi);
        m.Start();
        m.TunnelReady(TunnelOutcome.Ready);
        m.ImageMounted(true, CliError.None);
        m.ServeWebReady();

        m.Stop();
        m.Log(ServeWebLogKind.DeviceDisconnected);
        Assert.Equal(SessionPhase.Stopping, m.Phase);

        m.StopFinished();
        Assert.Equal(SessionPhase.Stopped, m.Phase);
    }

    [Fact]
    public async Task StopRacingWithReconnectLogLinesStillReachesStopped()
    {
        var m = new SessionMachine(CoreDeviceRoute.Wifi);
        m.Start();
        m.TunnelReady(TunnelOutcome.Ready);
        m.ImageMounted(true, CliError.None);
        m.ServeWebReady();

        var logs = Task.Run(() =>
        {
            for (var i = 0; i < 1000; i++)
            {
                m.Log(ServeWebLogKind.DeviceDisconnected);
                m.Log(ServeWebLogKind.DeviceReconnected);
            }
        });
        var stop = Task.Run(() => m.Stop());
        await Task.WhenAll(logs, stop);

        m.StopFinished();
        Assert.Equal(SessionPhase.Stopped, m.Phase);
    }
}
