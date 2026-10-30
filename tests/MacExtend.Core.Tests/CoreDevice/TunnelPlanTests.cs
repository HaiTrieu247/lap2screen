using MacExtend.Core.CoreDevice;

namespace MacExtend.Core.Tests.CoreDevice;

public class TunnelPlanTests
{
    [Fact]
    public void ATunnelThatIsAlreadyThereIsUsedAndNotShutDown()
    {
        var step = TunnelPlan.First(tunneldAnswers: true, hasTunnelForDevice: true);

        Assert.Equal(TunnelStep.UseExisting, step);
        Assert.False(TunnelPlan.ShutdownAfter(step));
    }

    [Fact]
    public void SomeoneElsesTunneldIsWaitedForNotStartedTwiceAndNotShutDown()
    {
        var step = TunnelPlan.First(tunneldAnswers: true, hasTunnelForDevice: false);

        Assert.Equal(TunnelStep.WaitForExisting, step);
        Assert.False(TunnelPlan.ShutdownAfter(step));
    }

    [Fact]
    public void OnlyATunneldThisSessionStartedIsShutDown()
    {
        var step = TunnelPlan.First(tunneldAnswers: false, hasTunnelForDevice: false);

        Assert.Equal(TunnelStep.StartTunneld, step);
        Assert.True(TunnelPlan.ShutdownAfter(step));
    }
}
