// tests/ReloadedHelper.Core.Tests/LoadOrderSorterTests.cs
using ReloadedHelper.Core;

namespace ReloadedHelper.Core.Tests;

public class LoadOrderSorterTests
{
    private static IReadOnlyDictionary<string, IReadOnlyList<string>> NoDeps =>
        new Dictionary<string, IReadOnlyList<string>>();

    [Fact]
    public void NoDependencies_PreservesCurrentOrder()
    {
        var order = new[] { "ModA", "ModB", "ModC" };
        var result = LoadOrderSorter.Sort(order, NoDeps);
        Assert.Equal(order, result);
    }

    [Fact]
    public void SimpleDependency_DependencyPlacedFirst()
    {
        // ModA depends on ModB → ModB must come before ModA
        var order = new[] { "ModA", "ModB" };
        var deps = new Dictionary<string, IReadOnlyList<string>>
        {
            ["ModA"] = new[] { "ModB" }
        };
        var result = LoadOrderSorter.Sort(order, deps);
        Assert.Equal(new[] { "ModB", "ModA" }, result);
    }

    [Fact]
    public void TransitiveDependency_AllSortedCorrectly()
    {
        // C depends on B, B depends on A → sorted: A, B, C
        var order = new[] { "C", "B", "A" };
        var deps = new Dictionary<string, IReadOnlyList<string>>
        {
            ["C"] = new[] { "B" },
            ["B"] = new[] { "A" }
        };
        var result = LoadOrderSorter.Sort(order, deps);
        Assert.Equal(new[] { "A", "B", "C" }, result);
    }

    [Fact]
    public void IndependentMods_PreserveRelativeOrder()
    {
        // ModC depends on ModA. ModB is independent.
        // Original: ModB(0), ModC(1), ModA(2)
        // ModB no deps → ready at idx 0; ModA no deps → ready at idx 2
        // ModC deps satisfied after ModA
        // Expected: ModB, ModA, ModC
        var order = new[] { "ModB", "ModC", "ModA" };
        var deps = new Dictionary<string, IReadOnlyList<string>>
        {
            ["ModC"] = new[] { "ModA" }
        };
        var result = LoadOrderSorter.Sort(order, deps);
        Assert.Equal(new[] { "ModB", "ModA", "ModC" }, result);
    }

    [Fact]
    public void CircularDependency_ReturnsAllModsWithoutThrowing()
    {
        var order = new[] { "ModA", "ModB" };
        var deps = new Dictionary<string, IReadOnlyList<string>>
        {
            ["ModA"] = new[] { "ModB" },
            ["ModB"] = new[] { "ModA" }
        };
        var result = LoadOrderSorter.Sort(order, deps);
        Assert.Equal(2, result.Count);
        Assert.Contains("ModA", result);
        Assert.Contains("ModB", result);
    }

    [Fact]
    public void UninstalledDependency_IsIgnored()
    {
        // ModA depends on LibX which is not in the list
        var order = new[] { "ModA", "ModB" };
        var deps = new Dictionary<string, IReadOnlyList<string>>
        {
            ["ModA"] = new[] { "LibX" }
        };
        var result = LoadOrderSorter.Sort(order, deps);
        // LibX not installed → no constraint on ModA → original order preserved
        Assert.Equal(new[] { "ModA", "ModB" }, result);
    }
}
