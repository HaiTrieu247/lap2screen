using MacExtend.Core.CoreDevice;

namespace MacExtend.Core.Tests.CoreDevice;

public class TunneldListingTests
{
    private const string Udid = "00008030-001E185E343B802E";

    private static string Listing(string udid, string address, string port, string iface) =>
        "{\"" + udid + "\": [{\"tunnel-address\": \"" + address + "\", \"tunnel-port\": " + port + ", \"interface\": \"" + iface + "\", \"auxiliary-metadata\": null, \"origin\": null}]}";

    [Fact]
    public void ReadsTunnelsByUdid()
    {
        var map = TunneldListing.Parse(Listing(Udid, "fd7b:167:4659::1", "60123", "usbmux-" + Udid + "-USB"));

        var entry = Assert.Single(map[Udid]);
        Assert.Equal("fd7b:167:4659::1", entry.Address);
        Assert.Equal(60123, entry.Port);
        Assert.Equal("usbmux-" + Udid + "-USB", entry.Interface);
        Assert.True(TunneldListing.HasTunnel(Listing(Udid, "fd7b::1", "1", "x"), Udid.ToLowerInvariant()));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("<!doctype html>")]
    [InlineData("{\"not-a-udid\": [{\"tunnel-address\": \"fd::1\", \"tunnel-port\": 1}]}")]
    [InlineData("{\"00008030-001E185E343B802E\": [{\"tunnel-address\": \"fd::1\", \"tunnel-port\": 0}]}")]
    [InlineData("{\"00008030-001E185E343B802E\": [{\"tunnel-port\": 5}]}")]
    public void NoTunnelForOtherAnswers(string json)
    {
        Assert.False(TunneldListing.HasTunnel(json, Udid));
    }
}
