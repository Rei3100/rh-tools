using ReloadedHelper.Core;
using Xunit;

namespace ReloadedHelper.Core.Tests;

public class ConstraintGraphOptimizerTests
{
    private static readonly Dictionary<string, IReadOnlyList<string>> NoDeps = new();
    private static IReadOnlyDictionary<string, ModType> Types(params (string id, ModType t)[] xs)
        => xs.ToDictionary(x => x.id, x => x.t, StringComparer.OrdinalIgnoreCase);
    private static Dictionary<string, int> Counts(params (string id, int n)[] xs)
        => xs.ToDictionary(x => x.id, x => x.n, StringComparer.OrdinalIgnoreCase);
    private static int Idx(OptimizeResult r, string id) => r.Order.ToList().FindIndex(x => x == id);

    [Fact]
    public void Framework_GoesFirst_ByGroupRank()
    {
        // lib(Library=rank0) は現在順で最後でも、グループ優先度で前へ。競合なし。
        var order = new[] { "skin", "lib" };
        var types = Types(("skin", ModType.SkinTexture), ("lib", ModType.Library));
        var r = ConstraintGraphOptimizer.Optimize(order, NoDeps,
            System.Array.Empty<FileConflict>(), types, Counts(("skin", 5), ("lib", 0)));
        Assert.True(Idx(r, "lib") < Idx(r, "skin"));
        Assert.Empty(r.Unresolved);
    }

    [Fact]
    public void Conflict_FewerResources_Wins_EvenSameGroup()
    {
        // 同グループ。big(100) と small(1) が競合 → small が後＝勝ち。
        var order = new[] { "small", "big" };
        var types = Types(("small", ModType.SkinTexture), ("big", ModType.SkinTexture));
        var conflicts = new[] { new FileConflict("file:a", new[] { "small", "big" }, "big") };
        var r = ConstraintGraphOptimizer.Optimize(order, NoDeps, conflicts, types,
            Counts(("small", 1), ("big", 100)));
        Assert.True(Idx(r, "small") > Idx(r, "big"));
    }

    [Fact]
    public void Dependency_AlwaysRespected_OverOverlap()
    {
        // big は small に依存（small が前）。重なりは big(100)→small(1) を要求するが、
        // 依存 small→big と矛盾するので重なり辺は捨てる。結果 small が前。
        var order = new[] { "small", "big" };
        var deps = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        { ["big"] = new[] { "small" } };
        var conflicts = new[] { new FileConflict("file:a", new[] { "small", "big" }, "big") };
        var types = Types(("small", ModType.SkinTexture), ("big", ModType.SkinTexture));
        var r = ConstraintGraphOptimizer.Optimize(order, deps, conflicts, types,
            Counts(("small", 1), ("big", 100)));
        Assert.True(Idx(r, "small") < Idx(r, "big"));
        Assert.Empty(r.Unresolved);
    }

    [Fact]
    public void NoConflict_SameGroup_KeepsCurrentOrder()
    {
        var order = new[] { "b", "a" };
        var types = Types(("a", ModType.SkinTexture), ("b", ModType.SkinTexture));
        var r = ConstraintGraphOptimizer.Optimize(order, NoDeps,
            System.Array.Empty<FileConflict>(), types, Counts(("a", 3), ("b", 3)));
        Assert.Equal(new[] { "b", "a" }, r.Order);
    }

    [Fact]
    public void Dependency_NeverViolated_InMixedDepOverlapCycle()
    {
        // counts z(30)>x(20)>y(10)。重なり: x→y, z→x。依存: z は y に依存（y が前）。
        // 素朴実装だと x→y→z→x の循環で全員 stranded → 現在順(z,y)で末尾追加され依存が壊れる。
        // 正しい実装は z→x を捨てて依存 (y が z より前) を守る。
        var order = new[] { "x", "z", "y" }; // z が y より前＝依存違反を誘発する現在順
        var deps = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        { ["z"] = new[] { "y" } }; // z depends on y → y must load before z
        var conflicts = new[]
        {
            new FileConflict("file:a", new[] { "x", "y" }, "y"),
            new FileConflict("file:b", new[] { "z", "x" }, "x"),
        };
        var types = Types(("x", ModType.SkinTexture), ("y", ModType.SkinTexture), ("z", ModType.SkinTexture));
        var r = ConstraintGraphOptimizer.Optimize(order, deps, conflicts, types,
            Counts(("x", 20), ("y", 10), ("z", 30)));
        Assert.True(Idx(r, "y") < Idx(r, "z")); // 依存（y が z より前）は絶対守る
    }

    [Fact]
    public void Placements_CarryTypeLabelAndReason()
    {
        var order = new[] { "a" };
        var types = Types(("a", ModType.SkinTexture));
        var reasons = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        { ["a"] = "名前からスキン・テクスチャと判定" };
        var r = ConstraintGraphOptimizer.Optimize(order, NoDeps,
            System.Array.Empty<FileConflict>(), types, Counts(("a", 1)), reasons);
        Assert.Single(r.Placements);
        Assert.Equal("名前からスキン・テクスチャと判定", r.Placements[0].Reason);
        Assert.Equal(ModTypeInfo.Label(ModType.SkinTexture), r.Placements[0].LayerLabel);
    }
}
