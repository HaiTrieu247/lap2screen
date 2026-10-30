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
        var expected = WindowsCommandLine.Join(CoreDeviceArguments.TunneldBootstrap(true)).Replace("'", "''");

        Assert.Contains("-Argument '" + expected + "' ", script);
        Assert.StartsWith("-I -X utf8 -c \"import sys,os,runpy;", expected);
        Assert.Contains("tunneld.log", expected);
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

    [Fact]
    public void RemovesAndQueriesTheSameNames()
    {
        Assert.Equal("Get-NetFirewallRule -Group 'MacExtend CoreDevice' -ErrorAction SilentlyContinue | Remove-NetFirewallRule", CoreDeviceSetupScripts.RemoveFirewallRules());
        Assert.Contains("-TaskPath '\\MacExtend\\' -TaskName 'MacExtend CoreDevice tunnel'", CoreDeviceSetupScripts.RemoveTask());
        Assert.Contains("\"rules=$rules task=$task\"", CoreDeviceSetupScripts.Query());
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
