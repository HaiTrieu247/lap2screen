using System;

namespace MacExtend.Core.Tests;

public class RestartPolicyTests
{
    private DateTime _now = new DateTime(2026, 10, 27, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void DelaysBackOffThenGiveUpWithinTwoMinutes()
    {
        var p = new RestartPolicy(() => _now);

        Assert.Equal(TimeSpan.FromSeconds(1), p.NextDelay());
        Assert.Equal(TimeSpan.FromSeconds(2), p.NextDelay());
        Assert.Equal(TimeSpan.FromSeconds(5), p.NextDelay());
        Assert.Equal(TimeSpan.FromSeconds(10), p.NextDelay());
        Assert.Equal(TimeSpan.FromSeconds(30), p.NextDelay());
        Assert.Null(p.NextDelay());
    }

    [Fact]
    public void FailuresSpacedFarApartNeverGiveUp()
    {
        var p = new RestartPolicy(() => _now);
        for (var i = 0; i < 20; i++)
        {
            Assert.NotNull(p.NextDelay());
            _now = _now.AddMinutes(3);
        }
    }

    [Fact]
    public void ReportHealthyResetsBackoff()
    {
        var p = new RestartPolicy(() => _now);
        p.NextDelay();
        p.NextDelay();
        p.NextDelay();

        p.ReportHealthy();

        Assert.Equal(TimeSpan.FromSeconds(1), p.NextDelay());
    }

    [Fact]
    public void OldFailuresExpireFromTheWindow()
    {
        var p = new RestartPolicy(() => _now);
        for (var i = 0; i < 4; i++) p.NextDelay();

        _now = _now.AddMinutes(2).AddSeconds(1);

        Assert.Equal(TimeSpan.FromSeconds(1), p.NextDelay());
    }
}
