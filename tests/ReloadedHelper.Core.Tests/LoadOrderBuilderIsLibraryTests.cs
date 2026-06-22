namespace ReloadedHelper.Core.Tests;

public class LoadOrderBuilderIsLibraryTests
{
    private static ModInfo MakeModInfo(string id, bool isLibrary) => new ModInfo(
        id, id, "Author", "1.0", "Desc",
        Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(),
        Array.Empty<string>(), null, null, null, null, @"C:\mods\" + id,
        isLibrary);

    private static GameInfo MakeGame(
        IReadOnlyList<string> enabled,
        IReadOnlyList<string> sorted) => new GameInfo(
            "game.exe", "Game", @"C:\game.exe", null,
            enabled, sorted, @"C:\game");

    [Fact]
    public void Build_IsLibraryTrue_AlwaysEnabled_EvenIfNotInEnabledMods()
    {
        var catalog = new Dictionary<string, ModInfo>
        {
            ["lib.mod"] = MakeModInfo("lib.mod", isLibrary: true)
        };
        var game = MakeGame(
            enabled: Array.Empty<string>(),  // lib.mod は EnabledMods に含まれていない
            sorted: new[] { "lib.mod" });

        var result = LoadOrderBuilder.Build(game, catalog);

        Assert.Single(result);
        Assert.True(result[0].Enabled);
        Assert.True(result[0].IsLibrary);
    }

    [Fact]
    public void Build_IsLibraryFalse_DisabledIfNotInEnabledMods()
    {
        var catalog = new Dictionary<string, ModInfo>
        {
            ["normal.mod"] = MakeModInfo("normal.mod", isLibrary: false)
        };
        var game = MakeGame(
            enabled: Array.Empty<string>(),
            sorted: new[] { "normal.mod" });

        var result = LoadOrderBuilder.Build(game, catalog);

        Assert.Single(result);
        Assert.False(result[0].Enabled);
        Assert.False(result[0].IsLibrary);
    }

    [Fact]
    public void Build_IsLibraryTrue_CategoryLabelIsFramework()
    {
        var catalog = new Dictionary<string, ModInfo>
        {
            ["lib.mod"] = MakeModInfo("lib.mod", isLibrary: true)
        };
        var game = MakeGame(
            enabled: Array.Empty<string>(),
            sorted: new[] { "lib.mod" });

        var result = LoadOrderBuilder.Build(game, catalog);

        Assert.Equal("フレームワーク", result[0].CategoryLabel);
    }
}
