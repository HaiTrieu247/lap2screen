namespace MacExtend.Core.Tests;

public class WindowFitTests
{
    // 1920 × 1080 laptop at 100 %, taskbar at the bottom
    private static readonly PixelRect Work1080 = new PixelRect(0, 0, 1920, 1032);

    [Fact]
    public void OversizedWindowShrinksUniformlyAndIsCentred()
    {
        // d3d11videosink sizes the window to a 1920 × 1080 picture plus its frame
        var window = new PixelRect(0, 0, 1936, 1119);

        var fit = WindowFit.Fit(window, Work1080);

        // height limits: scale = 1032 / 1119, width = floor(1936 * 1032 / 1119) = 1785
        Assert.Equal(1785, fit.Width);
        Assert.Equal(1032, fit.Height);
        Assert.Equal((1920 - 1785) / 2, fit.Left);
        Assert.Equal(0, fit.Top);
    }

    [Fact]
    public void WidthCanBeTheLimit()
    {
        var window = new PixelRect(100, 100, 3000, 1100);
        var work = new PixelRect(0, 0, 1500, 1000);

        var fit = WindowFit.Fit(window, work);

        Assert.Equal(1500, fit.Width);
        Assert.Equal(517, fit.Height); // floor(1000 * 1500 / 2900)
        Assert.Equal(0, fit.Left);
        Assert.Equal((1000 - 517) / 2, fit.Top);
    }

    [Fact]
    public void ResultNeverExceedsWorkArea()
    {
        var work = new PixelRect(-1280, 40, 0, 1024);
        foreach (var size in new[] { (4000, 2000), (2000, 4000), (1281, 985), (1919, 1081), (5000, 5000) })
        {
            var fit = WindowFit.Fit(new PixelRect(0, 0, size.Item1, size.Item2), work);

            Assert.True(fit.Width <= work.Width && fit.Height <= work.Height, size.ToString());
            Assert.True(fit.Left >= work.Left && fit.Top >= work.Top && fit.Right <= work.Right && fit.Bottom <= work.Bottom, size.ToString());
            Assert.True(fit.Width > 0 && fit.Height > 0, size.ToString());
        }
    }

    [Fact]
    public void ShrinkKeepsWindowAspectWithinRounding()
    {
        var window = new PixelRect(0, 0, 2576, 1489);

        var fit = WindowFit.Fit(window, Work1080);

        var before = (double)window.Width / window.Height;
        var after = (double)fit.Width / fit.Height;
        Assert.InRange(after, before - 0.002, before + 0.002);
    }

    [Fact]
    public void SmallWindowIsLeftAlone()
    {
        var window = new PixelRect(200, 150, 1000, 700);

        Assert.Equal(window, WindowFit.Fit(window, Work1080));
    }

    [Fact]
    public void WindowThatFitsExactlyIsLeftAlone()
    {
        Assert.Equal(Work1080, WindowFit.Fit(Work1080, Work1080));
    }

    [Fact]
    public void FittingWindowOutsideWorkAreaIsMovedInWithoutResizing()
    {
        var window = new PixelRect(1500, 900, 2300, 1500);

        var fit = WindowFit.Fit(window, Work1080);

        Assert.Equal(new PixelRect(1120, 432, 1920, 1032), fit);
    }

    [Fact]
    public void WindowAboveTopTaskbarIsMovedDown()
    {
        var work = new PixelRect(0, 48, 1920, 1080);
        var window = new PixelRect(0, 0, 1280, 720);

        Assert.Equal(new PixelRect(0, 48, 1280, 768), WindowFit.Fit(window, work));
    }

    [Fact]
    public void SecondaryMonitorWithNegativeOffsetIsHandled()
    {
        var work = new PixelRect(-2560, -200, 0, 1200);
        var window = new PixelRect(-2600, -300, -100, 1500);

        var fit = WindowFit.Fit(window, work);

        Assert.True(fit.Left >= work.Left && fit.Top >= work.Top && fit.Right <= work.Right && fit.Bottom <= work.Bottom);
        Assert.Equal(1400, fit.Height);
    }

    [Theory]
    [InlineData(0, 0, 0, 0)]
    [InlineData(10, 10, 10, 500)]
    [InlineData(10, 10, 500, 5)]
    public void EmptyWindowIsLeftAlone(int l, int t, int r, int b)
    {
        var window = new PixelRect(l, t, r, b);

        Assert.Equal(window, WindowFit.Fit(window, Work1080));
    }

    [Fact]
    public void EmptyWorkAreaLeavesWindowAlone()
    {
        var window = new PixelRect(0, 0, 3000, 2000);

        Assert.Equal(window, WindowFit.Fit(window, new PixelRect(0, 0, 0, 0)));
    }

    [Fact]
    public void RectEqualityComparesAllEdges()
    {
        Assert.Equal(new PixelRect(1, 2, 3, 4), new PixelRect(1, 2, 3, 4));
        Assert.NotEqual(new PixelRect(1, 2, 3, 4), new PixelRect(1, 2, 3, 5));
        Assert.True(new PixelRect(1, 2, 3, 4) == new PixelRect(1, 2, 3, 4));
        Assert.True(new PixelRect(0, 2, 3, 4) != new PixelRect(1, 2, 3, 4));
    }
}
