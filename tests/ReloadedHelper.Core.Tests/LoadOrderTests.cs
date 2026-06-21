namespace ReloadedHelper.Core.Tests;

public class LoadOrderTests
{
    private static ModInfo Mod(string id, string name) =>
        new(id, name, "", "", "", Array.Empty<string>(), Array.Empty<string>(),
            Array.Empty<string>(), Array.Empty<string>(), null, null, null, null, @"C:\x");

    private static GameInfo Game() =>
        new("p5r.exe", "P5R", "", null,
            EnabledMods: new[] { "a" },
            SortedMods: new[] { "lib", "a", "missing" },
            FolderPath: @"C:\Apps\p5r.exe");

    [Fact]
    public void Build_preserves_order_marks_enabled_and_tolerates_missing_info()
    {
        var catalog = new Dictionary<string, ModInfo>
        {
            ["lib"] = Mod("lib", "Library"),
            ["a"] = Mod("a", "Alpha"),
        };
        var entries = LoadOrderBuilder.Build(Game(), catalog);

        Assert.Equal(3, entries.Count);
        Assert.Equal(1, entries[0].Order);
        Assert.Equal("Library", entries[0].DisplayName);
        Assert.False(entries[0].Enabled);
        Assert.True(entries[1].Enabled);
        Assert.Null(entries[2].Info);
        Assert.Equal("missing", entries[2].DisplayName);
    }

    [Fact]
    public void Filter_matches_id_or_name_case_insensitive()
    {
        var catalog = new Dictionary<string, ModInfo>
        {
            ["lib"] = Mod("lib", "Library"),
            ["a"] = Mod("a", "Alpha"),
        };
        var entries = LoadOrderBuilder.Build(Game(), catalog);

        Assert.Equal(2, ModFilter.Filter(entries, "a").Count);
        Assert.Single(ModFilter.Filter(entries, "alph"));
        Assert.Equal(3, ModFilter.Filter(entries, "  ").Count);
    }

    [Fact]
    public void Build_injects_category_from_userdata()
    {
        var catalog = new Dictionary<string, ModInfo>
        {
            ["a"] = Mod("a", "Alpha"),
        };
        var game = new GameInfo("p5r.exe", "P5R", "", null,
            EnabledMods: new[] { "a" },
            SortedMods:  new[] { "a" },
            FolderPath: @"C:\Apps\p5r.exe");
        var userData = new UserDataFile();
        userData.Mods["a"] = new ModUserData { Category = "Sound" };

        var entries = LoadOrderBuilder.Build(game, catalog, userData);

        Assert.Equal("Sound", entries[0].Category);
        Assert.Equal("サウンド", entries[0].CategoryLabel);
    }

    [Fact]
    public void Build_without_userdata_leaves_category_null()
    {
        var catalog = new Dictionary<string, ModInfo> { ["a"] = Mod("a", "Alpha") };
        var game = new GameInfo("p5r.exe", "P5R", "", null,
            EnabledMods: new[] { "a" },
            SortedMods:  new[] { "a" },
            FolderPath: @"C:\Apps\p5r.exe");

        var entries = LoadOrderBuilder.Build(game, catalog);

        Assert.Null(entries[0].Category);
        Assert.Null(entries[0].CategoryLabel);
    }
}
