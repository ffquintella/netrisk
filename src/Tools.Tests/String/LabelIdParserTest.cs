#nullable enable
using JetBrains.Annotations;
using Tools.String;
using Xunit;

namespace Tools.Tests.String;

[TestSubject(typeof(LabelIdParser))]
public class LabelIdParserTest
{
    [Theory]
    [InlineData("Acme (123)", true, 123)]
    [InlineData("Acme(123)", true, 123)]
    [InlineData("Foo (Bar) (45)", true, 45)]          // name contains parentheses -> use the last pair
    [InlineData("host01 ( 7 )", true, 7)]             // whitespace inside the parens
    [InlineData("NoId", false, 0)]                    // no parentheses (the original crash case)
    [InlineData("", false, 0)]
    [InlineData("   ", false, 0)]
    [InlineData(null, false, 0)]
    [InlineData("Name (abc)", false, 0)]              // non-numeric id
    [InlineData("Name (", false, 0)]                  // unbalanced
    [InlineData("Name )(", false, 0)]                 // close before open
    public void TryParseTrailingIdTest(string? label, bool expectedSuccess, int expectedId)
    {
        var success = LabelIdParser.TryParseTrailingId(label, out var id);

        Assert.Equal(expectedSuccess, success);
        Assert.Equal(expectedId, id);
    }

    [Theory]
    [InlineData("Definition(host)", "host")]
    [InlineData("Definition( host )", "host")]
    [InlineData("NoParens", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void ExtractParenthesizedValueTest(string? label, string? expected)
    {
        Assert.Equal(expected, LabelIdParser.ExtractParenthesizedValue(label));
    }
}
