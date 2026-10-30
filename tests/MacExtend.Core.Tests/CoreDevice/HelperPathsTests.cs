using System;
using MacExtend.Core.CoreDevice;

namespace MacExtend.Core.Tests.CoreDevice;

public class HelperPathsTests
{
    private const string Name = "MacExtend-coredevice-0123456789abcdef0123456789abcdef.result";

    [Theory]
    [InlineData(@"C:\Users\Thịnh O'Hải\AppData\Local\Temp\MacExtend-1.0.70-coredevice-runtime.zip")]
    [InlineData(@"d:\runtime.zip")]
    [InlineData(@"C:\Program Files\MacExtend\x\..\runtime.zip")]
    public void AcceptsALocalDrivePath(string path)
    {
        Assert.True(HelperPaths.IsLocalDrivePath(path));
    }

    [Theory]
    [InlineData(@"\\server\share\runtime.zip")]
    [InlineData(@"\\?\C:\runtime.zip")]
    [InlineData(@"\\.\C:\runtime.zip")]
    [InlineData(@"\\.\pipe\runtime")]
    [InlineData(@"\\?\UNC\server\share\runtime.zip")]
    [InlineData(@"C:runtime.zip")]
    [InlineData(@"runtime.zip")]
    [InlineData(@"..\runtime.zip")]
    [InlineData(@"\runtime.zip")]
    [InlineData(@"C:/Temp/runtime.zip")]
    [InlineData(@"C:\Temp\runtime.zip:stream")]
    [InlineData(@"C:\Temp\*.zip")]
    [InlineData("C:\\Temp\\run\ntime.zip")]
    [InlineData(@"1:\runtime.zip")]
    [InlineData(@"C:\")]
    [InlineData("")]
    [InlineData(null)]
    public void RefusesEveryOtherKindOfPath(string path)
    {
        Assert.False(HelperPaths.IsLocalDrivePath(path));
    }

    [Fact]
    public void TheResultFileTheAppNamesIsAccepted()
    {
        var name = HelperPaths.ResultFileName(Guid.NewGuid());

        Assert.Matches(@"^MacExtend-coredevice-[0-9a-f]{32}\.result$", name);
        Assert.True(HelperPaths.IsResultFile(@"C:\Users\Thịnh O'Hải\AppData\Local\Temp\" + name));
        Assert.True(HelperPaths.IsResultFile(@"C:\Temp\" + Name));
    }

    [Theory]
    // any other name: the elevated helper must not be able to create a script, a profile or a shortcut
    [InlineData(@"C:\Users\u\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Startup\evil.bat")]
    [InlineData(@"C:\Windows\System32\WindowsPowerShell\v1.0\profile.ps1")]
    [InlineData(@"C:\Temp\MacExtend-coredevice-0123456789abcdef0123456789abcdef.result.bat")]
    [InlineData(@"C:\Temp\MacExtend-coredevice-0123456789ABCDEF0123456789ABCDEF.result")]
    [InlineData(@"C:\Temp\MacExtend-coredevice-0123456789abcdef0123456789abcde.result")]
    [InlineData(@"C:\Temp\MacExtend-update-0123456789abcdef0123456789abcdef.result")]
    [InlineData(@"C:\Temp\x-MacExtend-coredevice-0123456789abcdef0123456789abcdef.result")]
    // the right name in the wrong kind of place
    [InlineData(@"\\attacker\share\" + Name)]
    [InlineData(@"\\?\C:\Temp\" + Name)]
    [InlineData(@"C:" + Name)]
    [InlineData(Name)]
    [InlineData("")]
    [InlineData(null)]
    public void RefusesAnyOtherResultPath(string path)
    {
        Assert.False(HelperPaths.IsResultFile(path));
    }
}
