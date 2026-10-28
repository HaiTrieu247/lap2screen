using System;
using System.Collections.Generic;

namespace MacExtend.Core.Tests;

public class UxPlayArgumentsTests
{
    private static readonly UxPlayPaths Paths = new UxPlayPaths("K", "R");
    private static readonly RequestedVideo Video1080 = new RequestedVideo(new DisplayMode(1920, 1080, 60), false);

    private static IReadOnlyList<string> Build(AppSettings s, RequestedVideo video = null, UxPlayPaths paths = null,
                                               Func<string, string> resolver = null) =>
        UxPlayArguments.Build(s, "MacExtend-PC", video ?? Video1080, paths ?? Paths, resolver ?? (p => p));

    private const string FullscreenSink = "d3d11videosink fullscreen-toggle-mode=alt-enter+property fullscreen=true";

    [Fact]
    public void DefaultsMatchSecondDisplayProfile()
    {
        var args = Build(new AppSettings { Pin = "1234" });

        Assert.Equal(new[]
        {
            "-n", "MacExtend-PC", "-nh",
            "-s", "1920x1080@60", "-fps", "60",
            "-vsync", "no",
            "-vs", "d3d11videosink",
            "-nohold",
            "-key", "K",
            "-pin", "1234", "-reg", "R",
        }, args);
    }

    [Fact]
    public void FullscreenStartPassesSinkAndOptionsAsOneArgument()
    {
        var args = Build(new AppSettings { Pin = "1234", Fullscreen = true });

        Assert.Equal(new[]
        {
            "-n", "MacExtend-PC", "-nh",
            "-s", "1920x1080@60", "-fps", "60",
            "-vsync", "no",
            "-vs", FullscreenSink,
            "-nohold",
            "-key", "K",
            "-pin", "1234", "-reg", "R",
        }, args);
    }

    [Theory]
    [InlineData(false, "d3d11videosink")]
    [InlineData(true, FullscreenSink)]
    public void NeitherModePassesFsFlag(bool fullscreen, string sink)
    {
        var args = Build(new AppSettings { Pin = "1234", Fullscreen = fullscreen });

        Assert.DoesNotContain("-fs", args);
        AssertPair(args, "-vs", sink);
        Assert.DoesNotContain(args, a => a.StartsWith("fullscreen", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AlwaysOnTopDoesNotChangeArguments(bool fullscreen)
    {
        // MacExtend pins the window itself, so toggling it must not ask for a receiver restart
        var plain = Build(new AppSettings { Pin = "1234", Fullscreen = fullscreen });
        var pinned = Build(new AppSettings { Pin = "1234", Fullscreen = fullscreen, AlwaysOnTop = true });

        Assert.Equal(plain, pinned);
    }

    [Fact]
    public void FullscreenStartKeepsEveryOtherOption()
    {
        var s = new AppSettings { Pin = "4821", Fps = 30, AudioEnabled = false, Fullscreen = true };
        var video = new RequestedVideo(new DisplayMode(2560, 1440, 120), true);

        var windowed = Build(new AppSettings { Pin = "4821", Fps = 30, AudioEnabled = false }, video);
        var fullscreen = Build(s, video);

        Assert.Equal(windowed.Count, fullscreen.Count);
        var vs = IndexOf(windowed, "-vs") + 1;
        for (var i = 0; i < windowed.Count; i++)
        {
            if (i != vs) Assert.Equal(windowed[i], fullscreen[i]);
        }
        AssertPair(fullscreen, "-vsync", "no");
        AssertPair(fullscreen, "-s", "2560x1440@120");
        AssertPair(fullscreen, "-fps", "30");
        AssertPair(fullscreen, "-as", "0");
        AssertPair(fullscreen, "-pin", "4821");
        Assert.Contains("-h265", fullscreen);
        Assert.Contains("-nohold", fullscreen);
    }

    [Fact]
    public void FullscreenSinkSurvivesWindowsQuoting()
    {
        var line = WindowsCommandLine.Join(Build(new AppSettings { Pin = "1234", Fullscreen = true }));

        Assert.Contains(" -vs \"" + FullscreenSink + "\" -nohold ", line);
        Assert.DoesNotContain(" -fs", line);
    }

    [Fact]
    public void PinOffOmitsPinAndRegister()
    {
        var args = Build(new AppSettings { PinEnabled = false, Pin = "1234" });

        Assert.DoesNotContain("-pin", args);
        Assert.DoesNotContain("-reg", args);
        Assert.DoesNotContain("1234", args);
    }

    [Fact]
    public void PinOnWithInvalidPinThrows()
    {
        Assert.Throws<ArgumentException>(() => Build(new AppSettings { PinEnabled = true, Pin = "" }));
    }

    [Fact]
    public void AudioOffAddsNullAudioSink()
    {
        var args = Build(new AppSettings { AudioEnabled = false, Pin = "1234" });

        AssertPair(args, "-as", "0");
    }

    [Fact]
    public void AudioOnLeavesAudioSinkDefault()
    {
        Assert.DoesNotContain("-as", Build(new AppSettings { Pin = "1234" }));
    }

    [Fact]
    public void H265VideoAddsFlagAndMode()
    {
        var args = Build(new AppSettings { Pin = "1234" }, new RequestedVideo(new DisplayMode(2560, 1440, 120), true));

        Assert.Contains("-h265", args);
        AssertPair(args, "-s", "2560x1440@120");
    }

    [Fact]
    public void H264VideoHasNoH265Flag()
    {
        Assert.DoesNotContain("-h265", Build(new AppSettings { Pin = "1234" }));
    }

    [Fact]
    public void Fps30IsPassedThrough()
    {
        AssertPair(Build(new AppSettings { Fps = 30, Pin = "1234" }), "-fps", "30");
    }

    [Fact]
    public void NonAsciiPathUsesResolver()
    {
        var paths = new UxPlayPaths(@"C:\Users\Thịnh\AppData\Roaming\MacExtend\uxplay.pem", @"C:\Users\Thịnh\r.txt");

        var args = Build(new AppSettings { Pin = "1234" }, paths: paths,
                         resolver: p => p.Contains("Thịnh") ? p.Replace("Thịnh", "THINH~1") : p);

        AssertPair(args, "-key", @"C:\Users\THINH~1\AppData\Roaming\MacExtend\uxplay.pem");
        AssertPair(args, "-reg", @"C:\Users\THINH~1\r.txt");
    }

    private static void AssertPair(IReadOnlyList<string> args, string option, string value)
    {
        var i = IndexOf(args, option);
        Assert.True(i >= 0 && i + 1 < args.Count, option + " missing");
        Assert.Equal(value, args[i + 1]);
    }

    private static int IndexOf(IReadOnlyList<string> args, string option)
    {
        for (var i = 0; i < args.Count; i++)
        {
            if (args[i] == option) return i;
        }
        return -1;
    }
}
