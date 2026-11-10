using MacExtend.Core.Wda;

namespace MacExtend.Core.Tests.Wda;

public class WdaSettingsTests
{
    private const string Udid = "00008030-001E185E343B802E";

    [Fact]
    public void TheSwitchAndThePicksSurviveASaveAndLoad()
    {
        var s = new AppSettings { WdaControl = true, WdaDevices = WdaChoices.Remember("", "iPhone của Thịnh", Udid) };

        var back = SettingsStore.Parse(SettingsStore.Serialize(s));

        Assert.True(back.WdaControl);
        Assert.Equal(Udid, WdaChoices.Remembered(back.WdaDevices, "iPhone của Thịnh"));
    }

    [Fact]
    public void OffByDefaultAndJunkPicksAreDropped()
    {
        var s = SettingsStore.Parse("WdaDevices=nonsense\n");

        Assert.False(s.WdaControl);
        Assert.Equal("", s.WdaDevices);
    }

    [Fact]
    public void TheTrackerKeepsTheModelWhileConnected()
    {
        var tracker = new ConnectionTracker();

        Assert.True(tracker.OnLog(UxPlayLogParser.Parse("connection request from iPhone của Thịnh (iPhone12,1) with deviceID = 11:22:33:44:55:66")));
        Assert.Equal("iPhone12,1", tracker.ClientModel);
        Assert.True(tracker.OnLog(UxPlayLogParser.Parse("connection request from iPhone của Thịnh (iPad13,4) with deviceID = 11:22:33:44:55:66")));
        tracker.Reset();
        Assert.Null(tracker.ClientModel);
    }
}
