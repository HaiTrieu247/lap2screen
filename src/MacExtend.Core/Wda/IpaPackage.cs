using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace MacExtend.Core.Wda
{
    /// <summary>
    /// The WebDriverAgent build the setup window fetches: Appium's unsigned real-device runner, pinned like the Python
    /// runtime. Changing it means a new URL, size and SHA-256 here and table W of docs/TESTING.md again.
    /// </summary>
    public static class WdaPackage
    {
        public const string Version = "16.15.3";
        public const string Url = "https://github.com/appium/WebDriverAgent/releases/download/v" + Version + "/WebDriverAgentRunner-Runner.zip";
        public const long Size = 1145983;
        public const string Sha256 = "2176793538d902f6b1e61291e9997f68d306541f222ea715d256a7c6ce9d4fc5";
        public const string IpaName = "WebDriverAgentRunner.ipa";
        /// <summary>A download larger than this is refused before it is read whole.</summary>
        public const long MaxDownload = 16 * 1024 * 1024;

        /// <summary>The download is exactly the pinned file.</summary>
        public static bool IsPinned(byte[] data)
        {
            if (data == null || data.LongLength != Size) return false;
            using (var sha = SHA256.Create())
            {
                var hex = string.Concat(sha.ComputeHash(data).Select(b => b.ToString("x2", System.Globalization.CultureInfo.InvariantCulture)));
                return hex == Sha256;
            }
        }
    }

    /// <summary>
    /// Turns the runner zip (WebDriverAgentRunner-Runner.app/...) into an .ipa (Payload/WebDriverAgentRunner-Runner.app/...)
    /// that Sideloadly signs and installs. The ipa is written by <see cref="IpaWriter"/> as a Unix zip, so the Mach-O
    /// executables keep mode 0755 whatever system builds it (System.IO.Compression marks a zip made on Windows as such,
    /// and readers then ignore Unix modes).
    /// </summary>
    public static class IpaPackage
    {
        public const int MaxEntries = 4096;
        public const long MaxEntrySize = 64L * 1024 * 1024;

        /// <exception cref="InvalidDataException">Not a zip, no .app inside, or an entry too large.</exception>
        public static void FromRunnerZip(Stream runnerZip, Stream ipa)
        {
            if (runnerZip == null) throw new ArgumentNullException(nameof(runnerZip));
            if (ipa == null) throw new ArgumentNullException(nameof(ipa));
            ZipArchive source;
            try
            {
                source = new ZipArchive(runnerZip, ZipArchiveMode.Read, leaveOpen: true);
            }
            catch (Exception e) when (e is InvalidDataException || e is ArgumentException)
            {
                throw new InvalidDataException("not a zip archive");
            }
            using (source)
            {
                if (source.Entries.Count > MaxEntries) throw new InvalidDataException("too many entries");
                var names = source.Entries.Select(e => e.FullName).Where(IsKept).ToList();
                var roots = names.Where(n => n.IndexOf(".app/", StringComparison.Ordinal) >= 0)
                                 .Select(n => n.Substring(0, n.IndexOf(".app/", StringComparison.Ordinal) + 5))
                                 .Distinct(StringComparer.Ordinal)
                                 .OrderBy(n => n.Length)
                                 .ToList();
                if (roots.Count == 0) throw new InvalidDataException("no .app bundle inside");
                // the outermost bundle, e.g. "WebDriverAgentRunner-Runner.app/"; the folder holding it is dropped
                var appRoot = roots[0];
                var trimmed = appRoot.TrimEnd('/');
                var prefix = appRoot.Substring(0, trimmed.LastIndexOf('/') + 1);

                using (var writer = new IpaWriter(ipa))
                {
                    writer.AddDirectory("Payload/", DateTime.Now);
                    foreach (var entry in source.Entries.Where(e => IsKept(e.FullName) && e.FullName.StartsWith(appRoot, StringComparison.Ordinal))
                                                        .OrderBy(e => e.FullName, StringComparer.Ordinal))
                    {
                        var name = "Payload/" + entry.FullName.Substring(prefix.Length);
                        var when = entry.LastWriteTime.DateTime;
                        if (entry.FullName.EndsWith("/", StringComparison.Ordinal))
                        {
                            writer.AddDirectory(name, when);
                            continue;
                        }
                        if (entry.Length > MaxEntrySize) throw new InvalidDataException("an entry is too large");
                        writer.AddFile(name, ReadAll(entry), when);
                    }
                }
            }
        }

        /// <summary>A Mach-O file (thin or fat, either byte order): an executable or a library, mode 0755.</summary>
        public static bool IsMachO(byte[] data)
        {
            if (data == null || data.Length < 4) return false;
            var magic = (uint)(data[0] << 24 | data[1] << 16 | data[2] << 8 | data[3]);
            switch (magic)
            {
                case 0xFEEDFACE:
                case 0xFEEDFACF:
                case 0xCEFAEDFE:
                case 0xCFFAEDFE:
                case 0xCAFEBABE:
                case 0xBEBAFECA:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>No absolute path, no "..", no macOS metadata (__MACOSX, ._ files, .DS_Store).</summary>
        internal static bool IsKept(string name)
        {
            if (string.IsNullOrEmpty(name) || name.StartsWith("/", StringComparison.Ordinal) || name.IndexOf('\\') >= 0 || name.IndexOf(':') >= 0) return false;
            if (name.StartsWith("__MACOSX/", StringComparison.Ordinal)) return false;
            var parts = name.Split('/');
            if (parts.Any(p => p == ".." || p == ".")) return false;
            var last = parts[parts.Length - 1].Length > 0 ? parts[parts.Length - 1] : parts.Length > 1 ? parts[parts.Length - 2] : "";
            return !last.StartsWith("._", StringComparison.Ordinal) && last != ".DS_Store";
        }

        private static byte[] ReadAll(ZipArchiveEntry entry)
        {
            using (var input = entry.Open())
            using (var buffer = new MemoryStream())
            {
                var chunk = new byte[81920];
                int read;
                while ((read = input.Read(chunk, 0, chunk.Length)) > 0)
                {
                    if (buffer.Length + read > MaxEntrySize) throw new InvalidDataException("an entry is too large");
                    buffer.Write(chunk, 0, read);
                }
                return buffer.ToArray();
            }
        }
    }

    /// <summary>
    /// A small zip writer for the ipa: deflated files and directory entries, UTF-8 names, "made by Unix" so every
    /// entry carries its mode (directories 0755, Mach-O files 0755, the rest 0644). No ZIP64: the ipa is a few megabytes.
    /// </summary>
    public sealed class IpaWriter : IDisposable
    {
        private const ushort VersionNeeded = 20;
        private const ushort MadeByUnix = (3 << 8) | 20;
        private const ushort Utf8Names = 0x0800;

        private readonly Stream _out;
        private readonly List<Central> _central = new List<Central>();
        private bool _closed;

        public IpaWriter(Stream output)
        {
            _out = output ?? throw new ArgumentNullException(nameof(output));
        }

        public void AddDirectory(string name, DateTime when)
        {
            if (!name.EndsWith("/", StringComparison.Ordinal)) name += "/";
            Add(name, new byte[0], when, 0x41ED /* drwxr-xr-x */, true);
        }

        public void AddFile(string name, byte[] data, DateTime when) =>
            Add(name, data, when, IpaPackage.IsMachO(data) ? 0x81ED /* -rwxr-xr-x */ : 0x81A4 /* -rw-r--r-- */, false);

        public void Dispose()
        {
            if (_closed) return;
            _closed = true;
            var start = _out.Position;
            foreach (var c in _central)
            {
                Write32(0x02014B50);
                Write16(MadeByUnix);
                Write16(VersionNeeded);
                Write16(Utf8Names);
                Write16(c.Method);
                Write16(c.Time);
                Write16(c.Date);
                Write32(c.Crc);
                Write32((uint)c.Compressed);
                Write32((uint)c.Size);
                Write16((ushort)c.Name.Length);
                Write16(0);
                Write16(0);
                Write16(0);
                Write16(0);
                Write32(((uint)c.Mode << 16) | (c.Directory ? 0x10u : 0u));
                Write32((uint)c.Offset);
                _out.Write(c.Name, 0, c.Name.Length);
            }
            var size = _out.Position - start;
            if (_central.Count > ushort.MaxValue || start > uint.MaxValue) throw new InvalidDataException("the ipa would need ZIP64");
            Write32(0x06054B50);
            Write16(0);
            Write16(0);
            Write16((ushort)_central.Count);
            Write16((ushort)_central.Count);
            Write32((uint)size);
            Write32((uint)start);
            Write16(0);
            _out.Flush();
        }

        private void Add(string name, byte[] data, DateTime when, int mode, bool directory)
        {
            if (_closed) throw new ObjectDisposedException(nameof(IpaWriter));
            var nameBytes = Encoding.UTF8.GetBytes(name);
            if (nameBytes.Length > ushort.MaxValue) throw new InvalidDataException("an entry name is too long");
            byte[] stored;
            ushort method;
            if (data.Length == 0)
            {
                stored = data;
                method = 0;
            }
            else
            {
                using (var packed = new MemoryStream())
                {
                    using (var deflate = new DeflateStream(packed, CompressionLevel.Optimal, leaveOpen: true)) deflate.Write(data, 0, data.Length);
                    stored = packed.ToArray();
                }
                method = 8;
            }
            if (_out.Position > uint.MaxValue || stored.LongLength > uint.MaxValue) throw new InvalidDataException("the ipa would need ZIP64");
            var (time, date) = DosTime(when);
            var entry = new Central
            {
                Name = nameBytes,
                Method = method,
                Time = time,
                Date = date,
                Crc = Crc32.Compute(data),
                Compressed = stored.LongLength,
                Size = data.LongLength,
                Offset = _out.Position,
                Mode = mode,
                Directory = directory,
            };
            Write32(0x04034B50);
            Write16(VersionNeeded);
            Write16(Utf8Names);
            Write16(method);
            Write16(time);
            Write16(date);
            Write32(entry.Crc);
            Write32((uint)entry.Compressed);
            Write32((uint)entry.Size);
            Write16((ushort)nameBytes.Length);
            Write16(0);
            _out.Write(nameBytes, 0, nameBytes.Length);
            _out.Write(stored, 0, stored.Length);
            _central.Add(entry);
        }

        private static (ushort Time, ushort Date) DosTime(DateTime when)
        {
            if (when.Year < 1980) when = new DateTime(1980, 1, 1);
            if (when.Year > 2107) when = new DateTime(2107, 12, 31, 23, 59, 58);
            var time = (ushort)(when.Hour << 11 | when.Minute << 5 | when.Second / 2);
            var date = (ushort)((when.Year - 1980) << 9 | when.Month << 5 | when.Day);
            return (time, date);
        }

        private void Write16(ushort v)
        {
            _out.WriteByte((byte)v);
            _out.WriteByte((byte)(v >> 8));
        }

        private void Write32(uint v)
        {
            Write16((ushort)v);
            Write16((ushort)(v >> 16));
        }

        private sealed class Central
        {
            public byte[] Name;
            public ushort Method;
            public ushort Time;
            public ushort Date;
            public uint Crc;
            public long Compressed;
            public long Size;
            public long Offset;
            public int Mode;
            public bool Directory;
        }
    }

    /// <summary>CRC-32 (IEEE 802.3), as zip entries carry it.</summary>
    public static class Crc32
    {
        private static readonly uint[] Table = Build();

        public static uint Compute(byte[] data)
        {
            var crc = 0xFFFFFFFFu;
            foreach (var b in data) crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
            return crc ^ 0xFFFFFFFFu;
        }

        private static uint[] Build()
        {
            var table = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                var c = i;
                for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                table[i] = c;
            }
            return table;
        }
    }
}
