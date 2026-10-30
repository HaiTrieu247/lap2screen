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

        /// <summary>
        /// The label a release is tagged with ("v" + label): the informational version without the "+commit" the SDK
        /// appends, so "1.0.8-beta.1" for a pre-release; the 3-part version when there is none.
        /// </summary>
        public static string ReleaseLabel(string informationalVersion, Version fallback)
        {
            var label = (informationalVersion ?? "").Split('+')[0].Trim();
            return label.Length > 0 ? label : Normalize(fallback).ToString(3);
        }
    }
}
