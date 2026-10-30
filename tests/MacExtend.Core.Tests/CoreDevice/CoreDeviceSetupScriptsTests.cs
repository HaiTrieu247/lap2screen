using System;
using MacExtend.Core.CoreDevice;

namespace MacExtend.Core.Tests.CoreDevice;

public class CoreDeviceSetupScriptsTests
{
    [Fact]
    public void RegistersAnOnDemandTaskForTheUserWithHighestPrivileges()
    {
        var script = CoreDeviceSetupScripts.RegisterTask(@"C:\Program Files\MacExtend\MacExtend.exe", @"LEGION\thinh", @"C:\Users\thinh");

        Assert.StartsWith("$ErrorActionPreference = 'Stop'", script);
        Assert.Contains("-Execute '\"C:\\Program Files\\MacExtend\\MacExtend.exe\"'", script);
        Assert.Contains("-Argument '--coredevice-tunneld C:\\Users\\thinh'", script);
        Assert.Contains("-UserId 'LEGION\\thinh' -LogonType Interactive -RunLevel Highest", script);
        Assert.Contains("-TaskPath '\\MacExtend\\' -TaskName 'MacExtend CoreDevice tunnel'", script);
        Assert.DoesNotContain("-Trigger", script);
    }

    [Fact]
    public void KeepsUnicodeAndQuotesInTheTaskArgument()
    {
        var script = CoreDeviceSetupScripts.RegisterTask(@"C:\Program Files\MacExtend\MacExtend.exe", @"LEGION\Thịnh O'Hải", @"C:\Users\Thịnh O'Hải");

        Assert.Contains("-Argument '--coredevice-tunneld \"C:\\Users\\Thịnh O''Hải\"'", script);
        Assert.Contains("-UserId 'LEGION\\Thịnh O''Hải'", script);
    }

    [Fact]
    public void KeepsSpacesAndTypographicQuotesInsideTheLiterals()
    {
        var script = CoreDeviceSetupScripts.RegisterTask(@"C:\Program Files\MacExtend\MacExtend.exe", "LEGION\\Thịnh\u2019s", "C:\\Users\\Thịnh Hải\u2018x");

        Assert.Contains("-UserId 'LEGION\\Thịnh\u2019\u2019s'", script);
        Assert.Contains("-Argument '--coredevice-tunneld \"C:\\Users\\Thịnh Hải\u2018\u2018x\"'", script);
    }

    [Fact]
    public void QuotesTheExecutablePathInsideTheLiteralSoASpaceIsNotASplit()
    {
        var script = CoreDeviceSetupScripts.RegisterTask(@"C:\Program Files\MacExtend\MacExtend.exe", @"LEGION\thinh", @"C:\Users\thinh");

        Assert.Contains("-Execute '\"C:\\Program Files\\MacExtend\\MacExtend.exe\"' -Argument", script);
    }

    [Theory]
    [InlineData("exePath")]
    [InlineData("userName")]
    [InlineData("userProfile")]
    public void RefusesAPercentSignBecauseTaskSchedulerExpandsEnvironmentVariables(string parameter)
    {
        var exe = @"C:\Program Files\MacExtend\MacExtend.exe";
        var user = @"LEGION\thinh";
        var profile = @"C:\Users\thinh";
        if (parameter == "exePath") exe = @"C:\%TEMP%\MacExtend.exe";
        if (parameter == "userName") user = @"LEGION\th%inh";
        if (parameter == "userProfile") profile = @"C:\Users\%USERNAME%";

        var ex = Assert.Throws<ArgumentException>(() => CoreDeviceSetupScripts.RegisterTask(exe, user, profile));

        Assert.Equal(parameter, ex.ParamName);
    }

    [Fact]
    public void FirewallRulesAreScopedToTheRuntimePython()
    {
        var script = CoreDeviceSetupScripts.AddFirewallRules(@"C:\Program Files\MacExtend\coredevice\python\python.exe");

        Assert.Contains("Get-NetFirewallRule -Group 'MacExtend CoreDevice' -ErrorAction SilentlyContinue | Remove-NetFirewallRule", script);
        Assert.Contains("-Protocol UDP -RemoteAddress 'fc00::/7' -Program 'C:\\Program Files\\MacExtend\\coredevice\\python\\python.exe' -Profile Any", script);
        Assert.Contains("-Protocol UDP -LocalPort 5353 -Program 'C:\\Program Files\\MacExtend\\coredevice\\python\\python.exe' -Profile Private |", script);
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
