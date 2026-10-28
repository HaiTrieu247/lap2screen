using MacExtend.Core.Bluetooth;

namespace MacExtend.Core.Tests.Bluetooth;

public class MouseRouterTests
{
    [Fact]
    public void ButtonPressedDuringControlIsForwarded()
    {
        var router = new MouseRouter();

        Assert.True(router.OnButton(MouseButtons.Left, true));
        Assert.Equal(MouseButtons.Left, router.Buttons);
        Assert.True(router.OnButton(MouseButtons.Left, false));
        Assert.Equal(MouseButtons.None, router.Buttons);
    }

    [Fact]
    public void ButtonHeldByWindowsAtStartIsReleasedOnWindows()
    {
        // e.g. the click on "Bắt đầu điều khiển" is still down when control starts
        var router = new MouseRouter(MouseButtons.Left);

        Assert.False(router.OnButton(MouseButtons.Left, false));
        Assert.Equal(MouseButtons.None, router.Buttons);
        Assert.True(router.OnButton(MouseButtons.Left, true));
        Assert.Equal(MouseButtons.Left, router.Buttons);
    }

    [Fact]
    public void ReleaseWithoutPressIsPassedToWindows()
    {
        var router = new MouseRouter();

        Assert.False(router.OnButton(MouseButtons.Right, false));
    }

    [Fact]
    public void ButtonsCombine()
    {
        var router = new MouseRouter();
        router.OnButton(MouseButtons.Left, true);
        router.OnButton(MouseButtons.Right, true);

        Assert.Equal(MouseButtons.Left | MouseButtons.Right, router.Buttons);
    }

    [Fact]
    public void WholeWheelNotchesPassStraightThrough()
    {
        var router = new MouseRouter();

        Assert.Equal(1, router.OnWheel(120));
        Assert.Equal(-2, router.OnWheel(-240));
    }

    [Fact]
    public void SmoothWheelDeltasAddUpToNotches()
    {
        // precision touchpads send fractions of a notch
        var router = new MouseRouter();

        Assert.Equal(0, router.OnWheel(40));
        Assert.Equal(0, router.OnWheel(40));
        Assert.Equal(1, router.OnWheel(40));
        Assert.Equal(0, router.OnWheel(-60));
        Assert.Equal(-1, router.OnWheel(-60));
    }

    [Fact]
    public void ReleaseAllSaysWhetherAnythingWasHeld()
    {
        var router = new MouseRouter();
        router.OnButton(MouseButtons.Left, true);
        router.OnWheel(60);

        Assert.True(router.ReleaseAll());
        Assert.Equal(MouseButtons.None, router.Buttons);
        Assert.False(router.ReleaseAll());
        Assert.Equal(0, router.OnWheel(60)); // the half notch was dropped too
    }
}
