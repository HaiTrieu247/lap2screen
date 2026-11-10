using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using MacExtend.Core.CoreDevice;
using MacExtend.Core.Wda;

namespace MacExtend.Core.Tests.Wda;

public class IpaPackageTests
{
    private static readonly byte[] MachO = { 0xCF, 0xFA, 0xED, 0xFE, 0x0C, 0x00, 0x00, 0x01, 1, 2, 3 };

    private static MemoryStream RunnerZip(params (string Name, byte[] Data)[] entries)
    {
        var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, data) in entries)
            {
                var entry = zip.CreateEntry(name);
                if (data == null) continue;
                using var s = entry.Open();
                s.Write(data, 0, data.Length);
            }
        }
        stream.Position = 0;
        return stream;
    }

    private static MemoryStream Package(MemoryStream runner)
    {
        var ipa = new MemoryStream();
        IpaPackage.FromRunnerZip(runner, ipa);
        ipa.Position = 0;
        return ipa;
    }

    [Fact]
    public void PutsTheAppUnderPayloadAndDropsMacMetadata()
    {
        var runner = RunnerZip(
            ("WebDriverAgentRunner-Runner.app/", null),
            ("WebDriverAgentRunner-Runner.app/Info.plist", Encoding.UTF8.GetBytes("<plist/>")),
            ("WebDriverAgentRunner-Runner.app/WebDriverAgentRunner-Runner", MachO),
            ("WebDriverAgentRunner-Runner.app/PlugIns/WebDriverAgentRunner.xctest/WebDriverAgentRunner", MachO),
            ("__MACOSX/WebDriverAgentRunner-Runner.app/._Info.plist", new byte[] { 1 }),
            ("WebDriverAgentRunner-Runner.app/.DS_Store", new byte[] { 1 }),
            ("WebDriverAgentRunner-Runner.app/../evil", new byte[] { 1 }));

        using var ipa = new ZipArchive(Package(runner), ZipArchiveMode.Read);

        Assert.Equal(new[]
        {
            "Payload/",
            "Payload/WebDriverAgentRunner-Runner.app/",
            "Payload/WebDriverAgentRunner-Runner.app/Info.plist",
            "Payload/WebDriverAgentRunner-Runner.app/PlugIns/WebDriverAgentRunner.xctest/WebDriverAgentRunner",
            "Payload/WebDriverAgentRunner-Runner.app/WebDriverAgentRunner-Runner",
        }, ipa.Entries.Select(e => e.FullName));
        using var reader = new StreamReader(ipa.GetEntry("Payload/WebDriverAgentRunner-Runner.app/Info.plist").Open());
        Assert.Equal("<plist/>", reader.ReadToEnd());
    }

    [Fact]
    public void ExecutablesAreUnix0755AndTheRest0644()
    {
        var runner = RunnerZip(
            ("WebDriverAgentRunner-Runner.app/Info.plist", Encoding.UTF8.GetBytes("<plist/>")),
            ("WebDriverAgentRunner-Runner.app/WebDriverAgentRunner-Runner", MachO));

        using var ipa = new ZipArchive(Package(runner), ZipArchiveMode.Read);

        Assert.Equal(0x81ED, ipa.GetEntry("Payload/WebDriverAgentRunner-Runner.app/WebDriverAgentRunner-Runner").ExternalAttributes >>> 16);
        Assert.Equal(0x81A4, ipa.GetEntry("Payload/WebDriverAgentRunner-Runner.app/Info.plist").ExternalAttributes >>> 16);
        Assert.Equal(0x41ED, ipa.GetEntry("Payload/").ExternalAttributes >>> 16);
    }

    [Fact]
    public void TheCentralDirectorySaysMadeByUnix()
    {
        var bytes = Package(RunnerZip(("A.app/x", new byte[] { 1, 2, 3 }))).ToArray();
        var central = Enumerable.Range(0, bytes.Length - 4).First(i => bytes[i] == 0x50 && bytes[i + 1] == 0x4B && bytes[i + 2] == 1 && bytes[i + 3] == 2);

        Assert.Equal(3, bytes[central + 5]);
    }

    [Fact]
    public void AFolderAboveTheAppIsDropped()
    {
        using var ipa = new ZipArchive(Package(RunnerZip(("build/Release/X.app/x", new byte[] { 9 }))), ZipArchiveMode.Read);

        Assert.Contains(ipa.Entries, e => e.FullName == "Payload/X.app/x");
    }

    [Fact]
    public void RefusesWhatIsNotARunnerZip()
    {
        Assert.Throws<InvalidDataException>(() => IpaPackage.FromRunnerZip(new MemoryStream(new byte[] { 1, 2, 3 }), new MemoryStream()));
        Assert.Throws<InvalidDataException>(() => IpaPackage.FromRunnerZip(RunnerZip(("readme.txt", new byte[] { 1 })), new MemoryStream()));
    }

    [Fact]
    public void CrcMatchesTheStandardCheckValue()
    {
        Assert.Equal(0xCBF43926u, Crc32.Compute(Encoding.ASCII.GetBytes("123456789")));
    }

    [Fact]
    public void OnlyThePinnedDownloadIsAccepted()
    {
        Assert.False(WdaPackage.IsPinned(new byte[WdaPackage.Size]));
        Assert.False(WdaPackage.IsPinned(null));
        Assert.StartsWith("https://github.com/appium/WebDriverAgent/releases/download/v16.15.3/", WdaPackage.Url);
    }
}

public class UsbmuxConnectTests
{
    private const string Header = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n";

    private static byte[] Packet(string plist, uint tag)
    {
        var payload = Encoding.UTF8.GetBytes(plist);
        var packet = new byte[16 + payload.Length];
        BitConverter.GetBytes((uint)packet.Length).CopyTo(packet, 0);
        BitConverter.GetBytes(1u).CopyTo(packet, 4);
        BitConverter.GetBytes(8u).CopyTo(packet, 8);
        BitConverter.GetBytes(tag).CopyTo(packet, 12);
        payload.CopyTo(packet, 16);
        return packet;
    }

    [Fact]
    public void ConnectSendsThePortInNetworkByteOrder()
    {
        var packet = UsbmuxProtocol.ConnectRequest(7, 3, 8100);
        var text = Encoding.UTF8.GetString(packet, 16, packet.Length - 16);

        Assert.Equal((uint)packet.Length, BitConverter.ToUInt32(packet, 0));
        Assert.Equal(7u, BitConverter.ToUInt32(packet, 12));
        Assert.Contains("<key>MessageType</key>\n\t<string>Connect</string>", text);
        Assert.Contains("<key>DeviceID</key>\n\t<integer>3</integer>", text);
        // 8100 = 0x1FA4 -> 0xA41F = 42015
        Assert.Contains("<key>PortNumber</key>\n\t<integer>42015</integer>", text);
    }

    [Fact]
    public void ReadsTheResultNumber()
    {
        var ok = Packet(Header + "<plist version=\"1.0\"><dict><key>MessageType</key><string>Result</string><key>Number</key><integer>0</integer></dict></plist>", 5);
        var refused = Packet(Header + "<plist version=\"1.0\"><dict><key>MessageType</key><string>Result</string><key>Number</key><integer>3</integer></dict></plist>", 5);

        Assert.Equal(0, UsbmuxProtocol.Result(ok, 5));
        Assert.Equal(3, UsbmuxProtocol.Result(refused, 5));
        Assert.Null(UsbmuxProtocol.Result(ok, 6));
    }

    [Fact]
    public void ListsDeviceIdsOfUsbDevicesOnly()
    {
        var listing = Header + "<plist version=\"1.0\"><dict><key>DeviceList</key><array>"
                      + "<dict><key>DeviceID</key><integer>4</integer><key>MessageType</key><string>Attached</string><key>Properties</key><dict>"
                      + "<key>ConnectionType</key><string>USB</string><key>DeviceID</key><integer>4</integer><key>SerialNumber</key><string>00008030-001E185E343B802E</string></dict></dict>"
                      + "<dict><key>DeviceID</key><integer>9</integer><key>MessageType</key><string>Attached</string><key>Properties</key><dict>"
                      + "<key>ConnectionType</key><string>Network</string><key>DeviceID</key><integer>9</integer><key>SerialNumber</key><string>00008110-001A2B3C4D5E6F70</string></dict></dict>"
                      + "</array></dict></plist>";

        var ids = UsbmuxProtocol.UsbDeviceIds(Packet(listing, 2), 2);

        var only = Assert.Single(ids);
        Assert.Equal("00008030-001E185E343B802E", only.Key);
        Assert.Equal(4, only.Value);
    }
}
