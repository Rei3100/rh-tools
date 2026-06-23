using ReloadedHelper.Core;
using ReloadedHelper.Core.Analyzers;
using Xunit;

namespace ReloadedHelper.Core.Tests;

public class ConflictDetectorResourceTests
{
    private static ModResources R(string id, params ResourceKey[] keys) => new(id, keys);

    [Fact]
    public void Detect_SongConflict_LastWins()
    {
        var song = new ResourceKey(ResourceKind.Song, "12000");
        var conflicts = ConflictDetector.Detect(new[]
        {
            R("a", song),
            R("b", song),
        });

        var c = Assert.Single(conflicts);
        Assert.Equal("song:12000", c.PathKey);
        Assert.Equal("b", c.WinnerModId); // 読み込み順で後＝勝者
        Assert.Equal(new[] { "a", "b" }, c.ModIds);
    }

    [Fact]
    public void Detect_NoConflict_WhenDistinctResources()
    {
        var conflicts = ConflictDetector.Detect(new[]
        {
            R("a", new ResourceKey(ResourceKind.File, "x")),
            R("b", new ResourceKey(ResourceKind.File, "y")),
        });
        Assert.Empty(conflicts);
    }
}
