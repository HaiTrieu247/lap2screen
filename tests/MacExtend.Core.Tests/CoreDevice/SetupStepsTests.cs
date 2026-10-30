using System.Linq;
using MacExtend.Core.CoreDevice;

namespace MacExtend.Core.Tests.CoreDevice;

public class SetupStepsTests
{
    private const string Udid = "00008030-001E185E343B802E";

    private static SetupFacts AllDone() => new SetupFacts
    {
        RuntimeInstalled = true,
        WindowsSetupDone = true,
        AppleServiceReachable = true,
        UsbUdid = Udid,
        Trusted = true,
        DeveloperMode = true,
        DiskImageMounted = true,
    };

    private static SetupStatus Status(SetupFacts f, SetupStepId id) => SetupSteps.Evaluate(f).Single(s => s.Id == id).Status;

    [Fact]
    public void EverythingDoneIsReadyAndWifiStaysOptional()
    {
        var steps = SetupSteps.Evaluate(AllDone());

        Assert.True(SetupSteps.Ready(steps));
        Assert.Equal(SetupStatus.Optional, steps.Single(s => s.Id == SetupStepId.Wifi).Status);
        Assert.Equal(7, steps.Count);
    }

    [Fact]
    public void NothingDoneStartsWithTheComponentsAndTheAppleService()
    {
        var f = new SetupFacts();

        Assert.Equal(SetupStatus.Todo, Status(f, SetupStepId.Components));
        Assert.Equal(SetupStatus.Todo, Status(f, SetupStepId.AppleService));
        Assert.Equal(SetupStatus.Waiting, Status(f, SetupStepId.Device));
        Assert.Equal(SetupStatus.Waiting, Status(f, SetupStepId.Trust));
        Assert.Equal(SetupStatus.Waiting, Status(f, SetupStepId.DeveloperMode));
        Assert.Equal(SetupStatus.Waiting, Status(f, SetupStepId.DiskImage));
        Assert.False(SetupSteps.Ready(SetupSteps.Evaluate(f)));
    }

    [Fact]
    public void EachDeviceStepWaitsForTheOneBefore()
    {
        var f = AllDone();
        f.Trusted = null;
        f.DeveloperMode = null;
        f.DiskImageMounted = false;

        Assert.Equal(SetupStatus.Todo, Status(f, SetupStepId.Trust));
        Assert.Equal(SetupStatus.Waiting, Status(f, SetupStepId.DeveloperMode));
        Assert.Equal(SetupStatus.Waiting, Status(f, SetupStepId.DiskImage));

        f.Trusted = true;
        f.DeveloperMode = false;
        Assert.Equal(SetupStatus.Todo, Status(f, SetupStepId.DeveloperMode));

        f.DeveloperMode = true;
        Assert.Equal(SetupStatus.Todo, Status(f, SetupStepId.DiskImage));
    }

    [Fact]
    public void WifiWantedNeedsPairing()
    {
        var f = AllDone();
        f.WifiWanted = true;

        Assert.Equal(SetupStatus.Todo, Status(f, SetupStepId.Wifi));
        Assert.False(SetupSteps.Ready(SetupSteps.Evaluate(f)));

        f.WifiPaired = true;
        Assert.Equal(SetupStatus.Done, Status(f, SetupStepId.Wifi));
    }

    [Fact]
    public void EveryStepHasVietnameseText()
    {
        foreach (var step in SetupSteps.Evaluate(new SetupFacts()).Concat(SetupSteps.Evaluate(AllDone())))
        {
            Assert.False(string.IsNullOrWhiteSpace(step.Title));
            Assert.False(string.IsNullOrWhiteSpace(step.Detail));
        }
    }
}
