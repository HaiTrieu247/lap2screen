using System;

namespace MacExtend.Core.Updates
{
    /// <summary>Release versions are Major.Minor.Build; assembly versions carry a fourth part that must not count.</summary>
    public static class AppVersion
    {
        public static Version Normalize(Version v) => new Version(v.Major, Math.Max(0, v.Minor), Math.Max(0, v.Build));

        public static bool TryParse(string text, out Version version)
        {
            version = null;
            var s = (text ?? "").Trim();
            if (s.StartsWith("v", StringComparison.OrdinalIgnoreCase)) s = s.Substring(1);
            if (!Version.TryParse(s, out var parsed)) return false;
            version = Normalize(parsed);
            return true;
        }
    }
}
