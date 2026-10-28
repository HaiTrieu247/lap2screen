using System;
using MacExtend.Core.Bluetooth;

namespace MacExtend.Core.Tests.Bluetooth;

public class BluetoothStatusTests
{
    [Fact]
    public void SwitchedOffWinsOverEverything()
    {
        Assert.Equal(BluetoothState.Off, BluetoothStatus.Derive(false, true, PeripheralState.RadioOff, 1, "iPhone", true));
    }

    [Theory]
    [InlineData(PeripheralState.NoAdapter, BluetoothState.NoAdapter)]
    [InlineData(PeripheralState.NoPeripheral, BluetoothState.NoPeripheral)]
    [InlineData(PeripheralState.RadioOff, BluetoothState.RadioOff)]
    [InlineData(PeripheralState.Blocked, BluetoothState.Blocked)]
    [InlineData(PeripheralState.Failed, BluetoothState.Failed)]
    public void ProblemsShowEvenWhileNotMirroring(PeripheralState peripheral, BluetoothState expected)
    {
        Assert.Equal(expected, BluetoothStatus.Derive(true, false, peripheral, 0, null, false));
    }

    [Fact]
    public void WaitsForMirroringWhileNotRunning()
    {
        Assert.Equal(BluetoothState.WaitingForMirroring, BluetoothStatus.Derive(true, false, PeripheralState.Idle, 0, null, false));
    }

    [Fact]
    public void StartingWhileTheServiceComesUp()
    {
        Assert.Equal(BluetoothState.Starting, BluetoothStatus.Derive(true, true, PeripheralState.Starting, 0, null, false));
    }

    [Theory]
    [InlineData(0, null, BluetoothState.WaitingForDevice)]
    [InlineData(2, null, BluetoothState.ChooseDevice)]
    [InlineData(1, "iPhone", BluetoothState.Connected)]
    public void ReadyStatesFollowTheConnectedHosts(int connected, string target, BluetoothState expected)
    {
        Assert.Equal(expected, BluetoothStatus.Derive(true, true, PeripheralState.Ready, connected, target, false));
    }

    [Fact]
    public void ControllingWhileInputGoesToTheDevice()
    {
        Assert.Equal(BluetoothState.Controlling, BluetoothStatus.Derive(true, true, PeripheralState.Ready, 1, "iPhone", true));
    }

    [Fact]
    public void TextsNameTheDeviceAndTheFailure()
    {
        Assert.Contains("iPhone của Thịnh", BluetoothStatus.Describe(BluetoothState.Connected, "iPhone của Thịnh", null));
        Assert.Contains("iPhone của Thịnh", BluetoothStatus.Describe(BluetoothState.Controlling, "iPhone của Thịnh", null));
        Assert.Contains("ResourceInUse", BluetoothStatus.Describe(BluetoothState.Failed, null, "ResourceInUse"));
    }

    [Fact]
    public void EveryStateHasAText()
    {
        foreach (BluetoothState state in Enum.GetValues(typeof(BluetoothState)))
        {
            Assert.False(string.IsNullOrWhiteSpace(BluetoothStatus.Describe(state, "iPad", "OtherError")), state.ToString());
        }
    }
}
