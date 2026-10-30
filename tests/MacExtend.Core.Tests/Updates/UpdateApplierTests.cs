using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using MacExtend.Core.Updates;

namespace MacExtend.Core.Tests.Updates;

public sealed class UpdateApplierTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "macextend-update-" + Guid.NewGuid().ToString("N"));
    private string Install => Path.Combine(_root, "install");
    private string Staging => Path.Combine(_root, "staging");

    public UpdateApplierTests()
    {
        UpdateApplier.RetryDelay = TimeSpan.FromMilliseconds(1);
        Write(Install, "MacExtend.exe", "old exe");
        Write(Install, "MacExtend.Core.dll", "old core");
        Write(Install, "settings-note.txt", "keep me");
        Write(Install, "uxplay/bin/uxplay.exe", "old uxplay");
        Write(Install, "uxplay/RUNTIME_ID", "rt-1");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private static void Write(string dir, string relative, string content)
    {
        var path = Path.Combine(dir, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, content);
    }

    private string Read(string relative) => File.ReadAllText(Path.Combine(Install, relative));

    [Fact]
    public void CoreDeviceRuntimeReplacesItsWholeFolder()
    {
        Write(Install, "coredevice/python/python.exe", "old python");
        Write(Install, "coredevice/RUNTIME_ID", "cd-1");
        Write(Staging, "coredevice/python/python.exe", "new python");
        Write(Staging, "coredevice/RUNTIME_ID", "cd-2");

        UpdateApplier.Apply(Staging, Install, _ => { });

        Assert.Equal("new python", Read("coredevice/python/python.exe"));
        Assert.Equal("cd-2", Read("coredevice/RUNTIME_ID"));
        Assert.Equal("old uxplay", Read("uxplay/bin/uxplay.exe"));
        Assert.False(Directory.Exists(Path.Combine(Install, "coredevice.old")));
    }

    [Fact]
    public void CopiesAppFilesAndKeepsUnrelatedFiles()
    {
        Write(Staging, "app/MacExtend.exe", "new exe");
        Write(Staging, "app/MacExtend.Core.dll", "new core");
        Write(Staging, "app/licenses/new.txt", "new license");

        UpdateApplier.Apply(Staging, Install, _ => { });

        Assert.Equal("new exe", Read("MacExtend.exe"));
        Assert.Equal("new core", Read("MacExtend.Core.dll"));
        Assert.Equal("new license", Read("licenses/new.txt"));
        Assert.Equal("keep me", Read("settings-note.txt"));
        Assert.Equal("old uxplay", Read("uxplay/bin/uxplay.exe"));
        Assert.Empty(Directory.GetFiles(Install, "*.old", SearchOption.AllDirectories));
    }

    [Fact]
    public void RuntimeReplacesTheWholeUxplayFolder()
    {
        Write(Staging, "app/MacExtend.exe", "new exe");
        Write(Staging, "runtime/bin/uxplay.exe", "new uxplay");
        Write(Staging, "runtime/RUNTIME_ID", "rt-2");

        UpdateApplier.Apply(Staging, Install, _ => { });

        Assert.Equal("new uxplay", Read("uxplay/bin/uxplay.exe"));
        Assert.Equal("rt-2", Read("uxplay/RUNTIME_ID"));
        Assert.False(Directory.Exists(Path.Combine(Install, "uxplay.old")));
        Assert.True(Directory.Exists(Path.Combine(Staging, "runtime")), "staging stays usable for a retry");
    }

    [Fact]
    public void RollsBackWhenAFileCannotBeReplaced()
    {
        Write(Staging, "app/MacExtend.exe", "new exe");
        Write(Staging, "app/zz/blocked.txt", "cannot land");
        Write(Install, "zz", "a file where the update needs a folder");
        Write(Staging, "runtime/bin/uxplay.exe", "new uxplay");

        Assert.ThrowsAny<IOException>(() => UpdateApplier.Apply(Staging, Install, _ => { }));

        Assert.Equal("old exe", Read("MacExtend.exe"));
        Assert.Equal("old uxplay", Read("uxplay/bin/uxplay.exe"));
        Assert.Equal("a file where the update needs a folder", Read("zz"));
        Assert.False(Directory.Exists(Path.Combine(Install, "uxplay.old")));
        Assert.Empty(Directory.GetFiles(Install, "*.old", SearchOption.AllDirectories));
    }

    [Fact]
    public void CleanupRemovesLeftoversFromAnInterruptedUpdate()
    {
        Write(Install, "MacExtend.exe.old", "stale");
        Write(Install, "uxplay.old/bin/uxplay.exe", "stale");

        UpdateApplier.CleanupLeftovers(Install);

        Assert.False(File.Exists(Path.Combine(Install, "MacExtend.exe.old")));
        Assert.False(Directory.Exists(Path.Combine(Install, "uxplay.old")));
        Assert.Equal("old exe", Read("MacExtend.exe"));
    }
}

public sealed class UpdateStagerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "macextend-stager-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private string MakeZip(params (string name, string content)[] entries)
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, Guid.NewGuid().ToString("N") + ".zip");
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            foreach (var (name, content) in entries)
            {
                using (var w = new StreamWriter(zip.CreateEntry(name).Open())) w.Write(content);
            }
        }
        return path;
    }

    [Fact]
    public void ExtractsNestedFiles()
    {
        var zip = MakeZip(("MacExtend.exe", "exe"), ("licenses/OFL.txt", "ofl"));
        var target = Path.Combine(_root, "out");

        UpdateStager.Extract(zip, target);

        Assert.Equal("exe", File.ReadAllText(Path.Combine(target, "MacExtend.exe")));
        Assert.Equal("ofl", File.ReadAllText(Path.Combine(target, "licenses", "OFL.txt")));
    }

    [Fact]
    public void RejectsEntriesOutsideTarget()
    {
        var zip = MakeZip(("ok.txt", "fine"), ("../evil.txt", "escape"));
        var target = Path.Combine(_root, "out");

        Assert.Throws<InvalidDataException>(() => UpdateStager.Extract(zip, target));

        Assert.False(File.Exists(Path.Combine(_root, "evil.txt")));
        Assert.False(File.Exists(Path.Combine(target, "ok.txt")), "nothing is written when any entry is unsafe");
    }

    [Fact]
    public void ReplacesAPreviousPartialExtraction()
    {
        var target = Path.Combine(_root, "out");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "stale.txt"), "old");

        UpdateStager.Extract(MakeZip(("fresh.txt", "new")), target);

        Assert.False(File.Exists(Path.Combine(target, "stale.txt")));
        Assert.True(File.Exists(Path.Combine(target, "fresh.txt")));
    }
}

public class FileHashTests
{
    [Fact]
    public void Sha256OfKnownContent()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "abc");
            Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", FileHash.Sha256(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
