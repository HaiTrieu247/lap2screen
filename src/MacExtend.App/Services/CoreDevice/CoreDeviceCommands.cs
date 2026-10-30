using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Threading.Tasks;
using MacExtend.Core.CoreDevice;
using MacExtend.CoreDeviceHost;

namespace MacExtend.Services.CoreDevice
{
    /// <summary>The short pymobiledevice3 commands setup and sessions run, each in its own job with its own deadline.</summary>
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

        /// <summary>UDIDs of the devices on a cable; empty when the command fails.</summary>
        public static Task<IReadOnlyList<string>> UsbDevicesAsync() => Task.Run(() =>
        {
            var r = Run(null, ListTimeout, "usbmux", "list", "--usb", "--simple");
            return r.Succeeded ? DeviceQueries.ParseUdids(r.Stdout) : (IReadOnlyList<string>)new string[0];
        });

        /// <summary>stdout is true/false (DeviceQueries.ParseBool).</summary>
        public static Task<CommandResult> DeveloperModeAsync(string udid) => Task.Run(() => Run(udid, TrustTimeout, "amfi", "developer-mode-status"));

        public static Task<CommandResult> RevealDeveloperModeAsync(string udid) => Task.Run(() => Run(udid, ShortTimeout, "amfi", "reveal-developer-mode"));

        /// <summary>auto-mount needs a tunnel on iOS 17+: the userspace one over the cable, tunneld's over Wi-Fi. Mounted already counts as success.</summary>
        public static Task<CommandResult> MountAsync(string udid, CoreDeviceRoute route) => Task.Run(() =>
            route == CoreDeviceRoute.Wifi
                ? Run(udid, MountTimeout, "mounter", "auto-mount", "--tunnel", udid)
                : Run(udid, MountTimeout, "mounter", "auto-mount", "--userspace"));

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
                    return client.ConnectAsync("127.0.0.1", UsbmuxPort).Wait(500) && client.Connected;
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
