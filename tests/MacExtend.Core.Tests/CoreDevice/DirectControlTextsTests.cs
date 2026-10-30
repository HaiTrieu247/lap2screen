using MacExtend.Core.CoreDevice;

namespace MacExtend.Core.Tests.CoreDevice;

public class DirectControlTextsTests
{
    private const string A = "00008030-001E185E343B802E";

    private static SessionView View(SessionPhase phase, CoreDeviceRoute route = CoreDeviceRoute.Usb, bool forced = false,
                                    SessionFailure failure = SessionFailure.None) =>
        SessionView.Of(phase, failure, route, A, phase == SessionPhase.Ready ? 50123 : 0, forced);

    [Fact]
    public void TheSettingsLineShowsTheRunningSession()
    {
        Assert.Equal("Đang điều khiển", DirectControlTexts.Settings(View(SessionPhase.Ready), runtimeCurrent: true, opening: false));
        Assert.Equal("Đang dừng phiên trước...",
                     DirectControlTexts.Settings(SessionView.WaitingFor(CoreDeviceRoute.Usb, A), runtimeCurrent: true, opening: false));
    }

    [Fact]
    public void TheSettingsLineSaysWhileTheDeviceIsLookedFor()
    {
        Assert.Equal("Đang tìm thiết bị...", DirectControlTexts.Settings(SessionView.Idle, runtimeCurrent: true, opening: true));
    }

    [Fact]
    public void TheSettingsLineSaysWhetherSetupWasDone()
    {
        Assert.Equal(DirectControlTexts.SetUp, DirectControlTexts.Settings(SessionView.Idle, runtimeCurrent: true, opening: false));
        Assert.Equal(DirectControlTexts.NotSetUp, DirectControlTexts.Settings(SessionView.Idle, runtimeCurrent: false, opening: false));
        Assert.Equal(DirectControlTexts.SetUp,
                     DirectControlTexts.Settings(View(SessionPhase.Failed, failure: SessionFailure.Exited), runtimeCurrent: true, opening: false));
        Assert.Contains("Mở điều khiển", DirectControlTexts.SetUp);
        Assert.Contains("Developer Mode", DirectControlTexts.NotSetUp);
    }

    [Fact]
    public void TheTrayItemStartsStopsOrWaits()
    {
        Assert.Equal("Điều khiển trực tiếp...", DirectControlTexts.TrayItem(SessionView.Idle));
        Assert.Equal("Điều khiển trực tiếp...", DirectControlTexts.TrayItem(View(SessionPhase.Failed, failure: SessionFailure.Exited)));
        Assert.Equal("Dừng điều khiển trực tiếp", DirectControlTexts.TrayItem(View(SessionPhase.Preparing)));
        Assert.Equal("Dừng điều khiển trực tiếp", DirectControlTexts.TrayItem(View(SessionPhase.Ready)));
        Assert.Equal("Đang dừng điều khiển trực tiếp...", DirectControlTexts.TrayItem(View(SessionPhase.Stopping)));
        Assert.Equal("Đang dừng điều khiển trực tiếp...", DirectControlTexts.TrayItem(SessionView.WaitingFor(CoreDeviceRoute.Wifi, A)));
    }

    [Fact]
    public void TheTrayItemIsOffOnlyWhileStopping()
    {
        Assert.True(DirectControlTexts.TrayItemEnabled(SessionView.Idle));
        Assert.True(DirectControlTexts.TrayItemEnabled(View(SessionPhase.Ready)));
        Assert.False(DirectControlTexts.TrayItemEnabled(View(SessionPhase.Stopping)));
        Assert.False(DirectControlTexts.TrayItemEnabled(SessionView.WaitingFor(CoreDeviceRoute.Usb, A)));
    }

    [Fact]
    public void TheTrayStatusNamesTheRouteOnlyWhileTheDeviceIsShown()
    {
        Assert.Equal("Đang điều khiển trực tiếp (qua cáp)", DirectControlTexts.TrayStatus(View(SessionPhase.Ready)));
        Assert.Equal("Đang điều khiển trực tiếp (qua Wi-Fi)", DirectControlTexts.TrayStatus(View(SessionPhase.Ready, CoreDeviceRoute.Wifi)));
        Assert.Null(DirectControlTexts.TrayStatus(View(SessionPhase.Starting)));
        Assert.Null(DirectControlTexts.TrayStatus(SessionView.Idle));
    }

    [Fact]
    public void AForcedStopIsToldOnceTheSessionIsOver()
    {
        Assert.True(DirectControlTexts.WarnForcedStop(View(SessionPhase.Stopped, forced: true)));
        Assert.True(DirectControlTexts.WarnForcedStop(View(SessionPhase.Failed, forced: true, failure: SessionFailure.Exited)));
        Assert.False(DirectControlTexts.WarnForcedStop(View(SessionPhase.Stopped)));
        Assert.False(DirectControlTexts.WarnForcedStop(View(SessionPhase.Ready, forced: true)));
        Assert.False(DirectControlTexts.WarnForcedStop(SessionView.Idle));
    }

    [Fact]
    public void TheWindowsSetupPromptNamesItsButtonAndTheUacPrompt()
    {
        var detail = DirectControlTexts.ProblemDetail(SessionFailure.WindowsSetupMissing);
        Assert.Contains("Bấm " + DirectControlTexts.Resetup, detail);
        Assert.Contains("quyền quản trị", detail);
        Assert.Contains("cáp", detail);
        Assert.Equal("Thiết lập lại", DirectControlTexts.Resetup);
        Assert.Contains(DirectControlTexts.Resetup, DirectControlTexts.SetupBusy);
    }

    [Fact]
    public void TheNotFoundPromptIsTheSessionsOwnMessage()
    {
        Assert.Equal(CoreDeviceMessages.Failure(SessionFailure.DeviceNotFound), DirectControlTexts.ProblemDetail(SessionFailure.DeviceNotFound));
        Assert.False(string.IsNullOrWhiteSpace(DirectControlTexts.ProblemTitle(SessionFailure.DeviceNotFound)));
        Assert.NotEqual(DirectControlTexts.ProblemTitle(SessionFailure.DeviceNotFound), DirectControlTexts.ProblemTitle(SessionFailure.WindowsSetupMissing));
    }
}
