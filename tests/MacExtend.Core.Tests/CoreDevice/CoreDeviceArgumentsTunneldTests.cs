using System.Collections;
using System.Collections.Generic;
using System.Text;
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

    private const string Golden = @"import os,sys,ctypes; b=ctypes.create_unicode_buffer(32768); ctypes.windll.kernel32.GetSystemWindowsDirectoryW(b,32768); w=b.value; w or sys.exit(1); k=('USERPROFILE','HOMEDRIVE','HOMEPATH','APPDATA','LOCALAPPDATA','USERNAME','USERDOMAIN','COMPUTERNAME','NUMBER_OF_PROCESSORS','PROCESSOR_ARCHITECTURE','TEMP','TMP'); e={x:os.environ[x] for x in k if x in os.environ}; os.environ.clear(); os.environ.update(e); os.environ.update(SystemRoot=w,windir=w,PATH=os.pathsep.join([os.path.join(w,'System32'),w]),OPENSSL_CONF=os.devnull); import runpy; f=open(os.devnull,'w'); sys.stdout=sys.stderr=f; sys.argv=['pymobiledevice3','--no-color','remote','tunneld','--no-usb','--no-usbmux','--no-mobdev2']; runpy.run_module('pymobiledevice3',run_name='__main__',alter_sys=True)";

    [Fact]
    public void BootstrapIsExactlyTheApprovedOneLiner()
    {
        Assert.Equal(new[] { "-I", "-X", "utf8", "-c", Golden }, CoreDeviceArguments.TunneldBootstrap());
    }

    [Fact]
    public void BootstrapKeepsOnlyRemotePairingAndNeverLogsToDisk()
    {
        var code = CoreDeviceArguments.TunneldBootstrap()[4];

        Assert.Contains("'--no-color','remote','tunneld','--no-usb','--no-usbmux','--no-mobdev2'", code);
        Assert.Contains("OPENSSL_CONF=os.devnull", code);
        Assert.DoesNotContain("tunneld.log", code);
    }

    [Fact]
    public void BootstrapHasNoDoubleQuotePercentBackslashOrNewline()
    {
        var code = CoreDeviceArguments.TunneldBootstrap()[4];

        Assert.DoesNotContain("\"", code);
        Assert.DoesNotContain("%", code);
        Assert.DoesNotContain("\\", code);
        Assert.DoesNotContain("\n", code);
    }

    [Fact]
    public void BootstrapSurvivesWindowsArgvParsingAsOneArgument()
    {
        var pythonw = @"C:\Program Files\MacExtend\coredevice\python\pythonw.exe";
        var line = WindowsCommandLine.Quote(pythonw) + " " + WindowsCommandLine.Join(CoreDeviceArguments.TunneldBootstrap());

        Assert.Equal(new[] { pythonw, "-I", "-X", "utf8", "-c", Golden }, ParseArgv(line));
    }

    // Test-only MSVCRT / CommandLineToArgvW rules: whitespace separates, '"' toggles quoting, 2n backslashes before '"'
    // give n backslashes and toggle, 2n+1 give n backslashes and a literal '"', other backslashes are literal.
    private static List<string> ParseArgv(string line)
    {
        var args = new List<string>();
        var cur = new StringBuilder();
        var inArg = false;
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '\\')
            {
                var n = 0;
                while (i < line.Length && line[i] == '\\') { n++; i++; }
                if (i < line.Length && line[i] == '"')
                {
                    cur.Append('\\', n / 2);
                    if (n % 2 == 1) cur.Append('"'); else quoted = !quoted;
                }
                else
                {
                    cur.Append('\\', n);
                    i--;
                }
                inArg = true;
            }
            else if (c == '"') { quoted = !quoted; inArg = true; }
            else if ((c == ' ' || c == '\t') && !quoted)
            {
                if (inArg) { args.Add(cur.ToString()); cur.Clear(); inArg = false; }
            }
            else { cur.Append(c); inArg = true; }
        }
        if (inArg) args.Add(cur.ToString());
        return args;
    }
}
