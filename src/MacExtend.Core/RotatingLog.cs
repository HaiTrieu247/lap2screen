using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace MacExtend.Core
{
    /// <summary>
    /// Thread-safe append-only log that keeps one previous file ("path.1") when it grows past a limit.
    /// Logging must never take the app down: I/O failures drop the line and the file is reopened on the next write.
    /// </summary>
    public sealed class RotatingLog : IDisposable
    {
        private readonly string _path;
        private readonly long _maxBytes;
        private readonly object _lock = new object();
        private StreamWriter _writer;
        private long _rotateAt;
        private bool _disposed;

        public RotatingLog(string path, long maxBytes)
        {
            _path = path;
            _maxBytes = maxBytes;
            _rotateAt = maxBytes;
            lock (_lock) Guard(Open);
        }

        public void WriteLine(string line)
        {
            lock (_lock)
            {
                if (_disposed) return;
                Guard(() =>
                {
                    if (_writer == null) Open();
                    if (_writer.BaseStream.Length >= _rotateAt) Rotate();
                    _writer.Write(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss ", CultureInfo.InvariantCulture));
                    _writer.Write(line);
                    _writer.Write('\n');
                });
            }
        }

        public void Dispose()
        {
            lock (_lock)
            {
                _disposed = true;
                Close();
            }
        }

        private void Open()
        {
            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var stream = new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
            _writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
        }

        private void Rotate()
        {
            Close();
            try
            {
                var old = _path + ".1";
                if (File.Exists(old)) File.Delete(old);
                File.Move(_path, old);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                // another program holds the old file: keep appending and try again later
            }
            Open();
            _rotateAt = _writer.BaseStream.Length + _maxBytes;
        }

        private void Guard(Action action)
        {
            try
            {
                action();
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ObjectDisposedException)
            {
                Close();
            }
        }

        private void Close()
        {
            try
            {
                _writer?.Dispose();
            }
            catch (IOException)
            {
            }
            _writer = null;
        }
    }
}
