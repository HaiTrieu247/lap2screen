using System;
using System.Collections.Generic;
using System.Linq;

namespace MacExtend.Core.CoreDevice
{
    public sealed class TunnelEntry
    {
        public TunnelEntry(string address, int port, string interfaceName)
        {
            Address = address;
            Port = port;
            Interface = interfaceName;
        }

        public string Address { get; }
        public int Port { get; }

        /// <summary>How tunneld reached the device: "usbmux-&lt;UDID&gt;-USB", "mobdev2-...", or the device's address for RemotePairing.</summary>
        public string Interface { get; }
    }

    /// <summary>tunneld's GET / on 127.0.0.1:49151: {"&lt;UDID&gt;": [{"tunnel-address", "tunnel-port", "interface", ...}]}.</summary>
    public static class TunneldListing
    {
        public const string Host = "127.0.0.1";
        public const int Port = 49151;

        public static IReadOnlyDictionary<string, IReadOnlyList<TunnelEntry>> Parse(string json)
        {
            var result = new Dictionary<string, IReadOnlyList<TunnelEntry>>(StringComparer.OrdinalIgnoreCase);
            if (!MiniJson.TryParse(json, out var value) || !(value is Dictionary<string, object> map)) return result;
            foreach (var pair in map)
            {
                if (!CoreDeviceOptions.IsValidUdid(pair.Key) || !(pair.Value is List<object> entries)) continue;
                var tunnels = new List<TunnelEntry>();
                foreach (var entry in entries.OfType<Dictionary<string, object>>())
                {
                    if (!(Get(entry, "tunnel-address") is string address) || address.Length == 0) continue;
                    if (!(Get(entry, "tunnel-port") is double port) || port < 1 || port > 65535 || port != Math.Floor(port)) continue;
                    tunnels.Add(new TunnelEntry(address, (int)port, Get(entry, "interface") as string ?? ""));
                }
                if (tunnels.Count > 0) result[pair.Key] = tunnels;
            }
            return result;
        }

        public static bool HasTunnel(string json, string udid) => udid != null && Parse(json).ContainsKey(udid);

        /// <summary>
        /// One log line for what GET / returned: counts and the kinds of tunnel (usbmux, mobdev2, wifi), never a UDID or
        /// an address. Null json means tunneld did not answer.
        /// </summary>
        public static string Summary(string json)
        {
            if (json == null) return "tunneld does not answer";
            var map = Parse(json);
            if (map.Count == 0) return "no tunnels";
            var all = map.Values.SelectMany(v => v).ToList();
            var kinds = all.Select(e => KindOf(e.Interface)).Distinct();
            return map.Count + " device(s), " + all.Count + " tunnel(s): " + string.Join(", ", kinds);
        }

        private static string KindOf(string iface) =>
            iface.StartsWith("usbmux-", StringComparison.Ordinal) ? "usbmux" : iface.StartsWith("mobdev2-", StringComparison.Ordinal) ? "mobdev2" : "wifi";

        private static object Get(Dictionary<string, object> map, string key) => map.TryGetValue(key, out var v) ? v : null;
    }
}
