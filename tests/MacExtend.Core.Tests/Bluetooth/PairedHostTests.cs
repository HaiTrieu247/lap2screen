using MacExtend.Core.Bluetooth;

namespace MacExtend.Core.Tests.Bluetooth;

public class PairedHostTests
{
    [Fact]
    public void HostShowsItsAddressWhenItHasNoName()
    {
        Assert.Equal("iPad", new PairedHost("x-7c:04:d0:aa:bb:cc", "iPad").Display);
        Assert.Equal("7c:04:d0:aa:bb:cc", new PairedHost("BluetoothLE#BluetoothLEe4:aa-7c:04:d0:aa:bb:cc", "").Display);
    }
}
