using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using MacExtend.Core.CoreDevice;
using MacExtend.CoreDeviceHost;

namespace MacExtend.Services.CoreDevice
{
    /// <summary>
    /// The short pymobiledevice3 commands setup and sessions run, each in its own job with its own deadline, and the one
    /// question asked of the Apple Mobile Device Service directly (which devices are on a cable), with no Python at all.
    /// </summary>
    internal static class CoreDeviceCommands
    {
        private static readonly TimeSpan ListTimeout = TimeSpan.FromSeconds(20);
        // waits for the Trust dialog when the device is not paired yet
        private static readonly TimeSpan TrustTimeout = TimeSpan.FromSeconds(120);
        private static readonly TimeSpan ShortTimeout = TimeSpan.FromSeconds(30);
        // the first mount downloads and personalizes the image
        private static readonly TimeSpan MountTimeout = TimeSpan.FromSeconds(180);

        // Apple Mobile Device Service (iTunes, Apple Devices) listens here on Windows
        private const int UsbmuxPort = 27015;
        private static readonly TimeSpan UsbmuxConnectTimeout = TimeSpan.FromSeconds(1);
        private static readonly TimeSpan UsbmuxReplyTimeout = TimeSpan.FromSeconds(2);
        private static int _usbmuxTag;

        /// <summary>UDIDs of the devices on a cable; empty when the command fails.</summary>
        public static Task<IReadOnlyList<string>> UsbDevicesAsync() => Task.Run(() =>
        {
            var r = Run(null, ListTimeout, "usbmux", "list", "--usb", "--simple");
            return r.Succeeded ? DeviceQueries.ParseUdids(r.Stdout) : (IReadOnlyList<string>)new string[0];
        });

        /// <summary>
        /// UDIDs of the devices on a cable, from usbmuxd's ListDevices on 127.0.0.1:27015 instead of a Python process: the
        /// presence check that runs every few seconds of a cable session. null when the service gave no proper answer (not
        /// running, refused, too slow, an odd or oversized reply): unknown, never "not there". Runs on the thread pool.
        /// </summary>
        public static Task<IReadOnlyList<string>> UsbmuxUsbDevicesAsync() => Task.Run(() => QueryUsbmux());

        private static IReadOnlyList<string> QueryUsbmux()
        {
            var tag = unchecked((uint)Interlocked.Increment(ref _usbmuxTag));
            try
            {
                using (var client = new TcpClient(AddressFamily.InterNetwork))
                {
                    var connect = client.ConnectAsync(IPAddress.Loopback, UsbmuxPort);
                    // an abandoned attempt faults when the client is disposed: observe it so it never reaches the crash log
                    connect.ContinueWith(t => { var ignored = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
                    if (!connect.Wait(UsbmuxConnectTimeout) || !client.Connected) return null;
                    var socket = client.Client;
                    var watch = Stopwatch.StartNew();
                    socket.SendTimeout = (int)UsbmuxReplyTimeout.TotalMilliseconds;
                    var request = UsbmuxProtocol.ListDevicesRequest(tag);
                    if (socket.Send(request) != request.Length) return null;
                    var header = new byte[UsbmuxProtocol.HeaderSize];
                    if (!ReceiveExactly(socket, header, 0, header.Length, watch)) return null;
                    // the size is checked before anything is allocated for it
                    var size = UsbmuxProtocol.DeclaredSize(header);
                    if (size < 0) return null;
                    var packet = new byte[size];
                    Buffer.BlockCopy(header, 0, packet, 0, header.Length);
                    if (!ReceiveExactly(socket, packet, header.Length, size - header.Length, watch)) return null;
                    return UsbmuxProtocol.UsbDevices(packet, tag);
                }
            }
            catch (Exception e) when (e is SocketException || e is IOException || e is ObjectDisposedException || e is AggregateException)
            {
                // refused (the service is not running), timed out, or reset
                return null;
            }
        }

        /// <summary>Reads exactly <paramref name="count"/> bytes within the reply deadline; false when the deadline passes or the service hangs up.</summary>
        private static bool ReceiveExactly(Socket socket, byte[] buffer, int offset, int count, Stopwatch watch)
        {
            while (count > 0)
            {
                var left = UsbmuxReplyTimeout - watch.Elapsed;
                if (left <= TimeSpan.Zero) return false;
                socket.ReceiveTimeout = Math.Max(1, (int)left.TotalMilliseconds);
                var read = socket.Receive(buffer, offset, count, SocketFlags.None);
                if (read == 0) return false;
                offset += read;
                count -= read;
            }
            return true;
        }

        /// <summary>stdout is true/false (DeviceQueries.ParseBool).</summary>
        public static Task<CommandResult> DeveloperModeAsync(string udid) => Task.Run(() => Run(udid, TrustTimeout, "amfi", "developer-mode-status"));

        public static Task<CommandResult> RevealDeveloperModeAsync(string udid) => Task.Run(() => Run(udid, ShortTimeout, "amfi", "reveal-developer-mode"));

        /// <summary>auto-mount needs a tunnel on iOS 17+: the userspace one over the cable, tunneld's over Wi-Fi. Mounted already counts as success.</summary>
        public static Task<CommandResult> MountAsync(string udid, CoreDeviceRoute route) => Task.Run(() =>
            route == CoreDeviceRoute.Wifi
                ? Run(udid, MountTimeout, "mounter", "auto-mount", "--tunnel", udid)
                : Run(udid, MountTimeout, "mounter", "auto-mount", "--userspace"));

        /// <summary>
        /// `mounter list`: the images mounted now, as the JSON list DeviceQueries.ParseDeveloperImageMounted reads. Over
        /// lockdown on the cable (pymobiledevice3 retries over a tunnel by itself where lockdown cannot serve it).
        /// </summary>
        public static Task<CommandResult> MountedImagesAsync(string udid) => Task.Run(() => Run(udid, ShortTimeout, "mounter", "list"));

        /// <summary>RemotePairing pair-setup over the trusted cable: no dialog on the device; writes remote_&lt;UDID&gt;.plist.</summary>
        public static Task<CommandResult> RemotePairAsync(string udid) => Task.Run(() => Run(udid, ShortTimeout, "lockdown", "remotepairing", "--pair"));

        public static bool RemotePaired(string udid)
        {
            // the UDID becomes a file name: nothing but the characters a UDID has
            if (string.IsNullOrEmpty(udid) || udid.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || udid.Contains("..")) return false;
            return File.Exists(Path.Combine(AppPaths.PymobiledeviceHome, "remote_" + udid + ".plist"));
        }

        public static bool AppleServiceReachable()
        {
            try
            {
                using (var client = new TcpClient())
                {
                    var connect = client.ConnectAsync("127.0.0.1", UsbmuxPort);
                    // an abandoned attempt faults when the client is disposed: observe it so it never reaches the crash log
                    connect.ContinueWith(t => { var ignored = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
                    return connect.Wait(500) && client.Connected;
                }
            }
            catch (AggregateException)
            {
                return false;
            }
            catch (SocketException)
            {
                return false;
            }
        }

        private static CommandResult Run(string udid, TimeSpan timeout, params string[] words) =>
            CoreDeviceProcessHost.RunCommand(AppPaths.CoreDevicePython, CoreDeviceArguments.Command(words),
                CoreDeviceArguments.CommandEnvironment(Environment.GetEnvironmentVariables(), udid), timeout);
    }
}
