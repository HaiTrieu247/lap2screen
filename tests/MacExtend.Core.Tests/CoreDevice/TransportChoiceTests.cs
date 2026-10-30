using MacExtend.Core.CoreDevice;

namespace MacExtend.Core.Tests.CoreDevice;

public class TransportChoiceTests
{
    private const string A = "00008030-001E185E343B802E";
    private const string B = "00008101-000A1B2C3D4E5F60";

    [Fact]
    public void TheCableWinsOverWifi()
    {
        Assert.Equal(CoreDeviceRoute.Usb, TransportChoice.Choose(A, new[] { A }, new[] { A }, wifiAvailable: true));
    }

    [Fact]
    public void WifiOnlyForADeviceSetUpForItAndOnlyWhenWindowsIsReady()
    {
        Assert.Equal(CoreDeviceRoute.Wifi, TransportChoice.Choose(A, new string[0], new[] { A.ToLowerInvariant() }, wifiAvailable: true));
        Assert.Equal(CoreDeviceRoute.None, TransportChoice.Choose(A, new string[0], new[] { A }, wifiAvailable: false));
        Assert.Equal(CoreDeviceRoute.None, TransportChoice.Choose(A, new string[0], new[] { B }, wifiAvailable: true));
        Assert.Equal(CoreDeviceRoute.None, TransportChoice.Choose(null, new[] { A }, null, wifiAvailable: true));
    }

    [Fact]
    public void PicksTheLastDeviceWhenItIsThere()
    {
        Assert.Equal(B, TransportChoice.PickDevice(new[] { A, B }, new string[0], true, B.ToLowerInvariant()));
    }

    [Fact]
    public void PicksTheOnlyDeviceOrAsks()
    {
        Assert.Equal(A, TransportChoice.PickDevice(new[] { A }, new[] { A }, true, null));
        Assert.Null(TransportChoice.PickDevice(new[] { A }, new[] { B }, true, null));
        Assert.Equal(A, TransportChoice.PickDevice(new[] { A }, new[] { B }, false, null));
        Assert.Null(TransportChoice.PickDevice(new string[0], new string[0], true, A));
    }
}
