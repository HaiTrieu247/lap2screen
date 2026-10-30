using System;
using System.IO;
using System.Threading;
using MacExtend.Core.Updates;

namespace MacExtend.Core.CoreDevice
{
    /// <summary>
    /// Puts a CoreDevice runtime zip in place as {app}\coredevice: only this build's own package (SHA-256 and
    /// RUNTIME_ID), only inside its folder, and never leaving the machine without the runtime it had.
    /// </summary>
    public static class RuntimeInstaller
    {
        public const string RuntimeIdFile = "RUNTIME_ID";

        private const int MoveAttempts = 5;

        // tests make a move fail or skip the wait
        internal static Action<string, string> MoveDirectory = Directory.Move;
        internal static TimeSpan RetryDelay = TimeSpan.FromMilliseconds(500);

        /// <summary>
        /// Opens <paramref name="zipPath"/> once, refusing other writers, renames and deletes while it is open, checks its
        /// SHA-256 on that handle and extracts from the same handle to TARGET.new, so the bytes installed are the bytes
        /// checked. The tree must hold python\python.exe, python\pythonw.exe and the expected RUNTIME_ID. TARGET then
        /// moves to TARGET.old and TARGET.new takes its place; whatever fails after that, TARGET.old comes back.
        /// </summary>
        /// <exception cref="InvalidDataException">The zip is not <paramref name="expected"/>, or not a usable runtime.</exception>
        public static void Install(string zipPath, RuntimePackage expected, string target, Action<string> log)
        {
            if (expected == null) throw new ArgumentNullException(nameof(expected));
            log = log ?? (_ => { });
            var fresh = target + ".new";
            var aside = target + ".old";
            try
            {
                using (var stream = new FileStream(zipPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    if (!expected.Matches(FileHash.Sha256(stream)))
                    {
                        throw new InvalidDataException("Gói runtime không phải của bản MacExtend này (sai mã SHA-256).");
                    }
                    stream.Position = 0;
                    UpdateStager.Extract(stream, fresh);
                }
                CheckLayout(fresh, expected.Id);

                if (Directory.Exists(aside)) Directory.Delete(aside, true);
                var hadRuntime = Directory.Exists(target);
                if (hadRuntime) Move(target, aside);
                try
                {
                    Move(fresh, target);
                }
                catch (Exception)
                {
                    if (hadRuntime) Restore(target, aside, log);
                    throw;
                }
            }
            finally
            {
                TryDelete(fresh);
            }
            // a .old that cannot go now is removed by the next install, or by UpdateApplier.CleanupLeftovers
            TryDelete(aside);
            log("runtime " + expected.Id + " installed");
        }

        private static void CheckLayout(string dir, string id)
        {
            foreach (var exe in new[] { "python.exe", "pythonw.exe" })
            {
                if (!File.Exists(Path.Combine(dir, "python", exe))) throw new InvalidDataException("Gói runtime thiếu python\\" + exe + ".");
            }
            var idFile = Path.Combine(dir, RuntimeIdFile);
            var found = File.Exists(idFile) ? File.ReadAllText(idFile).Trim() : "";
            if (!string.Equals(found, id, StringComparison.Ordinal))
            {
                throw new InvalidDataException("Gói runtime có RUNTIME_ID khác với bản MacExtend này.");
            }
        }

        // a tunneld that was just ended, or a virus scanner, can hold the files for a moment
        private static void Move(string from, string to)
        {
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    MoveDirectory(from, to);
                    return;
                }
                catch (Exception e) when ((e is IOException || e is UnauthorizedAccessException) && attempt < MoveAttempts)
                {
                    Thread.Sleep(RetryDelay);
                }
            }
        }

        private static void Restore(string target, string aside, Action<string> log)
        {
            try
            {
                if (!Directory.Exists(target) && Directory.Exists(aside)) Move(aside, target);
            }
            catch (Exception e)
            {
                // the caller rethrows the failure that got here; this one is only reported
                log("could not put the previous runtime back: " + e.Message);
            }
        }

        private static void TryDelete(string dir)
        {
            try
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                // left for the next install
            }
        }
    }
}
