using MacExtend.Core.CoreDevice;

namespace MacExtend.Core.Tests.CoreDevice;

public class DeviceQueriesTests
{
    private const string A = "00008030-001E185E343B802E";
    private const string B = "0123456789abcdef0123456789abcdef01234567";

    [Fact]
    public void ReadsUdidsAndDropsAnythingElse()
    {
        var udids = DeviceQueries.ParseUdids("[\n    \"" + A + "\",\n    \"" + B + "\",\n    \"not-a-udid\",\n    7,\n    \"" + A.ToLowerInvariant() + "\"\n]\n");

        Assert.Equal(new[] { A, B }, udids);
    }

    [Theory]
    [InlineData("")]
    [InlineData("[]")]
    [InlineData("Traceback (most recent call last):")]
    public void NoDevicesWhenTheOutputIsEmptyOrNotAList(string stdout)
    {
        Assert.Empty(DeviceQueries.ParseUdids(stdout));
    }

    [Theory]
    [InlineData("true\n", true)]
    [InlineData("false\n", false)]
    public void ReadsDeveloperModeStatus(string stdout, bool expected)
    {
        Assert.Equal(expected, DeviceQueries.ParseBool(stdout));
    }

    [Theory]
    [InlineData("")]
    [InlineData("\"true\"")]
    [InlineData("1")]
    public void UnknownWhenTheStatusIsNotABool(string stdout)
    {
        Assert.Null(DeviceQueries.ParseBool(stdout));
    }

    // `mounter list` prints the image mounter's CopyDevices entries with print_json (sorted keys, signature in hex)
    private const string PersonalizedListed = "[\n    {\n        \"DiskImageType\": \"Personalized\",\n        \"ImageSignature\": \"0a1b2c\",\n        \"IsMounted\": true,\n        \"PersonalizedImageType\": \"DeveloperDiskImage\"\n    }\n]\n";

    [Fact]
    public void ReadsAMountedPersonalizedImage()
    {
        Assert.True(DeviceQueries.ParseDeveloperImageMounted(PersonalizedListed));
    }

    [Fact]
    public void ReadsAMountedDeveloperImageOfAnOlderDevice()
    {
        Assert.True(DeviceQueries.ParseDeveloperImageMounted("[{\"DiskImageType\": \"Developer\", \"ImageSignature\": \"ff\", \"IsMounted\": true}]"));
    }

    [Theory]
    [InlineData("[]\n")]
    [InlineData("[{\"DiskImageType\": \"Cryptex\"}]")]
    [InlineData("[{\"ImageSignature\": \"ff\"}, 7, \"Personalized\"]")]
    public void NoDeveloperImageWhenNoneIsListed(string stdout)
    {
        Assert.False(DeviceQueries.ParseDeveloperImageMounted(stdout));
    }

    [Theory]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("Traceback (most recent call last):")]
    public void UnknownWhenTheImageListIsNotAList(string stdout)
    {
        Assert.Null(DeviceQueries.ParseDeveloperImageMounted(stdout));
    }

    [Fact]
    public void ADeviceThatAnswersTheStatusTrustsTheLaptop()
    {
        Assert.True(DeviceQueries.Trusted(new CommandResult(0, "true\n", CliError.None, false)));
    }

    [Theory]
    [InlineData(CliError.NeedsTrust)]
    [InlineData(CliError.TrustDenied)]
    [InlineData(CliError.DeviceLocked)]
    public void ATrustProblemMeansNotTrusted(CliError error)
    {
        Assert.False(DeviceQueries.Trusted(new CommandResult(1, "", error, false)));
    }

    [Fact]
    public void AStatusStillWaitingForTheTrustDialogMeansNotTrusted()
    {
        Assert.False(DeviceQueries.Trusted(new CommandResult(-1, "", CliError.None, true)));
    }

    [Theory]
    [InlineData(CliError.NotConnected)]
    [InlineData(CliError.Other)]
    [InlineData(CliError.None)]
    public void AnyOtherFailureLeavesTrustUnknown(CliError error)
    {
        Assert.Null(DeviceQueries.Trusted(new CommandResult(1, "", error, false)));
    }
}
