using ReloadedHelper.Core;
using Xunit;

namespace ReloadedHelper.Core.Tests;

public class LoadOrderSorterByEdgesTests
{
    private static Dictionary<string, int> Ranks(params (string id, int r)[] xs)
        => xs.ToDictionary(x => x.id, x => x.r, StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void NoEdges_GroupRankOrders_ThenCurrentOrder()
    {
        // 辺なし。rank昇順（土台=0が前）。同rankは現在順。
        var order = new[] { "vis", "base1", "base2" };
        var ranks = Ranks(("vis", 7), ("base1", 0), ("base2", 0));
        var res = LoadOrderSorter.SortByEdges(order, System.Array.Empty<(string, string)>(), ranks);
        Assert.Equal(new[] { "base1", "base2", "vis" }, res);
    }

    [Fact]
    public void NoEdges_SameRank_KeepsCurrentOrder()
    {
        var order = new[] { "b", "a", "c" };
        var ranks = Ranks(("a", 5), ("b", 5), ("c", 5));
        var res = LoadOrderSorter.SortByEdges(order, System.Array.Empty<(string, string)>(), ranks);
        Assert.Equal(new[] { "b", "a", "c" }, res);
    }

    [Fact]
    public void HardEdge_OverridesGroupRank()
    {
        // 辺 a→b は rank に逆らってでも守る（a が前）。
        var order = new[] { "a", "b" };
        var ranks = Ranks(("a", 9), ("b", 0)); // rankだけなら b が前
        var res = LoadOrderSorter.SortByEdges(order, new[] { ("a", "b") }, ranks);
        Assert.Equal(new[] { "a", "b" }, res);
    }

    [Fact]
    public void Edge_MovesAfterNode_Later()
    {
        var order = new[] { "x", "y", "z" };
        var ranks = Ranks(("x", 0), ("y", 0), ("z", 0));
        // y は z より後（z→y）。
        var res = LoadOrderSorter.SortByEdges(order, new[] { ("z", "y") }, ranks);
        Assert.True(res.ToList().IndexOf("y") > res.ToList().IndexOf("z"));
    }

    [Fact]
    public void Cycle_RemainderAppendedInCurrentOrder()
    {
        var order = new[] { "a", "b" };
        var ranks = Ranks(("a", 0), ("b", 0));
        var res = LoadOrderSorter.SortByEdges(order, new[] { ("a", "b"), ("b", "a") }, ranks);
        Assert.Equal(2, res.Count);
        Assert.Contains("a", res);
        Assert.Contains("b", res);
    }

    [Fact]
    public void UnknownNodeInEdge_Ignored()
    {
        var order = new[] { "a", "b" };
        var ranks = Ranks(("a", 0), ("b", 0));
        var res = LoadOrderSorter.SortByEdges(order, new[] { ("ghost", "a") }, ranks);
        Assert.Equal(new[] { "a", "b" }, res);
    }
}
