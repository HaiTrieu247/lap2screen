using System;
using System.IO;

namespace MacExtend.Core.Tests;

public class SettingsStoreTests
{
    private static AppSettings Sample() => new AppSettings
    {
        DeviceName = "Phong khach = TV 2",
        ResolutionMode = ResolutionMode.Custom,
        CustomWidth = 2560,
        CustomHeight = 1440,
        CustomRefresh = 120,
        CustomH265 = true,
        Fps = 30,
        AudioEnabled = false,
        PinEnabled = false,
        Pin = "4821",
        StartWithWindows = true,
    };

    private static void AssertSame(AppSettings expected, AppSettings actual)
    {
        Assert.Equal(expected.DeviceName, actual.DeviceName);
        Assert.Equal(expected.ResolutionMode, actual.ResolutionMode);
        Assert.Equal(expected.CustomWidth, actual.CustomWidth);
        Assert.Equal(expected.CustomHeight, actual.CustomHeight);
        Assert.Equal(expected.CustomRefresh, actual.CustomRefresh);
        Assert.Equal(expected.CustomH265, actual.CustomH265);
        Assert.Equal(expected.Fps, actual.Fps);
        Assert.Equal(expected.AudioEnabled, actual.AudioEnabled);
        Assert.Equal(expected.PinEnabled, actual.PinEnabled);
        Assert.Equal(expected.Pin, actual.Pin);
        Assert.Equal(expected.StartWithWindows, actual.StartWithWindows);
    }

    [Fact]
    public void SerializeThenParseRoundTripsEveryField()
    {
        var original = Sample();

        var parsed = SettingsStore.Parse(SettingsStore.Serialize(original));

        AssertSame(original, parsed);
    }

    [Fact]
    public void EmptyTextGivesDefaults()
    {
        AssertSame(new AppSettings(), SettingsStore.Parse(""));
    }

    [Fact]
    public void GarbageFallsBackToDefaults()
    {
        var parsed = SettingsStore.Parse("%%%\n=x\nFps=abc\nAudioEnabled=maybe\nResolutionMode=Sideways\n\0\0");

        AssertSame(new AppSettings(), parsed);
    }

    [Fact]
    public void ClampsOutOfRange()
    {
        var parsed = SettingsStore.Parse("CustomWidth=99999\nCustomHeight=-5\nCustomRefresh=1000\nFps=45");

        Assert.Equal(4096, parsed.CustomWidth);
        Assert.Equal(480, parsed.CustomHeight);
        Assert.Equal(240, parsed.CustomRefresh);
        Assert.Equal(60, parsed.Fps);
    }

    [Fact]
    public void UnknownKeysAndCommentsAreIgnored()
    {
        var parsed = SettingsStore.Parse("# comment\nColor=blue\nfps=30\n  AudioEnabled = FALSE  ");

        Assert.Equal(30, parsed.Fps);
        Assert.False(parsed.AudioEnabled);
    }

    [Fact]
    public void LoadOfMissingFileGivesDefaults()
    {
        var path = Path.Combine(Path.GetTempPath(), "macextend-" + Guid.NewGuid().ToString("N"), "settings.ini");

        AssertSame(new AppSettings(), SettingsStore.Load(path));
    }

    [Fact]
    public void SaveThenLoadRoundTripsAndCreatesDirectory()
    {
        var dir = Path.Combine(Path.GetTempPath(), "macextend-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(dir, "settings.ini");
        try
        {
            SettingsStore.Save(path, Sample());
            SettingsStore.Save(path, Sample());

            AssertSame(Sample(), SettingsStore.Load(path));
            Assert.Single(Directory.GetFiles(dir));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void CloneIsIndependent()
    {
        var original = Sample();
        var copy = original.Clone();

        copy.Fps = 60;
        copy.DeviceName = "other";

        Assert.Equal(30, original.Fps);
        Assert.Equal("Phong khach = TV 2", original.DeviceName);
    }
}
