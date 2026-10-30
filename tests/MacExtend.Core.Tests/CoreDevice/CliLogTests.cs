using MacExtend.Core.CoreDevice;

namespace MacExtend.Core.Tests.CoreDevice;

public class CliLogTests
{
    private const string Main = "2026-10-30 12:36:22 LEGION __main__[19724] ERROR ";

    [Theory]
    [InlineData("PairingDialogResponsePendingError", CliError.NeedsTrust)]
    [InlineData("UserDeniedPairingError", CliError.TrustDenied)]
    [InlineData("PasswordRequiredError", CliError.DeviceLocked)]
    [InlineData("DeveloperModeIsNotEnabledError", CliError.DeveloperModeOff)]
    [InlineData("ConnectionFailedToUsbmuxdError", CliError.NoUsbmux)]
    [InlineData("failed to query DeveloperDiskImage versions", CliError.NoInternet)]
    [InlineData("Failed to start service. Possible reasons are:", CliError.ServiceUnavailable)]
    [InlineData("Device is not connected", CliError.NotConnected)]
    [InlineData("NoDeviceConnectedError", CliError.NotConnected)]
    [InlineData("Connection was terminated abruptly.", CliError.Other)]
    public void ClassifiesTheCliErrors(string message, CliError expected)
    {
        Assert.Equal(expected, CliLog.Parse(Main + message));
        Assert.Equal(expected, CliLog.Parse("2026-10-30 12:36:22 LEGION pymobiledevice3.__main__[1] ERROR " + message));
    }

    [Theory]
    [InlineData("2026-10-30 12:36:22 LEGION __main__[19724] WARNING Trying again over a no-root userspace tunnel")]
    [InlineData("2026-10-30 12:36:23 LEGION pymobiledevice3.cli.mounter[19724] INFO DeveloperDiskImage already mounted")]
    [InlineData("Traceback (most recent call last):")]
    [InlineData("")]
    [InlineData(null)]
    public void OtherLinesAreNotErrors(string line)
    {
        Assert.Equal(CliError.None, CliLog.Parse(line));
    }

    [Fact]
    public void ASpecificErrorWinsOverOther()
    {
        Assert.Equal(CliError.DeviceLocked, CliLog.Pick(new[] { CliError.Other, CliError.DeviceLocked, CliError.Other }));
        Assert.Equal(CliError.Other, CliLog.Pick(new[] { CliError.None, CliError.Other }));
        Assert.Equal(CliError.None, CliLog.Pick(new CliError[0]));
    }
}
