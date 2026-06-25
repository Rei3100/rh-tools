using ReloadedHelper.Core;
using Xunit;

namespace ReloadedHelper.Core.Tests;

public class LoadOrderOptimizerTests
{
    private static readonly Dictionary<string, IReadOnlyList<string>> NoDeps = new();
    private static IReadOnlyDictionary<string, ModType> Types(params (string id, ModType t)[] xs)
        => xs.ToDictionary(x => x.id, x => x.t, StringComparer.OrdinalIgnoreCase);
    private static PreferenceStore EmptyPrefs()
        => new(System.IO.Directory.CreateTempSubdirectory().FullName);

    [Fact]
    public void Layering_PlacesVisualAfterGameplay()
    {
        // skintex(rank7) は gameplay(rank1) より後ろへ層整列される。衝突移動は不要。
        var order = new[] { "skin", "play" };
        var conflicts = new[] { new FileConflict("file:a", new[] { "skin", "play" }, "play") };
        var types = Types(("skin", ModType.SkinTexture), ("play", ModType.Gameplay));

        var res = LoadOrderOptimizer.Optimize("p5r", order, NoDeps, conflicts, types, EmptyPrefs());

        Assert.True(res.Order.ToList().IndexOf("skin") > res.Order.ToList().IndexOf("play"));
        Assert.Empty(res.Unresolved);
        Assert.Equal(2, res.Placements.Count);
    }

    [Fact]
    public void Preference_OverridesType()
    {
        var order = new[] { "skin", "play" };
        var conflicts = new[] { new FileConflict("file:a", new[] { "skin", "play" }, "play") };
        var types = Types(("skin", ModType.SkinTexture), ("play", ModType.Gameplay));
        var prefs = EmptyPrefs();
        prefs.SetWinner("p5r", "skin", "play", "play"); // ユーザーは play を勝たせたい

        var res = LoadOrderOptimizer.Optimize("p5r", order, NoDeps, conflicts, types, prefs);

        Assert.True(res.Order.ToList().IndexOf("play") > res.Order.ToList().IndexOf("skin"));
    }

    [Fact]
    public void SameType_IsUnresolved_NoMove()
    {
        var order = new[] { "a", "b" };
        var conflicts = new[] { new FileConflict("file:a", new[] { "a", "b" }, "b") };
        var types = Types(("a", ModType.SkinTexture), ("b", ModType.SkinTexture));

        var res = LoadOrderOptimizer.Optimize("p5r", order, NoDeps, conflicts, types, EmptyPrefs());

        Assert.Single(res.Unresolved);
    }

    [Fact]
    public void UnknownTop_IsUnresolved_NoMove()
    {
        var order = new[] { "x", "y" };
        var conflicts = new[] { new FileConflict("file:a", new[] { "x", "y" }, "y") };
        var types = Types(("x", ModType.Unknown), ("y", ModType.Unknown));

        var res = LoadOrderOptimizer.Optimize("p5r", order, NoDeps, conflicts, types, EmptyPrefs());

        Assert.Equal(new[] { "x", "y" }, res.Order);
        Assert.Single(res.Unresolved);
    }

    [Fact]
    public void Unresolved_IsDeduplicated()
    {
        var order = new[] { "x", "y" };
        var types = Types(("x", ModType.Unknown), ("y", ModType.Unknown));
        var conflicts = new[]
        {
            new FileConflict("file:a", new[] { "x", "y" }, "y"),
            new FileConflict("file:b", new[] { "x", "y" }, "y"),
            new FileConflict("file:c", new[] { "y", "x" }, "x"),
        };

        var res = LoadOrderOptimizer.Optimize("p5r", order, NoDeps, conflicts, types, EmptyPrefs());

        Assert.Single(res.Unresolved);
    }

    [Fact]
    public void Placements_UseProvidedTypeReasons()
    {
        var order = new[] { "a" };
        var types = Types(("a", ModType.SkinTexture));
        var reasons = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["a"] = "名前・説明からスキン・テクスチャと判定",
        };

        var res = LoadOrderOptimizer.Optimize("p5r", order, NoDeps,
            System.Array.Empty<FileConflict>(), types, EmptyPrefs(), reasons);

        Assert.Equal("名前・説明からスキン・テクスチャと判定", res.Placements[0].Reason);
        Assert.Equal(ModTypeInfo.Rank(ModType.SkinTexture), res.Placements[0].LayerRank);
        Assert.Equal(ModTypeInfo.Label(ModType.SkinTexture), res.Placements[0].LayerLabel);
    }
}
