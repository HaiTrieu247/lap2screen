using System;
using MacExtend.Core.CoreDevice;

namespace MacExtend.Core.Tests.CoreDevice;

public class CoreDeviceSetupScriptsTests
{
    private const string Pythonw = @"C:\Program Files\MacExtend\coredevice\python\pythonw.exe";
    private const string WorkDir = @"C:\Program Files\MacExtend\coredevice\python";

    [Fact]
    public void RegistersAnOnDemandTaskThatRunsPythonwWithTheBootstrap()
    {
        var script = CoreDeviceSetupScripts.RegisterTask(Pythonw, WorkDir, @"LEGION\thinh");

        Assert.StartsWith("$ErrorActionPreference = 'Stop'", script);
        Assert.Contains("-Execute '\"C:\\Program Files\\MacExtend\\coredevice\\python\\pythonw.exe\"'", script);
        Assert.Contains("-WorkingDirectory 'C:\\Program Files\\MacExtend\\coredevice\\python'", script);
        Assert.Contains("-UserId 'LEGION\\thinh' -LogonType Interactive -RunLevel Highest", script);
        Assert.Contains("-TaskPath '\\MacExtend\\' -TaskName 'MacExtend CoreDevice tunnel'", script);
        Assert.Contains("-Force | Out-Null", script);
        Assert.DoesNotContain("-Trigger", script);
        Assert.DoesNotContain("MacExtend.exe", script);
        Assert.DoesNotContain("coredevice-tunneld", script);
    }

    [Fact]
    public void TheTaskArgumentIsTheLoggingBootstrapAsOneQuotedArgument()
    {
        var script = CoreDeviceSetupScripts.RegisterTask(Pythonw, WorkDir, @"LEGION\thinh");
        var expected = WindowsCommandLine.Join(CoreDeviceArguments.TunneldBootstrap()).Replace("'", "''");

        Assert.Contains("-Argument '" + expected + "' ", script);
        Assert.StartsWith("-I -X utf8 -c \"import os,sys,ctypes;", expected);
        Assert.DoesNotContain("tunneld.log", expected);
    }

    [Fact]
    public void KeepsUnicodeAndQuotesInTheLiterals()
    {
        var script = CoreDeviceSetupScripts.RegisterTask(@"C:\Thịnh O'Hải\python\pythonw.exe", @"C:\Thịnh O'Hải\python", @"LEGION\Thịnh O'Hải");

        Assert.Contains("-Execute '\"C:\\Thịnh O''Hải\\python\\pythonw.exe\"'", script);
        Assert.Contains("-WorkingDirectory 'C:\\Thịnh O''Hải\\python'", script);
        Assert.Contains("-UserId 'LEGION\\Thịnh O''Hải'", script);
    }

    [Fact]
    public void KeepsTypographicQuotesInsideTheLiterals()
    {
        var script = CoreDeviceSetupScripts.RegisterTask(@"C:\Thịnh\python\pythonw.exe", "C:\\Hải\u2018x\\python", "LEGION\\Thịnh\u2019s");

        Assert.Contains("-UserId 'LEGION\\Thịnh\u2019\u2019s'", script);
        Assert.Contains("-WorkingDirectory 'C:\\Hải\u2018\u2018x\\python'", script);
    }

    [Fact]
    public void QuotesTheExecutablePathInsideTheLiteralSoASpaceIsNotASplit()
    {
        var script = CoreDeviceSetupScripts.RegisterTask(Pythonw, WorkDir, @"LEGION\thinh");

        Assert.Contains("-Execute '\"C:\\Program Files\\MacExtend\\coredevice\\python\\pythonw.exe\"' -Argument", script);
    }

    [Theory]
    [InlineData("pythonwPath")]
    [InlineData("workingDirectory")]
    [InlineData("userName")]
    public void RefusesAPercentSignBecauseTaskSchedulerExpandsEnvironmentVariables(string parameter)
    {
        var exe = Pythonw;
        var dir = WorkDir;
        var user = @"LEGION\thinh";
        if (parameter == "pythonwPath") exe = @"C:\%TEMP%\pythonw.exe";
        if (parameter == "workingDirectory") dir = @"C:\%TEMP%\python";
        if (parameter == "userName") user = @"LEGION\th%inh";

        var ex = Assert.Throws<ArgumentException>(() => CoreDeviceSetupScripts.RegisterTask(exe, dir, user));

        Assert.Equal(parameter, ex.ParamName);
    }

    [Fact]
    public void FirewallRulesCoverBothPythonAndPythonw()
    {
        var script = CoreDeviceSetupScripts.AddFirewallRules(@"C:\Program Files\MacExtend\coredevice\python");

        Assert.Contains("Get-NetFirewallRule -Group 'MacExtend CoreDevice' -ErrorAction SilentlyContinue | Remove-NetFirewallRule", script);
        foreach (var exe in new[] { "python.exe", "pythonw.exe" })
        {
            var program = "'C:\\Program Files\\MacExtend\\coredevice\\python\\" + exe + "'";
            Assert.Contains("-Protocol UDP -RemoteAddress 'fc00::/7' -Program " + program + " -Profile Any", script);
            Assert.Contains("-Protocol UDP -LocalPort 5353 -Program " + program + " -Profile Private |", script);
        }
        Assert.Equal(4, System.Text.RegularExpressions.Regex.Matches(script, "New-NetFirewallRule -Group 'MacExtend CoreDevice'").Count);
        Assert.DoesNotContain("Domain", script);
    }

    [Theory]
    [InlineData(@"C:\Program Files\MacExtend\coredevice\python\python.exe")]
    [InlineData(@"C:\x\PYTHONW.EXE")]
    public void FirewallRulesRefuseAProgramPathInsteadOfTheFolder(string path)
    {
        var ex = Assert.Throws<ArgumentException>(() => CoreDeviceSetupScripts.AddFirewallRules(path));

        Assert.Equal("pythonDirectory", ex.ParamName);
    }

    [Fact]
    public void RemovesAndQueriesTheSameNames()
    {
        Assert.Equal("$found = @(Get-NetFirewallRule -Group 'MacExtend CoreDevice' -ErrorAction SilentlyContinue)\n"
                     + "if ($found.Count -gt 0) { $found | Remove-NetFirewallRule -ErrorAction Stop }\nexit 0",
                     CoreDeviceSetupScripts.RemoveFirewallRules());
        Assert.Contains("-TaskPath '\\MacExtend\\' -TaskName 'MacExtend CoreDevice tunnel'", CoreDeviceSetupScripts.RemoveTask());
        Assert.Contains("\"rules=$rules task=$task\"", CoreDeviceSetupScripts.Query());
    }

    [Fact]
    public void EndsARunningTunneldOfTheTaskAndNothingElse()
    {
        Assert.Equal("$found = @(Get-ScheduledTask -TaskPath '\\MacExtend\\' -TaskName 'MacExtend CoreDevice tunnel' -ErrorAction SilentlyContinue)\n"
                     + "if ($found.Count -gt 0) { $found | Stop-ScheduledTask -ErrorAction Stop }\nexit 0",
                     CoreDeviceSetupScripts.StopTask());
    }

    // powershell.exe -Command exits 1 when its last command wrote an error, and a lookup that finds nothing writes one even
    // with SilentlyContinue: --coredevice-remove on a clean machine returned RemoveFailed
    [Fact]
    public void UndoScriptsSucceedWhenThereIsNothingToUndo()
    {
        foreach (var script in new[] { CoreDeviceSetupScripts.StopTask(), CoreDeviceSetupScripts.RemoveTask(), CoreDeviceSetupScripts.RemoveFirewallRules() })
        {
            Assert.StartsWith("$found = @(", script);
            Assert.Contains(" -ErrorAction SilentlyContinue)\nif ($found.Count -gt 0) { $found | ", script);
            Assert.EndsWith(" -ErrorAction Stop }\nexit 0", script);
        }
    }

    [Fact]
    public void TheRuleCountMatchesTheRulesTheScriptCreates()
    {
        var script = CoreDeviceSetupScripts.AddFirewallRules(@"C:\Program Files\MacExtend\coredevice\python");

        Assert.Equal(CoreDeviceSetupScripts.FirewallRuleCount,
                     System.Text.RegularExpressions.Regex.Matches(script, "New-NetFirewallRule ").Count);
    }

    [Theory]
    [InlineData(4, true, true, true)]
    [InlineData(4, false, true, false)]
    [InlineData(4, false, false, true)]
    [InlineData(5, true, true, true)]
    // the two rules of an older setup (python.exe only) leave pythonw.exe's tunneld without discovery
    [InlineData(2, true, true, false)]
    [InlineData(2, false, false, false)]
    [InlineData(0, true, false, false)]
    public void TheWindowsPartIsCompleteWithAllRulesAndTheTaskWhenOneIsUsed(int rules, bool task, bool needsTask, bool complete)
    {
        Assert.Equal(complete, new SetupQuery { Rules = rules, Task = task }.IsComplete(needsTask));
    }

    [Theory]
    [InlineData("rules=2 task=1\r\n", 2, true)]
    [InlineData("rules=0 task=0", 0, false)]
    [InlineData("", 0, false)]
    [InlineData("garbage", 0, false)]
    public void ReadsTheQuery(string output, int rules, bool task)
    {
        var state = CoreDeviceSetupScripts.ParseQuery(output);

        Assert.Equal(rules, state.Rules);
        Assert.Equal(task, state.Task);
    }
}
