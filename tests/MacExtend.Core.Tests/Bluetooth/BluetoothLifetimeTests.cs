using System;
using MacExtend.Core.Bluetooth;

namespace MacExtend.Core.Tests.Bluetooth;

public class BluetoothLifetimeTests
{
    private DateTime _now = new DateTime(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);

    private BluetoothLifetime Create() => new BluetoothLifetime(() => _now);

    [Fact]
    public void RunsOnlyWhileMirroring()
    {
        var lifetime = Create();

        Assert.False(lifetime.ShouldRun(true, false));
        Assert.True(lifetime.ShouldRun(true, true));
    }

    [Fact]
    public void NeverRunsWhenSwitchedOff()
    {
        var lifetime = Create();

        Assert.False(lifetime.ShouldRun(false, true));
    }

    [Fact]
    public void KeepsRunningForAWhileAfterMirroringEnds()
    {
        var lifetime = Create();
        lifetime.ShouldRun(true, true);

        _now += TimeSpan.FromSeconds(1);
        Assert.True(lifetime.ShouldRun(true, false));
        _now += BluetoothLifetime.Linger - TimeSpan.FromSeconds(1);
        Assert.True(lifetime.ShouldRun(true, false));
        _now += TimeSpan.FromSeconds(1);
        Assert.False(lifetime.ShouldRun(true, false));
    }

    [Fact]
    public void MirroringAgainWithinTheWaitKeepsItRunning()
    {
        var lifetime = Create();
        lifetime.ShouldRun(true, true);
        lifetime.ShouldRun(true, false);

        _now += TimeSpan.FromSeconds(5);
        Assert.True(lifetime.ShouldRun(true, true));
        _now += TimeSpan.FromSeconds(5);
        Assert.True(lifetime.ShouldRun(true, false));
        _now += BluetoothLifetime.Linger - TimeSpan.FromSeconds(1);
        Assert.True(lifetime.ShouldRun(true, false));
    }

    [Fact]
    public void SwitchingOffStopsAtOnce()
    {
        var lifetime = Create();
        lifetime.ShouldRun(true, true);
        lifetime.ShouldRun(true, false);

        Assert.False(lifetime.ShouldRun(false, false));
        Assert.False(lifetime.ShouldRun(true, false));
    }
}
