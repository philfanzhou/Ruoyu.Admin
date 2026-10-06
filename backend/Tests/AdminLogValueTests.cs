using Admin.WebApi;
using Xunit;

namespace Admin.WebApi.Tests;

/// <summary>
/// #91: the central log-value barrier. The runtime mirrors the ServiceMantle sink-side
/// normalization exactly — control/format/line/paragraph-separator runes become spaces — so
/// no user-controlled value can inject a fake log line, while ordinary text (including CJK
/// and surrogate pairs) passes through unchanged.
/// </summary>
public sealed class AdminLogValueTests
{
    [Theory]
    [InlineData("a\nb", "a b")]
    [InlineData("a\rb", "a b")]
    [InlineData("a\r\nb", "a  b")]
    [InlineData("a\rb\rc\r\nd", "a b c  d")]
    [InlineData("before\x1b[31mAFTER\x1b[0m", "before [31mAFTER [0m")]
    [InlineData("\x1b]2;fake-window-title\x07", " ]2;fake-window-title ")]
    [InlineData("line\u0000nul\u007fdel", "line nul del")]
    [InlineData("a\tb", "a b")]
    public void ReplacesControlCharactersWithSpaces(string input, string expected)
        => Assert.Equal(expected, AdminLogValue.Sanitize(input));

    [Fact]
    public void ReplacesUnicodeSeparatorAndFormatCategories()
    {
        // Line separator U+2028 and paragraph separator U+2029 split fake log lines.
        Assert.Equal("a b", AdminLogValue.Sanitize("a\u2028b"));
        Assert.Equal("a b", AdminLogValue.Sanitize("a\u2029b"));
        // Format category: soft hyphen U+00AD and word joiner U+2060 become spaces.
        Assert.Equal("soft  hyphen", AdminLogValue.Sanitize("soft\u00ad hyphen"));
        Assert.Equal("word  joiner", AdminLogValue.Sanitize("word\u2060 joiner"));
    }

    [Fact]
    public void OrdinaryTextIncludingCjkAndSpacesIsUnchanged()
    {
        Assert.Equal("正常中文日志 值", AdminLogValue.Sanitize("正常中文日志 值"));
        Assert.Equal("plain ascii value 42", AdminLogValue.Sanitize("plain ascii value 42"));
        Assert.Equal("record/upload_01.jpg?size=small", AdminLogValue.Sanitize("record/upload_01.jpg?size=small"));
    }

    [Fact]
    public void SurrogatePairsSurviveAndTheirControlsDoNot()
    {
        const string emoji = "学生😊名单";
        Assert.Equal(emoji, AdminLogValue.Sanitize(emoji));
        // A control rune between two astral characters is replaced by a single space.
        Assert.Equal("😊 😀", AdminLogValue.Sanitize("😊\n😀"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NullAndWhitespaceOnlyValuesPassThrough(string? value)
        => Assert.Equal(value, AdminLogValue.Sanitize(value));
}
