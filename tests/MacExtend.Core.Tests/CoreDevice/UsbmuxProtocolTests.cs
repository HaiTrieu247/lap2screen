using System;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using MacExtend.Core.CoreDevice;

namespace MacExtend.Core.Tests.CoreDevice;

public class UsbmuxProtocolTests
{
    private const string Serial = "00008110001A2B3C4D5E6F70";
    private const string Header = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n"
                                + "<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">\n";

    private static string Entry(string connectionType, string serial, int id = 3) =>
        "<dict><key>DeviceID</key><integer>" + id + "</integer><key>MessageType</key><string>Attached</string>"
        + "<key>Properties</key><dict><key>ConnectionSpeed</key><integer>480000000</integer>"
        + "<key>ConnectionType</key><string>" + connectionType + "</string><key>DeviceID</key><integer>" + id + "</integer>"
        + "<key>LocationID</key><integer>0</integer><key>ProductID</key><integer>4776</integer>"
        + "<key>SerialNumber</key><string>" + serial + "</string></dict></dict>";

    private static string NetworkEntry(string serial, int id = 9) =>
        "<dict><key>DeviceID</key><integer>" + id + "</integer><key>MessageType</key><string>Attached</string>"
        + "<key>Properties</key><dict><key>ConnectionType</key><string>Network</string><key>DeviceID</key><integer>" + id + "</integer>"
        + "<key>EscapedFullServiceName</key><string>aa:bb:cc:dd:ee:ff@fe80::1._apple-mobdev2._tcp.local.</string>"
        + "<key>InterfaceIndex</key><integer>12</integer><key>NetworkAddress</key><data>HgIAAAAAAAD+gAAAAAAAAAAAAAAAAAABDAAAAA==</data>"
        + "<key>SerialNumber</key><string>" + serial + "</string></dict></dict>";

    private static string Listing(params string[] entries) =>
        Header + "<plist version=\"1.0\">\n<dict>\n\t<key>DeviceList</key>\n\t<array>" + string.Concat(entries) + "</array>\n</dict>\n</plist>\n";

    private static byte[] Packet(string xml, uint tag = 7, uint version = 1, uint type = 8, int? declared = null) =>
        Packet(Encoding.UTF8.GetBytes(xml), tag, version, type, declared);

    private static byte[] Packet(byte[] payload, uint tag = 7, uint version = 1, uint type = 8, int? declared = null)
    {
        var packet = new byte[16 + payload.Length];
        BitConverter.GetBytes((uint)(declared ?? packet.Length)).CopyTo(packet, 0);
        BitConverter.GetBytes(version).CopyTo(packet, 4);
        BitConverter.GetBytes(type).CopyTo(packet, 8);
        BitConverter.GetBytes(tag).CopyTo(packet, 12);
        payload.CopyTo(packet, 16);
        return packet;
    }

    [Fact]
    public void TheRequestIsOnePlistListDevicesPacket()
    {
        var request = UsbmuxProtocol.ListDevicesRequest(42);

        Assert.Equal((uint)request.Length, BitConverter.ToUInt32(request, 0));
        Assert.Equal(1u, BitConverter.ToUInt32(request, 4));
        Assert.Equal(8u, BitConverter.ToUInt32(request, 8));
        Assert.Equal(42u, BitConverter.ToUInt32(request, 12));

        var dict = XDocument.Parse(Encoding.UTF8.GetString(request, 16, request.Length - 16)).Root.Element("dict");
        var pairs = dict.Elements("key").ToDictionary(k => k.Value, k => (XElement)k.NextNode);
        Assert.Equal("ListDevices", pairs["MessageType"].Value);
        Assert.Equal("MacExtend", pairs["ClientVersionString"].Value);
        Assert.Equal("MacExtend", pairs["ProgName"].Value);
        Assert.Equal("integer", pairs["kLibUSBMuxVersion"].Name.LocalName);
        Assert.Equal("3", pairs["kLibUSBMuxVersion"].Value);
    }

    [Fact]
    public void ADeviceOnTheCableIsListed()
    {
        var devices = UsbmuxProtocol.UsbDevices(Packet(Listing(Entry("USB", Serial))), 7);

        Assert.Equal(new[] { Serial }, devices);
    }

    [Fact]
    public void ADeviceOnlyOnTheNetworkIsNotOnTheCable()
    {
        var devices = UsbmuxProtocol.UsbDevices(Packet(Listing(NetworkEntry(Serial))), 7);

        Assert.NotNull(devices);
        Assert.Empty(devices);
    }

    [Fact]
    public void ADeviceOnBothIsOnTheCable()
    {
        var devices = UsbmuxProtocol.UsbDevices(Packet(Listing(NetworkEntry(Serial), Entry("USB", Serial), Entry("USB", "00008030000102030405060A", 4))), 7);

        Assert.Equal(new[] { Serial, "00008030000102030405060A" }, devices);
    }

    [Fact]
    public void AnEmptyListIsAnAnswer()
    {
        var devices = UsbmuxProtocol.UsbDevices(Packet(Header + "<plist version=\"1.0\"><dict><key>DeviceList</key><array/></dict></plist>"), 7);

        Assert.NotNull(devices);
        Assert.Empty(devices);
    }

    [Fact]
    public void ATrailingNulIsTolerated()
    {
        var payload = Encoding.UTF8.GetBytes(Listing(Entry("USB", Serial))).Concat(new byte[] { 0 }).ToArray();

        Assert.Equal(new[] { Serial }, UsbmuxProtocol.UsbDevices(Packet(payload), 7));
    }

    [Fact]
    public void EntriesWithoutACableSerialAreSkipped()
    {
        var noSerial = "<dict><key>MessageType</key><string>Attached</string><key>Properties</key><dict><key>ConnectionType</key><string>USB</string></dict></dict>";
        var detached = "<dict><key>MessageType</key><string>Detached</string><key>Properties</key><dict><key>ConnectionType</key><string>USB</string>"
                     + "<key>SerialNumber</key><string>00008030000102030405060A</string></dict></dict>";

        Assert.Equal(new[] { Serial }, UsbmuxProtocol.UsbDevices(Packet(Listing(noSerial, detached, Entry("USB", Serial))), 7));
    }

    [Fact]
    public void AReplyToAnotherRequestIsUnknown()
    {
        Assert.Null(UsbmuxProtocol.UsbDevices(Packet(Listing(Entry("USB", Serial)), tag: 8), 7));
    }

    [Fact]
    public void ABinaryProtocolOrNonPlistReplyIsUnknown()
    {
        Assert.Null(UsbmuxProtocol.UsbDevices(Packet(Listing(Entry("USB", Serial)), version: 0), 7));
        Assert.Null(UsbmuxProtocol.UsbDevices(Packet(Listing(Entry("USB", Serial)), type: 1), 7));
    }

    [Fact]
    public void ALengthThatDoesNotMatchIsUnknown()
    {
        var xml = Listing(Entry("USB", Serial));
        var length = 16 + Encoding.UTF8.GetByteCount(xml);

        Assert.Null(UsbmuxProtocol.UsbDevices(Packet(xml, declared: length + 1), 7));
        Assert.Null(UsbmuxProtocol.UsbDevices(Packet(xml, declared: length - 1), 7));
        Assert.Null(UsbmuxProtocol.UsbDevices(new byte[10], 7));
        Assert.Null(UsbmuxProtocol.UsbDevices(null, 7));
    }

    [Fact]
    public void AnOversizedReplyIsUnknown()
    {
        var filler = "<dict><key>Pad</key><string>" + new string('x', UsbmuxProtocol.MaxReplySize) + "</string></dict>";

        Assert.Null(UsbmuxProtocol.UsbDevices(Packet(Listing(Entry("USB", Serial), filler)), 7));
    }

    [Theory]
    [InlineData("bplist00\u0001\u0002")]
    [InlineData("not xml at all")]
    [InlineData("<plist version=\"1.0\"><dict><key>DeviceList</key><array><dict></array></dict></plist>")]
    [InlineData("<plist version=\"1.0\"><dict><key>MessageType</key><string>Result</string><key>Number</key><integer>1</integer></dict></plist>")]
    [InlineData("<plist version=\"1.0\"><dict><key>DeviceList</key><dict/></dict></plist>")]
    [InlineData("<plist version=\"1.0\"><dict><key>DeviceList</key><array><string>00008110001A2B3C4D5E6F70</string></array></dict></plist>")]
    [InlineData("<plist version=\"1.0\"><dict><key>DeviceList</key></dict></plist>")]
    [InlineData("<html><dict><key>DeviceList</key><array/></dict></html>")]
    public void AMalformedReplyIsUnknown(string payload)
    {
        Assert.Null(UsbmuxProtocol.UsbDevices(Packet(payload), 7));
    }

    [Fact]
    public void BytesThatAreNotUtf8AreUnknown()
    {
        var payload = Encoding.UTF8.GetBytes(Listing(Entry("USB", "SERIAL")));
        var at = Array.IndexOf(payload, (byte)'S', Encoding.UTF8.GetByteCount(Header));
        payload[at] = 0xFF;

        Assert.Null(UsbmuxProtocol.UsbDevices(Packet(payload), 7));
    }

    [Fact]
    public void DocumentTypeDeclarationsAreNotProcessed()
    {
        var xml = "<?xml version=\"1.0\"?><!DOCTYPE plist [<!ENTITY s \"" + Serial + "\">]>"
                + "<plist version=\"1.0\"><dict><key>DeviceList</key><array>" + Entry("USB", "&s;") + "</array></dict></plist>";

        Assert.Null(UsbmuxProtocol.UsbDevices(Packet(xml), 7));
    }

    [Fact]
    public void TheHeaderSaysHowMuchToRead()
    {
        var packet = Packet(Listing(Entry("USB", Serial)));

        Assert.Equal(packet.Length, UsbmuxProtocol.DeclaredSize(packet.Take(16).ToArray()));
        Assert.Equal(-1, UsbmuxProtocol.DeclaredSize(Packet(new byte[0], declared: 15).Take(16).ToArray()));
        Assert.Equal(-1, UsbmuxProtocol.DeclaredSize(Packet(new byte[0], declared: UsbmuxProtocol.MaxReplySize + 1).Take(16).ToArray()));
        Assert.Equal(-1, UsbmuxProtocol.DeclaredSize(new byte[3]));
        Assert.Equal(-1, UsbmuxProtocol.DeclaredSize(null));
    }
}
