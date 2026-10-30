namespace MacExtend.Core.Tests;

public class InstallLayoutTests
{
    [Theory]
    [InlineData("unins000.exe")]
    [InlineData("UNINS001.EXE")]
    [InlineData(@"C:\Program Files\MacExtend\unins000.exe")]
    [InlineData("C:/Program Files/MacExtend/unins000.exe")]
    public void AnInnoUninstallerMeansTheInstalledCopy(string file)
    {
        Assert.True(InstallLayout.IsInstalledCopy(new[] { "MacExtend.exe", file }));
    }

    [Theory]
    [InlineData("unins000.dat")]
    [InlineData("unins000.exe.old")]
    [InlineData("MacExtend.exe")]
    [InlineData(@"C:\unins000.exe\MacExtend.exe")]
    public void AnythingElseIsAPortableCopy(string file)
    {
        Assert.False(InstallLayout.IsInstalledCopy(new[] { file }));
    }

    [Fact]
    public void NoFilesIsAPortableCopy()
    {
        Assert.False(InstallLayout.IsInstalledCopy(new string[0]));
        Assert.False(InstallLayout.IsInstalledCopy(null));
    }
}
