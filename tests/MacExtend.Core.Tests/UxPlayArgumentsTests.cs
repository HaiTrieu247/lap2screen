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

    [Fact]
    public void DefaultsMatchSecondDisplayProfile()
    {
        var args = Build(new AppSettings { Pin = "1234" });

        Assert.Equal(new[]
        {
            "-n", "MacExtend-PC", "-nh",
            "-s", "1920x1080@60", "-fps", "60",
            "-vsync", "no",
            "-vs", "d3d11videosink", "-fs",
            "-nohold",
            "-key", "K",
            "-pin", "1234", "-reg", "R",
        }, args);
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
