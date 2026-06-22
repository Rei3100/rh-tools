namespace ReloadedHelper.Core.Tests;

public class HtmlTextTests
{
    [Theory]
    [InlineData("Hello<br><br>World", "Hello\n\nWorld")]
    [InlineData("<b>Bold</b> text", "Bold text")]
    [InlineData("A &amp; B &lt;tag&gt;", "A & B <tag>")]
    [InlineData("  spaced  ", "spaced")]
    public void Strip_removes_tags_and_decodes(string input, string expected)
    {
        Assert.Equal(expected, HtmlText.Strip(input));
    }
}
