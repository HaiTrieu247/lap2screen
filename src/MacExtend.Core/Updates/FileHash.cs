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
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(path))
            {
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
            }
        }
    }
}
