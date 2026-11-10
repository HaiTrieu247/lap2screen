using System;
using System.Collections.Generic;
using System.Linq;
using MacExtend.Core.CoreDevice;

namespace MacExtend.Core.Tests.CoreDevice;

public class SetupStepsTests
{
    private const string Udid = "00008030-001E185E343B802E";

    private static SetupFacts AllDone() => new SetupFacts
    {
        RuntimeInstalled = true,
        WindowsSetupDone = true,
        AppleServiceReachable = true,
        UsbUdid = Udid,
        Trusted = true,
        DeveloperMode = true,
        DiskImageMounted = true,
    };

    private static SetupStatus Status(SetupFacts f, SetupStepId id) => SetupSteps.Evaluate(f).Single(s => s.Id == id).Status;

    [Fact]
    public void EverythingDoneIsReadyAndWifiStaysOptional()
    {
        var steps = SetupSteps.Evaluate(AllDone());

        Assert.True(SetupSteps.Ready(steps));
        Assert.Equal(SetupStatus.Optional, steps.Single(s => s.Id == SetupStepId.Wifi).Status);
        Assert.Equal(SetupStatus.Optional, steps.Single(s => s.Id == SetupStepId.Wda).Status);
        Assert.Equal(8, steps.Count);
    }

    [Fact]
    public void NothingDoneStartsWithTheComponentsAndTheAppleService()
    {
        var f = new SetupFacts();

        Assert.Equal(SetupStatus.Todo, Status(f, SetupStepId.Components));
        Assert.Equal(SetupStatus.Todo, Status(f, SetupStepId.AppleService));
        Assert.Equal(SetupStatus.Waiting, Status(f, SetupStepId.Device));
        Assert.Equal(SetupStatus.Waiting, Status(f, SetupStepId.Trust));
        Assert.Equal(SetupStatus.Waiting, Status(f, SetupStepId.DeveloperMode));
        Assert.Equal(SetupStatus.Waiting, Status(f, SetupStepId.DiskImage));
        Assert.False(SetupSteps.Ready(SetupSteps.Evaluate(f)));
    }

    [Fact]
    public void EachDeviceStepWaitsForTheOneBefore()
    {
        var f = AllDone();
        f.Trusted = null;
        f.DeveloperMode = null;
        f.DiskImageMounted = false;

        Assert.Equal(SetupStatus.Todo, Status(f, SetupStepId.Trust));
        Assert.Equal(SetupStatus.Waiting, Status(f, SetupStepId.DeveloperMode));
        Assert.Equal(SetupStatus.Waiting, Status(f, SetupStepId.DiskImage));

        f.Trusted = true;
        f.DeveloperMode = false;
        Assert.Equal(SetupStatus.Todo, Status(f, SetupStepId.DeveloperMode));

        f.DeveloperMode = true;
        Assert.Equal(SetupStatus.Todo, Status(f, SetupStepId.DiskImage));
    }

    [Fact]
    public void TheImageDoesNotHoldBackReadinessOnceDeveloperModeIsOn()
    {
        // every session runs auto-mount before serve-web, and from iOS 27 the image mounter may not list the cryptex DDI
        var f = AllDone();
        f.DiskImageMounted = false;

        var image = Step(f, SetupStepId.DiskImage);
        Assert.Equal(SetupStatus.Todo, image.Status);
        Assert.Contains("trước mỗi phiên", image.Detail);
        Assert.True(SetupSteps.Ready(SetupSteps.Evaluate(f)));

        f.DeveloperMode = false;
        Assert.False(SetupSteps.Ready(SetupSteps.Evaluate(f)));
    }

    [Fact]
    public void WifiWantedNeedsPairing()
    {
        var f = AllDone();
        f.WifiWanted = true;

        Assert.Equal(SetupStatus.Todo, Status(f, SetupStepId.Wifi));
        Assert.False(SetupSteps.Ready(SetupSteps.Evaluate(f)));

        f.WifiPaired = true;
        Assert.Equal(SetupStatus.Done, Status(f, SetupStepId.Wifi));
    }

    [Fact]
    public void EveryStepHasVietnameseText()
    {
        foreach (var step in SetupSteps.Evaluate(new SetupFacts()).Concat(SetupSteps.Evaluate(AllDone())))
        {
            Assert.False(string.IsNullOrWhiteSpace(step.Title));
            Assert.False(string.IsNullOrWhiteSpace(step.Detail));
        }
    }

    private static SetupStep Step(SetupFacts f, SetupStepId id) => SetupSteps.Evaluate(f).Single(s => s.Id == id);

    /// <summary>Facts under which each step in turn is still to do, with and without the task.</summary>
    private static IEnumerable<SetupFacts> TodoFacts()
    {
        yield return new SetupFacts();
        yield return new SetupFacts { WithoutTask = true };
        yield return new SetupFacts { RuntimeInstalled = true };
        var device = AllDone();
        device.UsbUdid = null;
        yield return device;
        var trust = AllDone();
        trust.Trusted = false;
        yield return trust;
        var developer = AllDone();
        developer.DeveloperMode = false;
        yield return developer;
        var image = AllDone();
        image.DiskImageMounted = false;
        yield return image;
        var wifi = AllDone();
        wifi.WifiWanted = true;
        yield return wifi;
        var wifiWithoutTask = AllDone();
        wifiWithoutTask.WifiWanted = true;
        wifiWithoutTask.WithoutTask = true;
        yield return wifiWithoutTask;
        var wda = AllDone();
        wda.WdaWanted = true;
        yield return wda;
        var wdaExpiring = AllDone();
        wdaExpiring.WdaWanted = true;
        wdaExpiring.WdaBundle = "com.facebook.WebDriverAgentRunner.xctrunner";
        wdaExpiring.WdaExpiresUtc = wdaExpiring.NowUtc.AddHours(2);
        yield return wdaExpiring;
        var wdaExpired = AllDone();
        wdaExpired.WdaWanted = true;
        wdaExpired.WdaBundle = "com.facebook.WebDriverAgentRunner.xctrunner";
        wdaExpired.WdaExpiresUtc = wdaExpired.NowUtc.AddHours(-2);
        yield return wdaExpired;
    }

    private static IEnumerable<SetupFacts> AnyFacts()
    {
        foreach (var f in TodoFacts()) yield return f;
        yield return AllDone();
        var paired = AllDone();
        paired.WifiWanted = true;
        paired.WifiPaired = true;
        yield return paired;
        paired.WithoutTask = true;
        yield return paired;
        var standard = AllDone();
        standard.StandardAccount = true;
        standard.WifiWanted = true;
        yield return standard;
        var wda = AllDone();
        wda.WdaWanted = true;
        wda.WdaBundle = "com.facebook.WebDriverAgentRunner.xctrunner";
        wda.WdaExpiresUtc = wda.NowUtc.AddDays(5);
        yield return wda;
        wda.WdaExpiresUtc = null;
        yield return wda;
        var wdaUntrusted = AllDone();
        wdaUntrusted.WdaWanted = true;
        wdaUntrusted.Trusted = false;
        yield return wdaUntrusted;
    }

    [Fact]
    public void EveryStepToDoHasItsOwnButtonAndItsTextNamesThatButton()
    {
        var seen = new HashSet<SetupStepId>();
        foreach (var f in TodoFacts())
        {
            foreach (var step in SetupSteps.Evaluate(f).Where(s => s.Status == SetupStatus.Todo))
            {
                seen.Add(step.Id);
                Assert.False(string.IsNullOrEmpty(step.ActionText));
                Assert.Contains("ấm " + step.ActionText, step.Detail);
            }
        }
        Assert.Equal(Enum.GetValues(typeof(SetupStepId)).Length, seen.Count);
    }

    [Fact]
    public void OnlyAStepToDoHasAButton()
    {
        foreach (var f in AnyFacts())
        {
            foreach (var step in SetupSteps.Evaluate(f).Where(s => s.Status != SetupStatus.Todo)) Assert.Null(step.ActionText);
        }
    }

    [Fact]
    public void EveryButtonATextTellsTheUserToPressIsARealButton()
    {
        foreach (var f in AnyFacts())
        {
            foreach (var step in SetupSteps.Evaluate(f)) SetupMessagesTests.AssertPressesRealButtons(step.Detail);
        }
    }

    [Fact]
    public void TheButtonsAreTheOnesTheWindowShows()
    {
        Assert.Equal("Tải và cài", SetupActions.Install);
        Assert.Equal("Cài lại", SetupActions.Reinstall);
        Assert.Equal("Kiểm tra lại", SetupActions.Refresh);
        Assert.Equal("Kết nối", SetupActions.Connect);
        Assert.Equal("Hiện tùy chọn", SetupActions.Reveal);
        Assert.Equal("Gắn ảnh", SetupActions.Mount);
        Assert.Equal("Ghép nối", SetupActions.Pair);
        Assert.Equal("Chọn tệp runtime...", SetupActions.PickZip);
        Assert.Equal("Tải WDA", SetupActions.GetWda);
        Assert.Equal(9, SetupActions.All.Count);
    }

    [Fact]
    public void TheComponentsNeedNoDownloadWhenOnlyWindowsPartIsMissing()
    {
        var f = AllDone();
        f.WindowsSetupDone = false;

        var step = Step(f, SetupStepId.Components);
        Assert.Equal(SetupStatus.Todo, step.Status);
        Assert.Equal(SetupActions.Reinstall, step.ActionText);
        Assert.DoesNotContain("100 MB", step.Detail);
        Assert.Equal(SetupActions.Install, Step(new SetupFacts(), SetupStepId.Components).ActionText);
    }

    [Fact]
    public void ACopyWithoutTheTaskInstallsWithoutAdministratorRightsAndSaysWifiAsksEachTime()
    {
        var plain = Step(new SetupFacts { WithoutTask = true }, SetupStepId.Components);
        Assert.Contains("không cần quyền quản trị", plain.Detail);
        Assert.DoesNotContain("Windows hỏi quyền quản trị", plain.Detail);
        Assert.Contains("Windows hỏi quyền quản trị đúng một lần", Step(new SetupFacts(), SetupStepId.Components).Detail);

        var wifi = AllDone();
        wifi.WifiWanted = true;
        wifi.WithoutTask = true;
        Assert.Contains("quyền quản trị", Step(wifi, SetupStepId.Wifi).Detail);
        wifi.WifiPaired = true;
        Assert.Contains("Mỗi phiên qua Wi-Fi", Step(wifi, SetupStepId.Wifi).Detail);
        wifi.WithoutTask = false;
        Assert.DoesNotContain("quyền quản trị", Step(wifi, SetupStepId.Wifi).Detail);
    }

    [Fact]
    public void AStandardAccountGetsNoWifiAndIsToldWhy()
    {
        var f = AllDone();
        f.StandardAccount = true;
        f.WifiWanted = true;

        var step = Step(f, SetupStepId.Wifi);
        Assert.Equal(SetupStatus.Optional, step.Status);
        Assert.Equal(CoreDeviceMessages.Failure(SessionFailure.NeedsAdminAccount), step.Detail);
        Assert.Null(step.ActionText);
        Assert.True(SetupSteps.Ready(SetupSteps.Evaluate(f)));
        Assert.False(SetupSteps.WifiSwitchEnabled(f));
    }

    [Fact]
    public void TheWifiSwitchIsForTheDeviceOnTheCable()
    {
        var f = AllDone();
        Assert.True(SetupSteps.WifiSwitchEnabled(f));

        f.UsbUdid = null;
        Assert.False(SetupSteps.WifiSwitchEnabled(f));
    }

    [Theory]
    [InlineData(SetupStatus.Done, false, "Đã xong")]
    [InlineData(SetupStatus.Todo, false, "Cần làm")]
    [InlineData(SetupStatus.Waiting, false, "Chờ bước trước")]
    [InlineData(SetupStatus.Optional, false, "Tùy chọn")]
    [InlineData(SetupStatus.Todo, true, "Đang làm")]
    [InlineData(SetupStatus.Done, true, "Đang làm")]
    public void EachStatusHasAChip(SetupStatus status, bool running, string expected)
    {
        Assert.Equal(expected, SetupSteps.StatusText(status, running));
    }

    [Fact]
    public void TheWdaStepIsOptionalUntilWdaControlIsOnAndThenWaitsForTrust()
    {
        var f = AllDone();
        Assert.Equal(SetupStatus.Optional, Status(f, SetupStepId.Wda));
        f.WdaWanted = true;
        Assert.Equal(SetupStatus.Todo, Status(f, SetupStepId.Wda));
        Assert.Equal(SetupActions.GetWda, Step(f, SetupStepId.Wda).ActionText);
        f.Trusted = false;
        Assert.Equal(SetupStatus.Waiting, Status(f, SetupStepId.Wda));
    }

    [Fact]
    public void WdaIsDoneWhileItsSignatureLastsMoreThanADay()
    {
        var f = AllDone();
        f.WdaWanted = true;
        f.WdaBundle = "com.facebook.WebDriverAgentRunner.xctrunner";
        f.WdaExpiresUtc = f.NowUtc.AddDays(6);
        Assert.Equal(SetupStatus.Done, Status(f, SetupStepId.Wda));
        Assert.Contains("hết hạn ngày", Step(f, SetupStepId.Wda).Detail);

        f.WdaExpiresUtc = f.NowUtc.AddHours(3);
        Assert.Equal(SetupStatus.Todo, Status(f, SetupStepId.Wda));
        Assert.Contains("trong vòng 24 giờ", Step(f, SetupStepId.Wda).Detail);
    }

    [Fact]
    public void DirectControlIsReadyWithoutWda()
    {
        var f = AllDone();
        f.WdaWanted = true;

        Assert.True(SetupSteps.Ready(SetupSteps.Evaluate(f)));
    }
}
