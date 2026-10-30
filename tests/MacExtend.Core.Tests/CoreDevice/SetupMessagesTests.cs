using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using MacExtend.Core.CoreDevice;

namespace MacExtend.Core.Tests.CoreDevice;

public class SetupMessagesTests
{
    private const string Udid = "00008030-001E185E343B802E";

    private static CommandResult Ok(string stdout = "") => new CommandResult(0, stdout, CliError.None, false);

    private static CommandResult Failed(CliError error) => new CommandResult(1, "", error, false);

    private static readonly CommandResult TimedOut = new CommandResult(-1, "", CliError.None, true);

    [Fact]
    public void TheInstallOutcomeIsSaidInVietnamese()
    {
        Assert.Equal("Đã cài thành phần.", SetupMessages.Installed("ok"));
        Assert.Contains("quyền quản trị", SetupMessages.Installed("cancelled"));

        var reason = HelperOutcomes.Message((int)HelperOutcome.InstallFailed);
        var failed = SetupMessages.Installed("failed: " + reason);
        Assert.Contains(reason, failed);
        Assert.DoesNotContain("failed", failed);

        Assert.False(string.IsNullOrWhiteSpace(SetupMessages.Installed("something else")));
        Assert.DoesNotContain("something else", SetupMessages.Installed("something else"));
    }

    [Theory]
    [InlineData("ok", true)]
    [InlineData("cancelled", false)]
    [InlineData("failed: Không tạo được luật tường lửa.", false)]
    [InlineData(null, false)]
    public void OnlyOkIsASetupThatWorked(string outcome, bool expected)
    {
        Assert.Equal(expected, SetupMessages.IsOk(outcome));
    }

    [Fact]
    public void ADownloadProblemNeverShowsTheSystemsOwnText()
    {
        var network = SetupMessages.DownloadFailed(new HttpRequestException("No such host is known."));
        Assert.DoesNotContain("No such host", network);
        Assert.Contains("Internet", network);
        Assert.Equal(network, SetupMessages.DownloadFailed(new TaskCanceledException("A task was canceled.")));

        var disk = SetupMessages.DownloadFailed(new IOException("There is not enough space on the disk."));
        Assert.DoesNotContain("space", disk);
        Assert.Contains("ổ đĩa", disk);
        Assert.Equal(disk, SetupMessages.DownloadFailed(new UnauthorizedAccessException("Access to the path is denied.")));

        Assert.DoesNotContain("boom", SetupMessages.DownloadFailed(new InvalidOperationException("boom")));
    }

    [Fact]
    public void TheDownloadsOwnVietnameseReasonsAreKept()
    {
        const string notOnRelease = "Bản phát hành này không có gói runtime trên GitHub.";
        const string corrupt = "Tệp MacExtend-1.0.0-coredevice-runtime.zip tải về bị hỏng (sai mã SHA-256).";

        Assert.Equal(notOnRelease, SetupMessages.DownloadFailed(new FileNotFoundException(notOnRelease)));
        Assert.Equal(corrupt, SetupMessages.DownloadFailed(new InvalidDataException(corrupt)));
    }

    [Fact]
    public void APickedFileIsEitherNotThePackageOrCouldNotBeRead()
    {
        const string notThePackage = "Tệp này không phải gói runtime của bản MacExtend đang chạy.";
        Assert.Equal(notThePackage, SetupMessages.PickedZipFailed(new InvalidDataException(notThePackage)));

        var unreadable = SetupMessages.PickedZipFailed(new IOException("The process cannot access the file because it is being used by another process."));
        Assert.DoesNotContain("process", unreadable);
        Assert.Contains("tệp", unreadable);
        Assert.Equal(unreadable, SetupMessages.PickedZipFailed(new UnauthorizedAccessException("Access to the path is denied.")));
    }

    [Theory]
    [InlineData(0.424, "Đang tải thành phần... 42%")]
    [InlineData(1.0, "Đang tải thành phần... 100%")]
    [InlineData(-0.5, "Đang tải thành phần... 0%")]
    [InlineData(7.0, "Đang tải thành phần... 100%")]
    public void TheDownloadShowsItsPercentage(double fraction, string expected)
    {
        Assert.Equal(expected, SetupMessages.Downloading(fraction));
    }

    [Fact]
    public void ATrustedDeviceNeedsNoTrustMessage()
    {
        Assert.Null(SetupMessages.Trust(Ok("true\n")));
    }

    [Fact]
    public void EachTrustProblemSaysWhatToDo()
    {
        var locked = SetupMessages.Trust(Failed(CliError.DeviceLocked));
        Assert.Contains("Mở khóa", locked);
        Assert.Contains("bấm " + SetupActions.Connect, locked);

        Assert.Contains("Tin cậy", SetupMessages.Trust(Failed(CliError.NeedsTrust)));
        Assert.Contains("Tin cậy", SetupMessages.Trust(Failed(CliError.TrustDenied)));
        Assert.Contains("Tin cậy", SetupMessages.Trust(TimedOut));
        Assert.Contains(SetupActions.Refresh, SetupMessages.Trust(Failed(CliError.NotConnected)));
        Assert.Equal(CoreDeviceMessages.Failure(SessionFailure.NoAppleService), SetupMessages.Trust(Failed(CliError.NoUsbmux)));
        Assert.False(string.IsNullOrWhiteSpace(SetupMessages.Trust(Failed(CliError.Other))));
    }

    [Fact]
    public void AMountIsDoneWhenTheDeviceListsTheImageOrCannotSay()
    {
        Assert.Equal("Đã gắn ảnh DDI.", SetupMessages.Mount(Ok(), true));
        Assert.Equal("Đã gắn ảnh DDI.", SetupMessages.Mount(Ok(), null));
        Assert.Contains(SetupActions.Mount, SetupMessages.Mount(Ok(), false));
    }

    [Fact]
    public void AMountProblemSaysWhatToDo()
    {
        Assert.Equal(CoreDeviceMessages.Failure(SessionFailure.NoInternet), SetupMessages.Mount(Failed(CliError.NoInternet), false));
        Assert.Contains("Mở khóa", SetupMessages.Mount(Failed(CliError.DeviceLocked), false));
        Assert.Contains("Internet", SetupMessages.Mount(TimedOut, null));
        Assert.Contains("Developer Mode", SetupMessages.Mount(Failed(CliError.DeveloperModeOff), false));
        Assert.Contains(SetupActions.Mount, SetupMessages.Mount(Failed(CliError.Other), false));
    }

    [Fact]
    public void TheDeveloperModeOptionIsFoundInSettings()
    {
        var shown = SetupMessages.Reveal(Ok());
        Assert.Contains("Cài đặt > Quyền riêng tư & Bảo mật > Chế độ nhà phát triển", shown);
        Assert.Contains("bấm " + SetupActions.Refresh, shown);
        Assert.Contains("Mở khóa", SetupMessages.Reveal(Failed(CliError.DeviceLocked)));
    }

    [Fact]
    public void PairingSaysWhatWifiNeedsAndWhatTheFirewallDid()
    {
        Assert.Contains("Riêng tư", SetupMessages.Pair(Ok(), true, null));
        Assert.Contains("Riêng tư", SetupMessages.Pair(Ok(), true, "ok"));
        Assert.Contains("tường lửa", SetupMessages.Pair(Ok(), true, "cancelled"));

        var reason = HelperOutcomes.Message((int)HelperOutcome.FirewallFailed);
        var failed = SetupMessages.Pair(Ok(), true, "failed: " + reason);
        Assert.Contains(reason, failed);
        Assert.DoesNotContain("failed", failed);

        Assert.Contains("Mở khóa", SetupMessages.Pair(Failed(CliError.DeviceLocked), false, null));
        Assert.Contains(SetupActions.Pair, SetupMessages.Pair(Ok(), false, null));
    }

    [Fact]
    public void TheWifiSwitchSaysWhichDeviceItIsFor()
    {
        Assert.Contains("quản trị", SetupMessages.WifiSwitchHint(true, Udid));
        Assert.Contains("Cắm", SetupMessages.WifiSwitchHint(false, null));

        var forDevice = SetupMessages.WifiSwitchHint(false, Udid);
        Assert.Contains("3B802E", forDevice);
        Assert.DoesNotContain(Udid, forDevice);
    }

    private static IEnumerable<string> EveryMessage()
    {
        var results = new List<CommandResult> { Ok("true\n"), TimedOut };
        results.AddRange(Enum.GetValues(typeof(CliError)).Cast<CliError>().Select(Failed));
        foreach (var r in results)
        {
            yield return SetupMessages.Trust(r);
            yield return SetupMessages.Reveal(r);
            foreach (var detected in new bool?[] { true, false, null }) yield return SetupMessages.Mount(r, detected);
            foreach (var rules in new[] { null, "ok", "cancelled", "failed: x" })
            {
                yield return SetupMessages.Pair(r, true, rules);
                yield return SetupMessages.Pair(r, false, rules);
            }
        }
        foreach (var outcome in new[] { "ok", "cancelled", "failed: x", "?" }) yield return SetupMessages.Installed(outcome);
        yield return SetupMessages.DownloadFailed(new HttpRequestException("x"));
        yield return SetupMessages.DownloadFailed(new IOException("x"));
        yield return SetupMessages.DownloadFailed(new InvalidOperationException("x"));
        yield return SetupMessages.PickedZipFailed(new IOException("x"));
        yield return SetupMessages.WifiSwitchHint(true, Udid);
        yield return SetupMessages.WifiSwitchHint(false, null);
        yield return SetupMessages.WifiSwitchHint(false, Udid);
        yield return SetupMessages.Checking;
        yield return SetupMessages.ReadingDevice;
        yield return SetupMessages.CheckingZip;
        yield return SetupMessages.StoppingSession;
        yield return SetupMessages.Mounting;
        yield return SetupMessages.Revealing;
        yield return SetupMessages.Pairing;
        yield return SetupMessages.AddingRules;
        yield return SetupMessages.Installing(true);
        yield return SetupMessages.Installing(false);
        yield return SetupMessages.WindowsCheckTimedOut;
        yield return SetupMessages.Ready;
    }

    private static readonly Regex Press = new Regex(@"(?<!\p{L})[Bb]ấm ", RegexOptions.CultureInvariant);

    /// <summary>Every "bấm X" in <paramref name="text"/> names a button the setup window really has.</summary>
    internal static void AssertPressesRealButtons(string text)
    {
        foreach (Match m in Press.Matches(text))
        {
            var rest = text.Substring(m.Index + m.Length);
            Assert.True(SetupActions.All.Any(label => rest.StartsWith(label, StringComparison.Ordinal)), text);
        }
    }

    [Fact]
    public void EveryButtonAMessageTellsTheUserToPressIsARealButton()
    {
        foreach (var message in EveryMessage().Where(m => m != null))
        {
            Assert.False(string.IsNullOrWhiteSpace(message));
            AssertPressesRealButtons(message);
        }
    }

    [Fact]
    public void TheButtonCheckCatchesALabelTheWindowDoesNotHave()
    {
        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => AssertPressesRealButtons("Mở khóa thiết bị, rồi bấm Thử lại."));
        AssertPressesRealButtons("Bấm Gắn ảnh, rồi bấm Kiểm tra lại.");
    }
}
