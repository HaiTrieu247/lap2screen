using MacExtend.Core.CoreDevice;

namespace MacExtend.Core.Tests.CoreDevice;

public class SessionViewTests
{
    private const string Udid = "00008110-001A2B3C4D5E6F70";

    [Fact]
    public void IdleShowsNothingRunning()
    {
        var v = SessionView.Idle;

        Assert.Equal(SessionPhase.Idle, v.Phase);
        Assert.Equal(SessionFailure.None, v.Failure);
        Assert.Equal(CoreDeviceRoute.None, v.Route);
        Assert.Null(v.Udid);
        Assert.False(v.IsActive);
        Assert.Equal(0, v.Port);
        Assert.Null(v.ViewerUrl);
        Assert.False(v.LastStopWasForced);
        Assert.False(v.WaitingForPreviousStop);
        Assert.Equal("Chưa chạy", v.StatusText);
    }

    [Theory]
    [InlineData(SessionPhase.Ready)]
    [InlineData(SessionPhase.Reconnecting)]
    public void ThePortIsShownWhileTheViewerCanBeUsed(SessionPhase phase)
    {
        var v = SessionView.Of(phase, SessionFailure.None, CoreDeviceRoute.Wifi, Udid, 50123, false);

        Assert.Equal(50123, v.Port);
        Assert.Equal("http://127.0.0.1:50123/", v.ViewerUrl);
        Assert.True(v.IsActive);
    }

    [Theory]
    [InlineData(SessionPhase.Starting)]
    [InlineData(SessionPhase.WaitingForDevice)]
    [InlineData(SessionPhase.Stopping)]
    [InlineData(SessionPhase.Stopped)]
    [InlineData(SessionPhase.Failed)]
    public void ThePortIsHiddenOtherwise(SessionPhase phase)
    {
        var v = SessionView.Of(phase, SessionFailure.None, CoreDeviceRoute.Usb, Udid, 50123, false);

        Assert.Equal(0, v.Port);
        Assert.Null(v.ViewerUrl);
    }

    [Fact]
    public void AFailureExplainsItself()
    {
        var v = SessionView.Of(SessionPhase.Failed, SessionFailure.MediaInUse, CoreDeviceRoute.Usb, Udid, 0, false);

        Assert.False(v.IsActive);
        Assert.Equal(SessionFailure.MediaInUse, v.Failure);
        Assert.Equal(CoreDeviceMessages.Failure(SessionFailure.MediaInUse), v.StatusText);
    }

    [Fact]
    public void AFailureCountsOnlyOnceTheSessionFailed()
    {
        var v = SessionView.Of(SessionPhase.Ready, SessionFailure.MediaInUse, CoreDeviceRoute.Usb, Udid, 50123, false);

        Assert.Equal(SessionFailure.None, v.Failure);
        Assert.Equal("Đang điều khiển", v.StatusText);
    }

    [Theory]
    [InlineData(SessionPhase.StartingTunnel, true)]
    [InlineData(SessionPhase.Preparing, true)]
    [InlineData(SessionPhase.WaitingForDevice, true)]
    [InlineData(SessionPhase.Stopping, true)]
    [InlineData(SessionPhase.Stopped, false)]
    public void OtherwiseThePhaseSaysWhatHappens(SessionPhase phase, bool active)
    {
        var v = SessionView.Of(phase, SessionFailure.None, CoreDeviceRoute.Wifi, Udid, 0, false);

        Assert.Equal(active, v.IsActive);
        Assert.Equal(CoreDeviceMessages.Phase(phase), v.StatusText);
        Assert.Equal(CoreDeviceRoute.Wifi, v.Route);
        Assert.Equal(Udid, v.Udid);
    }

    [Fact]
    public void AForcedStopIsKept()
    {
        Assert.True(SessionView.Of(SessionPhase.Stopped, SessionFailure.None, CoreDeviceRoute.Usb, Udid, 0, true).LastStopWasForced);
    }

    [Fact]
    public void AnOpenWaitingForThePreviousStopSaysSo()
    {
        var v = SessionView.WaitingFor(CoreDeviceRoute.Wifi, Udid);

        Assert.True(v.WaitingForPreviousStop);
        Assert.True(v.IsActive);
        Assert.Equal(SessionPhase.Stopping, v.Phase);
        Assert.Equal(SessionFailure.None, v.Failure);
        Assert.Equal(CoreDeviceRoute.Wifi, v.Route);
        Assert.Equal(Udid, v.Udid);
        Assert.Equal(0, v.Port);
        Assert.False(v.LastStopWasForced);
        Assert.Equal("Đang dừng phiên trước...", v.StatusText);
    }

    [Fact]
    public void EqualViewsCompareEqual()
    {
        var a = SessionView.Of(SessionPhase.Ready, SessionFailure.None, CoreDeviceRoute.Usb, Udid, 50123, false);
        var b = SessionView.Of(SessionPhase.Ready, SessionFailure.None, CoreDeviceRoute.Usb, Udid, 50123, false);

        Assert.True(a.Equals(b));
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.False(a.Equals(SessionView.Of(SessionPhase.Ready, SessionFailure.None, CoreDeviceRoute.Usb, Udid, 50124, false)));
        Assert.False(a.Equals(SessionView.Of(SessionPhase.Reconnecting, SessionFailure.None, CoreDeviceRoute.Usb, Udid, 50123, false)));
        Assert.False(a.Equals(SessionView.WaitingFor(CoreDeviceRoute.Usb, Udid)));
        Assert.False(a.Equals(null));
    }
}
