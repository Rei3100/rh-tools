using ReloadedHelper.Core;
using Xunit;

namespace ReloadedHelper.Core.Tests;

public class OverlapEdgesTests
{
    private static Dictionary<string, int> Counts(params (string id, int n)[] xs)
        => xs.ToDictionary(x => x.id, x => x.n, StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void MoreResources_PointsTo_Fewer()
    {
        // big(100) と small(1) が file:a で重なる → big→small（small が後＝勝ち）。
        var conflicts = new[] { new FileConflict("file:a", new[] { "small", "big" }, "big") };
        var edges = OverlapEdges.Build(conflicts, Counts(("small", 1), ("big", 100)));
        Assert.Contains(("big", "small"), edges);
        Assert.Single(edges);
    }

    [Fact]
    public void EqualResources_NoEdge()
    {
        var conflicts = new[] { new FileConflict("file:a", new[] { "a", "b" }, "b") };
        var edges = OverlapEdges.Build(conflicts, Counts(("a", 5), ("b", 5)));
        Assert.Empty(edges);
    }

    [Fact]
    public void SamePairAcrossManyResources_Deduped()
    {
        var conflicts = new[]
        {
            new FileConflict("file:a", new[] { "small", "big" }, "big"),
            new FileConflict("file:b", new[] { "small", "big" }, "big"),
        };
        var edges = OverlapEdges.Build(conflicts, Counts(("small", 1), ("big", 100)));
        Assert.Single(edges);
        Assert.Contains(("big", "small"), edges);
    }

    [Fact]
    public void ThreeWayConflict_AllPairs()
    {
        // a(10),b(5),c(1) が同じ資源で重なる → a→b, a→c, b→c。
        var conflicts = new[] { new FileConflict("file:a", new[] { "a", "b", "c" }, "c") };
        var edges = OverlapEdges.Build(conflicts, Counts(("a", 10), ("b", 5), ("c", 1)));
        Assert.Contains(("a", "b"), edges);
        Assert.Contains(("a", "c"), edges);
        Assert.Contains(("b", "c"), edges);
        Assert.Equal(3, edges.Count);
    }

    [Fact]
    public void MissingCount_TreatedAsZero_SoKnownMoreWins()
    {
        // big に count あり、unk は未知(=0扱い) → big(5) > unk(0) → big→unk。
        var conflicts = new[] { new FileConflict("file:a", new[] { "big", "unk" }, "unk") };
        var edges = OverlapEdges.Build(conflicts, Counts(("big", 5)));
        Assert.Contains(("big", "unk"), edges);
    }
}
