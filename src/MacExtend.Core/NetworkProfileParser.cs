using System.Collections.Generic;
using System.Globalization;

namespace MacExtend.Core
{
    public sealed class NetworkConnection
    {
        public NetworkConnection(int interfaceIndex, string alias, string category)
        {
            InterfaceIndex = interfaceIndex;
            Alias = alias;
            Category = category;
        }

        public int InterfaceIndex { get; }
        public string Alias { get; }
        /// <summary>Windows NetworkCategory: "Public", "Private" or "DomainAuthenticated".</summary>
        public string Category { get; }
        public bool IsPublic => Category == "Public";
    }

    /// <summary>Parses "index|alias|category" lines printed by the network profile probe script.</summary>
    public static class NetworkProfileParser
    {
        public static IReadOnlyList<NetworkConnection> Parse(string output)
        {
            var list = new List<NetworkConnection>();
            foreach (var raw in (output ?? "").Split('\n'))
            {
                var parts = raw.Trim().Split('|');
                if (parts.Length != 3) continue;
                if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var index)) continue;
                if (parts[1].Length == 0 || parts[2].Length == 0) continue;
                list.Add(new NetworkConnection(index, parts[1], parts[2]));
            }
            return list;
        }
    }
}
