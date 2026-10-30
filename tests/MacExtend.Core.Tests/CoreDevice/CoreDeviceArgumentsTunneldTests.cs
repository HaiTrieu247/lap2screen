using System.Collections;
using System.Linq;
using MacExtend.Core.CoreDevice;

namespace MacExtend.Core.Tests.CoreDevice;

public class CoreDeviceArgumentsTunneldTests
{
    [Fact]
    public void CommandsUseTheSameInterpreterFlags()
    {
        Assert.Equal(new[] { "-I", "-X", "utf8", "-u", "-m", "pymobiledevice3", "usbmux", "list", "--usb", "--simple" },
                     CoreDeviceArguments.Command("usbmux", "list", "--usb", "--simple"));
    }

    [Fact]
    public void CommandEnvironmentScrubsUpstreamVariablesAndSetsTheDevice()
    {
        var inherited = new Hashtable { { "PATH", "C:\\Windows" }, { "PYMOBILEDEVICE3_TUNNEL", "x" }, { "FORCE_COLOR", "1" } };

        var env = CoreDeviceArguments.CommandEnvironment(inherited, "00008030-001E185E343B802E");

        Assert.Equal("C:\\Windows", env["PATH"]);
        Assert.Equal("00008030-001E185E343B802E", env["PYMOBILEDEVICE3_UDID"]);
        Assert.Equal("1", env["NO_COLOR"]);
        Assert.DoesNotContain("PYMOBILEDEVICE3_TUNNEL", env.Keys);
        Assert.DoesNotContain("FORCE_COLOR", env.Keys);
        Assert.DoesNotContain("PYMOBILEDEVICE3_UDID", CoreDeviceArguments.CommandEnvironment(inherited, null).Keys);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void BootstrapIsIsolatedUtf8InlineCode(bool logToFile)
    {
        var args = CoreDeviceArguments.TunneldBootstrap(logToFile);

        Assert.Equal(5, args.Count);
        Assert.Equal(new[] { "-I", "-X", "utf8", "-c" }, args.Take(4));
        Assert.Contains("'--no-color','remote','tunneld','--no-usb','--no-usbmux','--no-mobdev2'", args[4]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void BootstrapHasNoDoubleQuoteOrPercent(bool logToFile)
    {
        var code = CoreDeviceArguments.TunneldBootstrap(logToFile)[4];

        Assert.DoesNotContain("\"", code);
        Assert.DoesNotContain("%", code);
        Assert.DoesNotContain("\n", code);
    }

    [Fact]
    public void LoggingBootstrapDerivesTheLogFromTheInterpreter()
    {
        var code = CoreDeviceArguments.TunneldBootstrap(true)[4];

        Assert.Contains("os.path.dirname(os.path.dirname(os.path.abspath(sys.executable)))", code);
        Assert.Contains("os.path.join(d,'tunneld.log'),'w',encoding='utf-8',buffering=1", code);
        Assert.DoesNotContain(":\\", code);
        Assert.DoesNotContain("os.devnull", code);
    }

    [Fact]
    public void PortableBootstrapWritesToDevnullAndNeverToTheLog()
    {
        var code = CoreDeviceArguments.TunneldBootstrap(false)[4];

        Assert.Contains("open(os.devnull,'w',encoding='utf-8',buffering=1)", code);
        Assert.DoesNotContain("tunneld.log", code);
    }
}
