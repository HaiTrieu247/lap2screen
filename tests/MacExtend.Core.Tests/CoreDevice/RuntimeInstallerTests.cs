using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using MacExtend.Core.CoreDevice;
using MacExtend.Core.Updates;

namespace MacExtend.Core.Tests.CoreDevice;

public sealed class RuntimeInstallerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "macextend-runtime-" + Guid.NewGuid().ToString("N"));
    private readonly List<string> _log = new List<string>();

    public RuntimeInstallerTests()
    {
        RuntimeInstaller.RetryDelay = TimeSpan.FromMilliseconds(1);
        Directory.CreateDirectory(App);
    }

    public void Dispose()
    {
        RuntimeInstaller.MoveDirectory = Directory.Move;
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private string App => Path.Combine(_root, "app");
    private string Target => Path.Combine(App, "coredevice");
    private string ZipPath => Path.Combine(_root, "runtime.zip");

    private static (string Name, string Text)[] Runtime(string id = "cd-1") => new[]
    {
        ("python/python.exe", "new python"),
        ("python/pythonw.exe", "new pythonw"),
        ("python/Lib/site.py", "lib"),
        ("RUNTIME_ID", id + "\n"),
    };

    private void WriteZip(params (string Name, string Text)[] entries)
    {
        using (var file = File.Create(ZipPath))
        using (var zip = new ZipArchive(file, ZipArchiveMode.Create))
        {
            foreach (var (name, text) in entries)
            {
                using (var w = new StreamWriter(zip.CreateEntry(name).Open())) w.Write(text);
            }
        }
    }

    /// <summary>The package metadata a CI build would embed for the zip written last.</summary>
    private RuntimePackage PackageForZip(string id = "cd-1") => Package(id, FileHash.Sha256(ZipPath), new FileInfo(ZipPath).Length);

    private static RuntimePackage Package(string id, string sha256, long size)
    {
        var meta = new Dictionary<string, string>
        {
            ["CoreDeviceRuntimeId"] = id,
            ["CoreDeviceRuntimeFile"] = "MacExtend-1.0.70-coredevice-runtime.zip",
            ["CoreDeviceRuntimeSha256"] = sha256,
            ["CoreDeviceRuntimeSize"] = size.ToString(CultureInfo.InvariantCulture),
        };
        return RuntimePackage.FromMetadata(k => meta.TryGetValue(k, out var v) ? v : null);
    }

    private void InstallOldRuntime()
    {
        Write(Target, "python/python.exe", "old python");
        Write(Target, "python/pythonw.exe", "old pythonw");
        Write(Target, "RUNTIME_ID", "cd-0\n");
        Write(Target, "stale.txt", "only in the old runtime");
    }

    private static void Write(string dir, string relative, string content)
    {
        var path = Path.Combine(dir, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, content);
    }

    private string Read(string relative) => File.ReadAllText(Path.Combine(Target, relative));

    private void AssertOldRuntimeUntouched()
    {
        Assert.Equal("old python", Read("python/python.exe"));
        Assert.Equal("cd-0\n", Read("RUNTIME_ID"));
        Assert.True(File.Exists(Path.Combine(Target, "stale.txt")));
        AssertNoLeftovers();
    }

    private void AssertNoLeftovers()
    {
        Assert.False(Directory.Exists(Target + ".new"));
        Assert.False(Directory.Exists(Target + ".old"));
    }

    [Fact]
    public void ReplacesTheOldRuntimeAsAWhole()
    {
        InstallOldRuntime();
        WriteZip(Runtime());

        RuntimeInstaller.Install(ZipPath, PackageForZip(), Target, _log.Add);

        Assert.Equal("new python", Read("python/python.exe"));
        Assert.Equal("new pythonw", Read("python/pythonw.exe"));
        Assert.Equal("cd-1\n", Read("RUNTIME_ID"));
        Assert.False(File.Exists(Path.Combine(Target, "stale.txt")));
        AssertNoLeftovers();
        Assert.Contains("runtime cd-1 installed", _log);
    }

    [Fact]
    public void InstallsWhenNoRuntimeWasThere()
    {
        WriteZip(Runtime());

        RuntimeInstaller.Install(ZipPath, PackageForZip(), Target, _log.Add);

        Assert.Equal("new python", Read("python/python.exe"));
        AssertNoLeftovers();
    }

    [Fact]
    public void ClearsTheLeftoversOfAnInterruptedInstall()
    {
        InstallOldRuntime();
        Write(Target + ".new", "python/python.exe", "half extracted");
        Write(Target + ".old", "python/python.exe", "older still");
        WriteZip(Runtime());

        RuntimeInstaller.Install(ZipPath, PackageForZip(), Target, _log.Add);

        Assert.Equal("new python", Read("python/python.exe"));
        AssertNoLeftovers();
    }

    [Fact]
    public void RefusesAZipThatIsNotThisBuildsPackage()
    {
        InstallOldRuntime();
        WriteZip(Runtime());
        var expected = PackageForZip();
        WriteZip(Runtime().Concat(new[] { ("python/Lib/extra.py", "one more file") }).ToArray());

        Assert.Throws<InvalidDataException>(() => RuntimeInstaller.Install(ZipPath, expected, Target, _log.Add));

        AssertOldRuntimeUntouched();
    }

    [Fact]
    public void RefusesTheRightBytesUnderAnotherRuntimeId()
    {
        InstallOldRuntime();
        WriteZip(Runtime("cd-2"));

        Assert.Throws<InvalidDataException>(() => RuntimeInstaller.Install(ZipPath, PackageForZip("cd-1"), Target, _log.Add));

        AssertOldRuntimeUntouched();
    }

    [Theory]
    [InlineData("python/python.exe")]
    [InlineData("python/pythonw.exe")]
    [InlineData("RUNTIME_ID")]
    public void RefusesAZipWithoutAPartTheAppNeeds(string missing)
    {
        InstallOldRuntime();
        WriteZip(Runtime().Where(e => e.Name != missing).ToArray());

        Assert.Throws<InvalidDataException>(() => RuntimeInstaller.Install(ZipPath, PackageForZip(), Target, _log.Add));

        AssertOldRuntimeUntouched();
    }

    [Fact]
    public void RefusesAnEntryOutsideItsFolderEvenWithTheRightHash()
    {
        InstallOldRuntime();
        WriteZip(Runtime().Concat(new[] { ("../escape.txt", "x") }).ToArray());

        Assert.Throws<InvalidDataException>(() => RuntimeInstaller.Install(ZipPath, PackageForZip(), Target, _log.Add));

        Assert.False(File.Exists(Path.Combine(App, "escape.txt")));
        AssertOldRuntimeUntouched();
    }

    [Theory]
    [InlineData(typeof(UnauthorizedAccessException))]
    [InlineData(typeof(IOException))]
    [InlineData(typeof(InvalidOperationException))]
    public void PutsThePreviousRuntimeBackWhenTheSwapFails(Type failure)
    {
        InstallOldRuntime();
        WriteZip(Runtime());
        RuntimeInstaller.MoveDirectory = (from, to) =>
        {
            if (from.EndsWith(".new", StringComparison.Ordinal)) throw (Exception)Activator.CreateInstance(failure, "refused");
            Directory.Move(from, to);
        };

        var thrown = Assert.ThrowsAny<Exception>(() => RuntimeInstaller.Install(ZipPath, PackageForZip(), Target, _log.Add));

        Assert.IsType(failure, thrown);
        AssertOldRuntimeUntouched();
    }

    [Fact]
    public void RetriesAMoveThatIsRefusedForAMoment()
    {
        InstallOldRuntime();
        WriteZip(Runtime());
        var refusals = 2;
        RuntimeInstaller.MoveDirectory = (from, to) =>
        {
            // tunneld's pythonw.exe was just ended and still holds its files
            if (from == Target && refusals-- > 0) throw new IOException("in use");
            Directory.Move(from, to);
        };

        RuntimeInstaller.Install(ZipPath, PackageForZip(), Target, _log.Add);

        Assert.Equal("new python", Read("python/python.exe"));
        AssertNoLeftovers();
    }

    [Fact]
    public void KeepsTheOldRuntimeWhenItStaysInUse()
    {
        InstallOldRuntime();
        WriteZip(Runtime());
        RuntimeInstaller.MoveDirectory = (from, to) =>
        {
            if (from == Target) throw new IOException("in use");
            Directory.Move(from, to);
        };

        Assert.Throws<IOException>(() => RuntimeInstaller.Install(ZipPath, PackageForZip(), Target, _log.Add));

        AssertOldRuntimeUntouched();
    }

    [Fact]
    public void NeedsThePackageOfThisBuild()
    {
        WriteZip(Runtime());

        Assert.Throws<ArgumentNullException>(() => RuntimeInstaller.Install(ZipPath, null, Target, _log.Add));
    }
}
