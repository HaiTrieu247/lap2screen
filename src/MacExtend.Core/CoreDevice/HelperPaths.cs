using System;
using System.Text.RegularExpressions;

namespace MacExtend.Core.CoreDevice
{
    /// <summary>
    /// The paths the elevated helper modes accept from their unelevated caller. Only a local drive path ("X:\..."):
    /// never UNC (an SMB server can serve other bytes on a second read, and an elevated open sends the NTLM hash),
    /// "\\?\", "\\.\", a drive-relative "X:name" or a relative path. RESULT must also carry the name the app makes,
    /// so the helper can never be pointed at a Startup script, a PowerShell profile or any other file.
    /// </summary>
    public static class HelperPaths
    {
        private static readonly Regex ResultName = new Regex(@"^MacExtend-coredevice-[0-9a-f]{32}\.result\z", RegexOptions.CultureInvariant);

        // '/' would be a second separator, ':' an alternate data stream; the rest are never in a file name
        private static readonly char[] Refused = { '/', ':', '*', '?', '"', '<', '>', '|' };

        /// <summary>The result file name for one run: "MacExtend-coredevice-" + 32 lowercase hex + ".result".</summary>
        public static string ResultFileName(Guid id) => "MacExtend-coredevice-" + id.ToString("N") + ".result";

        public static bool IsLocalDrivePath(string path)
        {
            if (string.IsNullOrEmpty(path) || path.Length < 4) return false;
            var drive = path[0];
            if (!((drive >= 'A' && drive <= 'Z') || (drive >= 'a' && drive <= 'z')) || path[1] != ':' || path[2] != '\\') return false;
            for (var i = 3; i < path.Length; i++)
            {
                if (path[i] < ' ' || Array.IndexOf(Refused, path[i]) >= 0) return false;
            }
            return true;
        }

        public static bool IsResultFile(string path) =>
            IsLocalDrivePath(path) && ResultName.IsMatch(path.Substring(path.LastIndexOf('\\') + 1));
    }
}
