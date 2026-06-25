using ReloadedHelper.Core;
using Xunit;

namespace ReloadedHelper.Core.Tests;

public class WinnerResolverTests
{
    // 既定値のヘルパ：すべて中立。テストごとに必要な軸だけ差し替える。
    private static WinnerEvidence Ev(
        string[] mods,
        System.Func<string, PlacementHint>? hint = null,
        System.Func<string, string, bool>? deps = null,
        System.Func<string, int>? resCount = null,
        System.Func<string, ModType>? type = null)
    {
        var index = mods.Select((m, i) => (m, i))
            .ToDictionary(x => x.m, x => x.i, System.StringComparer.OrdinalIgnoreCase);
        return new WinnerEvidence(
            mods,
            hint ?? (_ => PlacementHint.None),
            deps ?? ((_, _) => false),
            resCount ?? (_ => 1),
            type ?? (_ => ModType.SkinTexture),
            m => index[m]);
    }

    [Fact]
    public void CurrentIndex_BreaksTie_LastWins()
    {
        // すべて互角 → 現在順で後ろの "b" が勝つ（last-wins 既定）。
        Assert.Equal("b", WinnerResolver.Resolve(Ev(new[] { "a", "b" })));
    }

    [Fact]
    public void Specificity_FewerResources_Wins()
    {
        // a は1ファイルだけの狙った上書き、b は500ファイルの大型 → a が勝つ。
        Assert.Equal("a", WinnerResolver.Resolve(
            Ev(new[] { "a", "b" }, resCount: m => m == "a" ? 1 : 500)));
    }

    [Fact]
    public void Dependency_ConsumerWins()
    {
        // a が b に依存（a は b を土台に使う拡張側）→ a が後ろ＝勝ち。資源数は a が多くても依存が優先。
        Assert.Equal("a", WinnerResolver.Resolve(
            Ev(new[] { "a", "b" }, deps: (x, y) => x == "a" && y == "b", resCount: m => m == "a" ? 50 : 1)));
    }

    [Fact]
    public void AuthorHint_Late_Wins_OverSpecificity()
    {
        // b は「下に置け」と明記 → 資源数で劣っても b が勝つ（指示が最優先）。
        Assert.Equal("b", WinnerResolver.Resolve(
            Ev(new[] { "a", "b" },
               hint: m => m == "b" ? PlacementHint.Late : PlacementHint.None,
               resCount: m => m == "a" ? 1 : 500)));
    }

    [Fact]
    public void AlwaysDecides_ThreeWay()
    {
        // 3件でも必ず1つ返す（null や例外にならない）。
        var w = WinnerResolver.Resolve(Ev(new[] { "x", "y", "z" }));
        Assert.Contains(w, new[] { "x", "y", "z" });
    }
}
