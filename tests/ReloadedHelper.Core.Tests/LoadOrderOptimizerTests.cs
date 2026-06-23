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
    public void RoleRule_VisualWinsOverBase_MovedAfterLoser()
    {
        // 現状順: visual が先(=負けてる)。base が後(=勝ってる)。Visual を勝たせたい→ visual を後へ。
        var order = new[] { "visual", "base" };
        var conflicts = new[] { new FileConflict("file:hair.bin", new[] { "visual", "base" }, "base") };
        var roles = Roles(("visual", ModRole.VisualOverride), ("base", ModRole.BaseLayer));

        var res = LoadOrderOptimizer.Optimize("p5r", order, NoDeps, conflicts, roles, EmptyPrefs());

        Assert.True(res.Order.ToList().IndexOf("visual") > res.Order.ToList().IndexOf("base"));
        Assert.Empty(res.Unresolved);
        Assert.Single(res.Reasons);
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
}
