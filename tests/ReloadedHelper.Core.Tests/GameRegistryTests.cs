namespace ReloadedHelper.Core.Tests;

public class GameRegistryTests
{
    [Fact]
    public void GameIdsFor_returns_ids_for_known_appids()
    {
        Assert.Equal(new[] { "16951" }, GameRegistry.GameIdsFor(new[] { "p5r.exe" }));
        Assert.Equal(new[] { "17755", "8263" }, GameRegistry.GameIdsFor(new[] { "p4g.exe" }));
        Assert.Equal(new[] { "9099" }, GameRegistry.GameIdsFor(new[] { "P5S.EXE" })); // 大文字小文字無視
    }

    [Fact]
    public void GameIdsFor_returns_empty_for_unknown()
    {
        Assert.Empty(GameRegistry.GameIdsFor(new[] { "unknown.exe" }));
    }

    [Fact]
    public void GameIdsFor_dedupes_across_appids()
    {
        var ids = GameRegistry.GameIdsFor(new[] { "p5r.exe", "p5r.exe" });
        Assert.Equal(new[] { "16951" }, ids);
    }
}
