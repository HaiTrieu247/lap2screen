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
                return Hex(sha.ComputeHash(stream));
            }
        }

        /// <summary>
        /// Copies the rest of <paramref name="source"/> to <paramref name="destination"/> in one pass, hashing the bytes
        /// as they are written: the hash is of the copy, whatever the source would answer to a second read.
        /// </summary>
        /// <returns>Lowercase hex SHA-256 of the bytes copied.</returns>
        public static string CopySha256(Stream source, Stream destination)
        {
            using (var sha = SHA256.Create())
            {
                var buffer = new byte[81920];
                int read;
                while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
                {
                    sha.TransformBlock(buffer, 0, read, null, 0);
                    destination.Write(buffer, 0, read);
                }
                sha.TransformFinalBlock(buffer, 0, 0);
                return Hex(sha.Hash);
            }
        }

        private static string Hex(byte[] hash) => BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
    }
}
