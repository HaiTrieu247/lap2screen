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
        AutoCheckUpdates = false,
        FocusOnConnect = false,
        HideCursor = false,
        Fullscreen = true,
        AlwaysOnTop = true,
        BluetoothControl = true,
        DirectWifiDevices = "00008030-001E185E343B802E",
        DirectLastDevice = "00008030-001E185E343B802E",
        DirectControl = true,
        DirectDevices = "iPhone của Thịnh=00008030-001E185E343B802E",
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
        Assert.Equal(expected.AutoCheckUpdates, actual.AutoCheckUpdates);
        Assert.Equal(expected.FocusOnConnect, actual.FocusOnConnect);
        Assert.Equal(expected.HideCursor, actual.HideCursor);
        Assert.Equal(expected.Fullscreen, actual.Fullscreen);
        Assert.Equal(expected.AlwaysOnTop, actual.AlwaysOnTop);
        Assert.Equal(expected.BluetoothControl, actual.BluetoothControl);
        Assert.Equal(expected.DirectWifiDevices, actual.DirectWifiDevices);
        Assert.Equal(expected.DirectLastDevice, actual.DirectLastDevice);
        Assert.Equal(expected.DirectControl, actual.DirectControl);
        Assert.Equal(expected.DirectDevices, actual.DirectDevices);
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

    [Fact]
    public void UpdatesAreCheckedByDefault()
    {
        Assert.True(SettingsStore.Parse("").AutoCheckUpdates);
    }

    [Fact]
    public void AirPlayScreenTakesOverByDefault()
    {
        var s = SettingsStore.Parse("");

        Assert.True(s.FocusOnConnect);
        Assert.True(s.HideCursor);
    }

    [Fact]
    public void AirPlayOpensInResizableWindowByDefault()
    {
        Assert.False(new AppSettings().Fullscreen);
        Assert.False(SettingsStore.Parse("").Fullscreen);
    }

    [Fact]
    public void SettingsFromOlderVersionWithoutFullscreenOpenWindowed()
    {
        var parsed = SettingsStore.Parse("# MacExtend settings\nFps=30\nAudioEnabled=false\nFocusOnConnect=true\nHideCursor=true\n");

        Assert.False(parsed.Fullscreen);
        Assert.Equal(30, parsed.Fps);
        Assert.False(parsed.AudioEnabled);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void FullscreenRoundTrips(bool fullscreen)
    {
        var text = SettingsStore.Serialize(new AppSettings { Fullscreen = fullscreen });

        Assert.Contains("Fullscreen=" + (fullscreen ? "true" : "false") + "\n", text);
        Assert.Equal(fullscreen, SettingsStore.Parse(text).Fullscreen);
    }

    [Theory]
    [InlineData("fullscreen=true")]
    [InlineData("FULLSCREEN=True")]
    [InlineData("  FullScreen = TRUE  ")]
    public void FullscreenKeyIgnoresCase(string line)
    {
        Assert.True(SettingsStore.Parse(line).Fullscreen);
    }

    [Theory]
    [InlineData("Fullscreen=yes")]
    [InlineData("Fullscreen=1")]
    [InlineData("Fullscreen=")]
    public void InvalidFullscreenFallsBackToWindowedAndKeepsOtherSettings(string line)
    {
        var parsed = SettingsStore.Parse("Fps=30\n" + line + "\nHideCursor=false");

        Assert.False(parsed.Fullscreen);
        Assert.Equal(30, parsed.Fps);
        Assert.False(parsed.HideCursor);
    }

    [Fact]
    public void VideoWindowIsNotPinnedOnTopByDefault()
    {
        Assert.False(new AppSettings().AlwaysOnTop);
        Assert.False(SettingsStore.Parse("").AlwaysOnTop);
    }

    [Fact]
    public void SettingsFromOlderVersionWithoutAlwaysOnTopAreNotPinned()
    {
        var parsed = SettingsStore.Parse("# MacExtend settings\nFps=30\nHideCursor=true\nFullscreen=true\n");

        Assert.False(parsed.AlwaysOnTop);
        Assert.True(parsed.Fullscreen);
        Assert.Equal(30, parsed.Fps);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AlwaysOnTopRoundTrips(bool onTop)
    {
        var text = SettingsStore.Serialize(new AppSettings { AlwaysOnTop = onTop });

        Assert.Contains("AlwaysOnTop=" + (onTop ? "true" : "false") + "\n", text);
        Assert.Equal(onTop, SettingsStore.Parse(text).AlwaysOnTop);
    }

    [Theory]
    [InlineData("alwaysontop=true")]
    [InlineData("  ALWAYSONTOP = True  ")]
    public void AlwaysOnTopKeyIgnoresCase(string line)
    {
        Assert.True(SettingsStore.Parse(line).AlwaysOnTop);
    }

    [Theory]
    [InlineData("AlwaysOnTop=yes")]
    [InlineData("AlwaysOnTop=1")]
    [InlineData("AlwaysOnTop=")]
    public void InvalidAlwaysOnTopFallsBackToNotPinnedAndKeepsOtherSettings(string line)
    {
        var parsed = SettingsStore.Parse("Fps=30\n" + line + "\nHideCursor=false");

        Assert.False(parsed.AlwaysOnTop);
        Assert.Equal(30, parsed.Fps);
        Assert.False(parsed.HideCursor);
    }

    [Fact]
    public void BluetoothControlIsOffByDefaultAndInOlderFiles()
    {
        Assert.False(new AppSettings().BluetoothControl);
        Assert.False(SettingsStore.Parse("# MacExtend settings\nFps=30\nAlwaysOnTop=true\n").BluetoothControl);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void BluetoothControlRoundTrips(bool on)
    {
        var text = SettingsStore.Serialize(new AppSettings { BluetoothControl = on });

        Assert.Contains("BluetoothControl=" + (on ? "true" : "false") + "\n", text);
        Assert.Equal(on, SettingsStore.Parse(text).BluetoothControl);
    }

    [Fact]
    public void ClampKeepsOnlyValidWifiDevices()
    {
        var s = new AppSettings { DirectWifiDevices = "00008030-001E185E343B802E;junk;;00008030-001E185E343B802E", DirectLastDevice = "junk" };

        s.Clamp();

        Assert.Equal("00008030-001E185E343B802E", s.DirectWifiDevices);
        Assert.Equal("", s.DirectLastDevice);
    }

    [Fact]
    public void WifiDevicesCanBeTurnedOnAndOff()
    {
        var s = new AppSettings();

        s.SetWifiDevice("00008030-001E185E343B802E", true);
        s.SetWifiDevice("00008101-000A1B2C3D4E5F60", true);
        s.SetWifiDevice("00008030-001E185E343B802E", false);

        Assert.Equal(new[] { "00008101-000A1B2C3D4E5F60" }, s.WifiDevices());
    }

    [Theory]
    [InlineData("bluetoothcontrol=TRUE", true)]
    [InlineData("BluetoothControl=on", false)]
    [InlineData("BluetoothControl=", false)]
    public void BluetoothControlParsesLikeOtherSwitches(string line, bool expected)
    {
        Assert.Equal(expected, SettingsStore.Parse("Fps=30\n" + line).BluetoothControl);
    }

    [Fact]
    public void DirectControlIsOffByDefaultAndInOlderFiles()
    {
        Assert.False(new AppSettings().DirectControl);
        // the WDA switch it replaced and the viewer window's settings are left behind as unknown lines
        var old = SettingsStore.Parse("# MacExtend settings\nWdaControl=true\nWdaDevices=x=00008030-001E185E343B802E\nDirectOnTop=true\n");
        Assert.False(old.DirectControl);
        Assert.Equal("", old.DirectDevices);
    }

    [Theory]
    [InlineData("directcontrol=TRUE", true)]
    [InlineData("DirectControl=true", true)]
    [InlineData("DirectControl=yes", false)]
    [InlineData("DirectControl=", false)]
    public void DirectControlParsesLikeOtherSwitches(string line, bool expected)
    {
        Assert.Equal(expected, SettingsStore.Parse("Fps=30\n" + line).DirectControl);
    }

    [Fact]
    public void TheDevicePickedForEachAirPlayNameRoundTrips()
    {
        const string udid = "00008030-001E185E343B802E";
        var s = new AppSettings { DirectControl = true, DirectDevices = MacExtend.Core.CoreDevice.RememberedDevices.Remember("", "iPhone của Thịnh", udid) };

        var back = SettingsStore.Parse(SettingsStore.Serialize(s));

        Assert.True(back.DirectControl);
        Assert.Equal(udid, MacExtend.Core.CoreDevice.RememberedDevices.Remembered(back.DirectDevices, "iPhone của Thịnh"));
    }

    [Fact]
    public void ClampDropsARememberedListWithNothingValid()
    {
        var s = new AppSettings { DirectDevices = "junk|=x|a=not-a-udid" };

        s.Clamp();

        Assert.Equal("", s.DirectDevices);
    }
}
