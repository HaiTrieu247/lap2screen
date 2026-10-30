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
}
