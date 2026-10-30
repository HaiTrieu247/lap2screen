using System;
using MacExtend.Core.CoreDevice;

namespace MacExtend.Core.Tests.CoreDevice;

public class CoreDeviceMessagesTests
{
    [Fact]
    public void EveryPhaseAndFailureHasAMessage()
    {
        foreach (SessionPhase phase in Enum.GetValues(typeof(SessionPhase))) Assert.False(string.IsNullOrWhiteSpace(CoreDeviceMessages.Phase(phase)));
        foreach (SessionFailure failure in Enum.GetValues(typeof(SessionFailure)))
        {
            if (failure == SessionFailure.None) continue;
            Assert.False(string.IsNullOrWhiteSpace(CoreDeviceMessages.Failure(failure)));
        }
    }

    [Fact]
    public void WifiProblemsOfThePortAndTheAccountSaySoInVietnamese()
    {
        Assert.Contains("49151", CoreDeviceMessages.Failure(SessionFailure.PortInUse));
        Assert.Equal("Điều khiển qua Wi-Fi cần tài khoản Windows có quyền quản trị; tài khoản này chỉ dùng được qua cáp.",
                     CoreDeviceMessages.Failure(SessionFailure.NeedsAdminAccount));
    }

    [Fact]
    public void ANewSessionWaitingForThePreviousStopSaysSo()
    {
        Assert.Equal("Đang dừng phiên trước...", CoreDeviceMessages.WaitingForPreviousStop);
    }

    [Fact]
    public void TheCameraMessageSaysWhatToClose()
    {
        Assert.Contains("Camera", CoreDeviceMessages.Failure(SessionFailure.MediaInUse));
    }

    [Fact]
    public void SetupCanFixTrustDeveloperModeAndWindows()
    {
        Assert.True(CoreDeviceMessages.NeedsSetup(SessionFailure.NeedsTrust));
        Assert.True(CoreDeviceMessages.NeedsSetup(SessionFailure.DeveloperSetup));
        Assert.True(CoreDeviceMessages.NeedsSetup(SessionFailure.WindowsSetupMissing));
        Assert.True(CoreDeviceMessages.NeedsSetup(SessionFailure.NoAppleService));
        Assert.False(CoreDeviceMessages.NeedsSetup(SessionFailure.MediaInUse));
    }

    [Fact]
    public void RoutesHaveLabels()
    {
        Assert.Equal("Qua cáp", CoreDeviceMessages.Route(CoreDeviceRoute.Usb));
        Assert.Equal("Qua Wi-Fi", CoreDeviceMessages.Route(CoreDeviceRoute.Wifi));
    }
}
