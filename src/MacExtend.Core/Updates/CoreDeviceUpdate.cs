using System;
using MacExtend.Core.CoreDevice;

namespace MacExtend.Core.Updates
{
    /// <summary>The checks the updater makes around the optional CoreDevice runtime slot.</summary>
    public static class CoreDeviceUpdate
    {
        public const string StagingFolder = "coredevice";

        /// <summary>The staging folder carries a new CoreDevice runtime, so tunneld and python must stop before it is applied.</summary>
        public static bool IsStaged(string stagingDir) => System.IO.Directory.Exists(System.IO.Path.Combine(stagingDir, StagingFolder));

        /// <summary>
        /// An extracted runtime is only staged when it has python\python.exe, python\pythonw.exe and the RUNTIME_ID the
        /// manifest announced (the zip's SHA-256 was already checked against the manifest).
        /// </summary>
        /// <exception cref="RuntimePackageException">Incomplete runtime or another RUNTIME_ID.</exception>
        public static void CheckStaged(string extractedDir, string manifestId) => RuntimeInstaller.CheckLayout(extractedDir, manifestId);

        /// <summary>Whether a running program's image lies inside the runtime folder (and so holds its files open).</summary>
        public static bool IsInsideRuntime(string exePath, string runtimeDir)
        {
            if (string.IsNullOrEmpty(exePath) || string.IsNullOrEmpty(runtimeDir)) return false;
            var root = runtimeDir.TrimEnd('\\', '/') + "\\";
            return exePath.StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }
    }
}
