using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MacExtend.Core.Wda;

namespace MacExtend.Core.Tests.Wda;

/// <summary>
/// A WebDriverAgent stand-in on a loopback port: a tiny HTTP/1.1 server with keep-alive that records every request and
/// answers the way WDA v16 does. A test can make it forget the session or stop answering.
/// </summary>
internal sealed class FakeWda : IDisposable
{
    private readonly TcpListener _listener = new TcpListener(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new CancellationTokenSource();
    private int _sessions;

    public FakeWda()
    {
        _listener.Start();
        _ = AcceptAsync();
    }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;
    public Uri Base => new Uri("http://127.0.0.1:" + Port);
    public ConcurrentQueue<(string Method, string Path, string Body)> Requests { get; } = new();
    public int Connections;
    public volatile bool ForgetSessions;
    public volatile bool Silent;
    public volatile int Width = 390;
    public volatile int Height = 844;
    /// <summary>Paths answered with HTTP 500 and this W3C error.</summary>
    public ConcurrentDictionary<string, string> Errors { get; } = new();
    public string CurrentSession => "S" + Volatile.Read(ref _sessions);

    public IEnumerable<string> Paths(string method) => Requests.Where(r => r.Method == method).Select(r => r.Path);

    public bool WaitFor(Func<FakeWda, bool> condition, int ms = 5000)
    {
        var until = DateTime.UtcNow.AddMilliseconds(ms);
        while (DateTime.UtcNow < until)
        {
            if (condition(this)) return true;
            Thread.Sleep(10);
        }
        return condition(this);
    }

    public void Dispose()
    {
        _stop.Cancel();
        _listener.Stop();
    }

    private async Task AcceptAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync();
            }
            catch (Exception)
            {
                return;
            }
            Interlocked.Increment(ref Connections);
            _ = ServeAsync(client);
        }
    }

    private async Task ServeAsync(TcpClient client)
    {
        using (client)
        {
            var stream = client.GetStream();
            var reader = new StreamReader(stream, Encoding.ASCII);
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync();
                    if (string.IsNullOrEmpty(line)) return;
                    var parts = line.Split(' ');
                    var length = 0;
                    string header;
                    while (!string.IsNullOrEmpty(header = await reader.ReadLineAsync()))
                    {
                        if (header.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)) length = int.Parse(header.Substring(15).Trim());
                    }
                    var body = new char[length];
                    var read = 0;
                    while (read < length) read += await reader.ReadAsync(body, read, length - read);
                    Requests.Enqueue((parts[0], parts[1], new string(body)));
                    if (Silent) return;
                    var (status, json) = Answer(parts[0], parts[1]);
                    var payload = Encoding.UTF8.GetBytes(json);
                    var head = Encoding.ASCII.GetBytes("HTTP/1.1 " + status + " X\r\nContent-Type: application/json\r\nContent-Length: " + payload.Length + "\r\n\r\n");
                    await stream.WriteAsync(head, 0, head.Length);
                    await stream.WriteAsync(payload, 0, payload.Length);
                }
            }
            catch (Exception)
            {
                // the client went away
            }
        }
    }

    private (int, string) Answer(string method, string path)
    {
        if (path == "/status") return (200, "{\"value\":{\"ready\":true,\"ios\":{\"ip\":\"192.168.2.5\"}},\"sessionId\":null}");
        if (method == "POST" && path == "/session")
        {
            var id = "S" + Interlocked.Increment(ref _sessions);
            return (200, "{\"value\":{\"sessionId\":\"" + id + "\",\"capabilities\":{}},\"sessionId\":\"" + id + "\"}");
        }
        if (path == "/wda/homescreen") return (200, "{\"value\":null}");
        if (path.StartsWith("/session/", StringComparison.Ordinal))
        {
            var rest = path.Substring(9);
            var slash = rest.IndexOf('/');
            var id = slash < 0 ? rest : rest.Substring(0, slash);
            var sub = slash < 0 ? "" : rest.Substring(slash);
            if (ForgetSessions || id != CurrentSession)
            {
                ForgetSessions = false;
                return (404, "{\"value\":{\"error\":\"invalid session id\",\"message\":\"Session does not exist\"}}");
            }
            if (Errors.TryGetValue(sub, out var error)) return (500, "{\"value\":{\"error\":\"" + error + "\",\"message\":\"x\"}}");
            if (sub == "/window/size") return (200, "{\"value\":{\"width\":" + Width + ",\"height\":" + Height + "}}");
            return (200, "{\"value\":null,\"sessionId\":\"" + id + "\"}");
        }
        return (404, "{\"value\":{\"error\":\"unknown command\"}}");
    }
}

public class WdaClientTests
{
    [Fact]
    public async Task StartsASessionAndReadsTheSize()
    {
        using var wda = new FakeWda();
        using var client = new WdaClient(wda.Base);

        Assert.True(await client.IsReadyAsync(TimeSpan.FromSeconds(2), CancellationToken.None));
        await client.StartSessionAsync(CancellationToken.None);
        var size = await client.WindowSizeAsync(CancellationToken.None);

        Assert.Equal("S1", client.SessionId);
        Assert.Equal(new DeviceSize(390, 844), size);
        Assert.Contains("/session/S1/appium/settings", wda.Paths("POST"));
    }

    [Fact]
    public async Task AForgottenSessionIsStartedAgainOnce()
    {
        using var wda = new FakeWda();
        using var client = new WdaClient(wda.Base);
        await client.StartSessionAsync(CancellationToken.None);
        wda.ForgetSessions = true;

        await client.TouchAsync(WdaGestures.Tap(10, 10), CancellationToken.None);

        Assert.Equal("S2", client.SessionId);
        Assert.Contains("/session/S2/actions", wda.Paths("POST"));
    }

    [Fact]
    public async Task KeepsOneConnectionAlive()
    {
        using var wda = new FakeWda();
        using var client = new WdaClient(wda.Base);
        await client.StartSessionAsync(CancellationToken.None);
        for (var i = 0; i < 5; i++) await client.TouchAsync(WdaGestures.Tap(i, i), CancellationToken.None);

        Assert.Equal(1, wda.Connections);
    }

    [Fact]
    public async Task AnErrorAnswerCarriesItsStatusAndCode()
    {
        using var wda = new FakeWda();
        using var client = new WdaClient(wda.Base);
        await client.StartSessionAsync(CancellationToken.None);
        wda.Errors["/wda/keys"] = "invalid element state";

        var e = await Assert.ThrowsAsync<WdaException>(() => client.TypeAsync("x", CancellationToken.None));

        Assert.Equal(500, e.Status);
        Assert.Equal("invalid element state", e.Error);
        Assert.False(e.NoAnswer);
    }

    [Fact]
    public async Task NoAnswerIsReportedAsSuch()
    {
        using var wda = new FakeWda();
        using var client = new WdaClient(wda.Base);
        wda.Silent = true;

        Assert.False(await client.IsReadyAsync(TimeSpan.FromSeconds(1), CancellationToken.None));
        var e = await Assert.ThrowsAsync<WdaException>(() => client.StartSessionAsync(CancellationToken.None));
        Assert.True(e.NoAnswer);
    }

    [Fact]
    public void RefusesHttps()
    {
        Assert.Throws<ArgumentException>(() => new WdaClient(new Uri("https://127.0.0.1:8100")));
    }
}

public class WdaDriverTests
{
    private static async Task<(FakeWda, WdaClient, WdaDriver)> Start()
    {
        var wda = new FakeWda();
        var client = new WdaClient(wda.Base);
        var driver = new WdaDriver(client, () => DateTime.UtcNow);
        await driver.StartAsync(CancellationToken.None);
        return (wda, client, driver);
    }

    [Fact]
    public async Task ATapLandsInPointsOfTheScreen()
    {
        var (wda, client, driver) = await Start();
        using (wda) using (client) using (driver)
        {
            driver.Tap(0.5, 0.25);

            Assert.True(wda.WaitFor(w => w.Paths("POST").Contains("/session/S1/actions")));
            var body = wda.Requests.Last(r => r.Path == "/session/S1/actions").Body;
            Assert.Contains("\"x\":195,\"y\":211", body);
        }
    }

    [Fact]
    public async Task TypingThatPilesUpIsSentTogether()
    {
        var (wda, client, driver) = await Start();
        using (wda) using (client) using (driver)
        {
            wda.Errors["/actions"] = "x";
            driver.LongPress(0.5, 0.5);
            driver.Type("Th");
            driver.Type("ị");
            driver.Type("nh\r\n");

            Assert.True(wda.WaitFor(w => w.Paths("POST").Any(p => p.EndsWith("/wda/keys"))));
            Thread.Sleep(200);
            var keys = wda.Requests.Where(r => r.Path.EndsWith("/wda/keys")).Select(r => r.Body).ToList();
            Assert.Contains("[\"T\",\"h\",\"\\u1ecb\",\"n\",\"h\",\"\\n\"]", string.Join("|", keys));
        }
    }

    [Fact]
    public async Task ARefusedCommandIsSkippedAndTheNextOneRuns()
    {
        var (wda, client, driver) = await Start();
        using (wda) using (client) using (driver)
        {
            var lost = false;
            driver.Lost += _ => lost = true;
            wda.Errors["/wda/keys"] = "invalid element state";
            driver.Type("x");
            driver.Home();

            Assert.True(wda.WaitFor(w => w.Paths("POST").Contains("/wda/homescreen")));
            Assert.False(lost);
        }
    }

    [Fact]
    public async Task NoAnswerAndNoStatusIsLost()
    {
        var (wda, client, driver) = await Start();
        using (wda) using (client) using (driver)
        {
            var lost = new ManualResetEventSlim();
            WdaFailure failure = WdaFailure.None;
            driver.Lost += f =>
            {
                failure = f;
                lost.Set();
            };
            wda.Silent = true;
            driver.Tap(0.1, 0.1);

            Assert.True(lost.Wait(TimeSpan.FromSeconds(30)));
            Assert.Equal(WdaFailure.Lost, failure);
        }
    }

    [Fact]
    public async Task RotationIsNoticedWhileIdle()
    {
        var (wda, client, driver) = await Start();
        using (wda) using (client) using (driver)
        {
            var changed = new ManualResetEventSlim();
            driver.SizeChanged += changed.Set;
            wda.Width = 844;
            wda.Height = 390;

            Assert.True(changed.Wait(TimeSpan.FromSeconds(5)));
            Assert.True(driver.Size.IsLandscape);
        }
    }

    [Fact]
    public async Task AppSwitcherOnAFaceIdPhoneIsADragUpThatHolds()
    {
        var (wda, client, driver) = await Start();
        using (wda) using (client) using (driver)
        {
            driver.AppSwitcher();

            Assert.True(wda.WaitFor(w => w.Paths("POST").Contains("/session/S1/actions")));
            var body = wda.Requests.Last(r => r.Path == "/session/S1/actions").Body;
            Assert.Contains("\"y\":842", body);
            Assert.Contains("{\"type\":\"pause\",\"duration\":450}", body);
        }
    }

    [Fact]
    public async Task AppSwitcherWithAHomeButtonPressesHomeTwice()
    {
        var wda = new FakeWda { Width = 375, Height = 667 };
        using var client = new WdaClient(wda.Base);
        using var driver = new WdaDriver(client, () => DateTime.UtcNow);
        using (wda)
        {
            await driver.StartAsync(CancellationToken.None);
            driver.AppSwitcher();

            Assert.True(wda.WaitFor(w => w.Paths("POST").Count(p => p.EndsWith("/wda/performIoHidEvent")) == 2));
        }
    }

    [Fact]
    public async Task VolumeAndPasteAreCapped()
    {
        var (wda, client, driver) = await Start();
        using (wda) using (client) using (driver)
        {
            driver.Volume(true);
            driver.Paste(new string('a', 5000));

            Assert.True(wda.WaitFor(w => w.Paths("POST").Any(p => p.EndsWith("/wda/keys"))));
            Assert.Contains(wda.Requests, r => r.Path.EndsWith("/wda/pressButton") && r.Body == "{\"name\":\"volumeUp\"}");
            var typed = wda.Requests.Where(r => r.Path.EndsWith("/wda/keys")).Sum(r => r.Body.Split("\"a\"").Length - 1);
            Assert.Equal(WdaDriver.MaxClipboardText, typed);
        }
    }
}

/// <summary>usbmuxd's side of a Connect: answers ListDevices with one USB device, then Result 0, then hands the socket to WDA.</summary>
internal sealed class FakeUsbmuxd : IDisposable
{
    public const string Udid = "00008030-001E185E343B802E";
    private readonly TcpListener _listener = new TcpListener(IPAddress.Loopback, 0);
    private readonly int _wdaPort;

    public FakeUsbmuxd(int wdaPort)
    {
        _wdaPort = wdaPort;
        _listener.Start();
        _ = AcceptAsync();
    }

    public IPEndPoint EndPoint => (IPEndPoint)_listener.LocalEndpoint;
    public volatile bool Refuse;
    public volatile bool NoDevice;
    public ConcurrentQueue<string> Seen { get; } = new();

    public void Dispose() => _listener.Stop();

    private async Task AcceptAsync()
    {
        while (true)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync();
            }
            catch (Exception)
            {
                return;
            }
            _ = ServeAsync(client);
        }
    }

    private async Task ServeAsync(TcpClient client)
    {
        try
        {
            var stream = client.GetStream();
            var header = new byte[16];
            await ReadExactly(stream, header);
            var size = BitConverter.ToInt32(header, 0);
            var tag = BitConverter.ToUInt32(header, 12);
            var body = new byte[size - 16];
            await ReadExactly(stream, body);
            var text = Encoding.UTF8.GetString(body);
            if (text.Contains("<string>ListDevices</string>"))
            {
                Seen.Enqueue("list");
                var device = NoDevice ? "" : "<dict><key>MessageType</key><string>Attached</string><key>Properties</key><dict><key>ConnectionType</key><string>USB</string>"
                                             + "<key>DeviceID</key><integer>4</integer><key>SerialNumber</key><string>" + Udid + "</string></dict></dict>";
                await Reply(stream, "<plist version=\"1.0\"><dict><key>DeviceList</key><array>" + device + "</array></dict></plist>", tag);
                client.Dispose();
                return;
            }
            Seen.Enqueue(text.Contains("<integer>4</integer>") && text.Contains("<integer>42015</integer>") ? "connect 4:8100" : "connect ?");
            await Reply(stream, "<plist version=\"1.0\"><dict><key>MessageType</key><string>Result</string><key>Number</key><integer>" + (Refuse ? 3 : 0) + "</integer></dict></plist>", tag);
            if (Refuse)
            {
                client.Dispose();
                return;
            }
            var wda = new TcpClient();
            await wda.ConnectAsync(IPAddress.Loopback, _wdaPort);
            var a = Pump(stream, wda.GetStream());
            var b = Pump(wda.GetStream(), stream);
            await Task.WhenAny(a, b);
            wda.Dispose();
            client.Dispose();
        }
        catch (Exception)
        {
            client.Dispose();
        }
    }

    private static async Task Pump(Stream from, Stream to)
    {
        var buffer = new byte[8192];
        try
        {
            int n;
            while ((n = await from.ReadAsync(buffer, 0, buffer.Length)) > 0) await to.WriteAsync(buffer, 0, n);
        }
        catch (Exception)
        {
            // closed
        }
    }

    private static async Task Reply(Stream stream, string plist, uint tag)
    {
        var payload = Encoding.UTF8.GetBytes("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" + plist);
        var packet = new byte[16 + payload.Length];
        BitConverter.GetBytes((uint)packet.Length).CopyTo(packet, 0);
        BitConverter.GetBytes(1u).CopyTo(packet, 4);
        BitConverter.GetBytes(8u).CopyTo(packet, 8);
        BitConverter.GetBytes(tag).CopyTo(packet, 12);
        payload.CopyTo(packet, 16);
        await stream.WriteAsync(packet, 0, packet.Length);
    }

    private static async Task ReadExactly(Stream stream, byte[] buffer)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var n = await stream.ReadAsync(buffer, read, buffer.Length - read);
            if (n == 0) throw new EndOfStreamException();
            read += n;
        }
    }
}

public class UsbmuxForwarderTests
{
    [Fact]
    public async Task WdaIsReachedThroughTheForward()
    {
        using var wda = new FakeWda();
        using var usbmuxd = new FakeUsbmuxd(wda.Port);
        using var forwarder = new UsbmuxForwarder(usbmuxd.EndPoint, FakeUsbmuxd.Udid, WdaClient.DevicePort);
        forwarder.Start();
        using var client = new WdaClient(new Uri("http://127.0.0.1:" + forwarder.LocalPort));

        await client.StartSessionAsync(CancellationToken.None);
        await client.TouchAsync(WdaGestures.Tap(5, 5), CancellationToken.None);

        Assert.Contains("/session/S1/actions", wda.Paths("POST"));
        Assert.Contains("connect 4:8100", usbmuxd.Seen);
        Assert.Equal(1, wda.Connections);
    }

    [Fact]
    public async Task ARefusedConnectClosesTheLocalConnection()
    {
        using var wda = new FakeWda();
        using var usbmuxd = new FakeUsbmuxd(wda.Port) { Refuse = true };
        using var forwarder = new UsbmuxForwarder(usbmuxd.EndPoint, FakeUsbmuxd.Udid, WdaClient.DevicePort);
        forwarder.Start();
        using var client = new WdaClient(new Uri("http://127.0.0.1:" + forwarder.LocalPort));

        Assert.False(await client.IsReadyAsync(TimeSpan.FromSeconds(3), CancellationToken.None));
        Assert.True(forwarder.Failures >= 1);
    }

    [Fact]
    public async Task ADeviceOffTheCableIsNotConnected()
    {
        using var wda = new FakeWda();
        using var usbmuxd = new FakeUsbmuxd(wda.Port) { NoDevice = true };
        using var forwarder = new UsbmuxForwarder(usbmuxd.EndPoint, FakeUsbmuxd.Udid, WdaClient.DevicePort);
        var lines = new ConcurrentQueue<string>();
        forwarder.Log += lines.Enqueue;

        Assert.Null(await forwarder.OpenAsync(CancellationToken.None));
        Assert.DoesNotContain("connect 4:8100", usbmuxd.Seen);
        Assert.Contains(lines, l => l.Contains("not on the cable"));
        Assert.DoesNotContain(lines, l => l.Contains(FakeUsbmuxd.Udid));
    }

    [Fact]
    public void RefusesAnythingButAUdid()
    {
        Assert.Throws<ArgumentException>(() => new UsbmuxForwarder(UsbmuxForwarder.AppleService, "x", 8100));
    }
}
