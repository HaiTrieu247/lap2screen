using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using MacExtend.Core.Updates;

namespace MacExtend.Core.Tests.Updates;

public sealed class UpdateStagerStreamTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "macextend-stager-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private static MemoryStream Zip(params (string Name, string Text)[] entries)
    {
        var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, text) in entries)
            {
                using (var w = new StreamWriter(zip.CreateEntry(name).Open())) w.Write(text);
            }
        }
        ms.Position = 0;
        return ms;
    }

    [Fact]
    public void ExtractsFromAStream()
    {
        using (var zip = Zip(("python/python.exe", "py"), ("RUNTIME_ID", "cd-1")))
        {
            UpdateStager.Extract(zip, _root);
        }

        Assert.Equal("py", File.ReadAllText(Path.Combine(_root, "python", "python.exe")));
    }

    [Fact]
    public void RejectsEntriesOutsideTarget()
    {
        using (var zip = Zip(("../escape.txt", "x")))
        {
            Assert.Throws<InvalidDataException>(() => UpdateStager.Extract(zip, Path.Combine(_root, "t")));
        }
        Assert.False(File.Exists(Path.Combine(_root, "escape.txt")));
    }

    [Fact]
    public void HashesAStreamToTheKnownSha256Vector()
    {
        using (var s = new MemoryStream(Encoding.ASCII.GetBytes("abc")))
        {
            Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", FileHash.Sha256(s));
        }
    }
}
