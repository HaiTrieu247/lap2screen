using MacExtend.Core.CoreDevice;

namespace MacExtend.Core.Tests.CoreDevice;

public class TouchMapTests
{
    [Theory]
    // Python 3.11+ prints an IntEnum as its number; older ones as its name
    [InlineData("1\n", ScreenOrientation.Portrait)]
    [InlineData("2", ScreenOrientation.PortraitUpsideDown)]
    [InlineData("3\r\n", ScreenOrientation.LandscapeHomeRight)]
    [InlineData("4", ScreenOrientation.LandscapeHomeLeft)]
    [InlineData("InterfaceOrientation.PORTRAIT", ScreenOrientation.Portrait)]
    [InlineData("InterfaceOrientation.PORTRAIT_UPSIDE_DOWN", ScreenOrientation.PortraitUpsideDown)]
    [InlineData("InterfaceOrientation.LANDSCAPE", ScreenOrientation.LandscapeHomeRight)]
    [InlineData("LANDSCAPE_HOME_TO_LEFT", ScreenOrientation.LandscapeHomeLeft)]
    [InlineData("", ScreenOrientation.Unknown)]
    [InlineData(null, ScreenOrientation.Unknown)]
    [InlineData("0", ScreenOrientation.Unknown)]
    [InlineData("5", ScreenOrientation.Unknown)]
    [InlineData("Traceback (most recent call last):", ScreenOrientation.Unknown)]
    public void SpringBoardsAnswerIsRead(string stdout, ScreenOrientation expected)
    {
        Assert.Equal(expected, TouchMap.ParseOrientation(stdout));
    }

    [Fact]
    public void TheLastAnswerLineCounts()
    {
        Assert.Equal(ScreenOrientation.LandscapeHomeLeft, TouchMap.ParseOrientation("some warning\n4\n"));
    }

    [Theory]
    [InlineData(0.0, 0.0, 0, 0)]
    [InlineData(1.0, 1.0, 65535, 65535)]
    [InlineData(0.25, 0.75, 16384, 49151)]
    public void PortraitIsThePanelItself(double xf, double yf, int x, int y)
    {
        TouchMap.ToHid(xf, yf, ScreenOrientation.Portrait, out var hx, out var hy);

        Assert.Equal(x, hx);
        Assert.Equal(y, hy);
    }

    [Fact]
    public void UpsideDownTurnsThePointHalfWayRound()
    {
        TouchMap.ToHid(0.25, 0.1, ScreenOrientation.PortraitUpsideDown, out var x, out var y);

        Assert.Equal(49151, x);
        Assert.Equal(58982, y);
    }

    [Fact]
    public void HomeOnTheRightPutsThePanelsTopOnThePicturesLeft()
    {
        // the device was turned a quarter left: the picture's left edge is the panel's top, its bottom the panel's left
        TouchMap.ToHid(0.0, 0.5, ScreenOrientation.LandscapeHomeRight, out var x, out var y);
        Assert.Equal(32768, x);
        Assert.Equal(0, y);

        TouchMap.ToHid(0.5, 1.0, ScreenOrientation.LandscapeHomeRight, out x, out y);
        Assert.Equal(0, x);
        Assert.Equal(32768, y);

        TouchMap.ToHid(0.9, 0.2, ScreenOrientation.LandscapeHomeRight, out x, out y);
        Assert.Equal(52428, x);
        Assert.Equal(58982, y);
    }

    [Fact]
    public void HomeOnTheLeftIsTheOtherQuarterTurn()
    {
        // turned a quarter right: the picture's right edge is the panel's top, its top the panel's left
        TouchMap.ToHid(1.0, 0.5, ScreenOrientation.LandscapeHomeLeft, out var x, out var y);
        Assert.Equal(32768, x);
        Assert.Equal(0, y);

        TouchMap.ToHid(0.5, 0.0, ScreenOrientation.LandscapeHomeLeft, out x, out y);
        Assert.Equal(0, x);
        Assert.Equal(32768, y);

        TouchMap.ToHid(0.9, 0.2, ScreenOrientation.LandscapeHomeLeft, out x, out y);
        Assert.Equal(13107, x);
        Assert.Equal(6553, y);
    }

    [Fact]
    public void TheTwoLandscapesAreHalfATurnApart()
    {
        // quarters are exact in binary, so the rounding cannot tip either sum
        TouchMap.ToHid(0.25, 0.75, ScreenOrientation.LandscapeHomeRight, out var rx, out var ry);
        TouchMap.ToHid(0.25, 0.75, ScreenOrientation.LandscapeHomeLeft, out var lx, out var ly);

        Assert.Equal(65535, rx + lx);
        Assert.Equal(65535, ry + ly);
    }

    [Fact]
    public void FractionsOutsideThePictureStayOnTheScreen()
    {
        TouchMap.ToHid(-0.5, 1.5, ScreenOrientation.Portrait, out var x, out var y);

        Assert.Equal(0, x);
        Assert.Equal(65535, y);
    }

    [Theory]
    // the picture's shape wins over an answer it contradicts: a stale or failed query never maps a landscape picture as portrait
    [InlineData(ScreenOrientation.LandscapeHomeLeft, PictureShape.Landscape, ScreenOrientation.LandscapeHomeLeft)]
    [InlineData(ScreenOrientation.LandscapeHomeRight, PictureShape.Landscape, ScreenOrientation.LandscapeHomeRight)]
    [InlineData(ScreenOrientation.Portrait, PictureShape.Landscape, ScreenOrientation.LandscapeHomeRight)]
    [InlineData(ScreenOrientation.Unknown, PictureShape.Landscape, ScreenOrientation.LandscapeHomeRight)]
    [InlineData(ScreenOrientation.LandscapeHomeLeft, PictureShape.Portrait, ScreenOrientation.Portrait)]
    [InlineData(ScreenOrientation.PortraitUpsideDown, PictureShape.Portrait, ScreenOrientation.PortraitUpsideDown)]
    [InlineData(ScreenOrientation.Unknown, PictureShape.Portrait, ScreenOrientation.Portrait)]
    // no picture size yet: the answer as it is, portrait when there is none
    [InlineData(ScreenOrientation.LandscapeHomeLeft, PictureShape.Unknown, ScreenOrientation.LandscapeHomeLeft)]
    [InlineData(ScreenOrientation.Unknown, PictureShape.Unknown, ScreenOrientation.Portrait)]
    public void TheOrientationUsedFollowsThePicturesShape(ScreenOrientation reported, PictureShape shape, ScreenOrientation used)
    {
        Assert.Equal(used, TouchMap.Effective(reported, shape));
    }

    [Theory]
    [InlineData(498, 1080, PictureShape.Portrait)]
    [InlineData(1920, 888, PictureShape.Landscape)]
    [InlineData(1000, 1000, PictureShape.Portrait)]
    [InlineData(0, 1080, PictureShape.Unknown)]
    [InlineData(1920, 0, PictureShape.Unknown)]
    public void ThePicturesShapeComesFromItsSize(int width, int height, PictureShape shape)
    {
        Assert.Equal(shape, TouchMap.Shape(width, height));
    }
}

public class ScrollDragTests
{
    [Fact]
    public void TheWheelTurnedAwayDragsTheFingerDown()
    {
        // Windows: a positive delta is the wheel turned away from the user, which scrolls up: the content moves down
        var d = ScrollDrag.For(0.5, 0.4, 1, horizontal: false);

        Assert.Equal(0.5, d.X1);
        Assert.Equal(0.5, d.X2);
        Assert.True(d.Y2 > d.Y1);
        Assert.Equal(ScrollDrag.PerNotch, d.Y2 - d.Y1, 6);
    }

    [Fact]
    public void TheWheelTurnedTowardsDragsTheFingerUp()
    {
        var d = ScrollDrag.For(0.5, 0.6, -2, horizontal: false);

        Assert.Equal(2 * ScrollDrag.PerNotch, d.Y1 - d.Y2, 6);
    }

    [Fact]
    public void TiltingRightDragsTheFingerLeft()
    {
        var d = ScrollDrag.For(0.5, 0.5, 1, horizontal: true);

        Assert.True(d.X2 < d.X1);
        Assert.Equal(0.5, d.Y1);
        Assert.Equal(0.5, d.Y2);
    }

    [Fact]
    public void ManyNotchesAreCappedAndFewAreStillADrag()
    {
        Assert.Equal(ScrollDrag.MaxDistance, ScrollDrag.For(0.5, 0.2, 30, false).Y2 - ScrollDrag.For(0.5, 0.2, 30, false).Y1, 6);
        Assert.Equal(ScrollDrag.MinDistance, ScrollDrag.For(0.5, 0.5, 0.1, false).Y2 - 0.5, 6);
    }

    [Fact]
    public void ADragNearTheEdgeIsMovedToFitOnTheScreen()
    {
        var d = ScrollDrag.For(0.5, 0.95, 3, horizontal: false);

        Assert.True(d.Y2 <= 1 - ScrollDrag.Margin + 1e-9, d.Y2.ToString());
        Assert.Equal(3 * ScrollDrag.PerNotch, d.Y2 - d.Y1, 6);
    }
}
