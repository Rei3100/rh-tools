namespace ReloadedHelper.Core.Tests;

public class ConflictDetectorTests
{
    [Fact]
    public void Detect_finds_shared_path_winner_is_last_in_order()
    {
        var a = new ModOverrides("A", new[] { "p/x.bin", "p/a-only.bin" });
        var b = new ModOverrides("B", new[] { "p/x.bin" });
        var conflicts = ConflictDetector.Detect(new[] { a, b }); // A 先, B 後

        var c = Assert.Single(conflicts);
        Assert.Equal("p/x.bin", c.PathKey);
        Assert.Equal(new[] { "A", "B" }, c.ModIds);
        Assert.Equal("B", c.WinnerModId); // 後勝ち
    }

    [Fact]
    public void Detect_no_conflict_when_paths_disjoint()
    {
        var a = new ModOverrides("A", new[] { "p/a.bin" });
        var b = new ModOverrides("B", new[] { "p/b.bin" });
        Assert.Empty(ConflictDetector.Detect(new[] { a, b }));
    }
}
