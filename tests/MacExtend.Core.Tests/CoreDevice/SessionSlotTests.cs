using System;
using MacExtend.Core.CoreDevice;

namespace MacExtend.Core.Tests.CoreDevice;

public class SessionSlotTests
{
    private const string Udid = "00008110-001A2B3C4D5E6F70";
    private const string Other = "00008030-000102030405060A";

    private static SessionRequest Usb(string udid = Udid) => new SessionRequest(udid, CoreDeviceRoute.Usb);

    [Fact]
    public void TheFirstOpenStartsGenerationOne()
    {
        var slot = new SessionSlot();

        Assert.Equal(OpenOutcome.Started, slot.Open(Usb(), currentIsOver: false));
        Assert.Equal(1, slot.Generation);
        Assert.True(slot.Occupied);
        Assert.True(slot.IsCurrent(1));
        Assert.Null(slot.Queued);
    }

    [Fact]
    public void AnOpenWhileASessionRunsIsIgnored()
    {
        var slot = new SessionSlot();
        slot.Open(Usb(), false);

        Assert.Equal(OpenOutcome.AlreadyRunning, slot.Open(Usb(Other), currentIsOver: false));
        Assert.Equal(1, slot.Generation);
        Assert.Null(slot.Queued);
    }

    [Fact]
    public void AnOpenWhileThePreviousSessionIsStoppingWaitsForIt()
    {
        var slot = new SessionSlot();
        slot.Open(Usb(), false);
        var next = new SessionRequest(Other, CoreDeviceRoute.Wifi);

        Assert.Equal(OpenOutcome.Queued, slot.Open(next, currentIsOver: true));
        Assert.Same(next, slot.Queued);
        Assert.Equal(1, slot.Generation);
        Assert.True(slot.IsCurrent(1));
    }

    [Fact]
    public void TheLatestWaitingOpenWins()
    {
        var slot = new SessionSlot();
        slot.Open(Usb(), false);
        slot.Open(Usb(), true);
        var latest = Usb(Other);

        slot.Open(latest, true);

        Assert.Same(latest, slot.Queued);
    }

    [Fact]
    public void TearingDownStartsTheWaitingOpenAsTheNextGeneration()
    {
        var slot = new SessionSlot();
        slot.Open(Usb(), false);
        var next = Usb(Other);
        slot.Open(next, true);

        Assert.Same(next, slot.TornDown(1));
        Assert.Equal(2, slot.Generation);
        Assert.True(slot.Occupied);
        Assert.Null(slot.Queued);
        Assert.False(slot.IsCurrent(1));
        Assert.True(slot.IsCurrent(2));
    }

    [Fact]
    public void TearingDownWithNothingWaitingFreesTheSlot()
    {
        var slot = new SessionSlot();
        slot.Open(Usb(), false);

        Assert.Null(slot.TornDown(1));
        Assert.False(slot.Occupied);
        Assert.False(slot.IsCurrent(1));

        Assert.Equal(OpenOutcome.Started, slot.Open(Usb(), currentIsOver: true));
        Assert.Equal(2, slot.Generation);
    }

    [Fact]
    public void AnOldSessionsTeardownChangesNothing()
    {
        var slot = new SessionSlot();
        slot.Open(Usb(), false);
        slot.Open(Usb(Other), true);
        slot.TornDown(1);

        Assert.Null(slot.TornDown(1));
        Assert.True(slot.Occupied);
        Assert.Equal(2, slot.Generation);
        Assert.True(slot.IsCurrent(2));
    }

    [Fact]
    public void AStopDropsTheWaitingOpen()
    {
        var slot = new SessionSlot();
        slot.Open(Usb(), false);
        slot.Open(Usb(Other), true);

        slot.CancelQueued();

        Assert.Null(slot.Queued);
        Assert.Null(slot.TornDown(1));
        Assert.False(slot.Occupied);
    }

    [Fact]
    public void NoGenerationIsCurrentBeforeTheFirstOpen()
    {
        var slot = new SessionSlot();

        Assert.False(slot.IsCurrent(0));
        Assert.Null(slot.TornDown(0));
    }

    [Fact]
    public void ARequestNeedsAValidDeviceAndARoute()
    {
        Assert.Throws<ArgumentException>(() => new SessionRequest("not-a-udid", CoreDeviceRoute.Usb));
        Assert.Throws<ArgumentException>(() => new SessionRequest(null, CoreDeviceRoute.Usb));
        Assert.Throws<ArgumentException>(() => new SessionRequest(Udid, CoreDeviceRoute.None));

        var request = new SessionRequest(Udid, CoreDeviceRoute.Wifi);
        Assert.Equal(Udid, request.Udid);
        Assert.Equal(CoreDeviceRoute.Wifi, request.Route);
    }
}
