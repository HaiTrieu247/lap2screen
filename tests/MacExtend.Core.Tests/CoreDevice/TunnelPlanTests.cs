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
    public void AListenerThatIsNotTunneldIsNeverStartedOverOrShutDown()
    {
        var step = TunnelPlan.First(tunneldAnswers: false, hasTunnelForDevice: false, portListens: true);

        Assert.Equal(TunnelStep.PortBusy, step);
        Assert.False(TunnelPlan.ShutdownAfter(step));
    }

    [Fact]
    public void AClosedPortStartsTunneldAndAnAnsweringOneIsUsed()
    {
        Assert.Equal(TunnelStep.StartTunneld, TunnelPlan.First(false, false, portListens: false));
        Assert.Equal(TunnelStep.UseExisting, TunnelPlan.First(true, true, portListens: true));
        Assert.Equal(TunnelStep.WaitForExisting, TunnelPlan.First(true, false, portListens: true));
    }

    [Theory]
    [InlineData(TunnelOutcome.Timeout, true, true)]
    [InlineData(TunnelOutcome.Timeout, false, false)]
    [InlineData(TunnelOutcome.AdminDeclined, true, false)]
    [InlineData(TunnelOutcome.SetupMissing, true, false)]
    [InlineData(TunnelOutcome.Ready, true, false)]
    public void RulesAreReaddedOnlyAfterATimeoutOfOurOwnStart(TunnelOutcome outcome, bool startedByUs, bool expected) =>
        Assert.Equal(expected, TunnelPlan.ReaddRulesAfter(outcome, startedByUs));

    [Fact]
    public void OnlyATunneldThisSessionStartedIsShutDown()
    {
        var step = TunnelPlan.First(tunneldAnswers: false, hasTunnelForDevice: false);

        Assert.Equal(TunnelStep.StartTunneld, step);
        Assert.True(TunnelPlan.ShutdownAfter(step));
    }
}
