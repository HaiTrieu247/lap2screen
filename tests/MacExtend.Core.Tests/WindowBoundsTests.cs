using System.Globalization;

namespace MacExtend.Core.Tests;

public class WindowBoundsTests
{
    // a 1920 x 1080 laptop at 150 %, taskbar at the bottom: 1280 x 688 WPF units of work area
    private static readonly WindowBounds Laptop = new WindowBounds(0, 0, 1280, 688);
    // a 1920 x 1080 monitor at 100 % to the left of the laptop
    private static readonly WindowBounds LeftMonitor = new WindowBounds(-1920, -200, 1920, 1040);

    private const double MinWidth = 360;
    private const double MinHeight = 480;

    private static WindowBounds? Place(string saved, params WindowBounds[] screens) =>
        WindowBounds.Place(saved, screens, MinWidth, MinHeight);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("10,20,520")]
    [InlineData("10,20,520,600,1")]
    [InlineData("a,b,c,d")]
    [InlineData("10;20;520;600")]
    [InlineData("10,20,0,600")]
    [InlineData("10,20,520,-600")]
    [InlineData("NaN,20,520,600")]
    [InlineData("10,Infinity,520,600")]
    [InlineData("10,20,1e400,600")]
    public void NothingUsableSavedMeansTheDefaultPosition(string saved)
    {
        Assert.False(WindowBounds.TryParse(saved, out _));
        Assert.Null(Place(saved, Laptop));
    }

    [Fact]
    public void SavedBoundsThatFitTheirScreenAreKept()
    {
        Assert.Equal(new WindowBounds(100, 40, 420, 600), Place("100,40,420,600", Laptop));
    }

    [Fact]
    public void BoundsOffEveryScreenMeanTheDefaultPosition()
    {
        // saved on the left monitor, which is unplugged now
        Assert.Null(Place("-1500,100,420,600", Laptop));
        // and far away from any screen
        Assert.Null(Place("5000,5000,420,600", Laptop, LeftMonitor));
    }

    [Fact]
    public void AMonitorLeftOfOrAboveTheMainOneIsAScreenToo()
    {
        Assert.Equal(new WindowBounds(-1500, -150, 420, 600), Place("-1500,-150,420,600", Laptop, LeftMonitor));
    }

    [Fact]
    public void AWindowAcrossTwoScreensGoesToTheOneShowingMostOfIt()
    {
        // 320 of 420 units on the left monitor, 100 on the laptop
        Assert.Equal(new WindowBounds(-420, 100, 420, 600), Place("-320,100,420,600", Laptop, LeftMonitor));
        // 100 on the left monitor, 320 on the laptop
        Assert.Equal(new WindowBounds(0, 40, 420, 600), Place("-100,40,420,600", Laptop, LeftMonitor));
    }

    [Fact]
    public void APartlyHiddenWindowMovesBackInside()
    {
        Assert.Equal(new WindowBounds(860, 88, 420, 600), Place("1100,400,420,600", Laptop));
        Assert.Equal(new WindowBounds(0, 0, 420, 600), Place("-30,-10,420,600", Laptop));
    }

    [Fact]
    public void AWindowTallerThanItsScreenShrinksToIt()
    {
        // saved at 100 % (960 high), opened again at 150 % on the same laptop
        Assert.Equal(new WindowBounds(100, 0, 520, 688), Place("100,20,520,960", Laptop));
    }

    [Fact]
    public void NeverSmallerThanTheWindowsMinimum()
    {
        Assert.Equal(new WindowBounds(10, 10, MinWidth, MinHeight), Place("10,10,100,100", Laptop));
    }

    [Fact]
    public void FormatsWholeUnitsWhateverTheCulture()
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            // Vietnamese writes 10,6 for ten point six, which would break "x,y,w,h"
            CultureInfo.CurrentCulture = new CultureInfo("vi-VN");

            Assert.Equal("-1500,21,421,600", new WindowBounds(-1500.2, 20.6, 420.5, 600).Format());
            Assert.Equal("0,0,420,600", new WindowBounds(-0.3, 0, 420, 600).Format());
            Assert.True(WindowBounds.TryParse("10,20,520,960", out var parsed));
            Assert.Equal(new WindowBounds(10, 20, 520, 960), parsed);
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    [Fact]
    public void ReadsWhatItWrites()
    {
        var bounds = new WindowBounds(-1200, 35, 444, 812);

        Assert.True(WindowBounds.TryParse(bounds.Format(), out var parsed));
        Assert.Equal(bounds, parsed);
        Assert.True(WindowBounds.TryParse(" 10 , 20 , 520.5 , 960 ", out parsed));
        Assert.Equal(new WindowBounds(10, 20, 520.5, 960), parsed);
    }

    [Fact]
    public void ScreensWithoutAreaAreIgnored()
    {
        Assert.Null(Place("100,40,420,600", new WindowBounds(0, 0, 0, 0)));
        Assert.Null(Place("100,40,420,600"));
    }
}
