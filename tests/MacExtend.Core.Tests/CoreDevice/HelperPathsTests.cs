using MacExtend.Core.CoreDevice;

namespace MacExtend.Core.Tests.CoreDevice;

public class HelperPathsTests
{
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
}
