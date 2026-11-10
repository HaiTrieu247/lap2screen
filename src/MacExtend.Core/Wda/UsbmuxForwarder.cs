using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using MacExtend.Core.CoreDevice;

namespace MacExtend.Core.Wda
{
    /// <summary>
    /// A loopback TCP port that leads to a TCP port on a device on the cable, through the Apple Mobile Device Service
    /// (usbmuxd): every connection accepted here gets its own usbmux Connect, then bytes flow both ways until either side
    /// closes. It adds no exposure: any local process can already ask usbmuxd for the same connection. The device's
    /// DeviceID is looked up again for every connection, so a device plugged in again keeps working.
    /// </summary>
    public sealed class UsbmuxForwarder : IDisposable
    {
        public static readonly IPEndPoint AppleService = new IPEndPoint(IPAddress.Loopback, 27015);
        private static readonly TimeSpan ExchangeTimeout = TimeSpan.FromSeconds(3);

        private readonly IPEndPoint _usbmuxd;
        private readonly string _udid;
        private readonly int _devicePort;
        private readonly CancellationTokenSource _stop = new CancellationTokenSource();
        private readonly ConcurrentDictionary<TcpClient, bool> _open = new ConcurrentDictionary<TcpClient, bool>();
        private TcpListener _listener;
        private int _tag;
        private int _failures;

        public UsbmuxForwarder(IPEndPoint usbmuxd, string udid, int devicePort)
        {
            _usbmuxd = usbmuxd ?? throw new ArgumentNullException(nameof(usbmuxd));
            if (!CoreDeviceOptions.IsValidUdid(udid)) throw new ArgumentException("not a device UDID", nameof(udid));
            if (devicePort < 1 || devicePort > 65535) throw new ArgumentOutOfRangeException(nameof(devicePort));
            _udid = udid;
            _devicePort = devicePort;
        }

        /// <summary>A line for the log: never the UDID or any byte that went through.</summary>
        public event Action<string> Log;

        /// <summary>The loopback port to connect to; 0 before <see cref="Start"/>.</summary>
        public int LocalPort { get; private set; }

        /// <summary>Connections usbmuxd refused or could not open since the start.</summary>
        public int Failures => Volatile.Read(ref _failures);

        public void Start()
        {
            if (_listener != null) throw new InvalidOperationException("already started");
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            LocalPort = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _ = AcceptLoopAsync();
        }

        public void Dispose()
        {
            _stop.Cancel();
            try
            {
                _listener?.Stop();
            }
            catch (SocketException)
            {
                // already closed
            }
            foreach (var client in _open.Keys.ToList()) Close(client);
        }

        /// <summary>
        /// A connection to the device port: the DeviceID from ListDevices, then Connect on a second usbmuxd connection.
        /// null when the device is not on the cable or usbmuxd refused; never throws for a network failure.
        /// </summary>
        public async Task<TcpClient> OpenAsync(CancellationToken token)
        {
            var id = await DeviceIdAsync(token).ConfigureAwait(false);
            if (id == null) return null;
            var client = new TcpClient(AddressFamily.InterNetwork) { NoDelay = true };
            try
            {
                await ConnectAsync(client, token).ConfigureAwait(false);
                var stream = client.GetStream();
                var tag = NextTag();
                var request = UsbmuxProtocol.ConnectRequest(tag, id.Value, _devicePort);
                await stream.WriteAsync(request, 0, request.Length, token).ConfigureAwait(false);
                var reply = await ReadPacketAsync(stream, token).ConfigureAwait(false);
                var result = reply == null ? null : UsbmuxProtocol.Result(reply, tag);
                if (result == UsbmuxProtocol.ConnectOk) return client;
                Write("usbmux Connect to port " + _devicePort + " refused (" + (result?.ToString() ?? "no answer") + ")");
            }
            catch (Exception e) when (IsNetwork(e))
            {
                Write("usbmux Connect failed: " + e.GetType().Name);
            }
            client.Dispose();
            return null;
        }

        private async Task<int?> DeviceIdAsync(CancellationToken token)
        {
            using (var client = new TcpClient(AddressFamily.InterNetwork))
            {
                try
                {
                    await ConnectAsync(client, token).ConfigureAwait(false);
                    var stream = client.GetStream();
                    var tag = NextTag();
                    var request = UsbmuxProtocol.ListDevicesRequest(tag);
                    await stream.WriteAsync(request, 0, request.Length, token).ConfigureAwait(false);
                    var reply = await ReadPacketAsync(stream, token).ConfigureAwait(false);
                    var devices = reply == null ? null : UsbmuxProtocol.UsbDeviceIds(reply, tag);
                    if (devices == null) return null;
                    foreach (var d in devices)
                    {
                        if (string.Equals(d.Key, _udid, StringComparison.OrdinalIgnoreCase)) return d.Value;
                    }
                    Write("the device is not on the cable");
                    return null;
                }
                catch (Exception e) when (IsNetwork(e))
                {
                    Write("usbmux ListDevices failed: " + e.GetType().Name);
                    return null;
                }
            }
        }

        private async Task AcceptLoopAsync()
        {
            while (!_stop.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync().ConfigureAwait(false);
                }
                catch (Exception e) when (e is ObjectDisposedException || e is SocketException || e is InvalidOperationException)
                {
                    return;
                }
                client.NoDelay = true;
                _ = ServeAsync(client);
            }
        }

        private async Task ServeAsync(TcpClient client)
        {
            _open[client] = true;
            TcpClient device = null;
            try
            {
                device = await OpenAsync(_stop.Token).ConfigureAwait(false);
                if (device == null)
                {
                    Interlocked.Increment(ref _failures);
                    return;
                }
                _open[device] = true;
                var local = client.GetStream();
                var remote = device.GetStream();
                // either direction ending ends both: HTTP keep-alive closes the pair together anyway
                await Task.WhenAny(PumpAsync(local, remote), PumpAsync(remote, local)).ConfigureAwait(false);
            }
            catch (Exception e) when (IsNetwork(e))
            {
                // a side went away
            }
            finally
            {
                if (device != null) Close(device);
                Close(client);
            }
        }

        private async Task PumpAsync(Stream from, Stream to)
        {
            var buffer = new byte[16384];
            try
            {
                while (true)
                {
                    var read = await from.ReadAsync(buffer, 0, buffer.Length, _stop.Token).ConfigureAwait(false);
                    if (read <= 0) return;
                    await to.WriteAsync(buffer, 0, read, _stop.Token).ConfigureAwait(false);
                }
            }
            catch (Exception e) when (IsNetwork(e))
            {
                // closed or reset
            }
        }

        private async Task ConnectAsync(TcpClient client, CancellationToken token)
        {
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token, _stop.Token))
            {
                timeout.CancelAfter(ExchangeTimeout);
                var connect = client.ConnectAsync(_usbmuxd.Address, _usbmuxd.Port);
                // an abandoned attempt faults when the client is disposed: observe it
                _ = connect.ContinueWith(t => { var ignored = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
                var done = await Task.WhenAny(connect, Task.Delay(Timeout.Infinite, timeout.Token)).ConfigureAwait(false);
                if (done != connect) throw new TimeoutException("usbmuxd did not accept the connection");
                await connect.ConfigureAwait(false);
            }
        }

        /// <summary>One whole usbmux packet within the exchange deadline, or null when it is not one.</summary>
        private async Task<byte[]> ReadPacketAsync(Stream stream, CancellationToken token)
        {
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token, _stop.Token))
            {
                timeout.CancelAfter(ExchangeTimeout);
                var header = new byte[UsbmuxProtocol.HeaderSize];
                if (!await ReadExactlyAsync(stream, header, 0, header.Length, timeout.Token).ConfigureAwait(false)) return null;
                var size = UsbmuxProtocol.DeclaredSize(header);
                if (size < 0) return null;
                var packet = new byte[size];
                Buffer.BlockCopy(header, 0, packet, 0, header.Length);
                return await ReadExactlyAsync(stream, packet, header.Length, size - header.Length, timeout.Token).ConfigureAwait(false) ? packet : null;
            }
        }

        private static async Task<bool> ReadExactlyAsync(Stream stream, byte[] buffer, int offset, int count, CancellationToken token)
        {
            while (count > 0)
            {
                var read = await ReadAsync(stream, buffer, offset, count, token).ConfigureAwait(false);
                if (read <= 0) return false;
                offset += read;
                count -= read;
            }
            return true;
        }

        /// <summary>A read that a cancellation really ends: a NetworkStream on .NET Framework ignores the token.</summary>
        private static async Task<int> ReadAsync(Stream stream, byte[] buffer, int offset, int count, CancellationToken token)
        {
            var read = stream.ReadAsync(buffer, offset, count, token);
            _ = read.ContinueWith(t => { var ignored = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
            var done = await Task.WhenAny(read, Task.Delay(Timeout.Infinite, token)).ConfigureAwait(false);
            if (done != read) throw new TimeoutException("usbmuxd did not answer in time");
            return await read.ConfigureAwait(false);
        }

        private void Close(TcpClient client)
        {
            _open.TryRemove(client, out _);
            try
            {
                client.Dispose();
            }
            catch (SocketException)
            {
                // already gone
            }
        }

        private uint NextTag() => unchecked((uint)Interlocked.Increment(ref _tag));

        private void Write(string line)
        {
            try
            {
                Log?.Invoke(line);
            }
            catch (Exception)
            {
                // a log listener never breaks the forward
            }
        }

        private static bool IsNetwork(Exception e) =>
            e is SocketException || e is IOException || e is ObjectDisposedException || e is TimeoutException || e is OperationCanceledException
            || e is InvalidOperationException;
    }
}
