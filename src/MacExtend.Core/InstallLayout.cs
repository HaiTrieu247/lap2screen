using System;
using System.Collections.Generic;
using System.Linq;

namespace MacExtend.Core
{
    /// <summary>
    /// Installed by the Inno Setup installer, or a portable copy, and whether the copy may start tunneld through the
    /// Scheduled Task. Decided by the files and the folder, never by write access: an elevated process can write
    /// Program Files too.
    /// </summary>
    public static class InstallLayout
    {
        /// <param name="files">Names or full paths of the files in the app folder.</param>
        /// <returns>true when one of them is an Inno Setup uninstaller (unins000.exe, unins001.exe, ...).</returns>
        public static bool IsInstalledCopy(IEnumerable<string> files) =>
            files != null && files.Select(FileName).Any(name =>
                name.StartsWith("unins", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// The task runs the runtime's pythonw.exe with highest privileges and no UAC, so only an installed copy inside
        /// the 64-bit Program Files folder, whose files only administrators can replace, may have one. Any other folder
        /// (C:\MacExtend picked in the installer is writable by its user, parent included) is treated like a portable
        /// copy: no task, and Wi-Fi asks UAC per session.
        /// </summary>
        /// <param name="installDir">The folder of MacExtend.exe.</param>
        /// <param name="programFiles">Environment.SpecialFolder.ProgramFiles of a 64-bit process.</param>
        public static bool AllowsTask(bool installedCopy, string installDir, string programFiles)
        {
            if (!installedCopy) return false;
            var dir = Normalize(installDir);
            var root = Normalize(programFiles);
            return dir != null && root != null && dir.StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// A plain drive path ("C:\...") with "." and ".." resolved and one trailing separator, so "C:\Program FilesX"
        /// does not start with "C:\Program Files\". null for anything else (relative, UNC, "\\?\", a ".." above the
        /// root, a ':' or wildcard inside), which is never trusted.
        /// </summary>
        private static string Normalize(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            path = path.Replace('/', '\\');
            var drive = path[0];
            if (path.Length < 3 || !((drive >= 'A' && drive <= 'Z') || (drive >= 'a' && drive <= 'z')) || path[1] != ':' || path[2] != '\\') return null;
            if (path.IndexOfAny(new[] { ':', '*', '?', '"', '<', '>', '|', '\0' }, 2) >= 0) return null;

            var segments = new List<string>();
            foreach (var segment in path.Substring(3).Split('\\'))
            {
                if (segment.Length == 0 || segment == ".") continue;
                if (segment == "..")
                {
                    if (segments.Count == 0) return null;
                    segments.RemoveAt(segments.Count - 1);
                    continue;
                }
                segments.Add(segment);
            }
            return drive + ":\\" + string.Concat(segments.Select(s => s + "\\"));
        }

        // both separators, so a Windows path reads the same on every platform
        private static string FileName(string path)
        {
            path = path ?? "";
            return path.Substring(path.LastIndexOfAny(new[] { '\\', '/' }) + 1);
        }
    }
}
