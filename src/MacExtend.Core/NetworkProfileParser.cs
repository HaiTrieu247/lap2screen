using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace MacExtend.Core
{
    public sealed class NetworkConnection
    {
        public NetworkConnection(int interfaceIndex, string alias, string category,
                                 string ipv4Connectivity = null, string ipv6Connectivity = null, bool hasDefaultRoute = false)
        {
            InterfaceIndex = interfaceIndex;
            Alias = alias;
            Category = category;
            Ipv4Connectivity = ipv4Connectivity;
            Ipv6Connectivity = ipv6Connectivity;
            HasDefaultRoute = hasDefaultRoute;
        }

        public int InterfaceIndex { get; }
        public string Alias { get; }
        /// <summary>Windows NetworkCategory: "Public", "Private" or "DomainAuthenticated".</summary>
        public string Category { get; }
        /// <summary>Windows NetworkConnectivityLevel: "NoTraffic", "LocalNetwork" or "Internet" (null when unknown).</summary>
        public string Ipv4Connectivity { get; }
        public string Ipv6Connectivity { get; }
        /// <summary>The interface holds a default route, i.e. it is the network the PC actually uses (Wi-Fi or LAN).</summary>
        public bool HasDefaultRoute { get; }
        public bool IsPublic => Category == "Public";

        public bool CarriesLanTraffic => IsTraffic(Ipv4Connectivity) || IsTraffic(Ipv6Connectivity);

        private static bool IsTraffic(string level) => level == "LocalNetwork" || level == "Internet";
    }

    /// <summary>Parses "index|alias|category[|ipv4|ipv6|defaultRoute]" lines printed by the network profile probe script.</summary>
    public static class NetworkProfileParser
    {
        public static IReadOnlyList<NetworkConnection> Parse(string output)
        {
            var list = new List<NetworkConnection>();
            foreach (var raw in (output ?? "").Split('\n'))
            {
                var parts = raw.Trim().Split('|');
                if (parts.Length != 3 && parts.Length != 6) continue;
                if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var index)) continue;
                if (parts[1].Length == 0 || parts[2].Length == 0) continue;
                list.Add(parts.Length == 6
                    ? new NetworkConnection(index, parts[1], parts[2], parts[3], parts[4], parts[5] == "1")
                    : new NetworkConnection(index, parts[1], parts[2]));
            }
            return list;
        }

        /// <summary>
        /// The connections that decide whether the Mac can reach this PC. Hyper-V, WSL, VPN and other
        /// virtual adapters are often "Public" but carry no LAN traffic, so they are left out.
        /// </summary>
        public static IReadOnlyList<NetworkConnection> Relevant(IReadOnlyList<NetworkConnection> all)
        {
            var routed = all.Where(n => n.HasDefaultRoute).ToList();
            if (routed.Count > 0) return routed;
            var lan = all.Where(n => n.CarriesLanTraffic).ToList();
            return lan.Count > 0 ? lan : all;
        }
    }
}
