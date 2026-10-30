using System;
using System.IO;
using System.Security.Cryptography;

namespace MacExtend.Core.Updates
{
    public static class FileHash
    {
        /// <returns>Lowercase hex SHA-256, the format update.ini uses.</returns>
        public static string Sha256(string path)
        {
            using (var stream = File.OpenRead(path)) return Sha256(stream);
        }

        /// <returns>Lowercase hex SHA-256 of the rest of <paramref name="stream"/>.</returns>
        public static string Sha256(Stream stream)
        {
            using (var sha = SHA256.Create())
            {
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
            }
        }
    }
}
