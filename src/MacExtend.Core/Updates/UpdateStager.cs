using System;
using System.IO;
using System.IO.Compression;

namespace MacExtend.Core.Updates
{
    public static class UpdateStager
    {
        /// <summary>
        /// Extracts a downloaded zip into an empty <paramref name="targetDir"/>. Every entry is checked
        /// first: an entry that would land outside the target ("..\") rejects the whole zip.
        /// </summary>
        public static void Extract(string zipPath, string targetDir)
        {
            using (var stream = File.OpenRead(zipPath)) Extract(stream, targetDir);
        }

        /// <summary>Same checks as <see cref="Extract(string, string)"/>, from an open stream (the caller may have hashed it first).</summary>
        public static void Extract(Stream zipStream, string targetDir)
        {
            var root = Path.GetFullPath(targetDir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            using (var zip = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: true))
            {
                foreach (var entry in zip.Entries)
                {
                    if (!Destination(root, entry.FullName).StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidDataException("Update package entry escapes its folder: " + entry.FullName);
                    }
                }

                if (Directory.Exists(targetDir)) Directory.Delete(targetDir, true);
                Directory.CreateDirectory(targetDir);
                foreach (var entry in zip.Entries)
                {
                    var dest = Destination(root, entry.FullName);
                    if (entry.FullName.EndsWith("/") || entry.FullName.EndsWith("\\"))
                    {
                        Directory.CreateDirectory(dest);
                        continue;
                    }
                    Directory.CreateDirectory(Path.GetDirectoryName(dest));
                    entry.ExtractToFile(dest, true);
                }
            }
        }

        private static string Destination(string root, string entryName) => Path.GetFullPath(Path.Combine(root, entryName));
    }
}
