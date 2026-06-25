# 証拠ベース競合エンジン フェーズ1（順序の正しさ）実装計画

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 自動配置エンジンを「種類ラダーで全体整列」から「依存＋実資源競合＋複数証拠で勝者を必ず決め切る」形へ作り替え、churn（無駄な並び替え）と "要確認" をなくす。

**Architecture:** 既存の資源解析（Analyzers→ConflictDetector）はそのまま使い、`LoadOrderOptimizer` の中身を入れ替える。種類の大域整列を撤去し、依存トポロジカルソート後に「実際に同じ資源を奪い合うMODだけ」を、証拠スコア（作者の配置指示 > 依存/拡張 > ピンポイント度 > 弱:種類 > 現在順）で勝者を決めて移動する。決められない場合でも現在順インデックスで必ず一意化するため "要確認" は発生しない。

**Tech Stack:** C# / .NET 10 / WPF（UIは本フェーズ対象外）/ xUnit。

**設計書:** docs/superpowers/specs/2026-06-25-autosort-evidence-engine-design.md（A・B・C がフェーズ1。D・E は別計画）。

## Global Constraints

- ランタイム NuGet パッケージの追加禁止（`System.Text.Json` のみ可）。テスト用 xUnit は可。
- ユーザーデータは `%APPDATA%\ReloadedHelper` 以外に保存禁止。
- 実行時に外部AI/Claude を呼ばない（オフライン・無料・ルール＋解析のみ）。
- 失敗・未対応は握りつぶさず記録する。自動修正前に必ずバックアップ、ワンタップ復元。
- **並び替えUIを新設しない**（ユーザーは並び替えしない・できない）。"要確認" でユーザーに順序を聞かない。
- コミットは Claude Code 経由で実施（`.claude/hooks/pre-commit-guard.ps1` がビルド・テストゲートを発火）。
- ビルド: `dotnet build reloaded-helper.slnx` / テスト: `dotnet test` / 個別: `dotnet test --filter "FullyQualifiedName~Class.Method"`。
- 既存ファイルの流儀に従う（static クラス＋record、日本語コメント、`StringComparer.OrdinalIgnoreCase`）。

## File Structure

| ファイル | 責務 | 区分 |
|---|---|---|
| `src/ReloadedHelper.Core/PlacementHint.cs` | 作者の配置指示（説明文）の解析。`PlacementHint`＋`PlacementHintParser` | 新規 |
| `src/ReloadedHelper.Core/WinnerResolver.cs` | 競合の勝者を複数証拠で必ず1つ決める。`WinnerEvidence`＋`WinnerResolver` | 新規 |
| `src/ReloadedHelper.Core/ModTypeClassifier.cs` | 資源を最優先に種類判定する overload を追加 | 変更 |
| `src/ReloadedHelper.Core/LoadOrderOptimizer.cs` | 大域整列撤去＋証拠勝者＋Unresolved廃止＋依存安全 | 変更 |
| `src/ReloadedHelper.Core/GameDiagnostics.cs` | 解析した資源（`ModResources`）を結果に同梱 | 変更 |
| `src/ReloadedHelper.Core/MainViewModel.cs` | 資源→種類/指示/資源数→新Optimize の配線。死にコード撤去 | 変更 |
| `src/ReloadedHelper.Core/ModDiagnostics.cs` | 「順序を入れ替えてください」文言を撤去（並べ替えさせない） | 変更 |
| `tests/.../PlacementHintParserTests.cs` | Task1 テスト | 新規 |
| `tests/.../WinnerResolverTests.cs` | Task2 テスト | 新規 |
| `tests/.../ModTypeClassifierResourceTests.cs` | Task3 テスト | 新規 |
| `tests/.../LoadOrderOptimizerTests.cs` | Task4 で全面書き換え | 変更 |
| `tests/.../GameDiagnosticsTests.cs` | Task5 で資源同梱を追加検証 | 変更 |
| `tests/.../AutoSortWiringTests.cs` | Task6 で配線更新 | 変更 |
| `tests/.../ManualReorderLearningTests.cs` | Task7 で削除 | 削除 |

---

## Task 1: 作者の配置指示パーサ（PlacementHintParser）

**Files:**
- Create: `src/ReloadedHelper.Core/PlacementHint.cs`
- Test: `tests/ReloadedHelper.Core.Tests/PlacementHintParserTests.cs`

**Interfaces:**
- Produces:
  - `enum PlacementHint { None, Late, Early }`
  - `static PlacementHint PlacementHintParser.Parse(ModInfo mod)`

- [ ] **Step 1: 失敗するテストを書く**

`tests/ReloadedHelper.Core.Tests/PlacementHintParserTests.cs`:

```csharp
using ReloadedHelper.Core;
using Xunit;

namespace ReloadedHelper.Core.Tests;

public class PlacementHintParserTests
{
    private static ModInfo Mod(string desc) => new(
        "id", "name", "", "1", desc,
        System.Array.Empty<string>(), System.Array.Empty<string>(), System.Array.Empty<string>(),
        new[] { "p5r" }, null, null, null, null, "");

    [Fact]
    public void Late_WhenDescriptionSaysLoadBelow()
        => Assert.Equal(PlacementHint.Late, PlacementHintParser.Parse(Mod("Make sure to load this below other mods.")));

    [Fact]
    public void Late_WhenJapaneseSaysBottom()
        => Assert.Equal(PlacementHint.Late, PlacementHintParser.Parse(Mod("このMODは一番下に置いてください。")));

    [Fact]
    public void Early_WhenDescriptionSaysLoadAbove()
        => Assert.Equal(PlacementHint.Early, PlacementHintParser.Parse(Mod("Load this above everything else.")));

    [Fact]
    public void None_ForNeutralDescription()
        => Assert.Equal(PlacementHint.None, PlacementHintParser.Parse(Mod("A retexture of Futaba.")));

    [Fact]
    public void None_WhenBothDirectionsPresent()
        => Assert.Equal(PlacementHint.None, PlacementHintParser.Parse(Mod("Load above A but below B.")));

    [Fact]
    public void None_ForEmpty()
        => Assert.Equal(PlacementHint.None, PlacementHintParser.Parse(Mod("")));
}
```

- [ ] **Step 2: 失敗を確認**

Run: `dotnet test --filter "FullyQualifiedName~PlacementHintParserTests"`
Expected: コンパイルエラー（`PlacementHint`/`PlacementHintParser` 未定義）。

- [ ] **Step 3: 実装する**

`src/ReloadedHelper.Core/PlacementHint.cs`:

```csharp
using System.Text.RegularExpressions;

namespace ReloadedHelper.Core;

// 作者がMODの説明文に書いた「読み込み順の指示」を、限定的・高精度なパターンで拾う。
// 確信が持てる表現だけを拾い、曖昧／相反する文は None（証拠なし）にする。
public enum PlacementHint { None, Late, Early }

public static class PlacementHintParser
{
    // 「後ろ/下/最後＝このMODを後勝ちにしたい」
    private static readonly Regex Late = new(
        @"load\s+(this\s+)?(mod\s+)?(below|after|last)\b|at\s+the\s+bottom|highest\s+priority|一番下に|最後に(読み込|ロード|配置|入れ)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // 「前/上/最初＝このMODを土台側にしたい」
    private static readonly Regex Early = new(
        @"load\s+(this\s+)?(mod\s+)?(above|before|first)\b|at\s+the\s+top|lowest\s+priority|一番上に|最初に(読み込|ロード|配置|入れ)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static PlacementHint Parse(ModInfo mod)
    {
        var text = mod.ModDescription;
        if (string.IsNullOrWhiteSpace(text)) return PlacementHint.None;
        bool late = Late.IsMatch(text);
        bool early = Early.IsMatch(text);
        if (late && !early) return PlacementHint.Late;
        if (early && !late) return PlacementHint.Early;
        return PlacementHint.None; // 相反／どちらも無し → 確信なし
    }
}
```

- [ ] **Step 4: テスト成功を確認**

Run: `dotnet test --filter "FullyQualifiedName~PlacementHintParserTests"`
Expected: PASS（6件）。

- [ ] **Step 5: コミット**

```bash
git add src/ReloadedHelper.Core/PlacementHint.cs tests/ReloadedHelper.Core.Tests/PlacementHintParserTests.cs
git commit -m "feat: 作者の配置指示パーサ PlacementHintParser を追加"
```

---

## Task 2: 証拠で勝者を決める WinnerResolver

**Files:**
- Create: `src/ReloadedHelper.Core/WinnerResolver.cs`
- Test: `tests/ReloadedHelper.Core.Tests/WinnerResolverTests.cs`

**Interfaces:**
- Consumes: `ModType`/`ModTypeInfo.Rank`（既存）、`PlacementHint`（Task 1）
- Produces:
  - `sealed record WinnerEvidence(IReadOnlyList<string> ConflictMods, Func<string,PlacementHint> HintOf, Func<string,string,bool> DependsOn, Func<string,int> ResourceCount, Func<string,ModType> TypeOf, Func<string,int> CurrentIndex)`
  - `static string WinnerResolver.Resolve(WinnerEvidence ev)` — 競合MODから「後ろ＝勝ち」にすべき1つを必ず返す。

- [ ] **Step 1: 失敗するテストを書く**

`tests/ReloadedHelper.Core.Tests/WinnerResolverTests.cs`:

```csharp
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
```

- [ ] **Step 2: 失敗を確認**

Run: `dotnet test --filter "FullyQualifiedName~WinnerResolverTests"`
Expected: コンパイルエラー（`WinnerEvidence`/`WinnerResolver` 未定義）。

- [ ] **Step 3: 実装する**

`src/ReloadedHelper.Core/WinnerResolver.cs`:

```csharp
namespace ReloadedHelper.Core;

// 競合する全MODの中から「後ろ＝勝ち（反映される側）」にすべき1つを、複数の証拠で必ず決め切る。
// 証拠は強い順にタプル比較し、最後は現在順インデックスで必ず一意化する（＝"決められない"が無い）。
public sealed record WinnerEvidence(
    IReadOnlyList<string> ConflictMods,
    Func<string, PlacementHint> HintOf,           // 作者の配置指示
    Func<string, string, bool> DependsOn,         // a が b に依存
    Func<string, int> ResourceCount,              // 触る資源の総数（少ない＝狙った上書き）
    Func<string, ModType> TypeOf,
    Func<string, int> CurrentIndex);              // 現在順での位置（後ろほど大）

public static class WinnerResolver
{
    public static string Resolve(WinnerEvidence ev)
    {
        string? best = null;
        (int hint, int ext, int spec, int rank, int idx) bestKey = default;
        foreach (var m in ev.ConflictMods)
        {
            var key = KeyFor(m, ev);
            if (best is null || Compare(key, bestKey) > 0) { best = m; bestKey = key; }
        }
        return best!; // ConflictMods は2件以上（呼び出し側が保証）
    }

    private static (int hint, int ext, int spec, int rank, int idx) KeyFor(string m, WinnerEvidence ev)
    {
        int hint = ev.HintOf(m) switch { PlacementHint.Late => 1, PlacementHint.Early => -1, _ => 0 };
        int ext = ev.ConflictMods.Any(o =>
            !string.Equals(o, m, StringComparison.OrdinalIgnoreCase) && ev.DependsOn(m, o)) ? 1 : 0;
        int spec = -ev.ResourceCount(m);            // 資源が少ない＝後ろ
        int rank = ModTypeInfo.Rank(ev.TypeOf(m));  // 弱い tie-break
        int idx = ev.CurrentIndex(m);               // 現在順で後ろほど勝ち＋一意化
        return (hint, ext, spec, rank, idx);
    }

    private static int Compare(
        (int hint, int ext, int spec, int rank, int idx) a,
        (int hint, int ext, int spec, int rank, int idx) b)
    {
        int c;
        if ((c = a.hint.CompareTo(b.hint)) != 0) return c;
        if ((c = a.ext.CompareTo(b.ext)) != 0) return c;
        if ((c = a.spec.CompareTo(b.spec)) != 0) return c;
        if ((c = a.rank.CompareTo(b.rank)) != 0) return c;
        return a.idx.CompareTo(b.idx);
    }
}
```

- [ ] **Step 4: テスト成功を確認**

Run: `dotnet test --filter "FullyQualifiedName~WinnerResolverTests"`
Expected: PASS（5件）。

- [ ] **Step 5: コミット**

```bash
git add src/ReloadedHelper.Core/WinnerResolver.cs tests/ReloadedHelper.Core.Tests/WinnerResolverTests.cs
git commit -m "feat: 証拠で競合勝者を決める WinnerResolver を追加"
```

---

## Task 3: 資源を最優先にする種類判定 overload

**Files:**
- Modify: `src/ReloadedHelper.Core/ModTypeClassifier.cs`
- Test: `tests/ReloadedHelper.Core.Tests/ModTypeClassifierResourceTests.cs`

**Interfaces:**
- Consumes: `Analyzers.ResourceKey`/`Analyzers.ResourceKind`（既存）
- Produces: `static TypeDecision ModTypeClassifier.Classify(ModInfo mod, string? category, IReadOnlyList<Analyzers.ResourceKey> resources)`

- [ ] **Step 1: 失敗するテストを書く**

`tests/ReloadedHelper.Core.Tests/ModTypeClassifierResourceTests.cs`:

```csharp
using ReloadedHelper.Core;
using ReloadedHelper.Core.Analyzers;
using Xunit;

namespace ReloadedHelper.Core.Tests;

public class ModTypeClassifierResourceTests
{
    private static ModInfo Mod(string name) => new(
        "id", name, "", "1", "",
        System.Array.Empty<string>(), System.Array.Empty<string>(), System.Array.Empty<string>(),
        new[] { "p5r" }, null, null, null, null, "");

    [Fact]
    public void SongResource_WinsOverMisleadingName()
    {
        // 名前は音楽と無関係でも、曲資源を触るなら音楽。
        var res = new[] { new ResourceKey(ResourceKind.Song, "10") };
        Assert.Equal(ModType.Music, ModTypeClassifier.Classify(Mod("Cool Pack"), null, res).Type);
    }

    [Fact]
    public void CostumeResource_IsCostume()
    {
        var res = new[] { new ResourceKey(ResourceKind.Costume, "joker/0") };
        Assert.Equal(ModType.Costume, ModTypeClassifier.Classify(Mod("Pack"), null, res).Type);
    }

    [Fact]
    public void NoResources_FallsBackToExistingLogic()
    {
        // 資源が無ければ従来のキーワード判定（名前に "portrait"）。
        Assert.Equal(ModType.Portrait,
            ModTypeClassifier.Classify(Mod("Futaba portrait"), null, System.Array.Empty<ResourceKey>()).Type);
    }
}
```

- [ ] **Step 2: 失敗を確認**

Run: `dotnet test --filter "FullyQualifiedName~ModTypeClassifierResourceTests"`
Expected: コンパイルエラー（3引数 overload 未定義）。

- [ ] **Step 3: 実装する**

`src/ReloadedHelper.Core/ModTypeClassifier.cs` の先頭 using を追加：

```csharp
using System.IO;
using ReloadedHelper.Core.Analyzers;
```

`Classify(ModInfo mod, string? category)` の**直前**に overload を追加：

```csharp
    // 資源（実際にゲームの何を触るか）を最優先に種類を判定する。
    // 曲・コスチュームは資源で確定。それ以外は従来のカテゴリ/キーワード判定へ委譲。
    public static TypeDecision Classify(ModInfo mod, string? category, IReadOnlyList<ResourceKey> resources)
    {
        if (mod.IsLibrary)
            return new(ModType.Library, "ライブラリ指定のため前方に配置");
        if (resources.Any(r => r.Kind == ResourceKind.Song))
            return new(ModType.Music, "曲データを書き換えるため音楽として配置");
        if (resources.Any(r => r.Kind == ResourceKind.Costume))
            return new(ModType.Costume, "コスチューム枠を登録するため衣装として配置");
        return Classify(mod, category);
    }
```

- [ ] **Step 4: テスト成功を確認**

Run: `dotnet test --filter "FullyQualifiedName~ModTypeClassifierResourceTests"`
Expected: PASS（3件）。既存 `ModTypeClassifierTests` も緑のまま（`dotnet test --filter "FullyQualifiedName~ModTypeClassifierTests"`）。

- [ ] **Step 5: コミット**

```bash
git add src/ReloadedHelper.Core/ModTypeClassifier.cs tests/ReloadedHelper.Core.Tests/ModTypeClassifierResourceTests.cs
git commit -m "feat: 資源優先の種類判定 overload を ModTypeClassifier に追加"
```

---

## Task 4: LoadOrderOptimizer を作り替える（大域整列撤去・証拠勝者・Unresolved廃止）

**Files:**
- Modify: `src/ReloadedHelper.Core/LoadOrderOptimizer.cs`（`Optimize` 本体と `DecideByType` を置換）
- Test: `tests/ReloadedHelper.Core.Tests/LoadOrderOptimizerTests.cs`（全面書き換え）

**Interfaces:**
- Consumes: `WinnerResolver`/`WinnerEvidence`（Task 2）、`PlacementHint`（Task 1）、`LoadOrderSorter`（既存）
- Produces（新シグネチャ。**新引数は既存呼び出しを壊さないよう `typeReasons` の後ろに任意で追加**）:
  ```csharp
  static OptimizeResult Optimize(
      string appId,
      IReadOnlyList<string> currentOrder,
      IReadOnlyDictionary<string, IReadOnlyList<string>> dependenciesOf,
      IReadOnlyList<FileConflict> conflicts,
      IReadOnlyDictionary<string, ModType> typesByMod,
      PreferenceStore prefs,
      IReadOnlyDictionary<string, string>? typeReasons = null,
      IReadOnlyDictionary<string, int>? resourceCountByMod = null,
      IReadOnlyDictionary<string, PlacementHint>? hintsByMod = null)
  ```
  `OptimizeResult.Unresolved` は**常に空**（互換のため型は残す。完全撤去はフェーズ2）。

- [ ] **Step 1: テストを全面書き換え（失敗させる）**

`tests/ReloadedHelper.Core.Tests/LoadOrderOptimizerTests.cs` を以下で**置き換え**：

```csharp
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
        // winner=small を後ろにしたいが、big が small に依存（small が big の土台）。
        // 依存を壊さない＝small を前に保つ。Unresolved にはしない。理由を残す。
        var order = new[] { "small", "big" };
        var deps = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        { ["big"] = new[] { "small" } };
        var conflicts = new[] { new FileConflict("file:a", new[] { "small", "big" }, "big") };
        var types = Types(("small", ModType.SkinTexture), ("big", ModType.SkinTexture));
        var resCount = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["small"] = 1, ["big"] = 100 };

        var res = LoadOrderOptimizer.Optimize("p5r", order, deps, conflicts, types, EmptyPrefs(), null, resCount);

        Assert.Equal(new[] { "small", "big" }, res.Order); // 依存順を維持
        Assert.Empty(res.Unresolved);
        Assert.Contains(res.Reasons, r => r.MovedModId == "small"); // 動かせない理由を記録
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
```

- [ ] **Step 2: 失敗を確認**

Run: `dotnet test --filter "FullyQualifiedName~LoadOrderOptimizerTests"`
Expected: 旧実装は新シグネチャ/挙動に合わず FAIL（コンパイルエラー含む）。

- [ ] **Step 3: Optimize を作り替える**

`src/ReloadedHelper.Core/LoadOrderOptimizer.cs` の `Optimize` メソッド全体と `DecideByType` を、以下で**置き換え**（`PlacementReason`/`ModPlacement`/`OptimizeResult` の record 定義と `IndexOf` ヘルパはそのまま残す）：

```csharp
    public static OptimizeResult Optimize(
        string appId,
        IReadOnlyList<string> currentOrder,
        IReadOnlyDictionary<string, IReadOnlyList<string>> dependenciesOf,
        IReadOnlyList<FileConflict> conflicts,
        IReadOnlyDictionary<string, ModType> typesByMod,
        PreferenceStore prefs,
        IReadOnlyDictionary<string, string>? typeReasons = null,
        IReadOnlyDictionary<string, int>? resourceCountByMod = null,
        IReadOnlyDictionary<string, PlacementHint>? hintsByMod = null)
    {
        // 1. 依存を満たす基準順。大域的な種類整列は行わない（競合に無関係なMODは動かさない）。
        var order = LoadOrderSorter.Sort(currentOrder, dependenciesOf).ToList();
        var reasons = new List<PlacementReason>();

        // 証拠の引き当て（依存ソート後の位置を安定 tiebreak に使う）
        var indexSnapshot = order
            .Select((id, i) => (id, i))
            .ToDictionary(x => x.id, x => x.i, StringComparer.OrdinalIgnoreCase);
        int CurrentIndex(string id) => indexSnapshot.TryGetValue(id, out var i) ? i : int.MaxValue;
        int ResourceCount(string id) =>
            resourceCountByMod is not null && resourceCountByMod.TryGetValue(id, out var n) ? n : int.MaxValue;
        PlacementHint HintOf(string id) =>
            hintsByMod is not null && hintsByMod.TryGetValue(id, out var h) ? h : PlacementHint.None;
        ModType TypeOf(string id) => typesByMod.GetValueOrDefault(id, ModType.Unknown);
        bool DependsOn(string mod, string maybeDep) =>
            dependenciesOf.TryGetValue(mod, out var d) && d.Contains(maybeDep, StringComparer.OrdinalIgnoreCase);

        foreach (var c in conflicts)
        {
            if (c.ModIds.Count < 2) continue;

            // 勝者：2件はユーザーの好み（あれば）を最優先、無ければ証拠で必ず決め切る。
            string winner =
                (c.ModIds.Count == 2 ? prefs.GetWinner(appId, c.ModIds[0], c.ModIds[1]) : null)
                ?? WinnerResolver.Resolve(new WinnerEvidence(
                    c.ModIds, HintOf, DependsOn, ResourceCount, TypeOf, CurrentIndex));

            var losers = c.ModIds
                .Where(m => !string.Equals(m, winner, StringComparison.OrdinalIgnoreCase))
                .ToList();

            int wi = IndexOf(order, winner);
            if (wi < 0) continue;

            // 依存を壊す移動はしない（依存＝最強の制約）。勝たせられない事情を正直に記録。
            var blocking = losers.Where(l => DependsOn(l, winner)).ToList();
            if (blocking.Count > 0)
            {
                reasons.Add(new PlacementReason(winner, blocking[^1],
                    $"「{winner}」を後ろにしたいところですが、「{blocking[^1]}」が「{winner}」に依存するため前に保ちます（結果「{blocking[^1]}」側が反映されます）。"));
                continue;
            }

            int maxLoser = losers.Select(l => IndexOf(order, l)).DefaultIfEmpty(-1).Max();
            if (maxLoser < 0 || wi > maxLoser) continue; // すでに全敗者より後ろ＝OK

            order.RemoveAt(wi);
            maxLoser = losers.Select(l => IndexOf(order, l)).Max();
            order.Insert(maxLoser + 1, winner);
            var loserLabel = losers.Count == 1 ? losers[0] : $"{losers.Count}個のMOD";
            reasons.Add(new PlacementReason(winner, losers[^1],
                $"「{winner}」を{loserLabel}より後ろに配置しました（{winner} の上書きを反映）。"));
        }

        var placements = order.Select(id =>
        {
            var type = typesByMod.GetValueOrDefault(id, ModType.Unknown);
            var reason = typeReasons != null && typeReasons.TryGetValue(id, out var r) && !string.IsNullOrWhiteSpace(r)
                ? r
                : $"{ModTypeInfo.Label(type)}として配置";
            return new ModPlacement(id, ModTypeInfo.Rank(type), ModTypeInfo.Label(type), reason);
        }).ToList();

        // Unresolved は廃止（必ず決め切る）。互換のため空で返す。
        return new OptimizeResult(order, reasons,
            System.Array.Empty<(string A, string B)>(), placements);
    }
```

`DecideByType` メソッドは**削除**する（`WinnerResolver` に置換）。`IndexOf` は残す。

- [ ] **Step 4: テスト成功を確認**

Run: `dotnet test --filter "FullyQualifiedName~LoadOrderOptimizerTests"`
Expected: PASS（7件）。

- [ ] **Step 5: 全体ビルド・テストで巻き込み破損が無いか確認**

Run: `dotnet build reloaded-helper.slnx` → 0 errors（MainViewModel の既存7引数呼び出しは任意引数のおかげでそのままコンパイル）。
Run: `dotnet test` → 全件 PASS。

- [ ] **Step 6: コミット**

```bash
git add src/ReloadedHelper.Core/LoadOrderOptimizer.cs tests/ReloadedHelper.Core.Tests/LoadOrderOptimizerTests.cs
git commit -m "feat: LoadOrderOptimizer を証拠ベース競合エンジンへ作り替え（大域整列撤去・Unresolved廃止）"
```

---

## Task 5: 解析した資源を GameDiagnostics の結果に同梱

**Files:**
- Modify: `src/ReloadedHelper.Core/GameDiagnostics.cs`
- Test: `tests/ReloadedHelper.Core.Tests/GameDiagnosticsTests.cs`（検証を1件追加）

**Interfaces:**
- Produces: `GameDiagnosticsResult` に `IReadOnlyList<ModResources> Resources` を追加（第3引数）。`GameDiagnostics.Run` がそれを返す。

- [ ] **Step 1: 失敗するテストを追加**

`tests/ReloadedHelper.Core.Tests/GameDiagnosticsTests.cs` のクラス内に追加（既存 using に `using ReloadedHelper.Core.Analyzers;` が無ければ足す）：

```csharp
    [Fact]
    public void Run_ExposesAnalyzedResources()
    {
        // 有効MODごとに解析済み資源が返ること（件数は0以上、ModIdが揃う）。
        // ※ 既存テストの Game()/Catalog() ヘルパを流用。詳細な資源数は各 Analyzer テストで担保。
        var (game, catalog) = MinimalGame();
        var result = GameDiagnostics.Run(game, catalog);
        Assert.NotNull(result.Resources);
        Assert.All(result.Resources, r => Assert.False(string.IsNullOrEmpty(r.ModId)));
    }
```

> 実装メモ：`MinimalGame()` は既存テストに無ければ、同ファイル内の既存ヘルパ（`GameInfo`/`Dictionary<string,ModInfo>` を作る既存パターン）に合わせて1つ用意する。最小例：
> ```csharp
> private static (GameInfo, Dictionary<string, ModInfo>) MinimalGame()
> {
>     var game = new GameInfo("p5r", "P5R", "", null, new[] { "m" }, new[] { "m" }, "");
>     var catalog = new Dictionary<string, ModInfo>(StringComparer.OrdinalIgnoreCase)
>     {
>         ["m"] = new("m", "M", "", "1", "", System.Array.Empty<string>(), System.Array.Empty<string>(),
>             System.Array.Empty<string>(), new[] { "p5r" }, null, null, null, null, ""),
>     };
>     return (game, catalog);
> }
> ```

- [ ] **Step 2: 失敗を確認**

Run: `dotnet test --filter "FullyQualifiedName~GameDiagnosticsTests.Run_ExposesAnalyzedResources"`
Expected: コンパイルエラー（`GameDiagnosticsResult.Resources` 未定義）。

- [ ] **Step 3: 実装する**

`src/ReloadedHelper.Core/GameDiagnostics.cs`：

レコード定義を変更：
```csharp
public sealed record GameDiagnosticsResult(
    IReadOnlyList<FileConflict> Conflicts,
    IReadOnlyList<Diagnostic> Diagnostics,
    IReadOnlyList<ModResources> Resources);
```

`Run` の末尾 return を変更：
```csharp
        var conflicts = ConflictDetector.Detect(ordered);
        var diagnostics = ModDiagnostics.Analyze(game, catalog, conflicts, structureWarnings);
        return new GameDiagnosticsResult(conflicts, diagnostics, ordered);
```

- [ ] **Step 4: テスト成功＋全体確認**

Run: `dotnet test --filter "FullyQualifiedName~GameDiagnosticsTests"` → PASS。
Run: `dotnet build reloaded-helper.slnx` → 0 errors（`new GameDiagnosticsResult(...)` の直接生成箇所が他に無いことを確認。`Run` 経由の利用は読み取りのみで影響なし）。

- [ ] **Step 5: コミット**

```bash
git add src/ReloadedHelper.Core/GameDiagnostics.cs tests/ReloadedHelper.Core.Tests/GameDiagnosticsTests.cs
git commit -m "feat: GameDiagnostics の結果に解析済み資源(Resources)を同梱"
```

---

## Task 6: MainViewModel.RunAutoSort を新エンジンへ配線

**Files:**
- Modify: `src/ReloadedHelper.Core/MainViewModel.cs`（`RunAutoSort`、`BuildTypeDecisions`、`BuildHints` 追加）
- Test: `tests/ReloadedHelper.Core.Tests/AutoSortWiringTests.cs`（`BuildTypeDecisions` 呼び出しの更新）

**Interfaces:**
- Consumes: Task 4 の新 `Optimize`、Task 5 の `GameDiagnosticsResult.Resources`、Task 3 の資源 overload、Task 1 の `PlacementHintParser`
- Produces:
  - `BuildTypeDecisions(IReadOnlyList<ModLoadEntry>, IReadOnlyDictionary<string,ModInfo>, IReadOnlyDictionary<string,IReadOnlyList<Analyzers.ResourceKey>>)`（resources 引数を追加）
  - `private static IReadOnlyDictionary<string,PlacementHint> BuildHints(IReadOnlyList<ModLoadEntry>, IReadOnlyDictionary<string,ModInfo>)`

- [ ] **Step 1: 既存配線テストを新シグネチャに合わせて更新（失敗させる）**

`tests/ReloadedHelper.Core.Tests/AutoSortWiringTests.cs` 内で `MainViewModel.BuildTypeDecisions(entries, catalog)` を呼んでいる箇所を、第3引数（空の資源マップ）付きに更新。例：

```csharp
var resources = new Dictionary<string, IReadOnlyList<ReloadedHelper.Core.Analyzers.ResourceKey>>(
    System.StringComparer.OrdinalIgnoreCase);
var decisions = MainViewModel.BuildTypeDecisions(entries, catalog, resources);
```

> ファイル先頭に `using ReloadedHelper.Core.Analyzers;` が無ければ足す。`BuildTypeDecisions` を呼ぶ全テストを同様に更新する。

- [ ] **Step 2: 失敗を確認**

Run: `dotnet test --filter "FullyQualifiedName~AutoSortWiringTests"`
Expected: コンパイルエラー（`BuildTypeDecisions` の引数不一致）。

- [ ] **Step 3: 実装する**

`src/ReloadedHelper.Core/MainViewModel.cs`：

(3-1) `RunAutoSort` の解析〜Optimize 呼び出し部（現行 225〜234 行付近）を以下に置換：

```csharp
        var diagResult = GameDiagnostics.Run(game, _catalog);

        var resourcesByMod = diagResult.Resources.ToDictionary(
            r => r.ModId, r => r.Resources, StringComparer.OrdinalIgnoreCase);
        var resourceCount = diagResult.Resources.ToDictionary(
            r => r.ModId, r => r.Resources.Count, StringComparer.OrdinalIgnoreCase);

        var decisions = BuildTypeDecisions(_allEntries, _catalog, resourcesByMod);
        var types = decisions.ToDictionary(kv => kv.Key, kv => kv.Value.Type, StringComparer.OrdinalIgnoreCase);
        var typeReasons = decisions.ToDictionary(kv => kv.Key, kv => kv.Value.Reason, StringComparer.OrdinalIgnoreCase);
        var hints = BuildHints(_allEntries, _catalog);
        var depMap = _catalog.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlyList<string>)kv.Value.Dependencies,
            StringComparer.OrdinalIgnoreCase);

        var result = LoadOrderOptimizer.Optimize(appId, game.SortedMods, depMap, diagResult.Conflicts,
            types, _prefs, typeReasons, resourceCount, hints);
```

(3-2) `BuildTypeDecisions` を resources 受け取りに変更：

```csharp
    internal static IReadOnlyDictionary<string, TypeDecision> BuildTypeDecisions(
        IReadOnlyList<ModLoadEntry> entries,
        IReadOnlyDictionary<string, ModInfo> catalog,
        IReadOnlyDictionary<string, IReadOnlyList<Analyzers.ResourceKey>> resourcesByMod)
    {
        var map = new Dictionary<string, TypeDecision>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in entries)
        {
            var info = e.Info ?? (catalog.TryGetValue(e.ModId, out var ci) ? ci : null);
            var res = resourcesByMod.TryGetValue(e.ModId, out var r)
                ? r : System.Array.Empty<Analyzers.ResourceKey>();
            map[e.ModId] = info is null
                ? new TypeDecision(ModType.Unknown, "情報が無いため末尾に配置")
                : ModTypeClassifier.Classify(info, e.Category, res);
        }
        return map;
    }

    private static IReadOnlyDictionary<string, PlacementHint> BuildHints(
        IReadOnlyList<ModLoadEntry> entries,
        IReadOnlyDictionary<string, ModInfo> catalog)
    {
        var map = new Dictionary<string, PlacementHint>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in entries)
        {
            var info = e.Info ?? (catalog.TryGetValue(e.ModId, out var ci) ? ci : null);
            if (info is not null) map[e.ModId] = PlacementHintParser.Parse(info);
        }
        return map;
    }
```

> ファイル先頭の using に `using ReloadedHelper.Core.Analyzers;` が無ければ追加（`Analyzers.ResourceKey` を完全修飾で書いているため未追加でも可。既存の書き方に合わせる）。

- [ ] **Step 4: テスト成功を確認**

Run: `dotnet test --filter "FullyQualifiedName~AutoSortWiringTests"` → PASS。
Run: `dotnet build reloaded-helper.slnx` → 0 errors。
Run: `dotnet test` → 全件 PASS。

- [ ] **Step 5: コミット**

```bash
git add src/ReloadedHelper.Core/MainViewModel.cs tests/ReloadedHelper.Core.Tests/AutoSortWiringTests.cs
git commit -m "feat: RunAutoSort を資源・配置指示つき新エンジンへ配線"
```

---

## Task 7: 死にコード撤去と診断文言の修正

**Files:**
- Modify: `src/ReloadedHelper.Core/MainViewModel.cs`（`LearnFromManualOrder` を削除）
- Delete: `tests/ReloadedHelper.Core.Tests/ManualReorderLearningTests.cs`
- Modify: `src/ReloadedHelper.Core/ModDiagnostics.cs`（「順序を入れ替えてください」を撤去）
- Test: `tests/ReloadedHelper.Core.Tests/ModDiagnosticsTests.cs`（旧文言を検証していれば更新）

- [ ] **Step 1: 文言テストを先に直す（失敗させる）**

`ModDiagnosticsTests.cs` に「順序を入れ替えて」を検証しているアサーションがあれば、新文言「自動配置で後勝ち側を優先しています」に更新する。無ければ新規に1件追加：

```csharp
    [Fact]
    public void ConflictDiagnostic_DoesNotTellUserToReorder()
    {
        // ユーザーは並び替えしない・できない。手動並べ替えを促す文言を出さない。
        var loser = "loser"; var winner = "winner";
        var game = new GameInfo("p5r", "P5R", "", null, new[] { loser, winner }, new[] { loser, winner }, "");
        var catalog = new Dictionary<string, ModInfo>(StringComparer.OrdinalIgnoreCase)
        {
            [loser] = Mod(loser), [winner] = Mod(winner),
        };
        var conflicts = new[] { new FileConflict("file:a", new[] { loser, winner }, winner) };

        var diags = ModDiagnostics.Analyze(game, catalog, conflicts);

        Assert.DoesNotContain(diags, d => d.Message.Contains("入れ替え"));
    }

    private static ModInfo Mod(string id) => new(
        id, id, "", "1", "", System.Array.Empty<string>(), System.Array.Empty<string>(),
        System.Array.Empty<string>(), new[] { "p5r" }, null, null, null, null, "");
```

> `Mod` ヘルパが既にあれば重複定義しない。

- [ ] **Step 2: 失敗を確認**

Run: `dotnet test --filter "FullyQualifiedName~ModDiagnosticsTests.ConflictDiagnostic_DoesNotTellUserToReorder"`
Expected: FAIL（現行文言に「入れ替えてください」を含む）。

- [ ] **Step 3: 文言を修正**

`src/ReloadedHelper.Core/ModDiagnostics.cs` の競合 Info メッセージ（現行54行付近）を変更：

```csharp
        foreach (var (key, count) in pairCount)
            result.Add(new Diagnostic(key.Loser, DiagnosticSeverity.Info,
                $"このMODの {count} 個の項目が「{DisplayName(catalog, key.Winner)}」に上書きされています（自動配置で後勝ち側を優先しています）。"));
```

- [ ] **Step 4: 死にコードを削除**

`src/ReloadedHelper.Core/MainViewModel.cs` から `internal static IReadOnlyList<(string Winner, string Loser)> LearnFromManualOrder(...)` メソッド全体を削除（手動並び替えは存在しないため）。
`tests/ReloadedHelper.Core.Tests/ManualReorderLearningTests.cs` を削除。

```bash
git rm tests/ReloadedHelper.Core.Tests/ManualReorderLearningTests.cs
```

- [ ] **Step 5: テスト成功＋全体確認**

Run: `dotnet build reloaded-helper.slnx` → 0 errors（`LearnFromManualOrder` の参照が他に無いこと。App 層も grep 済みで未配線）。
Run: `dotnet test` → 全件 PASS。

- [ ] **Step 6: コミット**

```bash
git add -A
git commit -m "refactor: 手動並び替え学習(死にコード)を撤去し、診断の並べ替え促し文言を削除"
```

---

## Task 8: 全回帰＋実機スモーク＋last-wins前提の検証メモ

**Files:**
- なし（検証とドキュメントのみ）。必要なら `docs/superpowers/specs/2026-06-25-autosort-evidence-engine-design.md` に「実機検証結果」節を追記。

- [ ] **Step 1: 全テスト緑・Releaseビルド**

Run: `dotnet test` → 全件 PASS。
Run: `dotnet build reloaded-helper.slnx -c Release` → 0 errors。

- [ ] **Step 2: 自動更新を抑止したローカル発行**

Run:
```bash
dotnet publish src/ReloadedHelper.App -r win-x64 -c Release /p:PublishSingleFile=true /p:SelfContained=true /p:Version=9.9.9 -o publish/
```

- [ ] **Step 3: 実機スモーク（C:\FreeSoft\Reloaded-II / P5R）**

`publish/` の exe を起動し、P5R タブで以下を目視確認：
- 競合に無関係なMODが**動かない**（churn ゼロ）。
- 双葉の髪（`Black Hair Futaba` 系）やかすみ変種の競合で、配置理由が表示される。
- "要確認" 系の表示が出ない。
- クラッシュせず数百MODでも実用速度。

`%APPDATA%\ReloadedHelper\autosort-history.json` に配置理由が記録され、Unresolved が空であることを確認。

- [ ] **Step 4: last-wins 前提のサニティ検証メモ**

P5R で「同じ顔テクスチャを触る2MOD」を用意し、ゲーム内で**後ろ（リスト下）のMODが反映される**ことを1回確認（CriFs/AWB 系）。BGME（曲ID）・Costume（枠）も1件ずつ。
結果を設計書の末尾に追記（逆だった枠組みがあれば `ConflictDetector.Detect` の `mods[^1]`→`mods[0]` の1点修正で対応、と明記）。

- [ ] **Step 5: 記録コミット（メモを追記した場合のみ）**

```bash
git add docs/superpowers/specs/2026-06-25-autosort-evidence-engine-design.md
git commit -m "docs: フェーズ1 実機スモークと last-wins サニティ検証結果を記録"
```

---

## 完了の定義（フェーズ1）

- 競合に無関係なMODは並び替わらない（churn ゼロ）。
- すべての競合は証拠で一意に決まり、"要確認" が出ない。
- 種類は資源を最優先に判定され、作者の配置指示・依存・ピンポイント度が勝者に効く。
- 依存は決して壊さない（壊せない時は正直に理由を残す）。
- 全テスト緑・実機スモーク通過。

## フェーズ1スコープ外（後フェーズ）

- **D「片方だけ」検出・E「効いてる証拠」可視化**（UI含む）。
- **表示ソート（カテゴリ/作者/名前で閲覧用に並べ替え、実配置は不変）**＝UI機能のため後フェーズ。本フェーズはエンジン（A・B・C）に集中。
- `PreferenceStore` の「却下した"片方だけ"警告の記憶」への転用（フェーズ1では2件競合のユーザー上書きとして既存利用を維持）。
- `OptimizeResult.Unresolved` の型からの完全撤去（フェーズ1は常に空で互換維持）。
