using System;
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

    private static ShutdownAction Next(bool seen, bool listening, int elapsed, int? sinceFirst, bool task) =>
        TunnelPlan.NextShutdown(seen, listening, TimeSpan.FromSeconds(elapsed), sinceFirst.HasValue ? TimeSpan.FromSeconds(sinceFirst.Value) : (TimeSpan?)null, task);

    [Fact]
    public void ShutdownIsResentWhileThePortListens()
    {
        Assert.Equal(ShutdownAction.SendShutdown, Next(seen: true, listening: true, elapsed: 0, sinceFirst: null, task: true));
        Assert.Equal(ShutdownAction.SendShutdown, Next(true, true, 5, 3, task: false));
    }

    [Fact]
    public void ShutdownIsDoneOnceTheSeenPortCloses()
    {
        Assert.Equal(ShutdownAction.Done, Next(seen: true, listening: false, elapsed: 4, sinceFirst: 2, task: true));
    }

    [Fact]
    public void ALateBindingTunneldIsWaitedForUntilTheBound()
    {
        Assert.Equal(ShutdownAction.Wait, Next(seen: false, listening: false, elapsed: 10, sinceFirst: null, task: false));
        Assert.Equal(ShutdownAction.Done, Next(false, false, 30, null, task: false));
    }

    [Fact]
    public void TaskModeEndsTheTaskWhenThePortNeverCameUpOrStaysUp()
    {
        Assert.Equal(ShutdownAction.EndTask, Next(seen: false, listening: false, elapsed: 30, sinceFirst: null, task: true));
        Assert.Equal(ShutdownAction.EndTask, Next(seen: true, listening: true, elapsed: 12, sinceFirst: 10, task: true));
        Assert.Equal(ShutdownAction.SendShutdown, Next(true, true, 12, 9, task: true));
    }

    [Fact]
    public void UacModeStopsTryingAtTheBoundAndNeverEndsATask()
    {
        Assert.Equal(ShutdownAction.Done, Next(seen: true, listening: true, elapsed: 30, sinceFirst: 25, task: false));
        Assert.Equal(ShutdownAction.SendShutdown, Next(true, true, 12, 10, task: false));
    }

    [Fact]
    public void OnlyATunneldThisSessionStartedIsShutDown()
    {
        var step = TunnelPlan.First(tunneldAnswers: false, hasTunnelForDevice: false);

        Assert.Equal(TunnelStep.StartTunneld, step);
        Assert.True(TunnelPlan.ShutdownAfter(step));
    }
}
