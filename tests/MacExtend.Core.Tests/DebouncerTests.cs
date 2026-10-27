using System;

namespace MacExtend.Core.Tests;

public class DebouncerTests
{
    private DateTime _now = new DateTime(2026, 10, 27, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void FiresOnceAfterQuietPeriod()
    {
        var d = new Debouncer(TimeSpan.FromSeconds(5), () => _now);
        var t0 = _now;
        d.Signal();

        _now = t0.AddSeconds(4);
        Assert.False(d.ShouldFire());
        _now = t0.AddSeconds(5);
        Assert.True(d.ShouldFire());
        Assert.False(d.ShouldFire());
    }

    [Fact]
    public void NewSignalPushesFireTimeBack()
    {
        var d = new Debouncer(TimeSpan.FromSeconds(5), () => _now);
        var t0 = _now;
        d.Signal();
        _now = t0.AddSeconds(3);
        d.Signal();

        _now = t0.AddSeconds(7);
        Assert.False(d.ShouldFire());
        _now = t0.AddSeconds(8);
        Assert.True(d.ShouldFire());
    }

    [Fact]
    public void NeverFiresWithoutSignal()
    {
        var d = new Debouncer(TimeSpan.FromSeconds(5), () => _now);
        _now = _now.AddHours(1);

        Assert.False(d.ShouldFire());
    }
}
