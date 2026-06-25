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
    public void NoConflict_DoesNotReorder_EvenAcrossTypes()
    {
        // 競合が無ければ、種類が違っても現在順を保つ（churn ゼロ）。
        var order = new[] { "skin", "play" }; // skin(rank7) が前、play(rank1) が後ろ
        var types = Types(("skin", ModType.SkinTexture), ("play", ModType.Gameplay));

        var res = LoadOrderOptimizer.Optimize("p5r", order, NoDeps,
            System.Array.Empty<FileConflict>(), types, EmptyPrefs());

        Assert.Equal(new[] { "skin", "play" }, res.Order);
        Assert.Empty(res.Unresolved);
    }

    [Fact]
    public void Conflict_Specificity_TargetedOverrideWins()
    {
        // big は大型(資源100)、small は1ファイルだけ。small が後ろ＝勝ち。
        var order = new[] { "small", "big" };
        var conflicts = new[] { new FileConflict("file:a", new[] { "small", "big" }, "big") };
        var types = Types(("small", ModType.SkinTexture), ("big", ModType.SkinTexture));
        var resCount = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["small"] = 1, ["big"] = 100 };

        var res = LoadOrderOptimizer.Optimize("p5r", order, NoDeps, conflicts, types,
            EmptyPrefs(), null, resCount);

        Assert.True(res.Order.ToList().IndexOf("small") > res.Order.ToList().IndexOf("big"));
        Assert.Empty(res.Unresolved);
    }

    [Fact]
    public void Conflict_AlwaysDecides_NeverUnresolved()
    {
        // 同種・同資源数でも、現在順で必ず決め切る。Unresolved は出さない。
        var order = new[] { "a", "b" };
        var conflicts = new[] { new FileConflict("file:a", new[] { "a", "b" }, "b") };
        var types = Types(("a", ModType.SkinTexture), ("b", ModType.SkinTexture));

        var res = LoadOrderOptimizer.Optimize("p5r", order, NoDeps, conflicts, types, EmptyPrefs());

        Assert.Empty(res.Unresolved);
        Assert.Equal(new[] { "a", "b" }, res.Order); // b が後ろ＝勝ち、既に最後尾なので移動なし
    }

    [Fact]
    public void Preference_OverridesEvidence_For2Way()
    {
        var order = new[] { "small", "big" };
        var conflicts = new[] { new FileConflict("file:a", new[] { "small", "big" }, "big") };
        var types = Types(("small", ModType.SkinTexture), ("big", ModType.SkinTexture));
        var resCount = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["small"] = 1, ["big"] = 100 };
        var prefs = EmptyPrefs();
        prefs.SetWinner("p5r", "small", "big", "big"); // ユーザーは big を勝たせたい

        var res = LoadOrderOptimizer.Optimize("p5r", order, NoDeps, conflicts, types,
            prefs, null, resCount);

        Assert.True(res.Order.ToList().IndexOf("big") > res.Order.ToList().IndexOf("small"));
    }

    [Fact]
    public void Dependency_NotBroken_LogsReasonInstead()
    {
        // base が配置指示(Late)で勝者になるが、dependent が base に依存しているため
        // base を dependent より後ろへ動かせない（依存を壊さない）。
        // "要確認"にはせず、動かせない理由を残す。
        var order = new[] { "base", "dependent" };
        var deps = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        { ["dependent"] = new[] { "base" } };
        var conflicts = new[] { new FileConflict("file:a", new[] { "base", "dependent" }, "dependent") };
        var types = Types(("base", ModType.SkinTexture), ("dependent", ModType.SkinTexture));
        var hints = new Dictionary<string, PlacementHint>(StringComparer.OrdinalIgnoreCase)
        { ["base"] = PlacementHint.Late };

        var res = LoadOrderOptimizer.Optimize("p5r", order, deps, conflicts, types,
            EmptyPrefs(), null, null, hints);

        Assert.Equal(new[] { "base", "dependent" }, res.Order); // 依存順を維持
        Assert.Empty(res.Unresolved);
        Assert.Contains(res.Reasons, r => r.MovedModId == "base"); // 動かせない理由を記録
    }

    [Fact]
    public void AuthorHint_Late_Wins()
    {
        var order = new[] { "a", "b" };
        var conflicts = new[] { new FileConflict("file:a", new[] { "a", "b" }, "b") };
        var types = Types(("a", ModType.SkinTexture), ("b", ModType.SkinTexture));
        var hints = new Dictionary<string, PlacementHint>(StringComparer.OrdinalIgnoreCase)
        { ["a"] = PlacementHint.Late }; // a が「下に置け」

        var res = LoadOrderOptimizer.Optimize("p5r", order, NoDeps, conflicts, types,
            EmptyPrefs(), null, null, hints);

        Assert.True(res.Order.ToList().IndexOf("a") > res.Order.ToList().IndexOf("b"));
    }

    [Fact]
    public void Placements_UseProvidedTypeReasons()
    {
        var order = new[] { "a" };
        var types = Types(("a", ModType.SkinTexture));
        var reasons = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        { ["a"] = "名前・説明からスキン・テクスチャと判定" };

        var res = LoadOrderOptimizer.Optimize("p5r", order, NoDeps,
            System.Array.Empty<FileConflict>(), types, EmptyPrefs(), reasons);

        Assert.Equal("名前・説明からスキン・テクスチャと判定", res.Placements[0].Reason);
        Assert.Equal(ModTypeInfo.Rank(ModType.SkinTexture), res.Placements[0].LayerRank);
        Assert.Equal(ModTypeInfo.Label(ModType.SkinTexture), res.Placements[0].LayerLabel);
    }
}
