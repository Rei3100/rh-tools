using ReloadedHelper.Core;
using Xunit;

namespace ReloadedHelper.Core.Tests;

public class LoadOrderOptimizerTests
{
    private static readonly Dictionary<string, IReadOnlyList<string>> NoDeps = new();
    private static IReadOnlyDictionary<string, ModRole> Roles(params (string id, ModRole r)[] xs)
        => xs.ToDictionary(x => x.id, x => x.r, StringComparer.OrdinalIgnoreCase);

    private static PreferenceStore EmptyPrefs()
        => new(System.IO.Directory.CreateTempSubdirectory().FullName);

    [Fact]
    public void Layering_PlacesVisualAfterBase_NoMoveNeeded()
    {
        // 層整列で base(rank1) が先、visual(rank3) が後ろになる。衝突移動は不要。
        var order = new[] { "visual", "base" };
        var conflicts = new[] { new FileConflict("file:hair.bin", new[] { "visual", "base" }, "base") };
        var roles = Roles(("visual", ModRole.VisualOverride), ("base", ModRole.BaseLayer));

        var res = LoadOrderOptimizer.Optimize("p5r", order, NoDeps, conflicts, roles, EmptyPrefs());

        Assert.True(res.Order.ToList().IndexOf("visual") > res.Order.ToList().IndexOf("base"));
        Assert.Empty(res.Unresolved);
        Assert.Empty(res.Reasons);                 // 層整列で解決済み＝移動記録なし
        Assert.Equal(2, res.Placements.Count);     // 全MODに配置理由が付く
    }

    [Fact]
    public void Preference_OverridesRole()
    {
        var order = new[] { "visual", "base" };
        var conflicts = new[] { new FileConflict("file:hair.bin", new[] { "visual", "base" }, "base") };
        var roles = Roles(("visual", ModRole.VisualOverride), ("base", ModRole.BaseLayer));
        var prefs = EmptyPrefs();
        prefs.SetWinner("p5r", "visual", "base", "base"); // ユーザーは base を勝たせたい

        var res = LoadOrderOptimizer.Optimize("p5r", order, NoDeps, conflicts, roles, prefs);

        // base が勝者＝後ろ。現状すでに base が後ろなので並びは維持。
        Assert.True(res.Order.ToList().IndexOf("base") > res.Order.ToList().IndexOf("visual"));
    }

    [Fact]
    public void Unknown_VsUnknown_IsUnresolved_NoMove()
    {
        var order = new[] { "x", "y" };
        var conflicts = new[] { new FileConflict("file:a", new[] { "x", "y" }, "y") };
        var roles = Roles(("x", ModRole.Unknown), ("y", ModRole.Unknown));

        var res = LoadOrderOptimizer.Optimize("p5r", order, NoDeps, conflicts, roles, EmptyPrefs());

        Assert.Equal(new[] { "x", "y" }, res.Order);
        Assert.Single(res.Unresolved);
    }

    [Fact]
    public void ThreeWay_LayeringPlacesVisualLast()
    {
        var order = new[] { "visual", "base1", "base2" };
        var conflicts = new[]
        {
            new FileConflict("file:hair.bin", new[] { "visual", "base1", "base2" }, "base2"),
        };
        var roles = Roles(
            ("visual", ModRole.VisualOverride),
            ("base1", ModRole.BaseLayer),
            ("base2", ModRole.BaseLayer));

        var res = LoadOrderOptimizer.Optimize("p5r", order, NoDeps, conflicts, roles, EmptyPrefs());

        var idx = res.Order.ToList();
        Assert.True(idx.IndexOf("visual") > idx.IndexOf("base1"));
        Assert.True(idx.IndexOf("visual") > idx.IndexOf("base2"));
        Assert.Empty(res.Unresolved);
        Assert.Empty(res.Reasons);
    }

    [Fact]
    public void Unresolved_IsDeduplicated_AcrossMultipleConflictingFiles()
    {
        // 同じ2MODが複数ファイルで競合 → 要確認は1件にまとめる（重複排除・順不同）。
        var order = new[] { "x", "y" };
        var roles = Roles(("x", ModRole.Unknown), ("y", ModRole.Unknown));
        var conflicts = new[]
        {
            new FileConflict("file:a", new[] { "x", "y" }, "y"),
            new FileConflict("file:b", new[] { "x", "y" }, "y"),
            new FileConflict("file:c", new[] { "y", "x" }, "x"), // 順番違いも同一視
        };

        var res = LoadOrderOptimizer.Optimize("p5r", order, NoDeps, conflicts, roles, EmptyPrefs());

        Assert.Single(res.Unresolved);
    }

    [Fact]
    public void ThreeWay_TwoTopRoles_IsUnresolved_NoMove()
    {
        // v1,v2(rank3) は base(rank1) の後ろへ層整列される。勝者一意でないので衝突は要確認。
        var order = new[] { "v1", "v2", "base" };
        var conflicts = new[]
        {
            new FileConflict("file:a", new[] { "v1", "v2", "base" }, "base"),
        };
        var roles = Roles(
            ("v1", ModRole.VisualOverride),
            ("v2", ModRole.VisualOverride),
            ("base", ModRole.BaseLayer));

        var res = LoadOrderOptimizer.Optimize("p5r", order, NoDeps, conflicts, roles, EmptyPrefs());

        Assert.Equal(new[] { "base", "v1", "v2" }, res.Order); // 層整列後の順
        Assert.Single(res.Unresolved);
    }

    [Fact]
    public void Placements_UseProvidedRoleReasons()
    {
        var order = new[] { "a" };
        var roles = Roles(("a", ModRole.VisualOverride));
        var reasons = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["a"] = "衣装フォルダがあるため見た目として後方に配置",
        };

        var res = LoadOrderOptimizer.Optimize("p5r", order, NoDeps,
            Array.Empty<FileConflict>(), roles, EmptyPrefs(), reasons);

        Assert.Equal("衣装フォルダがあるため見た目として後方に配置", res.Placements[0].Reason);
    }
}
