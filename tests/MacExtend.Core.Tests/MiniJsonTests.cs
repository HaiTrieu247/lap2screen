using System;
using System.Collections.Generic;

namespace MacExtend.Core.Tests;

public class MiniJsonTests
{
    [Fact]
    public void ReadsPrintJsonOfAStringList()
    {
        var value = MiniJson.Parse("[\n    \"00008030-001E185E343B802E\"\n]\n");

        Assert.Equal(new List<object> { "00008030-001E185E343B802E" }, value);
    }

    [Fact]
    public void ReadsObjectsNumbersBoolsAndNull()
    {
        var value = (Dictionary<string, object>)MiniJson.Parse("{\"a\": 1.5, \"b\": [true, false, null], \"c\": {\"d\": -2}}");

        Assert.Equal(1.5, value["a"]);
        Assert.Equal(new List<object> { true, false, null }, value["b"]);
        Assert.Equal(-2.0, ((Dictionary<string, object>)value["c"])["d"]);
    }

    [Fact]
    public void DecodesEscapesAndUnicode()
    {
        Assert.Equal("Thịnh \"x\"\n\\", MiniJson.Parse("\"Th\\u1ecbnh \\\"x\\\"\\n\\\\\""));
        Assert.Equal("Thịnh", MiniJson.Parse("\"Thịnh\""));
    }

    [Theory]
    [InlineData("true false")]
    [InlineData("{\"a\":1,}")]
    [InlineData("[1 2]")]
    [InlineData("\"unterminated")]
    [InlineData("<html>")]
    [InlineData("")]
    public void RejectsWhatIsNotOneJsonValue(string text)
    {
        Assert.Throws<FormatException>(() => MiniJson.Parse(text));
        Assert.False(MiniJson.TryParse(text, out _));
    }

    [Fact]
    public void RejectsDeepNesting()
    {
        Assert.Throws<FormatException>(() => MiniJson.Parse(new string('[', 100) + new string(']', 100)));
    }
}
