using System.Linq;
using MacExtend.Core.Bluetooth;

namespace MacExtend.Core.Tests.Bluetooth;

public class HidReportsTests
{
    [Fact]
    public void KeyboardReportIsModifiersReservedThenSixKeys()
    {
        var report = HidReports.Keyboard(KeyModifiers.LeftShift | KeyModifiers.LeftGui, new byte[] { 0x04, 0x05 });

        Assert.Equal(new byte[] { 0x0A, 0x00, 0x04, 0x05, 0, 0, 0, 0 }, report);
    }

    [Fact]
    public void KeyboardReportKeepsTheFirstSixKeys()
    {
        var report = HidReports.Keyboard(KeyModifiers.None, new byte[] { 1, 2, 3, 4, 5, 6, 7 });

        Assert.Equal(new byte[] { 0, 0, 1, 2, 3, 4, 5, 6 }, report);
    }

    [Fact]
    public void KeyboardReleaseIsAllZero()
    {
        Assert.Equal(new byte[HidReports.KeyboardReportLength], HidReports.KeyboardRelease());
    }

    [Fact]
    public void MouseReportIsLittleEndianSigned()
    {
        var report = HidReports.Mouse(MouseButtons.Left | MouseButtons.Middle, 1, -1, -1);

        Assert.Equal(new byte[] { 0x05, 0x01, 0x00, 0xFF, 0xFF, 0xFF }, report);
    }

    [Fact]
    public void MouseReportClampsToTheDescriptorRange()
    {
        Assert.Equal(new byte[] { 0, 0xFF, 0x7F, 0x01, 0x80, 0x7F }, HidReports.Mouse(MouseButtons.None, 40000, -40000, 300));
        Assert.Equal(0x81, HidReports.Mouse(MouseButtons.None, 0, 0, -300)[5]);
    }

    [Fact]
    public void SixteenBitUuidsUseTheBluetoothBase()
    {
        Assert.Equal(new System.Guid("00001812-0000-1000-8000-00805f9b34fb"), HidDescriptors.HidService);
        Assert.Equal(new System.Guid("00002a4d-0000-1000-8000-00805f9b34fb"), HidDescriptors.Report);
    }

    [Fact]
    public void ReportMapDeclaresKeyboardAndMouseInBalancedCollections()
    {
        var map = HidDescriptors.ReportMapValue;

        Assert.Equal(3, map.Count(b => b == 0xA1));
        Assert.Equal(3, map.Count(b => b == 0xC0));
        Assert.Equal(6, IndexOfPair(map, 0x85, HidDescriptors.KeyboardReportId));
        Assert.True(IndexOfPair(map, 0x85, HidDescriptors.MouseReportId) > 6);
    }

    private static int IndexOfPair(byte[] data, byte a, byte b)
    {
        for (var i = 0; i + 1 < data.Length; i++)
        {
            if (data[i] == a && data[i + 1] == b) return i;
        }
        return -1;
    }
}
