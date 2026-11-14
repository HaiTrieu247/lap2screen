using MacExtend.Core.CoreDevice;

namespace MacExtend.Core.Tests.CoreDevice;

public class PictureMapTests
{
    [Fact]
    public void APortraitPhoneInAWideWindowHasBarsLeftAndRight()
    {
        var picture = PictureMap.Picture(new PixelRect(100, 50, 1100, 650), 390, 844);

        Assert.Equal(600, picture.Height);
        Assert.Equal(277, picture.Width);
        Assert.Equal(100 + (1000 - 277) / 2, picture.Left);
    }

    [Fact]
    public void ALandscapePhoneInATallWindowHasBarsAboveAndBelow()
    {
        var picture = PictureMap.Picture(new PixelRect(0, 0, 500, 1000), 844, 390);

        Assert.Equal(500, picture.Width);
        Assert.Equal(231, picture.Height);
    }

    [Fact]
    public void UnknownSizeUsesTheWholeClientArea()
    {
        var client = new PixelRect(0, 0, 300, 600);

        Assert.Equal(client, PictureMap.Picture(client, 0, 0));
    }

    [Fact]
    public void PointsBecomeFractionsAndOutsideIsClamped()
    {
        var picture = new PixelRect(100, 0, 300, 400);

        Assert.True(PictureMap.ToFraction(picture, 199, 199, out var x, out var y));
        Assert.Equal(0.4975, x, 6);
        Assert.Equal(0.49875, y, 6);
        Assert.False(PictureMap.ToFraction(picture, 50, 500, out x, out y));
        Assert.Equal(0, x);
        Assert.Equal(1, y);
    }
}
