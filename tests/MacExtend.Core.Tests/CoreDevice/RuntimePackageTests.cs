using System.Collections.Generic;
using MacExtend.Core.CoreDevice;

namespace MacExtend.Core.Tests.CoreDevice;

public class RuntimePackageTests
{
    private const string Hash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    private static Dictionary<string, string> Good() => new Dictionary<string, string>
    {
        ["CoreDeviceRuntimeId"] = "py3.13.9-abc",
        ["CoreDeviceRuntimeFile"] = "MacExtend-1.0.70-coredevice-runtime.zip",
        ["CoreDeviceRuntimeSha256"] = Hash.ToUpperInvariant(),
        ["CoreDeviceRuntimeSize"] = "95000000",
    };

    [Fact]
    public void ReadsTheEmbeddedRuntime()
    {
        var meta = Good();
        var p = RuntimePackage.FromMetadata(k => meta.TryGetValue(k, out var v) ? v : null);

        Assert.Equal("py3.13.9-abc", p.Id);
        Assert.Equal(Hash, p.Sha256);
        Assert.Equal(95000000, p.Size);
        Assert.True(p.Matches(Hash.ToUpperInvariant()));
        Assert.False(p.Matches(new string('0', 64)));
    }

    [Theory]
    [InlineData("CoreDeviceRuntimeId", "")]
    [InlineData("CoreDeviceRuntimeFile", "..\\evil.zip")]
    [InlineData("CoreDeviceRuntimeFile", "dir/x.zip")]
    [InlineData("CoreDeviceRuntimeSha256", "abc")]
    [InlineData("CoreDeviceRuntimeSize", "-1")]
    [InlineData("CoreDeviceRuntimeSize", "lots")]
    public void ALocalBuildOrABadValueHasNoRuntime(string key, string value)
    {
        var meta = Good();
        meta[key] = value;

        Assert.Null(RuntimePackage.FromMetadata(k => meta.TryGetValue(k, out var v) ? v : null));
    }
}
