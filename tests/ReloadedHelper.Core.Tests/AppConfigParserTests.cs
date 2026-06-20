namespace ReloadedHelper.Core.Tests;

public class AppConfigParserTests
{
    private const string Json = """
    {
      "AppId": "p5r.exe",
      "AppName": "ペルソナ5",
      "AppLocation": "C:\\Steam\\P5R.exe",
      "AppIcon": "Icon.png",
      "EnabledMods": ["a", "b"],
      "SortedMods": ["lib", "a", "b", "c"]
    }
    """;

    [Fact]
    public void Parses_game_with_order_and_enabled_set()
    {
        var g = AppConfigParser.Parse(Json, @"C:\Apps\p5r.exe");
        Assert.Equal("p5r.exe", g.AppId);
        Assert.Equal("ペルソナ5", g.AppName);
        Assert.Equal(@"C:\Steam\P5R.exe", g.AppLocation);
        Assert.Equal(new[] { "lib", "a", "b", "c" }, g.SortedMods);
        Assert.Equal(new[] { "a", "b" }, g.EnabledMods);
        Assert.Equal(@"C:\Apps\p5r.exe\Icon.png", g.IconPath);
    }

    [Fact]
    public void Missing_arrays_become_empty()
    {
        var g = AppConfigParser.Parse("""{ "AppId": "x", "AppName": "X" }""", @"C:\Apps\x");
        Assert.Empty(g.SortedMods);
        Assert.Empty(g.EnabledMods);
        Assert.Null(g.IconPath);
    }
}
