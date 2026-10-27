namespace MacExtend.Core.Tests;

public class ResolutionPolicyTests
{
    private static RequestedVideo Auto(int w, int h, int hz) =>
        ResolutionPolicy.Resolve(new AppSettings(), new DisplayMode(w, h, hz));

    [Theory]
    [InlineData(1920, 1080, 165, 1920, 1080, 60)]
    [InlineData(2560, 1600, 60, 1728, 1080, 60)]
    [InlineData(1366, 768, 60, 1366, 768, 60)]
    [InlineData(1280, 1024, 75, 1280, 1024, 60)]
    [InlineData(3840, 2160, 60, 1920, 1080, 60)]
    [InlineData(2560, 1080, 144, 1920, 810, 60)]
    [InlineData(1920, 1080, 30, 1920, 1080, 30)]
    [InlineData(1921, 1081, 60, 1918, 1080, 60)]
    [InlineData(1365, 767, 60, 1364, 766, 60)]
    public void AutoFitsNativeInside1080pAndCapsRefreshAt60(int w, int h, int hz, int ew, int eh, int ehz)
    {
        var result = Auto(w, h, hz);

        Assert.Equal(new DisplayMode(ew, eh, ehz), result.Mode);
        Assert.False(result.H265);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(-5)]
    public void AutoTreatsUnknownRefreshAs60(int hz)
    {
        Assert.Equal(60, Auto(1920, 1080, hz).Mode.RefreshHz);
    }

    [Fact]
    public void AutoFallsBackTo1080pWhenNativeSizeUnknown()
    {
        Assert.Equal(new DisplayMode(1920, 1080, 60), Auto(0, 0, 0).Mode);
    }

    [Fact]
    public void CustomIsUsedExactlyWithH265()
    {
        var s = new AppSettings
        {
            ResolutionMode = ResolutionMode.Custom,
            CustomWidth = 2560,
            CustomHeight = 1440,
            CustomRefresh = 120,
            CustomH265 = true,
        };

        var result = ResolutionPolicy.Resolve(s, new DisplayMode(1920, 1080, 165));

        Assert.Equal(new DisplayMode(2560, 1440, 120), result.Mode);
        Assert.True(result.H265);
    }

    [Fact]
    public void CustomWithoutH265StaysH264()
    {
        var s = new AppSettings { ResolutionMode = ResolutionMode.Custom, CustomWidth = 1600, CustomHeight = 900, CustomRefresh = 60 };

        var result = ResolutionPolicy.Resolve(s, new DisplayMode(1920, 1080, 165));

        Assert.Equal(new DisplayMode(1600, 900, 60), result.Mode);
        Assert.False(result.H265);
    }

    [Fact]
    public void DisplayModeFormatsForUxPlay()
    {
        Assert.Equal("1920x1080@60", new DisplayMode(1920, 1080, 60).ToString());
    }
}
