using System;
using MacExtend.Core.Updates;

namespace MacExtend.Core.Tests.Updates;

public class AppVersionTests
{
    [Fact]
    public void FourPartAssemblyVersionEqualsThreePartReleaseVersion()
    {
        Assert.Equal(AppVersion.Normalize(new Version(1, 0, 60)), AppVersion.Normalize(new Version(1, 0, 60, 0)));
    }

    [Fact]
    public void TwoPartVersionGetsZeroBuild()
    {
        Assert.Equal(new Version(1, 2, 0), AppVersion.Normalize(new Version(1, 2)));
    }

    [Theory]
    [InlineData("1.0.60", 1, 0, 60)]
    [InlineData("v1.0.60", 1, 0, 60)]
    [InlineData(" 2.1 ", 2, 1, 0)]
    public void TryParseAcceptsReleaseStyles(string text, int major, int minor, int build)
    {
        Assert.True(AppVersion.TryParse(text, out var v));
        Assert.Equal(new Version(major, minor, build), v);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("1.0.0-dev.5")]
    public void TryParseRejectsOtherText(string text)
    {
        Assert.False(AppVersion.TryParse(text, out _));
    }
}

public class UpdateManifestTests
{
    private const string HashA = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string HashB = "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";

    private static string Sample() =>
        "# MacExtend update manifest\n" +
        "version=1.0.60\n" +
        "app.file=MacExtend-1.0.60-app.zip\n" +
        "app.sha256=" + HashA + "\n" +
        "app.size=1234567\n" +
        "runtime.id=fingerprint-1\n" +
        "runtime.file=MacExtend-1.0.60-runtime.zip\n" +
        "runtime.sha256=" + HashB + "\n" +
        "runtime.size=45000000\n";

    [Fact]
    public void ParsesEveryField()
    {
        var m = UpdateManifest.Parse(Sample());

        Assert.Equal(new Version(1, 0, 60), m.Version);
        Assert.Equal("MacExtend-1.0.60-app.zip", m.App.File);
        Assert.Equal(HashA, m.App.Sha256);
        Assert.Equal(1234567, m.App.Size);
        Assert.Equal("fingerprint-1", m.RuntimeId);
        Assert.Equal("MacExtend-1.0.60-runtime.zip", m.Runtime.File);
        Assert.Equal(HashB.ToLowerInvariant(), m.Runtime.Sha256);
        Assert.Equal(45000000, m.Runtime.Size);
    }

    [Fact]
    public void SerializeThenParseRoundTrips()
    {
        var m = UpdateManifest.Parse(Sample());

        var again = UpdateManifest.Parse(UpdateManifest.Serialize(m));

        Assert.Equal(m.Version, again.Version);
        Assert.Equal(m.App.Sha256, again.App.Sha256);
        Assert.Equal(m.Runtime.File, again.Runtime.File);
        Assert.Equal(m.RuntimeId, again.RuntimeId);
    }

    [Fact]
    public void RuntimeIsOptional()
    {
        var m = UpdateManifest.Parse("version=1.0.61\napp.file=a.zip\napp.sha256=" + HashA + "\napp.size=10");

        Assert.NotNull(m);
        Assert.Null(m.Runtime);
        Assert.Null(m.RuntimeId);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("<html>Not Found</html>")]
    [InlineData("app.file=a.zip\napp.sha256=" + HashA)]
    [InlineData("version=1.0.61\napp.file=a.zip\napp.sha256=1234")]
    [InlineData("version=1.0.61\napp.sha256=" + HashA)]
    [InlineData("version=1.0.61\napp.file=../evil.zip\napp.sha256=" + HashA)]
    public void GarbageIsInvalid(string text)
    {
        Assert.Null(UpdateManifest.Parse(text));
    }

    [Fact]
    public void RuntimeWithBadHashIsDropped()
    {
        var m = UpdateManifest.Parse("version=1.0.61\napp.file=a.zip\napp.sha256=" + HashA + "\nruntime.id=x\nruntime.file=r.zip\nruntime.sha256=zz");

        Assert.NotNull(m);
        Assert.Null(m.Runtime);
    }
}

public class UpdateDecisionTests
{
    private const string Hash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    private static UpdateManifest Manifest(string version, string runtimeId = "rt-2") => UpdateManifest.Parse(
        "version=" + version + "\napp.file=a.zip\napp.sha256=" + Hash + "\napp.size=1000\n" +
        "runtime.id=" + runtimeId + "\nruntime.file=r.zip\nruntime.sha256=" + Hash + "\nruntime.size=50000");

    [Fact]
    public void NewerVersionIsAvailable()
    {
        var d = UpdateDecision.For(new Version(1, 0, 59, 0), "rt-2", Manifest("1.0.60"));

        Assert.True(d.Available);
        Assert.False(d.NeedsRuntime);
        Assert.Equal(1000, d.DownloadBytes);
    }

    [Theory]
    [InlineData("1.0.60")]
    [InlineData("1.0.59")]
    public void SameOrOlderIsNotAvailable(string version)
    {
        Assert.False(UpdateDecision.For(new Version(1, 0, 60, 0), "rt-2", Manifest(version)).Available);
    }

    [Theory]
    [InlineData("rt-1")]
    [InlineData("")]
    [InlineData(null)]
    public void DifferentOrUnknownRuntimeIsDownloaded(string installed)
    {
        var d = UpdateDecision.For(new Version(1, 0, 1), installed, Manifest("1.0.60"));

        Assert.True(d.NeedsRuntime);
        Assert.Equal(51000, d.DownloadBytes);
    }

    [Fact]
    public void MissingManifestIsNotAvailable()
    {
        Assert.False(UpdateDecision.For(new Version(1, 0, 1), "rt-2", null).Available);
    }
}
