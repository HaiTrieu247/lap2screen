using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace MacExtend.Core
{
    /// <summary>Thread-safe append-only log that keeps one previous file ("path.1") when it grows past a limit.</summary>
    public sealed class RotatingLog : IDisposable
    {
        private readonly string _path;
        private readonly long _maxBytes;
        private readonly object _lock = new object();
        private StreamWriter _writer;

        public RotatingLog(string path, long maxBytes)
        {
            _path = path;
            _maxBytes = maxBytes;
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            Open();
        }

        public void WriteLine(string line)
        {
            lock (_lock)
            {
                if (_writer == null) return;
                if (_writer.BaseStream.Length >= _maxBytes) Rotate();
                _writer.Write(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss ", CultureInfo.InvariantCulture));
                _writer.Write(line);
                _writer.Write('\n');
            }
        }

        public void Dispose()
        {
            lock (_lock)
            {
                _writer?.Dispose();
                _writer = null;
            }
        }

        private void Open()
        {
            var stream = new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
            _writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
        }

        private void Rotate()
        {
            _writer.Dispose();
            var old = _path + ".1";
            if (File.Exists(old)) File.Delete(old);
            File.Move(_path, old);
            Open();
        }
    }
}
