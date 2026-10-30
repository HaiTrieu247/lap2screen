using MacExtend.Core.CoreDevice;

namespace MacExtend.Core.Tests.CoreDevice;

public class TunneldLogTests
{
    private const string Server = "2026-10-30 12:50:11 LEGION pymobiledevice3.tunneld.server[7560] ";

    [Theory]
    [InlineData("INFO:     Uvicorn running on http://127.0.0.1:49151 (Press CTRL+C to quit)", TunneldLogKind.Listening)]
    [InlineData(Server + "INFO [start-tunnel-task-wifi-192.168.2.4] Created tunnel --rsd fd7b::1 60123", TunneldLogKind.TunnelCreated)]
    [InlineData(Server + "INFO Disconnected from tunnel --rsd fd7b::1 60123", TunneldLogKind.TunnelClosed)]
    [InlineData(Server + "WARNING [start-tunnel-task-wifi-192.168.2.4] QuicProtocolNotSupportedError: iOS 18.2+ removed QUIC tunnel support.", TunneldLogKind.QuicRefused)]
    [InlineData(Server + "ERROR Got exception from start-tunnel-task-wifi-192.168.2.4", TunneldLogKind.Error)]
    [InlineData(Server + "DEBUG Skipping remote pairing service x@fe80::1%eth:49152: gaierror(11001)", TunneldLogKind.Other)]
    [InlineData("Traceback (most recent call last):", TunneldLogKind.Other)]
    public void ClassifiesTunneldLines(string line, TunneldLogKind expected)
    {
        Assert.Equal(expected, TunneldLog.Parse(line));
    }

    [Fact]
    public void DescriptionsNeverCarryTheLine()
    {
        foreach (TunneldLogKind kind in System.Enum.GetValues(typeof(TunneldLogKind)))
        {
            var text = TunneldLog.Describe(kind);
            Assert.False(string.IsNullOrWhiteSpace(text));
            Assert.DoesNotContain("fd7b", text);
        }
    }
}
