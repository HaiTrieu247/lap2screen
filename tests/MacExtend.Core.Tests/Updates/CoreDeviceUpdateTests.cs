using System;
using System.IO;
using MacExtend.Core.CoreDevice;
using MacExtend.Core.Updates;

namespace MacExtend.Core.Tests.Updates;

public sealed class CoreDeviceUpdateTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "macextend-cdupdate-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private string Stage(string id, bool pythonw = true)
    {
        var dir = Path.Combine(_root, "coredevice");
        Directory.CreateDirectory(Path.Combine(dir, "python"));
        File.WriteAllText(Path.Combine(dir, "python", "python.exe"), "p");
        if (pythonw) File.WriteAllText(Path.Combine(dir, "python", "pythonw.exe"), "p");
        if (id != null) File.WriteAllText(Path.Combine(dir, "RUNTIME_ID"), id + "\n");
        return dir;
    }

    [Fact]
    public void IsStagedOnlyWhenTheStagingFolderHoldsACoreDeviceFolder()
    {
        Assert.False(CoreDeviceUpdate.IsStaged(_root));
        Directory.CreateDirectory(Path.Combine(_root, "coredevice"));
        Assert.True(CoreDeviceUpdate.IsStaged(_root));
    }

    [Fact]
    public void ACompleteRuntimeWithTheManifestIdIsAccepted()
    {
        CoreDeviceUpdate.CheckStaged(Stage("cd-2"), "cd-2");
    }

    [Fact]
    public void AnotherRuntimeIdIsRefused()
    {
        var e = Assert.Throws<RuntimePackageException>(() => CoreDeviceUpdate.CheckStaged(Stage("cd-1"), "cd-2"));
        Assert.Equal(RuntimeProblem.WrongRuntimeId, e.Problem);
    }

    [Fact]
    public void AMissingRuntimeIdOrProgramIsRefused()
    {
        Assert.Equal(RuntimeProblem.WrongRuntimeId, Assert.Throws<RuntimePackageException>(() => CoreDeviceUpdate.CheckStaged(Stage(null), "cd-2")).Problem);
        Directory.Delete(Path.Combine(_root, "coredevice"), true);
        Assert.Equal(RuntimeProblem.Incomplete, Assert.Throws<RuntimePackageException>(() => CoreDeviceUpdate.CheckStaged(Stage("cd-2", false), "cd-2")).Problem);
    }

    [Theory]
    [InlineData(@"C:\Program Files\MacExtend\coredevice\python\pythonw.exe", true)]
    [InlineData(@"c:\program files\macextend\COREDEVICE\python\python.exe", true)]
    [InlineData(@"C:\Program Files\MacExtend\coredevice.old\python\python.exe", false)]
    [InlineData(@"C:\Program Files\MacExtend\coredevice2\python.exe", false)]
    [InlineData(@"C:\Program Files\MacExtend\MacExtend.exe", false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    public void ARunningProgramHoldsTheRuntimeOnlyFromInsideItsFolder(string exe, bool inside)
    {
        Assert.Equal(inside, CoreDeviceUpdate.IsInsideRuntime(exe, @"C:\Program Files\MacExtend\coredevice"));
        Assert.Equal(inside, CoreDeviceUpdate.IsInsideRuntime(exe, @"C:\Program Files\MacExtend\coredevice\"));
    }
}
