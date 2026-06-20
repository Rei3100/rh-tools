namespace ReloadedHelper.Core.Tests;

public class ModConfigParserTests
{
    private const string Full = """
    {
      "ModId": "BGME.Framework",
      "ModName": "BGMEフレーム",
      "ModAuthor": "RyoTune",
      "ModVersion": "4.1.2",
      "ModDescription": "BGMを拡張",
      "ModIcon": "Preview.png",
      "Tags": [],
      "ModDependencies": ["Ryo.Reloaded", "crifs.v2.hook"],
      "OptionalDependencies": [],
      "SupportedAppId": ["p4g.exe", "p5r.exe"],
      "PluginData": { "GitHubRelease": { "UserName": "RyoTune", "RepositoryName": "BGME" } },
      "ProjectUrl": "https://gamebanana.com/mods/477399"
    }
    """;

    [Fact]
    public void Parses_core_fields_including_unicode()
    {
        var m = ModConfigParser.Parse(Full, @"C:\Mods\BGME.Framework");
        Assert.Equal("BGME.Framework", m.ModId);
        Assert.Equal("BGMEフレーム", m.ModName);
        Assert.Equal("RyoTune", m.ModAuthor);
        Assert.Equal("4.1.2", m.ModVersion);
        Assert.Equal(new[] { "Ryo.Reloaded", "crifs.v2.hook" }, m.Dependencies);
        Assert.Equal(new[] { "p4g.exe", "p5r.exe" }, m.SupportedAppIds);
        Assert.Equal("https://gamebanana.com/mods/477399", m.ProjectUrl);
        Assert.Equal("RyoTune", m.GitHubUserName);
        Assert.Equal("BGME", m.GitHubRepositoryName);
        Assert.Equal(@"C:\Mods\BGME.Framework\Preview.png", m.IconPath);
    }

    [Fact]
    public void Missing_optional_fields_become_null_or_empty()
    {
        var m = ModConfigParser.Parse("""{ "ModId": "x", "ModName": "X" }""", @"C:\Mods\x");
        Assert.Equal("x", m.ModId);
        Assert.Null(m.ProjectUrl);
        Assert.Null(m.GitHubUserName);
        Assert.Null(m.IconPath);
        Assert.Empty(m.Dependencies);
        Assert.Equal("", m.ModDescription);
    }
}
