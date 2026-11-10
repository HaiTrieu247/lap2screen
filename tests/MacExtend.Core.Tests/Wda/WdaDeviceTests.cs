using System;
using System.Collections.Generic;
using System.Linq;
using MacExtend.Core.CoreDevice;
using MacExtend.Core.Wda;

namespace MacExtend.Core.Tests.Wda;

public class WdaResponsesTests
{
    [Fact]
    public void ReadsStatusSessionAndSize()
    {
        Assert.True(WdaResponses.IsReady("{\"value\":{\"ready\":true,\"message\":\"WebDriverAgent is ready to accept commands\"},\"sessionId\":null}"));
        Assert.False(WdaResponses.IsReady("{\"value\":{\"ready\":false}}"));
        Assert.True(WdaResponses.IsReady("{\"value\":{\"build\":{}}}"));
        Assert.False(WdaResponses.IsReady("<html>"));
        Assert.Equal("A1B2-C3", WdaResponses.SessionId("{\"value\":{\"sessionId\":\"A1B2-C3\",\"capabilities\":{}},\"sessionId\":\"A1B2-C3\"}"));
        Assert.Equal("X9", WdaResponses.SessionId("{\"value\":{\"sessionId\":\"X9\"}}"));
        Assert.Equal(new DeviceSize(390, 844), WdaResponses.WindowSize("{\"value\":{\"width\":390,\"height\":844},\"sessionId\":\"s\"}"));
        Assert.False(WdaResponses.WindowSize("{\"value\":{}}").IsKnown);
    }

    [Fact]
    public void TellsAForgottenSessionFromOtherErrors()
    {
        Assert.True(WdaResponses.IsInvalidSession(404, "{\"value\":{\"error\":\"invalid session id\",\"message\":\"Session does not exist\"}}"));
        Assert.True(WdaResponses.IsInvalidSession(404, ""));
        Assert.True(WdaResponses.IsInvalidSession(200, "{\"status\":6,\"value\":{}}"));
        Assert.False(WdaResponses.IsInvalidSession(400, "{\"value\":{\"error\":\"invalid argument\"}}"));
        Assert.False(WdaResponses.IsInvalidSession(200, "{\"value\":null,\"sessionId\":\"s\"}"));
        Assert.True(WdaResponses.IsLegacyFailure(200, "{\"status\":13,\"value\":\"x\"}"));
        Assert.Equal("no such element", WdaResponses.Error("{\"value\":{\"error\":\"no such element\"}}"));
    }

    [Fact]
    public void ElongationTellsTheKindOfDevice()
    {
        Assert.InRange(new DeviceSize(390, 844).Elongation, 2.1, 2.2);
        Assert.InRange(new DeviceSize(667, 375).Elongation, 1.7, 1.8);
        Assert.InRange(new DeviceSize(820, 1180).Elongation, 1.4, 1.5);
    }
}

public class WdaLogTests
{
    [Theory]
    [InlineData("ERROR Unable to launch com.x.xctrunner because it has an invalid code signature, inadequate entitlements or its profile has not been explicitly trusted by the user.", WdaFailure.UntrustedDeveloper)]
    [InlineData("the provisioning profile has expired", WdaFailure.Expired)]
    [InlineData("__main__[12] ERROR DeveloperModeIsNotEnabled", WdaFailure.DeveloperModeOff)]
    [InlineData("pymobiledevice3.exceptions.PasswordRequiredError", WdaFailure.Locked)]
    [InlineData("ERROR device has no CoreDeviceProxy service (needs iOS 17.4+) and no RemotePairing service was found", WdaFailure.OldIos)]
    [InlineData("RuntimeError: Timed out waiting for XCTestDriverInterface — runner did not connect", WdaFailure.RunnerFailed)]
    [InlineData("ERROR NoDeviceConnectedError", WdaFailure.NoDevice)]
    [InlineData("INFO launched process pid 412", WdaFailure.None)]
    [InlineData("", WdaFailure.None)]
    public void ClassifiesRunnerLines(string line, WdaFailure expected)
    {
        Assert.Equal(expected, WdaLog.Classify(line));
    }

    [Fact]
    public void ASpecificFailureIsNotReplacedByAGenericOne()
    {
        Assert.Equal(WdaFailure.UntrustedDeveloper, WdaLog.Pick(WdaFailure.UntrustedDeveloper, WdaFailure.RunnerFailed));
        Assert.Equal(WdaFailure.Expired, WdaLog.Pick(WdaFailure.RunnerFailed, WdaFailure.Expired));
        Assert.Equal(WdaFailure.Locked, WdaLog.Pick(WdaFailure.None, WdaFailure.Locked));
        Assert.Equal(WdaFailure.Locked, WdaLog.Pick(WdaFailure.Locked, WdaFailure.None));
    }

    [Fact]
    public void CommandErrorsBecomeFailures()
    {
        Assert.Equal(WdaFailure.DeveloperModeOff, WdaLog.FromCommand(new CommandResult(1, "", CliError.DeveloperModeOff, false), WdaFailure.Other));
        Assert.Equal(WdaFailure.NotTrusted, WdaLog.FromCommand(new CommandResult(1, "", CliError.NeedsTrust, false), WdaFailure.Other));
        Assert.Equal(WdaFailure.DiskImageFailed, WdaLog.FromCommand(new CommandResult(1, "", CliError.Other, false), WdaFailure.DiskImageFailed));
    }
}

public class WdaTextsTests
{
    private static readonly DateTime Now = new DateTime(2026, 11, 10, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void EveryFailureAndStepHasAText()
    {
        foreach (WdaFailure f in Enum.GetValues(typeof(WdaFailure)))
        {
            if (f != WdaFailure.None) Assert.False(string.IsNullOrEmpty(WdaTexts.Failure(f)), f.ToString());
        }
        foreach (WdaStep s in Enum.GetValues(typeof(WdaStep))) Assert.False(string.IsNullOrEmpty(WdaTexts.Step(s)));
    }

    [Fact]
    public void ReadyShowsTheExpiryOrWarns()
    {
        Assert.Equal("", WdaTexts.Expiry(null, Now));
        Assert.Contains("hết hạn ngày", WdaTexts.Expiry(Now.AddDays(5), Now));
        Assert.Contains("trong vòng 24 giờ", WdaTexts.Expiry(Now.AddHours(3), Now));
        Assert.Contains("đã hết hạn", WdaTexts.Expiry(Now.AddMinutes(-1), Now));
        var ready = new WdaView(WdaPhase.Ready, WdaStep.Connecting, WdaFailure.None, "00008030-001E185E343B802E", Now.AddDays(5));
        Assert.StartsWith("Sẵn sàng qua cáp.", WdaTexts.Status(ready, Now));
    }

    [Fact]
    public void FailedShowsTheFailure()
    {
        var failed = new WdaView(WdaPhase.Failed, WdaStep.Launching, WdaFailure.NotInstalled, null, null);

        Assert.Equal(WdaTexts.Failure(WdaFailure.NotInstalled), WdaTexts.Status(failed, Now));
        Assert.True(WdaTexts.NeedsSetup(WdaFailure.NotInstalled));
        Assert.False(WdaTexts.NeedsSetup(WdaFailure.Lost));
    }

    [Fact]
    public void NoEmojiInAnyText()
    {
        var texts = Enum.GetValues(typeof(WdaFailure)).Cast<WdaFailure>().Select(WdaTexts.Failure)
                        .Concat(Enum.GetValues(typeof(WdaStep)).Cast<WdaStep>().Select(WdaTexts.Step))
                        .Concat(new[] { WdaTexts.Title, WdaTexts.Caption });
        foreach (var t in texts) Assert.DoesNotContain(t, c => char.IsSurrogate(c));
    }
}

public class WdaBundlesTests
{
    [Fact]
    public void PicksTheRunnerOverOtherWdaApps()
    {
        var json = "{\"com.example.notes\":{\"CFBundleName\":\"Notes\"},"
                   + "\"com.facebook.WebDriverAgentLib\":{\"CFBundleName\":\"WebDriverAgentLib\"},"
                   + "\"com.facebook.WebDriverAgentRunner.xctrunner\":{\"CFBundleName\":\"WebDriverAgentRunner-Runner\"}}";

        Assert.Equal("com.facebook.WebDriverAgentRunner.xctrunner", WdaBundles.Pick(json));
    }

    [Fact]
    public void FindsARenamedBundleByItsExecutable()
    {
        var json = "{\"com.thinh.ABC123.xctrunner\":{\"CFBundleExecutable\":\"WebDriverAgentRunner-Runner\"},\"com.example\":{}}";

        Assert.Equal("com.thinh.ABC123.xctrunner", WdaBundles.Pick(json));
    }

    [Fact]
    public void ReadsTheListFormToo()
    {
        Assert.Equal("a.WebDriverAgentRunner.xctrunner", WdaBundles.Pick("[{\"CFBundleIdentifier\":\"a.WebDriverAgentRunner.xctrunner\"}]"));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"com.example\":{\"CFBundleName\":\"Example\"}}")]
    [InlineData("{\"bad id; rm\":{\"CFBundleName\":\"WebDriverAgent\"}}")]
    [InlineData("nope")]
    public void NoneWhenThereIsNoUsableRunner(string json)
    {
        Assert.Null(WdaBundles.Pick(json));
    }
}

public class ProvisionProfilesTests
{
    private const string Bundle = "com.facebook.WebDriverAgentRunner.xctrunner";

    private static string Profile(string appId, string expires) =>
        "{\"Entitlements\":{\"application-identifier\":\"" + appId + "\"},\"ExpirationDate\":\"" + expires + "\",\"Name\":\"x\"}";

    [Fact]
    public void TheLatestMatchingProfileWins()
    {
        var json = "[" + Profile("TEAM1." + Bundle, "2026-11-12T10:00:00") + "," + Profile("TEAM1." + Bundle, "2026-11-16T10:00:00") + ","
                   + Profile("TEAM1.com.other", "2027-01-01T00:00:00") + "]";

        var expiry = ProvisionProfiles.Expiry(json, Bundle);

        Assert.Equal(new DateTime(2026, 11, 16, 10, 0, 0, DateTimeKind.Utc), expiry);
        Assert.Equal(DateTimeKind.Utc, expiry.Value.Kind);
    }

    [Fact]
    public void AWildcardProfileCovers()
    {
        Assert.NotNull(ProvisionProfiles.Expiry("[" + Profile("TEAM1.com.facebook.*", "2026-11-16T10:00:00") + "]", Bundle));
        Assert.Null(ProvisionProfiles.Expiry("[" + Profile("TEAM1.org.*", "2026-11-16T10:00:00") + "]", Bundle));
    }

    [Fact]
    public void SoonAndExpired()
    {
        var now = new DateTime(2026, 11, 10, 0, 0, 0, DateTimeKind.Utc);

        Assert.True(ProvisionProfiles.ExpiresSoon(now.AddHours(5), now));
        Assert.False(ProvisionProfiles.ExpiresSoon(now.AddDays(3), now));
        Assert.True(ProvisionProfiles.IsExpired(now, now));
        Assert.False(ProvisionProfiles.IsExpired(null, now));
    }
}

public class WdaLifetimeTests
{
    [Fact]
    public void RunsWhileAPhoneMirrorsAndTenSecondsMore()
    {
        var now = new DateTime(2026, 11, 10, 8, 0, 0, DateTimeKind.Utc);
        var life = new WdaLifetime(() => now);

        Assert.False(life.ShouldRun(true, false));
        Assert.True(life.ShouldRun(true, true));
        Assert.True(life.ShouldRun(true, false));
        now = now.AddSeconds(9);
        Assert.True(life.ShouldRun(true, false));
        now = now.AddSeconds(2);
        Assert.False(life.ShouldRun(true, false));
    }

    [Fact]
    public void SwitchingOffStopsAtOnce()
    {
        var life = new WdaLifetime(() => DateTime.UtcNow);
        life.ShouldRun(true, true);

        Assert.False(life.ShouldRun(false, true));
    }

    [Theory]
    [InlineData("iPhone12,1", true)]
    [InlineData("iPad13,4", true)]
    [InlineData("MacBookPro18,3", false)]
    [InlineData("Mac14,2", false)]
    [InlineData(null, false)]
    public void OnlyIPhonesAndIPads(string model, bool mobile)
    {
        Assert.Equal(mobile, WdaLifetime.IsMobileModel(model));
    }
}

public class WdaTargetTests
{
    private const string A = "00008030-001E185E343B802E";
    private const string B = "00008110-001A2B3C4D5E6F70";

    [Fact]
    public void OneDeviceIsUsedAtOnce()
    {
        var t = WdaTarget.Choose(new[] { A }, null);

        Assert.Equal(WdaTargetKind.Use, t.Kind);
        Assert.Equal(A, t.Udid);
    }

    [Fact]
    public void SeveralDevicesNeedAPickUnlessOneWasRemembered()
    {
        Assert.Equal(WdaTargetKind.Pick, WdaTarget.Choose(new[] { A, B }, null).Kind);
        Assert.Equal(B, WdaTarget.Choose(new[] { A, B }, B.ToLowerInvariant()).Udid);
        Assert.Equal(WdaTargetKind.None, WdaTarget.Choose(new string[0], A).Kind);
    }

    [Fact]
    public void ChoicesAreRememberedPerAirPlayName()
    {
        var text = WdaChoices.Remember("", "iPhone của Thịnh", A);
        text = WdaChoices.Remember(text, "iPad", B);
        text = WdaChoices.Remember(text, "iPhone của Thịnh", B);

        Assert.Equal(B, WdaChoices.Remembered(text, "iPhone của Thịnh"));
        Assert.Equal(B, WdaChoices.Remembered(text, "iPad"));
        Assert.Null(WdaChoices.Remembered(text, "Mac"));
        Assert.Equal(2, WdaChoices.Parse(text).Count);
    }

    [Fact]
    public void NamesWithSeparatorsAndBadUdidsAreSafe()
    {
        var text = WdaChoices.Remember("", "a|b=c\nd", A);

        Assert.Equal(A, WdaChoices.Remembered(text, "a|b=c\nd"));
        Assert.Equal(text, WdaChoices.Remember(text, "x", "not-a-udid"));
        Assert.Empty(WdaChoices.Parse("junk|=|x=y"));
    }

    [Fact]
    public void AtMostSixteenNamesAreKept()
    {
        var text = "";
        for (var i = 0; i < 20; i++) text = WdaChoices.Remember(text, "d" + i, A);

        Assert.Equal(WdaChoices.MaxEntries, WdaChoices.Parse(text).Count);
        Assert.Null(WdaChoices.Remembered(text, "d0"));
        Assert.Equal(A, WdaChoices.Remembered(text, "d19"));
    }
}

public class WdaArgumentsTests
{
    [Fact]
    public void XcuiTestRunsOverTheUserspaceTunnel()
    {
        var args = WdaArguments.XcuiTest("com.facebook.WebDriverAgentRunner.xctrunner");

        Assert.Equal(new[] { "-I", "-X", "utf8", "-u", "-m", "pymobiledevice3", "developer", "dvt", "xcuitest", "com.facebook.WebDriverAgentRunner.xctrunner", "--userspace" }, args);
    }

    [Fact]
    public void ABundleIdThatIsNotOneIsRefused()
    {
        Assert.Throws<ArgumentException>(() => WdaArguments.XcuiTest("a b"));
        Assert.Throws<ArgumentException>(() => WdaArguments.XcuiTest("--env=X"));
        Assert.Throws<ArgumentException>(() => WdaArguments.XcuiTest("--userspace"));
    }

}
