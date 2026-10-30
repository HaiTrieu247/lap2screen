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

    [Theory]
    [InlineData("{}", true)]
    [InlineData("{\"00008030-001E185E343B802E\": []}", true)]
    [InlineData("[]", false)]
    [InlineData("<html>", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void OnlyAJsonObjectIsATunneldListing(string json, bool expected) => Assert.Equal(expected, TunneldListing.IsListing(json));

    [Fact]
    public void SummaryNamesOnlyTheKindsOfTunnelNeverTheUdid()
    {
        var json = "{\"" + Udid + "\": [{\"tunnel-address\": \"fd::1\", \"tunnel-port\": 5, \"interface\": \"usbmux-" + Udid + "-USB\"},"
                   + "{\"tunnel-address\": \"fd::2\", \"tunnel-port\": 6, \"interface\": \"192.168.1.20\"}]}";

        var text = TunneldListing.Summary(json);

        Assert.Equal("1 device(s), 2 tunnel(s): usbmux, wifi", text);
        Assert.DoesNotContain(Udid, text);
        Assert.DoesNotContain("192.168", text);
    }

    [Theory]
    [InlineData("{}", "no tunnels")]
    [InlineData(null, "tunneld does not answer")]
    public void SummaryOfNothing(string json, string expected) => Assert.Equal(expected, TunneldListing.Summary(json));
}
