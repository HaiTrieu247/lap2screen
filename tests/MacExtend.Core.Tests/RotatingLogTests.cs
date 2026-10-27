using System;
using System.IO;
using System.Linq;

namespace MacExtend.Core.Tests;

public sealed class RotatingLogTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "macextend-log-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    [Fact]
    public void CreatesMissingDirectoryAndWritesTimestampedLines()
    {
        var path = Path.Combine(_dir, "logs", "uxplay.log");
        using (var log = new RotatingLog(path, 10_000))
        {
            log.WriteLine("hello Thịnh");
        }

        var line = File.ReadAllLines(path).Single();
        Assert.Matches(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2} hello Thịnh$", line);
    }

    [Fact]
    public void RotatesWhenFileExceedsLimit()
    {
        var path = Path.Combine(_dir, "uxplay.log");
        using (var log = new RotatingLog(path, 200))
        {
            for (var i = 0; i < 20; i++) log.WriteLine("line number " + i);
        }

        Assert.True(File.Exists(path + ".1"));
        Assert.True(new FileInfo(path).Length <= 200 + 64);
        Assert.Contains("line number 19", File.ReadAllText(path));
    }

    [Fact]
    public void AppendsToExistingFile()
    {
        var path = Path.Combine(_dir, "uxplay.log");
        using (var log = new RotatingLog(path, 10_000)) log.WriteLine("first");
        using (var log = new RotatingLog(path, 10_000)) log.WriteLine("second");

        Assert.Equal(2, File.ReadAllLines(path).Length);
    }

    [Fact]
    public void WriteNeverThrowsWhenRotationFails()
    {
        var path = Path.Combine(_dir, "uxplay.log");
        Directory.CreateDirectory(path + ".1"); // rotation target cannot be replaced
        using (var log = new RotatingLog(path, 100))
        {
            for (var i = 0; i < 20; i++) log.WriteLine("line number " + i);
        }

        Assert.Contains("line number 19", File.ReadAllText(path));
    }
}
