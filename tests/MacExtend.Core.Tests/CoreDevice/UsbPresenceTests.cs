using MacExtend.Core.CoreDevice;

namespace MacExtend.Core.Tests.CoreDevice;

public class UsbPresenceTests
{
    private const string Udid = "00008110-001A2B3C4D5E6F70";
    private const string Other = "00008030-000102030405060A";

    [Fact]
    public void TheDeviceIsListedWhateverTheCase()
    {
        Assert.True(UsbPresence.IsListed(new[] { Other, Udid.ToLowerInvariant() }, Udid));
        Assert.False(UsbPresence.IsListed(new[] { Other }, Udid));
        Assert.False(UsbPresence.IsListed(null, Udid));
    }

    [Fact]
    public void UsbmuxdMayDropTheDash()
    {
        Assert.True(UsbPresence.IsListed(new[] { "00008110001a2b3c4d5e6f70" }, Udid));
        Assert.False(UsbPresence.IsListed(new[] { "00008030000102030405060A" }, Udid));
    }

    [Fact]
    public void OneMissIsNotEnough()
    {
        var presence = new UsbPresence(Udid);

        Assert.False(presence.Gone(new[] { Other }));
    }

    [Fact]
    public void TwoMissesInARowMeanTheDeviceLeft()
    {
        var presence = new UsbPresence(Udid);

        presence.Gone(new string[0]);

        Assert.True(presence.Gone(new[] { Other }));
    }

    [Fact]
    public void BeingListedAgainStartsTheCountOver()
    {
        var presence = new UsbPresence(Udid);

        presence.Gone(new string[0]);
        Assert.False(presence.Gone(new[] { Udid }));
        Assert.False(presence.Gone(new string[0]));
        Assert.True(presence.Gone(new string[0]));
    }

    [Fact]
    public void AnUnknownAnswerIsNeverAMiss()
    {
        var presence = new UsbPresence(Udid);

        for (var i = 0; i < 10; i++) Assert.False(presence.Gone(null));
        Assert.False(presence.Gone(new string[0]));
    }

    [Fact]
    public void AnUnknownAnswerBetweenTwoMissesKeepsTheCount()
    {
        var presence = new UsbPresence(Udid);

        presence.Gone(new string[0]);
        Assert.False(presence.Gone(null));
        Assert.True(presence.Gone(new string[0]));
    }
}
