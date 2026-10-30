using System;
using System.Collections.Generic;
using System.Linq;

namespace MacExtend.Core
{
    /// <summary>
    /// Installed by the Inno Setup installer, or a portable copy. Decided by the uninstaller Inno leaves beside
    /// MacExtend.exe, never by write access: an elevated process can write Program Files too.
    /// </summary>
    public static class InstallLayout
    {
        /// <param name="files">Names or full paths of the files in the app folder.</param>
        /// <returns>true when one of them is an Inno Setup uninstaller (unins000.exe, unins001.exe, ...).</returns>
        public static bool IsInstalledCopy(IEnumerable<string> files) =>
            files != null && files.Select(FileName).Any(name =>
                name.StartsWith("unins", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));

        // both separators, so a Windows path reads the same on every platform
        private static string FileName(string path)
        {
            path = path ?? "";
            return path.Substring(path.LastIndexOfAny(new[] { '\\', '/' }) + 1);
        }
    }
}
