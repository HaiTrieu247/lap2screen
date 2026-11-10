using System;
using System.Collections.Generic;
using System.Linq;
using MacExtend.Core.Wda;

namespace MacExtend.Core.Tests.Wda;

public class WdaRequestsTests
{
    [Fact]
    public void WritesCompactInvariantJson()
    {
        var value = new List<KeyValuePair<string, object>>
        {
            new("b", 1.5), new("a", new List<object> { true, null, "x" }), new("n", -3),
        };

        Assert.Equal("{\"b\":1.5,\"a\":[true,null,\"x\"],\"n\":-3}", MiniJson.Write(value));
    }

    [Fact]
    public void EscapesQuotesControlAndNonAsciiCharacters()
    {
        var json = MiniJson.Write("Thịnh \"x\"\n\\\b");

        Assert.Equal("\"Th\\u1ecbnh \\\"x\\\"\\n\\\\\\b\"", json);
        Assert.Equal("Thịnh \"x\"\n\\\b", MiniJson.Parse(json));
    }

    [Fact]
    public void RefusesNaNAndUnknownTypes()
    {
        Assert.Throws<ArgumentException>(() => MiniJson.Write(double.NaN));
        Assert.Throws<ArgumentException>(() => MiniJson.Write(new object()));
    }

    [Fact]
    public void TapIsOneTouchPointerThatPressesAndLifts()
    {
        var json = WdaRequests.Actions(WdaGestures.Tap(100.4, 200.6));

        Assert.Equal("{\"actions\":[{\"type\":\"pointer\",\"id\":\"finger1\",\"parameters\":{\"pointerType\":\"touch\"},\"actions\":["
                     + "{\"type\":\"pointerMove\",\"duration\":0,\"x\":100,\"y\":201},{\"type\":\"pointerDown\",\"button\":0},"
                     + "{\"type\":\"pause\",\"duration\":40},{\"type\":\"pointerUp\",\"button\":0}]}]}", json);
    }

    [Fact]
    public void KeysKeepsSurrogatePairsAndCombiningMarksWhole()
    {
        var json = WdaRequests.Keys("á😀\b\n");
        var value = (List<object>)((Dictionary<string, object>)MiniJson.Parse(json))["value"];

        Assert.Equal(new List<object> { "á", "😀", "\b", "\n" }, value);
    }

    [Fact]
    public void SessionAsksForNoIdleWaits()
    {
        var root = (Dictionary<string, object>)MiniJson.Parse(WdaRequests.NewSession());
        var always = (Dictionary<string, object>)((Dictionary<string, object>)root["capabilities"])["alwaysMatch"];

        Assert.Equal(false, always["shouldWaitForQuiescence"]);
        Assert.Equal(0.0, always["waitForIdleTimeout"]);
    }

    [Fact]
    public void ButtonAndHidHome()
    {
        Assert.Equal("{\"name\":\"volumeUp\"}", WdaRequests.Button("volumeUp"));
        Assert.Equal("{\"page\":12,\"usage\":64,\"duration\":0.005}", WdaRequests.HidHome());
    }

    [Fact]
    public void ActionsNeedSteps()
    {
        Assert.Throws<ArgumentException>(() => WdaRequests.Actions(Array.Empty<TouchStep>()));
    }
}

public class WdaGesturesTests
{
    [Fact]
    public void PointsStayOnTheScreen()
    {
        Assert.Equal(0, WdaGestures.X(-0.2, 390));
        Assert.Equal(389, WdaGestures.X(1.0, 390));
        Assert.Equal(422, WdaGestures.Y(0.5, 844));
    }

    [Fact]
    public void LongPressHoldsSevenHundredMilliseconds()
    {
        var steps = WdaGestures.LongPress(10, 20);

        Assert.Equal(TouchStep.Pause(700), steps[2]);
    }

    [Fact]
    public void PathFollowsTheMouseInItsOwnRhythm()
    {
        var samples = new[] { new PathSample(0, 0.5, 0.8), new PathSample(50, 0.5, 0.6), new PathSample(150, 0.5, 0.3) };

        var steps = WdaGestures.Path(samples, 150, 400, 800);

        Assert.Equal(new[]
        {
            TouchStep.Move(200, 640, 0), TouchStep.Down(), TouchStep.Move(200, 480, 50), TouchStep.Move(200, 240, 100), TouchStep.Up(),
        }, steps);
    }

    [Fact]
    public void AStillPointerBeforeReleaseKeepsTheFingerStill()
    {
        var samples = new[] { new PathSample(0, 0.5, 0.8), new PathSample(100, 0.5, 0.3) };

        var steps = WdaGestures.Path(samples, 400, 400, 800);

        Assert.Equal(TouchStep.Pause(300), steps[steps.Count - 2]);
        Assert.Equal(TouchStep.Up(), steps[steps.Count - 1]);
    }

    [Fact]
    public void TheEndHoldIsCapped()
    {
        var samples = new[] { new PathSample(0, 0.1, 0.1), new PathSample(100, 0.2, 0.2) };

        var steps = WdaGestures.Path(samples, 5000, 400, 800);

        Assert.Equal(TouchStep.Pause(WdaGestures.MaxEndHoldMs), steps[steps.Count - 2]);
    }

    [Fact]
    public void ALongDragIsSqueezedButKeepsItsLength()
    {
        var samples = Enumerable.Range(0, 101).Select(i => new PathSample(i * 100, 0.5, i / 100.0)).ToList();

        var steps = WdaGestures.Path(samples, 10000, 400, 800);
        var moves = steps.Where(s => s.Kind == TouchKind.Move).ToList();

        Assert.True(moves.Count <= WdaGestures.MaxPathMoves + 1);
        Assert.InRange(moves.Sum(m => m.DurationMs), WdaGestures.MaxPathMs - 1, WdaGestures.MaxPathMs);
        Assert.Equal(799, moves.Last().Y);
    }

    [Fact]
    public void CloseSamplesAreMergedButTheLastPositionIsKept()
    {
        var samples = new[] { new PathSample(0, 0, 0), new PathSample(5, 0.1, 0), new PathSample(20, 0.2, 0), new PathSample(25, 0.3, 0) };

        var thinned = WdaGestures.Thin(samples);

        Assert.Equal(new[] { 0, 25 }, thinned.Select(s => s.TimeMs));
        Assert.Equal(0.3, thinned.Last().X);
    }
}

public class WdaScrollTests
{
    private static readonly DateTime T0 = new DateTime(2026, 11, 10, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void OneNotchAwayDragsTheFingerDownAndHolds()
    {
        var drags = new WdaScroll().Drags(0.5, 0.5, 1, false, 400, 800, T0);

        var drag = Assert.Single(drags);
        Assert.Equal(TouchStep.Move(200, 400, 0), drag[0]);
        Assert.Equal(TouchStep.Move(200, 496, WdaScroll.DragMs), drag[2]);
        Assert.Equal(TouchStep.Pause(WdaScroll.HoldMs), drag[3]);
    }

    [Fact]
    public void SmallTouchpadStepsAddUpToADrag()
    {
        var scroll = new WdaScroll();

        Assert.Empty(scroll.Drags(0.5, 0.5, 0.1, false, 400, 800, T0));
        var drags = scroll.Drags(0.5, 0.5, 0.2, false, 400, 800, T0.AddMilliseconds(100));

        Assert.Single(drags);
    }

    [Fact]
    public void AStaleOrReversedRestIsDropped()
    {
        var scroll = new WdaScroll();
        scroll.Drags(0.5, 0.5, 0.2, false, 400, 800, T0);

        Assert.Empty(scroll.Drags(0.5, 0.5, 0.1, false, 400, 800, T0.AddSeconds(2)));
        Assert.Empty(scroll.Drags(0.5, 0.5, -0.2, false, 400, 800, T0.AddSeconds(2.1)));
    }

    [Fact]
    public void ManyNotchesAreAtMostThreeDragsAwayFromTheEdges()
    {
        var drags = new WdaScroll().Drags(0.5, 0.02, -20, false, 400, 800, T0);

        Assert.Equal(WdaScroll.MaxSteps, drags.Count);
        foreach (var d in drags)
        {
            Assert.InRange(d[0].Y, 120, 680);
            Assert.InRange(d[2].Y, 120, 680);
        }
    }

    [Fact]
    public void TiltRightDragsTheFingerLeft()
    {
        var drag = Assert.Single(new WdaScroll().Drags(0.5, 0.5, 1, true, 400, 800, T0));

        Assert.True(drag[2].X < drag[0].X);
    }
}

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
