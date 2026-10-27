using System.Globalization;

namespace MacExtend.Core
{
    public sealed class FirewallStatus
    {
        public FirewallStatus(bool hasAllow, bool hasBlock, bool isKnown = true)
        {
            HasAllow = hasAllow;
            HasBlock = hasBlock;
            IsKnown = isKnown;
        }

        /// <summary>The probe ran and reported both counts; false when PowerShell failed.</summary>
        public bool IsKnown { get; }

        /// <summary>An enabled inbound allow rule exists for uxplay.exe.</summary>
        public bool HasAllow { get; }
        /// <summary>An enabled inbound block rule exists for uxplay.exe (Windows adds one when the first prompt is cancelled).</summary>
        public bool HasBlock { get; }
        public bool Ok => HasAllow && !HasBlock;
    }

    /// <summary>Parses the "ALLOW=n" / "BLOCK=n" lines printed by the firewall probe script.</summary>
    public static class FirewallStatusParser
    {
        public static FirewallStatus Parse(string output)
        {
            int allow = 0, block = 0;
            bool sawAllow = false, sawBlock = false;
            foreach (var raw in (output ?? "").Split('\n'))
            {
                var line = raw.Trim();
                if (line.StartsWith("ALLOW=")) sawAllow = int.TryParse(line.Substring(6), NumberStyles.Integer, CultureInfo.InvariantCulture, out allow);
                else if (line.StartsWith("BLOCK=")) sawBlock = int.TryParse(line.Substring(6), NumberStyles.Integer, CultureInfo.InvariantCulture, out block);
            }
            return new FirewallStatus(allow > 0, block > 0, sawAllow && sawBlock);
        }
    }
}
