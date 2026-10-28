using MacExtend.Core.Bluetooth;

namespace MacExtend.Core.Tests.Bluetooth;

public class ControlTargetTests
{
    [Fact]
    public void NothingIsSelectedAtFirst()
    {
        Assert.Null(new ControlTarget().HostId);
    }

    [Fact]
    public void SameSourceReconnectingKeepsTheTarget()
    {
        var target = new ControlTarget();
        target.Select("h1", "iPhone A");

        Assert.False(target.OnAirPlaySource(null));
        Assert.False(target.OnAirPlaySource("iPhone A"));
        Assert.Equal("h1", target.HostId);
    }

    [Fact]
    public void AnotherSourceClearsTheTarget()
    {
        var target = new ControlTarget();
        target.Select("h1", "iPhone A");

        Assert.True(target.OnAirPlaySource("iPad B"));
        Assert.Null(target.HostId);
        Assert.False(target.OnAirPlaySource("iPad B"));
    }

    [Fact]
    public void TargetChosenBeforeMirroringBelongsToTheNextSource()
    {
        var target = new ControlTarget();
        target.Select("h1", null);

        Assert.False(target.OnAirPlaySource("iPhone A"));
        Assert.Equal("h1", target.HostId);
        Assert.True(target.OnAirPlaySource("Mac"));
        Assert.Null(target.HostId);
    }

    [Fact]
    public void ClearForgetsTheTarget()
    {
        var target = new ControlTarget();
        target.Select("h1", "iPhone A");

        target.Clear();

        Assert.Null(target.HostId);
        Assert.False(target.OnAirPlaySource("iPad B"));
    }

    [Fact]
    public void ReadyOnlyWhenEverythingIsInPlace()
    {
        var hosts = new[] { "h1", "h2" };

        Assert.Equal(ControlBlocker.None, ControlReadiness.Evaluate(true, true, hosts, "h2", true, true));
    }

    [Theory]
    [InlineData(false, true, "h1", true, true, ControlBlocker.Off)]
    [InlineData(true, false, "h1", true, true, ControlBlocker.BluetoothNotReady)]
    [InlineData(true, true, null, true, true, ControlBlocker.NoTarget)]
    [InlineData(true, true, "gone", true, true, ControlBlocker.TargetDisconnected)]
    [InlineData(true, true, "h1", false, true, ControlBlocker.NoAirPlay)]
    [InlineData(true, true, "h1", true, false, ControlBlocker.NoVideo)]
    public void FirstMissingPieceIsReported(bool enabled, bool bluetoothReady, string selected, bool airPlay, bool video,
                                            ControlBlocker expected)
    {
        Assert.Equal(expected, ControlReadiness.Evaluate(enabled, bluetoothReady, new[] { "h1" }, selected, airPlay, video));
    }

    [Fact]
    public void HostIdsCompareWithoutCase()
    {
        Assert.Equal(ControlBlocker.None, ControlReadiness.Evaluate(true, true, new[] { "BluetoothLE#ABC" }, "bluetoothle#abc", true, true));
    }

    private static readonly PairedHost Phone = new PairedHost("h1", "iPhone của Thịnh");
    private static readonly PairedHost Tablet = new PairedHost("h2", "iPad");

    [Fact]
    public void TheOnlyConnectedHostIsPickedByItself()
    {
        var target = new ControlTarget();

        target.Resolve(new[] { Tablet }, "iPhone của Thịnh");

        Assert.Equal("h2", target.HostId);
    }

    [Fact]
    public void HostNamedLikeTheMirroringDeviceIsPicked()
    {
        var target = new ControlTarget();

        target.Resolve(new[] { Tablet, Phone }, "iPhone của Thịnh");

        Assert.Equal("h1", target.HostId);
    }

    [Fact]
    public void NameMatchIgnoresCaseAndSurroundingSpaces()
    {
        var target = new ControlTarget();

        target.Resolve(new[] { Tablet, Phone }, "  IPHONE CỦA THỊNH ");

        Assert.Equal("h1", target.HostId);
    }

    [Fact]
    public void SeveralHostsWithoutAMatchAreLeftToTheUser()
    {
        var target = new ControlTarget();

        target.Resolve(new[] { Tablet, Phone }, "Mac của Thịnh");

        Assert.Null(target.HostId);
    }

    [Fact]
    public void NoHostNoTarget()
    {
        var target = new ControlTarget();

        target.Resolve(new PairedHost[0], "iPad");

        Assert.Null(target.HostId);
    }

    [Fact]
    public void AutomaticChoiceFollowsTheHostsConnectedNow()
    {
        var target = new ControlTarget();
        target.Resolve(new[] { Phone }, "Mac");

        target.Resolve(new[] { Tablet }, "Mac");

        Assert.Equal("h2", target.HostId);
    }

    [Fact]
    public void UserChoiceSticksWhileTheSameDeviceMirrors()
    {
        var target = new ControlTarget();
        target.Select("h2", "iPhone của Thịnh");

        target.Resolve(new[] { Tablet, Phone }, "iPhone của Thịnh");
        Assert.Equal("h2", target.HostId);

        // gone for a moment: still the user's choice when it comes back
        target.Resolve(new[] { Phone }, "iPhone của Thịnh");
        Assert.Equal("h2", target.HostId);
    }

    [Fact]
    public void UserChoiceEndsWhenAnotherDeviceMirrors()
    {
        var target = new ControlTarget();
        target.Select("h2", "iPhone của Thịnh");

        target.Resolve(new[] { Tablet, Phone }, "iPad");

        Assert.Equal("h2", target.HostId);
        target.Resolve(new[] { Phone }, "iPad");
        Assert.Equal("h1", target.HostId);
    }
}
