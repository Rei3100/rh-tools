# 分類精度ラウンド＋冗長検出＋旧エンジン撤去 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** MODの種類ラベル精度を一般ルールで底上げし（Characters細分・土台緩和・判定不能救済）、冗長MODペア検出を追加し、本番未使用の旧エンジンを撤去する。

**Architecture:** `ModTypeClassifier`（純関数の分類器）と `ModType.cs`（順序ランク）を中心に汎用ルールを拡張。冗長検出は新 `RedundancyDetector` を追加し `GameDiagnostics` 経由で診断パネルへ。旧 `LoadOrderOptimizer`/`WinnerResolver`/`PlacementHint` は削除。

**Tech Stack:** C# / .NET 10 / xUnit。WPF UI 層は冗長検出の表示配線のみ。

## Global Constraints

- 手チューニング禁止：特定MOD名/ID固有の分岐を書かない。汎用語・汎用条件のみ。
- ランタイム NuGet 追加禁止（System.Text.Json のみ可）。テスト用 xUnit は可。
- ユーザーデータは `%APPDATA%\ReloadedHelper` 以外に保存禁止。
- 各タスクは TDD（失敗テスト→最小実装→緑→コミット）。
- 仕様書: docs/superpowers/specs/2026-06-26-classification-accuracy-round-design.md
- 最終検証は実機ダンプ（`_RealInstallDump.cs` を実行し `classification-dump.txt` を before→after 比較）。`_RealInstallDump.cs` は未コミットのまま手元保持。

---

## File Structure

- `src/ReloadedHelper.Core/ModTypeClassifier.cs` — 修正（①②③a のルール）
- `src/ReloadedHelper.Core/ModType.cs` — 修正（③b Unknown ランク）
- `src/ReloadedHelper.Core/RedundancyDetector.cs` — 新規（④a）
- `src/ReloadedHelper.Core/GameDiagnostics.cs` — 修正（④a 配線、`RedundantPairs` 公開）
- `src/ReloadedHelper.Core/ModDiagnostics.cs` — 修正（④a 冗長ペアを Diagnostic 化）
- 削除（④b）: `LoadOrderOptimizer.cs`/`WinnerResolver.cs`/`PlacementHint.cs` ＋各テスト
- テスト: `ModTypeClassifierFrameworkTests.cs`（②で既存テスト更新）、`ModTypeClassifierTests.cs`（①③a追加）、`RedundancyDetectorTests.cs`（新規）

---

### Task 1: 土台ルールを依存≥1へ緩める（②）

**Files:**
- Modify: `src/ReloadedHelper.Core/ModTypeClassifier.cs:65`
- Test: `tests/ReloadedHelper.Core.Tests/ModTypeClassifierFrameworkTests.cs:37-42`

**Interfaces:**
- Consumes: `ModTypeClassifier.Classify(ModInfo, string?, IReadOnlyList<ResourceKey>, int dependentsCount)`
- Produces: 依存1以上＋資源0で `ModType.Library` を返す挙動

- [ ] **Step 1: 既存テストを新仕様へ書き換える（失敗させる）**

`ModTypeClassifierFrameworkTests.cs` の `FewDependents_NoResources_IsNotForcedFramework`（37-42行）を次へ置換：

```csharp
    [Fact]
    public void SingleDependent_NoResources_IsFrameworkLibrary()
    {
        // 依存1でも資源0なら前提物（誰かに参照される土台）。
        var d = ModTypeClassifier.Classify(Mod("Texture Fixes Project"), null, None, dependentsCount: 1);
        Assert.Equal(ModType.Library, d.Type);
    }

    [Fact]
    public void ZeroDependents_NoResources_IsNotForcedFramework()
    {
        // 誰からも依存されないなら土台扱いしない。
        var d = ModTypeClassifier.Classify(Mod("Some Mod"), null, None, dependentsCount: 0);
        Assert.NotEqual(ModType.Library, d.Type);
    }
```

- [ ] **Step 2: テストを実行して失敗を確認**

Run: `dotnet test --filter "FullyQualifiedName~SingleDependent_NoResources_IsFrameworkLibrary"`
Expected: FAIL（現状 `>= 2` のため d1 は Library にならない）

- [ ] **Step 3: 最小実装**

`ModTypeClassifier.cs:65` を変更：

```csharp
        if (dependentsCount >= 1 && resources.Count == 0)
            return new(ModType.Library, $"{dependentsCount}個のMODから依存される土台のため前方に配置");
```

- [ ] **Step 4: テストを実行して緑を確認**

Run: `dotnet test --filter "FullyQualifiedName~ModTypeClassifierFrameworkTests"`
Expected: PASS（全件）

- [ ] **Step 5: コミット**

```bash
git add src/ReloadedHelper.Core/ModTypeClassifier.cs tests/ReloadedHelper.Core.Tests/ModTypeClassifierFrameworkTests.cs
git commit -m "feat: 土台判定を依存>=1+資源0に緩和（前提物の取りこぼし解消）"
```

---

### Task 2: Characters のキーワード語彙を広げる（①）

**Files:**
- Modify: `src/ReloadedHelper.Core/ModTypeClassifier.cs:14-25`（KeywordRules）
- Test: `tests/ReloadedHelper.Core.Tests/ModTypeClassifierTests.cs`

**Interfaces:**
- Consumes: `ModTypeClassifier.Classify(ModInfo, string? category)`（名前キーワード判定経路）
- Produces: `hair/eyes/face/recolor`→SkinTexture、`menu/title/mainmenu`→Ui、`mask/helmet`→Model

- [ ] **Step 1: 失敗テストを書く**

`ModTypeClassifierTests.cs` に追記（先頭の `using` と `Mod` ヘルパは既存に倣う。無ければ Framework テストの `Mod` と同形を用意）：

```csharp
    [Theory]
    [InlineData("Black Hair Futaba", ModType.SkinTexture)]   // 髪 → スキン
    [InlineData("Dynamic Main Menu", ModType.Ui)]            // メニュー → UI
    [InlineData("No Helmet Featherman", ModType.Model)]      // 兜 → モデル
    public void CharacterParts_RouteByName(string name, ModType expected)
    {
        var d = ModTypeClassifier.Classify(Mod(name), "Characters");
        Assert.Equal(expected, d.Type);
    }
```

- [ ] **Step 2: テストを実行して失敗を確認**

Run: `dotnet test --filter "FullyQualifiedName~CharacterParts_RouteByName"`
Expected: FAIL（現状すべて Characters→Model になる）

- [ ] **Step 3: 最小実装（KeywordRules に汎用語を追加）**

`ModTypeClassifier.cs:14-25` の該当行を次へ：

```csharp
        (ModType.Portrait, new[] { "portrait", "bustup", "立ち絵", "ポートレート", "バストアップ" }),
        (ModType.Costume, new[] { "costume", "outfit", "衣装", "コスチューム", "swimsuit", "水着" }),
        (ModType.SkinTexture, new[] { "skin", "texture", "スキン", "テクスチャ", "retexture", "recolor", "リテクスチャ", "hair", "eyes", "face" }),
        (ModType.Model, new[] { "model", "モデル", "mesh", "mask", "helmet" }),
        (ModType.Ui, new[] { "interface", "hud", "カットイン", "cutin", "cut-in", "dualsense", "menu", "title", "mainmenu" }),
```

注: KeywordRules は優先順（最初に当たったもの採用）。SkinTexture が Model より前なので `hair` 入りの名前はスキン優先になる。`mask`/`helmet` は Model 行（SkinTexture に該当語が無い前提）。

- [ ] **Step 4: テストを実行して緑を確認**

Run: `dotnet test --filter "FullyQualifiedName~ModTypeClassifierTests"`
Expected: PASS（全件。既存テストも壊れないこと）

- [ ] **Step 5: コミット**

```bash
git add src/ReloadedHelper.Core/ModTypeClassifier.cs tests/ReloadedHelper.Core.Tests/ModTypeClassifierTests.cs
git commit -m "feat: Characters細分（髪→スキン/メニュー→UI/兜→モデル）の汎用語追加"
```

---

### Task 3: 判定不能の救済語＋Unknownを中間ランクへ（③）

**Files:**
- Modify: `src/ReloadedHelper.Core/ModTypeClassifier.cs:16,21`（Music/Ui の語彙）
- Modify: `src/ReloadedHelper.Core/ModType.cs:11-24`（Rank の Unknown）
- Test: `tests/ReloadedHelper.Core.Tests/ModTypeClassifierTests.cs`

**Interfaces:**
- Consumes: `ModTypeClassifier.Classify(ModInfo, string?)`、`ModTypeInfo.Rank(ModType)`
- Produces: `audio`→Music、`confirm`→Ui、`ModTypeInfo.Rank(ModType.Unknown)` が中間値（Music=4 と Model=5 の間相当＝5、既存 Model 以降を後ろへずらす）

- [ ] **Step 1: 失敗テストを書く**

`ModTypeClassifierTests.cs` に追記：

```csharp
    [Theory]
    [InlineData("Audio Mix Control", ModType.Music)]
    [InlineData("Silent Menu Confirm", ModType.Ui)]
    public void RescueWords_RouteUnknownish(string name, ModType expected)
    {
        var d = ModTypeClassifier.Classify(Mod(name), null);
        Assert.Equal(expected, d.Type);
    }

    [Fact]
    public void Unknown_RankIsNeutralMiddle_NotLast()
    {
        // 正体不明を末尾（最強の上書き位置）に置かない。中間に。
        Assert.True(ModTypeInfo.Rank(ModType.Unknown) < ModTypeInfo.Rank(ModType.SkinTexture));
        Assert.True(ModTypeInfo.Rank(ModType.Unknown) > ModTypeInfo.Rank(ModType.Music));
    }
```

- [ ] **Step 2: テストを実行して失敗を確認**

Run: `dotnet test --filter "FullyQualifiedName~RescueWords_RouteUnknownish|FullyQualifiedName~Unknown_RankIsNeutralMiddle"`
Expected: FAIL（audio/confirm が未登録、Unknown=10 で末尾）

- [ ] **Step 3a: 救済語を追加**

`ModTypeClassifier.cs:16`（Music）と `:21`（Ui）へ語を足す：

```csharp
        (ModType.Music, new[] { "music", "bgm", "sound", "ost", "soundtrack", "song", "サウンド", "音楽", "audio" }),
```
```csharp
        (ModType.Ui, new[] { "interface", "hud", "カットイン", "cutin", "cut-in", "dualsense", "menu", "title", "mainmenu", "confirm" }),
```

（Task 2 で Ui 行を編集済みの場合は `confirm` を末尾に足すだけ。）

- [ ] **Step 3b: Unknown を中間ランクへ**

`ModType.cs:11-24` の `Rank` を、Unknown=5 を中間に挿入し Model 以降を1つ後ろへずらす：

```csharp
    public static int Rank(ModType t) => t switch
    {
        ModType.Library => 0,
        ModType.Gameplay => 1,
        ModType.Battle => 2,
        ModType.Event => 3,
        ModType.Music => 4,
        ModType.Unknown => 5,   // 中立（中間）。末尾に置かない。
        ModType.Model => 6,
        ModType.Costume => 7,
        ModType.SkinTexture => 8,
        ModType.Portrait => 9,
        ModType.Ui => 10,
        _ => 5,
    };
```

- [ ] **Step 3c: Unknown の理由文を中立化**

`ModTypeClassifier.cs:107` を変更：

```csharp
        return new(ModType.Unknown, "種類が特定できないため中間に配置");
```

- [ ] **Step 4: テストを実行して緑を確認**

Run: `dotnet test --filter "FullyQualifiedName~ModTypeClassifierTests"`
Expected: PASS（全件）

- [ ] **Step 5: コミット**

```bash
git add src/ReloadedHelper.Core/ModTypeClassifier.cs src/ReloadedHelper.Core/ModType.cs tests/ReloadedHelper.Core.Tests/ModTypeClassifierTests.cs
git commit -m "feat: 判定不能を救済語(audio/confirm)＋中間ランクで中立配置"
```

---

### Task 4: 冗長MODペア検出（④a）

**Files:**
- Create: `src/ReloadedHelper.Core/RedundancyDetector.cs`
- Test: `tests/ReloadedHelper.Core.Tests/RedundancyDetectorTests.cs`
- Modify: `src/ReloadedHelper.Core/GameDiagnostics.cs`（結果に `RedundantPairs` 追加）
- Modify: `src/ReloadedHelper.Core/ModDiagnostics.cs`（冗長ペアを Diagnostic 化）

**Interfaces:**
- Consumes: `ModResources`（`Analyzers` 名前空間）, `ResourceKey.ToString()`, `ModInfo.Dependencies`
- Produces:
  - `public sealed record RedundantPair(string ModA, string ModB, int SharedCount, int SmallerCount);`
  - `RedundancyDetector.Detect(IReadOnlyList<ModResources> orderedEnabled, IReadOnlyDictionary<string, ModInfo> catalog, double threshold = 0.8) : IReadOnlyList<RedundantPair>`
  - `GameDiagnosticsResult` に `IReadOnlyList<RedundantPair> RedundantPairs` を追加

- [ ] **Step 1: 失敗テストを書く**

新規 `tests/ReloadedHelper.Core.Tests/RedundancyDetectorTests.cs`：

```csharp
using ReloadedHelper.Core;
using ReloadedHelper.Core.Analyzers;
using Xunit;

namespace ReloadedHelper.Core.Tests;

public class RedundancyDetectorTests
{
    private static ModInfo Mod(string id, params string[] deps) => new(
        id, id, "", "1", "",
        deps, System.Array.Empty<string>(), System.Array.Empty<string>(),
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
```

- [ ] **Step 2: テストを実行して失敗を確認**

Run: `dotnet test --filter "FullyQualifiedName~RedundancyDetectorTests"`
Expected: FAIL（`RedundancyDetector` 未定義でコンパイルエラー）

- [ ] **Step 3: 最小実装**

新規 `src/ReloadedHelper.Core/RedundancyDetector.cs`：

```csharp
using ReloadedHelper.Core.Analyzers;

namespace ReloadedHelper.Core;

// 依存関係に無い2MODが同じ資源を大きく重ねて触る＝「片方だけ想定の重複」候補。
public sealed record RedundantPair(string ModA, string ModB, int SharedCount, int SmallerCount);

public static class RedundancyDetector
{
    public static IReadOnlyList<RedundantPair> Detect(
        IReadOnlyList<ModResources> orderedEnabled,
        IReadOnlyDictionary<string, ModInfo> catalog,
        double threshold = 0.8)
    {
        // 資源キー集合を MOD ごとに作る
        var sets = new List<(string Id, HashSet<string> Keys)>();
        foreach (var mr in orderedEnabled)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var rk in mr.Resources) keys.Add(rk.ToString());
            if (keys.Count > 0) sets.Add((mr.ModId, keys));
        }

        var result = new List<RedundantPair>();
        for (int i = 0; i < sets.Count; i++)
            for (int j = i + 1; j < sets.Count; j++)
            {
                var (aId, aKeys) = sets[i];
                var (bId, bKeys) = sets[j];
                if (DependsOn(catalog, aId, bId) || DependsOn(catalog, bId, aId)) continue;

                int shared = aKeys.Count(bKeys.Contains);
                if (shared == 0) continue;
                int smaller = Math.Min(aKeys.Count, bKeys.Count);
                if ((double)shared / smaller >= threshold)
                    result.Add(new RedundantPair(aId, bId, shared, smaller));
            }
        return result;
    }

    private static bool DependsOn(IReadOnlyDictionary<string, ModInfo> catalog, string mod, string maybeDep)
    {
        return catalog.TryGetValue(mod, out var info) &&
               info.Dependencies.Any(d => string.Equals(d, maybeDep, StringComparison.OrdinalIgnoreCase));
    }
}
```

- [ ] **Step 4: テストを実行して緑を確認**

Run: `dotnet test --filter "FullyQualifiedName~RedundancyDetectorTests"`
Expected: PASS（3件）

- [ ] **Step 5: GameDiagnostics へ配線**

`GameDiagnostics.cs`：`GameDiagnosticsResult` に `RedundantPairs` を追加し `Run` で算出。

record を変更：

```csharp
public sealed record GameDiagnosticsResult(
    IReadOnlyList<FileConflict> Conflicts,
    IReadOnlyList<Diagnostic> Diagnostics,
    IReadOnlyList<ModResources> Resources,
    IReadOnlyList<RedundantPair> RedundantPairs);
```

`Run` の末尾を変更（`conflicts` 算出の直後に追加し、return を差し替え）：

```csharp
        var conflicts = ConflictDetector.Detect(ordered);
        var redundant = RedundancyDetector.Detect(ordered, catalog);
        var diagnostics = ModDiagnostics.Analyze(game, catalog, conflicts, structureWarnings, redundant);
        return new GameDiagnosticsResult(conflicts, diagnostics, ordered, redundant);
```

- [ ] **Step 6: ModDiagnostics で冗長ペアを Diagnostic 化**

`ModDiagnostics.Analyze` のシグネチャに任意引数を追加し、末尾で Diagnostic を生成：

```csharp
    public static IReadOnlyList<Diagnostic> Analyze(
        GameInfo game,
        IReadOnlyDictionary<string, ModInfo> catalog,
        IReadOnlyList<FileConflict> conflicts,
        IReadOnlyList<StructureWarning>? structureWarnings = null,
        IReadOnlyList<RedundantPair>? redundantPairs = null)
```

`return result;` の直前に追加：

```csharp
        if (redundantPairs is not null)
            foreach (var p in redundantPairs)
                result.Add(new Diagnostic(p.ModA, DiagnosticSeverity.Info,
                    $"「{DisplayName(catalog, p.ModB)}」と内容が大きく重複しています（{p.SharedCount}項目）。片方の無効化を検討してください。"));
```

- [ ] **Step 7: ビルド・全テストを実行**

Run: `dotnet build reloaded-helper.slnx && dotnet test`
Expected: ビルド0エラー、全テストPASS（`GameDiagnosticsResult` を使う既存箇所のコンパイルが通ること。通らなければ呼び出し側を新コンストラクタ引数へ追従）

- [ ] **Step 8: コミット**

```bash
git add src/ReloadedHelper.Core/RedundancyDetector.cs src/ReloadedHelper.Core/GameDiagnostics.cs src/ReloadedHelper.Core/ModDiagnostics.cs tests/ReloadedHelper.Core.Tests/RedundancyDetectorTests.cs
git commit -m "feat: 冗長MODペア検出（資源重なり率＋依存除外）を診断へ追加"
```

---

### Task 5: 旧エンジン撤去（④b）

**Files:**
- Delete: `src/ReloadedHelper.Core/LoadOrderOptimizer.cs`, `tests/ReloadedHelper.Core.Tests/LoadOrderOptimizerTests.cs`
- Delete: `src/ReloadedHelper.Core/WinnerResolver.cs`, `tests/ReloadedHelper.Core.Tests/WinnerResolverTests.cs`
- Delete: `src/ReloadedHelper.Core/PlacementHint.cs`, `tests/ReloadedHelper.Core.Tests/PlacementHintParserTests.cs`

**Interfaces:**
- Consumes: なし（本番未参照を確認済み）
- Produces: なし（削除のみ）

- [ ] **Step 1: 本番参照が無いことを再確認**

Run: `git grep -n "LoadOrderOptimizer\|WinnerResolver\|PlacementHint" -- "src/ReloadedHelper.App" "src/ReloadedHelper.Core/AutoSortCoordinator.cs" "src/ReloadedHelper.Core/MainViewModel.cs"`
Expected: 出力なし（ヒットしたら、そのMODは未撤去依存があるので止めて調査）

- [ ] **Step 2: ファイル削除**

```bash
git rm src/ReloadedHelper.Core/LoadOrderOptimizer.cs tests/ReloadedHelper.Core.Tests/LoadOrderOptimizerTests.cs
git rm src/ReloadedHelper.Core/WinnerResolver.cs tests/ReloadedHelper.Core.Tests/WinnerResolverTests.cs
git rm src/ReloadedHelper.Core/PlacementHint.cs tests/ReloadedHelper.Core.Tests/PlacementHintParserTests.cs
```

- [ ] **Step 3: ビルド・全テストを実行（孤立参照ゼロ確認）**

Run: `dotnet build reloaded-helper.slnx && dotnet test`
Expected: ビルド0エラー、全テストPASS。エラーが出たら残依存があるので、その箇所を新エンジン（`ConstraintGraphOptimizer`）へ寄せるか不要コードとして除去。

- [ ] **Step 4: コミット**

```bash
git add -A
git commit -m "chore: 本番未使用の旧エンジン(LoadOrderOptimizer/WinnerResolver/PlacementHint)を撤去"
```

---

### Task 6: 実機ダンプ最終検証

**Files:**
- Run: `tests/ReloadedHelper.Core.Tests/_RealInstallDump.cs`（未コミット・手元）
- Inspect: `classification-dump.txt`

**Interfaces:**
- Consumes: 実機 `C:\FreeSoft\Reloaded-II`
- Produces: なし（観測のみ。コミット不要）

- [ ] **Step 1: ダンプ生成**

Run: `dotnet test --filter "FullyQualifiedName~_RealInstallDump"`
Expected: PASS、`classification-dump.txt` が更新される

- [ ] **Step 2: before→after を目視確認（完了条件）**

`classification-dump.txt` を開き、仕様書の完了条件を確認：
- Black Hair Futaba＝スキン・テクスチャ、dynmainmenu＝UI（Characters の内訳が分散）
- texturefixesproject / evt.fadeouttablemerge が「ライブラリ/前提」へ
- 判定不能が減り（audiomixcontrol＝音楽、silentmenuconfirm/uitoggler＝UI）、残りは理由文「中間に配置」
- 「新エンジン最終順序 先頭25件」が全て土台/前提のまま（順序不変）
- 冗長ペアが妥当に出る（変種ペアが出て、土台＋利用側を誤検出しない）

問題（誤爆・誤昇格）があれば該当タスクへ戻り、語彙やしきい値を調整して再検証。手チューニング（特定MOD名分岐）はしない。

- [ ] **Step 3: 報告**

before→after の差分要点をユーザーへ日本語で報告。`classification-dump.txt` と `_RealInstallDump.cs` はコミットしない。

---

## Self-Review

**Spec coverage:**
- ① Characters細分 → Task 2 ✓
- ② 土台緩和 → Task 1 ✓
- ③(a) 救済語 / ③(b) 中間配置 → Task 3 ✓
- ④(a) 冗長検出 → Task 4 ✓ / ④(b) 旧エンジン撤去 → Task 5 ✓
- 実機ダンプ検証 → Task 6 ✓

**Placeholder scan:** 各 code step に実コードあり。プレースホルダ無し。

**Type consistency:** `RedundantPair(ModA, ModB, SharedCount, SmallerCount)`、`RedundancyDetector.Detect(...)`、`GameDiagnosticsResult(...RedundantPairs)`、`ModDiagnostics.Analyze(..., redundantPairs)` がTask4内で一貫。`ModTypeInfo.Rank` の Unknown=5 と各ランクの再割当（Model=6..Ui=10）整合。
