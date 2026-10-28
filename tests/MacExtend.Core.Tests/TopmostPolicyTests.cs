namespace MacExtend.Core.Tests;

public class TopmostPolicyTests
{
    [Fact]
    public void NormalWindowIsPinnedWhenEnabled()
    {
        var action = TopmostPolicy.Decide(alwaysOnTop: true, minimized: false, fullscreen: false, topmost: false, pinnedByUs: false);

        Assert.Equal(TopmostAction.Set, action);
    }

    [Fact]
    public void WindowAlreadyOnTopIsLeftAlone()
    {
        Assert.Equal(TopmostAction.Keep, TopmostPolicy.Decide(true, false, false, topmost: true, pinnedByUs: true));
        Assert.Equal(TopmostAction.Keep, TopmostPolicy.Decide(true, false, false, topmost: true, pinnedByUs: false));
    }

    [Fact]
    public void PinIsRestoredWhenSomethingElseDroppedIt()
    {
        // e.g. bringing the window to the front, or the sink leaving full screen, clears the topmost flag
        var action = TopmostPolicy.Decide(true, minimized: false, fullscreen: false, topmost: false, pinnedByUs: true);

        Assert.Equal(TopmostAction.Set, action);
    }

    [Fact]
    public void MinimizedWindowWaitsUntilShownAgain()
    {
        var action = TopmostPolicy.Decide(true, minimized: true, fullscreen: false, topmost: false, pinnedByUs: false);

        Assert.Equal(TopmostAction.Keep, action);
    }

    [Fact]
    public void FullScreenDropsOurPin()
    {
        // a pinned full-screen window would stay over the whole monitor after Alt+Tab
        var action = TopmostPolicy.Decide(true, minimized: false, fullscreen: true, topmost: true, pinnedByUs: true);

        Assert.Equal(TopmostAction.Clear, action);
    }

    [Fact]
    public void FullScreenIsNeverPinned()
    {
        var action = TopmostPolicy.Decide(true, minimized: false, fullscreen: true, topmost: false, pinnedByUs: false);

        Assert.Equal(TopmostAction.Keep, action);
    }

    [Fact]
    public void TurningOffDropsOurPin()
    {
        Assert.Equal(TopmostAction.Clear, TopmostPolicy.Decide(false, minimized: false, fullscreen: false, topmost: true, pinnedByUs: true));
        Assert.Equal(TopmostAction.Clear, TopmostPolicy.Decide(false, minimized: true, fullscreen: false, topmost: true, pinnedByUs: true));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void TopmostSetByUxplayItselfIsNeverCleared(bool alwaysOnTop, bool fullscreen)
    {
        var action = TopmostPolicy.Decide(alwaysOnTop, minimized: false, fullscreen: fullscreen, topmost: true, pinnedByUs: false);

        Assert.Equal(TopmostAction.Keep, action);
    }

    [Fact]
    public void NothingHappensWhenOffAndNotOnTop()
    {
        Assert.Equal(TopmostAction.Keep, TopmostPolicy.Decide(false, false, false, topmost: false, pinnedByUs: false));
        Assert.Equal(TopmostAction.Keep, TopmostPolicy.Decide(false, false, false, topmost: false, pinnedByUs: true));
    }
}
