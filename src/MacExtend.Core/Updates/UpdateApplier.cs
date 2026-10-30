using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace MacExtend.Core.Updates
{
    /// <summary>
    /// Puts a staged update into the install folder: staging\app\** over the install folder and,
    /// when present, staging\runtime\** as the new uxplay folder and staging\coredevice\** as the new coredevice folder. Originals are moved aside first,
    /// so any failure puts every file back; staging is left intact for a retry.
    /// </summary>
    public static class UpdateApplier
    {
        private const string Aside = ".old";

        // staging folder -> installed folder; each is replaced as a whole when staged
        private static readonly string[][] RuntimeSlots = { new[] { "runtime", "uxplay" }, new[] { "coredevice", "coredevice" } };

        internal static TimeSpan RetryDelay = TimeSpan.FromMilliseconds(300);

        public static void Apply(string stagingDir, string installDir, Action<string> log)
        {
            var appSource = Path.Combine(stagingDir, "app");

            var movedAside = new List<string>();
            var added = new List<string>();
            var swapped = new List<string>();

            CleanupLeftovers(installDir);
            try
            {
                foreach (var slot in RuntimeSlots)
                {
                    var source = Path.Combine(stagingDir, slot[0]);
                    if (!Directory.Exists(source)) continue;
                    var target = Path.Combine(installDir, slot[1]);
                    if (Directory.Exists(target)) Retry(() => Directory.Move(target, target + Aside));
                    swapped.Add(target);
                    CopyTree(source, target);
                    log(slot[0] == "runtime" ? "runtime replaced" : slot[1] + " runtime replaced");
                }

                if (Directory.Exists(appSource))
                {
                    var files = Directory.GetFiles(appSource, "*", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.Ordinal);
                    foreach (var source in files)
                    {
                        var target = Path.Combine(installDir, source.Substring(appSource.Length + 1));
                        Retry(() => Directory.CreateDirectory(Path.GetDirectoryName(target)));
                        if (File.Exists(target))
                        {
                            Retry(() => File.Move(target, target + Aside));
                            movedAside.Add(target);
                        }
                        else
                        {
                            added.Add(target);
                        }
                        Retry(() => File.Copy(source, target, true));
                    }
                    log("app files replaced: " + (movedAside.Count + added.Count));
                }
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                log("update failed, restoring previous files: " + e.Message);
                Rollback(movedAside, added, swapped, log);
                throw;
            }

            foreach (var target in movedAside) TryDeleteFile(target + Aside);
            foreach (var target in swapped) TryDeleteDirectory(target + Aside);
        }

        /// <summary>Removes files moved aside by an update that was interrupted after it succeeded.</summary>
        public static void CleanupLeftovers(string installDir)
        {
            if (!Directory.Exists(installDir)) return;
            foreach (var slot in RuntimeSlots) TryDeleteDirectory(Path.Combine(installDir, slot[1] + Aside));
            foreach (var file in Directory.GetFiles(installDir, "*" + Aside, SearchOption.AllDirectories)) TryDeleteFile(file);
        }

        private static void Rollback(List<string> movedAside, List<string> added, List<string> swapped, Action<string> log)
        {
            foreach (var target in added) TryDeleteFile(target);
            foreach (var target in movedAside)
            {
                TryDeleteFile(target);
                Try(() => Retry(() => File.Move(target + Aside, target)), log);
            }
            foreach (var target in swapped)
            {
                TryDeleteDirectory(target);
                if (Directory.Exists(target + Aside)) Try(() => Retry(() => Directory.Move(target + Aside, target)), log);
            }
        }

        private static void CopyTree(string source, string target)
        {
            Directory.CreateDirectory(target);
            foreach (var dir in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
            {
                Directory.CreateDirectory(Path.Combine(target, dir.Substring(source.Length + 1)));
            }
            foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                var to = Path.Combine(target, file.Substring(source.Length + 1));
                Retry(() => File.Copy(file, to, true));
            }
        }

        // antivirus scanners and Explorer briefly lock fresh files; a short retry rides that out
        private static void Retry(Action action)
        {
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    action();
                    return;
                }
                catch (Exception e) when ((e is IOException || e is UnauthorizedAccessException) && attempt < 3)
                {
                    Thread.Sleep(RetryDelay);
                }
            }
        }

        private static void Try(Action action, Action<string> log)
        {
            try
            {
                action();
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                log("could not restore: " + e.Message);
            }
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                // left for CleanupLeftovers on the next start
            }
        }

        private static void TryDeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path)) Directory.Delete(path, true);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                // left for CleanupLeftovers on the next start
            }
        }
    }
}
