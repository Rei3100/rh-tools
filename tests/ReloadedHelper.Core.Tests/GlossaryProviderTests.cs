namespace ReloadedHelper.Core.Tests;

public class GlossaryProviderTests
{
    [Fact]
    public void P5R_replaces_character_full_name_before_short_name()
    {
        var result = GlossaryProvider.Apply("Ryuji Sakamoto and Ryuji", "p5r.exe");
        Assert.Equal("坂本竜司 and 竜司", result);
    }

    [Fact]
    public void P5R_replaces_term()
    {
        var result = GlossaryProvider.Apply("The Phantom Thieves enter the Palace.", "p5r.exe");
        Assert.Equal("The 怪盗団 enter the パレス.", result);
    }

    [Fact]
    public void P4G_replaces_term()
    {
        var result = GlossaryProvider.Apply("The Investigation Team watches the Midnight Channel.", "p4g.exe");
        Assert.Equal("The 自称特別捜査隊 watches the マヨナカテレビ.", result);
    }

    [Fact]
    public void P5S_replaces_term()
    {
        var result = GlossaryProvider.Apply("Sophia enters the Jail.", "p5s.exe");
        Assert.Equal("ソフィア enters the 監獄.", result);
    }

    [Fact]
    public void Unknown_appid_returns_text_unchanged()
    {
        var result = GlossaryProvider.Apply("Persona", "unknown.exe");
        Assert.Equal("Persona", result);
    }

    [Fact]
    public void Does_not_replace_partial_word_match()
    {
        var result = GlossaryProvider.Apply("Personalise", "p5r.exe");
        Assert.Equal("Personalise", result);
    }
}
