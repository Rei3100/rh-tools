using ReloadedHelper.Core;
using ReloadedHelper.Core.Analyzers;
using Xunit;

namespace ReloadedHelper.Core.Tests;

public class RedundancyDetectorTests
{
    private static ModInfo Mod(string id, params string[] deps) => new(
        id, id, "", "1", "",
        System.Array.Empty<string>(), deps, System.Array.Empty<string>(),
        new[] { "p5r" }, null, null, null, null, "");

    private static ModResources Res(string id, params string[] files)
    {
        var keys = new List<ResourceKey>();
        foreach (var f in files) keys.Add(new ResourceKey(ResourceKind.File, f));
        return new ModResources(id, keys);
    }

    private static Dictionary<string, ModInfo> Cat(params ModInfo[] mods)
    {
        var d = new Dictionary<string, ModInfo>(System.StringComparer.OrdinalIgnoreCase);
        foreach (var m in mods) d[m.ModId] = m;
        return d;
    }

    [Fact]
    public void HeavyOverlap_NoDependency_IsRedundant()
    {
        var ordered = new[] { Res("A", "x", "y", "z"), Res("B", "x", "y", "z") };
        var pairs = RedundancyDetector.Detect(ordered, Cat(Mod("A"), Mod("B")));
        Assert.Single(pairs);
    }

    [Fact]
    public void HeavyOverlap_WithDependency_IsNotRedundant()
    {
        // B が A に依存（土台＋利用側）なら重複ではない。
        var ordered = new[] { Res("A", "x", "y", "z"), Res("B", "x", "y", "z") };
        var pairs = RedundancyDetector.Detect(ordered, Cat(Mod("A"), Mod("B", "A")));
        Assert.Empty(pairs);
    }

    [Fact]
    public void SmallOverlap_IsNotRedundant()
    {
        var ordered = new[] { Res("A", "x", "y", "z", "p", "q"), Res("B", "x", "m", "n", "o", "r") };
        var pairs = RedundancyDetector.Detect(ordered, Cat(Mod("A"), Mod("B")));
        Assert.Empty(pairs);
    }
}
