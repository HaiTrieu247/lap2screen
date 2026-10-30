using System;
using System.Linq;
using MacExtend.Core.CoreDevice;

namespace MacExtend.Core.Tests.CoreDevice;

public class HelperOutcomeTests
{
    private static HelperOutcome[] All() => Enum.GetValues(typeof(HelperOutcome)).Cast<HelperOutcome>().ToArray();

    [Fact]
    public void EveryOutcomeHasItsOwnFixedMessage()
    {
        var messages = All().Select(o => HelperOutcomes.Message((int)o)).ToArray();

        Assert.All(messages, m => Assert.False(string.IsNullOrWhiteSpace(m)));
        Assert.Equal(messages.Length, messages.Distinct().Count());
        Assert.DoesNotContain(HelperOutcomes.Message(int.MaxValue), messages);
    }

    [Fact]
    public void MessagesCarryNoPathsOrScriptCharacters()
    {
        Assert.All(All(), o =>
        {
            var m = HelperOutcomes.Message((int)o);
            Assert.True(m.IndexOfAny(new[] { '\\', '/', ':', '\'', '"', ';', '%', '$', '`' }) < 0, o + ": " + m);
        });
    }

    [Theory]
    [InlineData(17)]
    [InlineData(-1)]
    [InlineData(1223)]
    // an unhandled CLR exception (0xE0434352) as the signed exit code .NET reports
    [InlineData(-532462766)]
    public void AnUnknownCodeGetsTheGenericMessage(int code)
    {
        var message = HelperOutcomes.Message(code);

        Assert.StartsWith("Bước cài đặt có quyền quản trị không thành công", message);
        Assert.Contains("(mã " + code + ")", message);
    }

    [Fact]
    public void ItsOwnSentencesAreRecognisedAndNothingElse()
    {
        Assert.All(All(), o => Assert.True(HelperOutcomes.IsMessage(HelperOutcomes.Message((int)o))));
        Assert.True(HelperOutcomes.IsMessage(HelperOutcomes.Message(17)));
        Assert.True(HelperOutcomes.IsMessage(HelperOutcomes.Message(-532462766)));

        Assert.False(HelperOutcomes.IsMessage("Access to the path 'C:\\Program Files\\MacExtend\\coredevice' is denied."));
        Assert.False(HelperOutcomes.IsMessage("Bước cài đặt có quyền quản trị không thành công (mã abc)."));
        Assert.False(HelperOutcomes.IsMessage("Bước cài đặt có quyền quản trị không thành công (mã 5). Thêm chữ."));
        Assert.False(HelperOutcomes.IsMessage(""));
        Assert.False(HelperOutcomes.IsMessage(null));
    }

    [Fact]
    public void TheCodesTheUninstallerAndCiReadStayFixed()
    {
        Assert.Equal(0, (int)HelperOutcome.Ok);
        Assert.Equal(1, (int)HelperOutcome.Failed);
        Assert.Equal(2, (int)HelperOutcome.BadArguments);
    }

    [Theory]
    [InlineData(RuntimeProblem.NotThePackage, HelperOutcome.HashMismatch)]
    [InlineData(RuntimeProblem.WrongRuntimeId, HelperOutcome.WrongRuntimeId)]
    [InlineData(RuntimeProblem.Incomplete, HelperOutcome.IncompleteRuntime)]
    public void ARuntimeProblemHasItsOwnOutcome(RuntimeProblem problem, HelperOutcome outcome)
    {
        Assert.Equal(outcome, HelperOutcomes.For(problem));
    }
}
