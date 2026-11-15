using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using MacExtend.Core.CoreDevice;

namespace MacExtend.Core.Tests.CoreDevice;

public class CoreDeviceArgumentsTests
{
    private const string Python = @"C:\MacExtend\tools\coredevice\.venv\Scripts\python.exe";
    private const string Udid = "00008110-000A1234ABCD801E";

    private static CoreDeviceOptions Options(CoreDeviceTransport transport = CoreDeviceTransport.Userspace, int port = 8080) =>
        new CoreDeviceOptions(Python, Udid, transport, port);

    [Theory]
    [InlineData(@"C:\x\python.exe")]
    [InlineData(@"d:/venv/Scripts/python.exe")]
    [InlineData(@"\\server\share\python.exe")]
    public void AcceptsWindowsAbsolutePythonPaths(string path)
    {
        Assert.Equal(path, new CoreDeviceOptions(path, Udid, CoreDeviceTransport.Userspace, 8080).PythonPath);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("python.exe")]
    [InlineData(@".venv\Scripts\python.exe")]
    [InlineData(@"\Scripts\python.exe")]
    [InlineData(@"C:python.exe")]
    [InlineData("/usr/bin/python3")]
    [InlineData("C:\\x\\py\"thon.exe")]
    public void RejectsPythonPathsThatAreNotAbsoluteWindowsPaths(string path)
    {
        var e = Assert.Throws<ArgumentException>(() => new CoreDeviceOptions(path, Udid, CoreDeviceTransport.Userspace, 8080));
        Assert.Contains("absolute", e.Message);
    }

    [Theory]
    [InlineData("00008110-000A1234ABCD801E")]
    [InlineData("00008030-001a35e80c42802e")]
    [InlineData("0123456789abcdef0123456789abcdef01234567")]
    public void AcceptsBothUdidFormats(string udid)
    {
        Assert.Equal(udid, new CoreDeviceOptions(Python, udid, CoreDeviceTransport.Userspace, 8080).Udid);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("00008110-000A1234ABCD801")]
    [InlineData("00008110-000A1234ABCD801E:49151")]
    [InlineData("00008110-000A1234ABCD801E@host")]
    [InlineData("00008110 000A1234ABCD801E")]
    [InlineData("0123456789abcdef0123456789abcdef0123456g")]
    public void RejectsMalformedUdids(string udid)
    {
        var e = Assert.Throws<ArgumentException>(() => new CoreDeviceOptions(Python, udid, CoreDeviceTransport.Userspace, 8080));
        Assert.Contains("UDID", e.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(80)]
    [InlineData(1023)]
    [InlineData(65536)]
    [InlineData(-1)]
    public void RejectsPortsOutsideTheUnprivilegedRange(int port)
    {
        var e = Assert.Throws<ArgumentException>(() => Options(port: port));
        Assert.Contains("port", e.Message);
    }

    [Fact]
    public void RejectsUndefinedTransport()
    {
        var e = Assert.Throws<ArgumentException>(() => Options((CoreDeviceTransport)7));
        Assert.Contains("transport", e.Message);
    }

    [Theory]
    [InlineData("userspace", CoreDeviceTransport.Userspace)]
    [InlineData("Tunnel", CoreDeviceTransport.Tunnel)]
    public void ParsesTransportNames(string text, CoreDeviceTransport expected)
    {
        Assert.True(CoreDeviceOptions.TryParseTransport(text, out var transport));
        Assert.Equal(expected, transport);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("native")]
    [InlineData("rsd")]
    [InlineData("1")]
    public void RefusesOtherTransports(string text)
    {
        Assert.False(CoreDeviceOptions.TryParseTransport(text, out _));
    }

    [Fact]
    public void ViewerUrlIsLoopbackOnTheChosenPort()
    {
        Assert.Equal("http://127.0.0.1:8123/", Options(port: 8123).ViewerUrl);
    }

    [Fact]
    public void ServeWebOverUserspaceBindsLoopbackAndNamesNoDevice()
    {
        var args = CoreDeviceArguments.ServeWeb(Options(port: 8123));

        Assert.Equal(new[]
        {
            "-I", "-X", "utf8", "-u", "-m", "pymobiledevice3",
            "developer", "core-device", "display", "serve-web",
            "--bind", "127.0.0.1", "--http-port", "8123", "--no-audio", "--userspace",
        }, args);
    }

    [Fact]
    public void DirectInputRunsTheAppsScriptOnLoopbackOverUserspace()
    {
        var args = CoreDeviceArguments.DirectInput(Options(port: 8123), @"C:\Program Files\MacExtend\helpers\direct_input.py");

        Assert.Equal(new[]
        {
            "-I", "-X", "utf8", "-u", @"C:\Program Files\MacExtend\helpers\direct_input.py",
            "--bind", "127.0.0.1", "--http-port", "8123", "--userspace",
        }, args);
    }

    [Fact]
    public void DirectInputOverTunnelNamesTheDeviceForTunneld()
    {
        var args = CoreDeviceArguments.DirectInput(Options(CoreDeviceTransport.Tunnel), @"C:\x\direct_input.py");

        Assert.Equal(new[] { "--tunnel", Udid }, args.Skip(args.Count - 2));
        Assert.DoesNotContain("--userspace", args);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("direct_input.py")]
    public void DirectInputNeedsTheScriptsFullPath(string script)
    {
        Assert.ThrowsAny<ArgumentException>(() => CoreDeviceArguments.DirectInput(Options(), script));
    }

    [Fact]
    public void ServeWebOverTunnelNamesTheDeviceForTunneld()
    {
        var args = CoreDeviceArguments.ServeWeb(Options(CoreDeviceTransport.Tunnel));

        Assert.Equal(new[] { "--tunnel", Udid }, args.Skip(args.Count - 2));
        Assert.DoesNotContain("--userspace", args);
    }

    [Theory]
    [InlineData(CoreDeviceTransport.Userspace)]
    [InlineData(CoreDeviceTransport.Tunnel)]
    public void ServeWebNeverPassesFlagsTheCommandDoesNotHave(CoreDeviceTransport transport)
    {
        var args = CoreDeviceArguments.ServeWeb(Options(transport));

        // serve-web has no --udid (the device comes from PYMOBILEDEVICE3_UDID) and no --mobdev2
        Assert.DoesNotContain("--udid", args);
        Assert.DoesNotContain("--mobdev2", args);
        Assert.DoesNotContain("--native", args);
        Assert.DoesNotContain("--rsd", args);
        Assert.DoesNotContain("--password", args);
        Assert.DoesNotContain("0.0.0.0", args);
        Assert.Equal(1, args.Count(a => a == "--userspace" || a == "--tunnel"));
    }

    [Fact]
    public void ServeWebCanRunAStandInScriptWithTheSameArguments()
    {
        var real = CoreDeviceArguments.ServeWeb(Options());
        var fake = CoreDeviceArguments.ServeWeb(Options(), @"C:\repo\tools\coredevice\tests\fake_serve_web.py");

        Assert.Equal(real.Take(4), fake.Take(4));
        Assert.Equal(@"C:\repo\tools\coredevice\tests\fake_serve_web.py", fake[4]);
        Assert.Equal(real.Skip(6), fake.Skip(5));
    }

    [Fact]
    public void VersionCheckRunsInTheSameIsolatedInterpreter()
    {
        var args = CoreDeviceArguments.VersionCheck();

        Assert.Equal(new[] { "-I", "-X", "utf8", "-u", "-c" }, args.Take(5));
        Assert.Contains("pymobiledevice3", args[5]);
    }

    [Fact]
    public void CommandLineQuotesTheExecutableAndArguments()
    {
        var line = CoreDeviceArguments.CommandLine(@"C:\Program Files\py\python.exe", new[] { "-c", "print(1); print(2)" });

        Assert.Equal("\"C:\\Program Files\\py\\python.exe\" -c \"print(1); print(2)\"", line);
    }

    [Fact]
    public void EnvironmentDropsInheritedDeviceSelectionAndSetsTheUdid()
    {
        var inherited = new Hashtable
        {
            ["Path"] = @"C:\Windows",
            ["PYMOBILEDEVICE3_TUNNEL"] = "other",
            ["pymobiledevice3_userspace"] = "1",
            ["PyMobileDevice3_Udid"] = "0123456789abcdef0123456789abcdef01234567",
            ["PYMOBILEDEVICE3_DEFAULT_FALLBACK"] = "tunneld",
            ["FORCE_COLOR"] = "1",
            ["USERPROFILE"] = @"C:\Users\t",
        };

        var env = CoreDeviceArguments.Environment(inherited, Options());

        Assert.Equal(Udid, env["PYMOBILEDEVICE3_UDID"]);
        Assert.Single(env.Keys, k => k.StartsWith("PYMOBILEDEVICE3_", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("1", env["NO_COLOR"]);
        Assert.False(env.ContainsKey("FORCE_COLOR"));
        Assert.Equal(@"C:\Windows", env["PATH"]);
        Assert.Equal(@"C:\Users\t", env["USERPROFILE"]);
    }

    [Fact]
    public void EnvironmentIgnoresEntriesWindowsCannotCarry()
    {
        var inherited = new Hashtable { [""] = "x", ["A=B"] = "x", ["NUL"] = "a\0b", ["OK"] = "v" };

        var env = CoreDeviceArguments.Environment(inherited, Options());

        Assert.False(env.ContainsKey(""));
        Assert.False(env.ContainsKey("A=B"));
        Assert.False(env.ContainsKey("NUL"));
        Assert.Equal("v", env["OK"]);
    }

    [Fact]
    public void EnvironmentBlockIsSortedCaseInsensitivelyAndDoubleTerminated()
    {
        var env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["b"] = "2", ["A"] = "1", ["c"] = "é" };

        Assert.Equal("A=1\0b=2\0c=é\0\0", CoreDeviceArguments.EnvironmentBlock(env));
    }

    [Fact]
    public void EmptyEnvironmentBlockStillTerminates()
    {
        Assert.Equal("\0\0", CoreDeviceArguments.EnvironmentBlock(new Dictionary<string, string>()));
    }
}
