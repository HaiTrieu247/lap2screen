namespace MacExtend.Core.Tests;

public class PixelRectTests
{
    private static readonly PixelRect Video = new PixelRect(100, 50, 1100, 650);

    [Theory]
    [InlineData(100, 50)]
    [InlineData(600, 300)]
    [InlineData(1099, 649)]
    public void PointsInsideAreContained(int x, int y)
    {
        Assert.True(Video.Contains(x, y));
    }

    [Theory]
    [InlineData(99, 300)]
    [InlineData(600, 49)]
    [InlineData(1100, 300)]
    [InlineData(600, 650)]
    public void RightAndBottomEdgesAreOutside(int x, int y)
    {
        Assert.False(Video.Contains(x, y));
    }

    [Fact]
    public void CenterIsTheMiddlePixel()
    {
        Assert.Equal(600, Video.CenterX);
        Assert.Equal(350, Video.CenterY);
        // (Left + Right) / 2 would round toward zero on a monitor left of the primary one
        Assert.Equal(-51, new PixelRect(-101, -10, 0, 0).CenterX);
    }
}
