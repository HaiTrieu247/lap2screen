namespace MacExtend.Core.CoreDevice
{
    /// <summary>
    /// The ZIP path the elevated --coredevice-setup accepts from its unelevated caller. Only a local drive path ("X:\..."):
    /// never UNC (an elevated open sends the NTLM hash), "\\?\", "\\.\", a drive-relative "X:name" or a relative path.
    /// Whatever the path resolves to, the installer hashes and extracts only its own private copy.
    /// </summary>
    public static class HelperPaths
    {
        // '/' would be a second separator, ':' an alternate data stream; the rest are never in a file name
        private static readonly char[] Refused = { '/', ':', '*', '?', '"', '<', '>', '|' };

        public static bool IsLocalDrivePath(string path)
        {
            if (string.IsNullOrEmpty(path) || path.Length < 4) return false;
            var drive = path[0];
            if (!((drive >= 'A' && drive <= 'Z') || (drive >= 'a' && drive <= 'z')) || path[1] != ':' || path[2] != '\\') return false;
            for (var i = 3; i < path.Length; i++)
            {
                if (path[i] < ' ' || System.Array.IndexOf(Refused, path[i]) >= 0) return false;
            }
            return true;
        }
    }
}
