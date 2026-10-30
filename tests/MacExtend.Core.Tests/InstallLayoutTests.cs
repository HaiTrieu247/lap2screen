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

    private const string ProgramFiles = @"C:\Program Files";

    [Theory]
    [InlineData(@"C:\Program Files\MacExtend\")]
    [InlineData(@"C:\Program Files\MacExtend")]
    [InlineData(@"c:\program files\MacExtend\")]
    [InlineData(@"C:/Program Files/MacExtend/")]
    [InlineData(@"C:\Program Files\.\MacExtend\")]
    [InlineData(@"C:\Program Files\Other\..\MacExtend\")]
    [InlineData(@"C:\Program Files\\MacExtend\")]
    public void AnInstalledCopyInsideProgramFilesUsesTheTask(string installDir)
    {
        Assert.True(InstallLayout.AllowsTask(true, installDir, ProgramFiles));
        Assert.True(InstallLayout.AllowsTask(true, installDir, ProgramFiles + @"\"));
    }

    [Theory]
    // a folder the user picked in the installer: writable by the user, its parent included
    [InlineData(@"C:\MacExtend\")]
    [InlineData(@"C:\Users\Thịnh O'Hải\MacExtend\")]
    [InlineData(@"C:\Program Files (x86)\MacExtend\")]
    [InlineData(@"D:\Program Files\MacExtend\")]
    // prefix tricks
    [InlineData(@"C:\Program FilesX\MacExtend\")]
    [InlineData(@"C:\Program Files\..\x\")]
    [InlineData(@"C:\Program Files\..\Program FilesX\MacExtend\")]
    [InlineData(@"C:\..\Program Files\MacExtend\")]
    // not a plain drive path: never trusted
    [InlineData(@"\\server\share\Program Files\MacExtend\")]
    [InlineData(@"\\?\C:\Program Files\MacExtend\")]
    [InlineData(@"Program Files\MacExtend\")]
    [InlineData(@"C:Program Files\MacExtend\")]
    [InlineData(@"C:\Program Files\Mac:Extend\")]
    [InlineData("")]
    [InlineData(null)]
    public void AnInstalledCopyAnywhereElseIsTreatedLikeAPortableOne(string installDir)
    {
        Assert.False(InstallLayout.AllowsTask(true, installDir, ProgramFiles));
    }

    [Fact]
    public void APortableCopyNeverUsesTheTask()
    {
        Assert.False(InstallLayout.AllowsTask(false, @"C:\Program Files\MacExtend\", ProgramFiles));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(@"Program Files")]
    public void AnUnknownProgramFilesFolderNeverAllowsTheTask(string programFiles)
    {
        Assert.False(InstallLayout.AllowsTask(true, @"C:\Program Files\MacExtend\", programFiles));
    }
}
