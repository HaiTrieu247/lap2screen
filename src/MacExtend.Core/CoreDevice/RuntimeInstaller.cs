using System;
using System.IO;
using System.Threading;
using MacExtend.Core.Updates;

namespace MacExtend.Core.CoreDevice
{
    public enum RuntimeProblem
    {
        /// <summary>The SHA-256 is not the one this build was made with.</summary>
        NotThePackage,
        /// <summary>No python\python.exe or python\pythonw.exe.</summary>
        Incomplete,
        /// <summary>RUNTIME_ID is missing or names another runtime.</summary>
        WrongRuntimeId,
    }

    /// <summary>A zip that is not this build's usable runtime. The message is a fixed sentence without paths.</summary>
    public sealed class RuntimePackageException : Exception
    {
        public RuntimePackageException(RuntimeProblem problem, string message) : base(message)
        {
            Problem = problem;
        }

        public RuntimeProblem Problem { get; }
    }

    /// <summary>
    /// Puts a CoreDevice runtime zip in place as {app}\coredevice: only this build's own package (SHA-256 and
    /// RUNTIME_ID), only inside its folder, and never leaving the machine without the runtime it had.
    /// </summary>
    public static class RuntimeInstaller
    {
        public const string RuntimeIdFile = "RUNTIME_ID";
        /// <summary>TARGET + this is the private copy of the zip while it is checked and extracted.</summary>
        public const string PrivateCopySuffix = ".zip.tmp";

        private const int MoveAttempts = 5;

        // tests make a move fail or skip the wait
        internal static Action<string, string> MoveDirectory = Directory.Move;
        internal static TimeSpan RetryDelay = TimeSpan.FromMilliseconds(500);

        /// <summary>
        /// The zip from a file, which other programs cannot change, rename or delete while it is read; the checks are
        /// those of the Stream overload.
        /// </summary>
        public static void Install(string zipPath, RuntimePackage expected, string target, Action<string> log)
        {
            if (expected == null) throw new ArgumentNullException(nameof(expected));
            using (var source = new FileStream(zipPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                Install(source, expected, target, log);
            }
        }

        /// <summary>
        /// Reads <paramref name="source"/> once into a private copy beside TARGET (TARGET.zip.tmp: no other handle, gone
        /// when closed), hashing the bytes as they are copied, and extracts only from that copy to TARGET.new. So the
        /// bytes installed are the bytes checked, whatever the source (an SMB share, a redirected path) would answer to a
        /// second read. The tree must hold python\python.exe, python\pythonw.exe and the expected RUNTIME_ID. TARGET then
        /// moves to TARGET.old and TARGET.new takes its place; whatever fails after that, TARGET.old comes back.
        /// </summary>
        /// <exception cref="RuntimePackageException">The zip is not <paramref name="expected"/>, or not a usable runtime.</exception>
        /// <exception cref="InvalidDataException">The right bytes, but a zip that cannot be extracted safely.</exception>
        public static void Install(Stream source, RuntimePackage expected, string target, Action<string> log)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (expected == null) throw new ArgumentNullException(nameof(expected));
            log = log ?? (_ => { });
            var fresh = target + ".new";
            var aside = target + ".old";
            try
            {
                using (var copy = new FileStream(target + PrivateCopySuffix, FileMode.Create, FileAccess.ReadWrite, FileShare.None, 81920,
                                                 FileOptions.DeleteOnClose))
                {
                    if (!expected.Matches(FileHash.CopySha256(source, copy)))
                    {
                        throw new RuntimePackageException(RuntimeProblem.NotThePackage, "Gói runtime không phải của bản MacExtend này (sai mã SHA-256).");
                    }
                    copy.Position = 0;
                    UpdateStager.Extract(copy, fresh);
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

        public static void CheckLayout(string dir, string id)
        {
            foreach (var exe in new[] { "python.exe", "pythonw.exe" })
            {
                if (!File.Exists(Path.Combine(dir, "python", exe)))
                {
                    throw new RuntimePackageException(RuntimeProblem.Incomplete, "Gói runtime thiếu python\\" + exe + ".");
                }
            }
            var idFile = Path.Combine(dir, RuntimeIdFile);
            var found = File.Exists(idFile) ? File.ReadAllText(idFile).Trim() : "";
            if (!string.Equals(found, id, StringComparison.Ordinal))
            {
                throw new RuntimePackageException(RuntimeProblem.WrongRuntimeId, "Gói runtime có RUNTIME_ID khác với bản MacExtend này.");
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
