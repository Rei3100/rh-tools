# 自動並び替え：層別フル整列エンジン Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 全MODを中身から推定した役割で層別にフル整列し、各MODに配置理由を付けて、起動時に一覧がガラッと整理されるようにする。

**Architecture:** 役割を `ContentRoleClassifier`（中身ベース）で全MODに付与し、`ModLayer` の層ランクで全件を安定整列してから依存ソート・衝突解決を重ねる。`OptimizeResult` に全MODの配置理由（`ModPlacement`）を持たせ、UI の詳細パネルに表示する。

**Tech Stack:** C# / .NET 10 / WPF。テストは xUnit。ランタイム追加パッケージなし（System.Text.Json のみ可）。

## Global Constraints

- ランタイム NuGet パッケージの追加禁止（System.Text.Json のみ可）。テスト用 xUnit は可。
- ユーザーデータは `%APPDATA%\ReloadedHelper` 以外に保存禁止。
- ロジックは `src/ReloadedHelper.Core/`、WPF UI は `src/ReloadedHelper.App/`、テストは `tests/ReloadedHelper.Core.Tests/`。
- ビルド: `dotnet build reloaded-helper.slnx` / テスト: `dotnet test`。
- 文字列・理由はすべて日本語（ユーザーは非プログラマー）。
- 既存の挙動を維持: 書込前に必ずバックアップ、順序が変わらなければ書込・履歴を作らない。

## File Structure

- `src/ReloadedHelper.Core/ModLayer.cs`（新規）— 役割→層ランク・層ラベルの純粋関数。
- `src/ReloadedHelper.Core/ContentRoleClassifier.cs`（新規）— MODの中身＋カテゴリから役割と理由を返す。`RoleDecision` レコードもここ。
- `src/ReloadedHelper.Core/LoadOrderOptimizer.cs`（変更）— 層別フル整列を初期順に組み込み、`OptimizeResult` に全MODの `ModPlacement` を追加。
- `src/ReloadedHelper.Core/ModContentScanner.cs`（変更）— 認識する redirect ルートを拡張。
- `src/ReloadedHelper.Core/Models.cs`（変更）— `ModLoadEntry` に `PlacementReason` を追加。
- `src/ReloadedHelper.Core/MainViewModel.cs`（変更）— `ContentRoleClassifier` で役割決定、配置理由を保持して `RebuildEntries` で各行へ注入。
- `src/ReloadedHelper.App/Views/ModListView.xaml`（変更）— 詳細パネルに配置理由を表示。
- 対応テスト: `tests/ReloadedHelper.Core.Tests/` 配下に各新規ファイルのテスト、既存テストの更新。

---

### Task 1: ModLayer（役割→層ランク・ラベル）

**Files:**
- Create: `src/ReloadedHelper.Core/ModLayer.cs`
- Test: `tests/ReloadedHelper.Core.Tests/ModLayerTests.cs`

**Interfaces:**
- Consumes: 既存 `ModRole` enum（`src/ReloadedHelper.Core/ModRoleClassifier.cs`）。
- Produces: `int ModLayer.Rank(ModRole)`、`string ModLayer.Label(ModRole)`。

- [ ] **Step 1: Write the failing test**

```csharp
// tests/ReloadedHelper.Core.Tests/ModLayerTests.cs
using ReloadedHelper.Core;
using Xunit;

namespace ReloadedHelper.Core.Tests;

public class ModLayerTests
{
    [Theory]
    [InlineData(ModRole.Library, 0)]
    [InlineData(ModRole.BaseLayer, 1)]
    [InlineData(ModRole.Music, 2)]
    [InlineData(ModRole.VisualOverride, 3)]
    [InlineData(ModRole.Unknown, 4)]
    public void Rank_OrdersWeakToStrong(ModRole role, int expected)
        => Assert.Equal(expected, ModLayer.Rank(role));

    [Fact]
    public void Label_IsNonEmptyJapanese()
    {
        Assert.False(string.IsNullOrWhiteSpace(ModLayer.Label(ModRole.Library)));
        Assert.False(string.IsNullOrWhiteSpace(ModLayer.Label(ModRole.Unknown)));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter ModLayerTests`
Expected: FAIL — `ModLayer` が存在しない（コンパイルエラー）。

- [ ] **Step 3: Write minimal implementation**

```csharp
// src/ReloadedHelper.Core/ModLayer.cs
namespace ReloadedHelper.Core;

// 役割を「弱い土台(前)〜勝たせたい見た目(後)」の層ランクに対応させる。
// ランクが小さいほどリストの前（弱い・負けてよい）。
public static class ModLayer
{
    public static int Rank(ModRole role) => role switch
    {
        ModRole.Library => 0,        // ライブラリ・前提
        ModRole.BaseLayer => 1,      // 大型改修・システム
        ModRole.Music => 2,          // 音楽・サウンド
        ModRole.VisualOverride => 3, // 見た目の上書き
        _ => 4,                      // Unknown：末尾寄せ
    };

    public static string Label(ModRole role) => role switch
    {
        ModRole.Library => "ライブラリ・前提",
        ModRole.BaseLayer => "大型改修・システム",
        ModRole.Music => "音楽・サウンド",
        ModRole.VisualOverride => "見た目の上書き",
        _ => "個別・その他",
    };
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter ModLayerTests`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/ReloadedHelper.Core/ModLayer.cs tests/ReloadedHelper.Core.Tests/ModLayerTests.cs
git commit -m "feat: 役割→層ランク・ラベルの ModLayer を追加"
```

---

### Task 2: ContentRoleClassifier（中身ベースの役割判定）

**Files:**
- Create: `src/ReloadedHelper.Core/ContentRoleClassifier.cs`
- Test: `tests/ReloadedHelper.Core.Tests/ContentRoleClassifierTests.cs`

**Interfaces:**
- Consumes: `ModInfo`（`Models.cs`）、`ModRole` / `ModRoleClassifier.Classify`、`ModLayer.Label`（Task 1）、`ModContentScanner.Scan`。
- Produces: `record RoleDecision(ModRole Role, string Reason)`、`RoleDecision ContentRoleClassifier.Classify(ModInfo mod, string? category)`。

判定の優先順位（上から順に最初に当たったもの）:
1. `IsLibrary` → Library
2. フォルダに `BGME/` → Music
3. フォルダに `Costumes/` → VisualOverride
4. カテゴリで決まる（`ModRoleClassifier.Classify` が Unknown 以外）→ その役割
5. ゲームファイルを上書きしている（`ModContentScanner.Scan` の Paths が1件以上）→ VisualOverride
6. それ以外 → Unknown

- [ ] **Step 1: Write the failing test**

```csharp
// tests/ReloadedHelper.Core.Tests/ContentRoleClassifierTests.cs
using System.IO;
using ReloadedHelper.Core;
using Xunit;

namespace ReloadedHelper.Core.Tests;

public class ContentRoleClassifierTests
{
    private static ModInfo Mod(string folder, bool lib = false) => new(
        "m", "M", "", "1.0", "", Array.Empty<string>(), Array.Empty<string>(),
        Array.Empty<string>(), Array.Empty<string>(), null, null, null, null, folder, lib);

    private static string MakeDir(params string[] subdirs)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"crc-{Guid.NewGuid():N}");
        foreach (var s in subdirs)
            Directory.CreateDirectory(Path.Combine(dir, s.Replace('/', Path.DirectorySeparatorChar)));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void Library_Wins_Regardless()
    {
        var d = Make_EmptyDir();
        try
        {
            var r = ContentRoleClassifier.Classify(Mod(d, lib: true), "Skin");
            Assert.Equal(ModRole.Library, r.Role);
            Assert.False(string.IsNullOrWhiteSpace(r.Reason));
        }
        finally { Directory.Delete(d, true); }
    }

    [Fact]
    public void BgmeFolder_IsMusic_EvenWithoutCategory()
    {
        var d = MakeDir("BGME");
        try { Assert.Equal(ModRole.Music, ContentRoleClassifier.Classify(Mod(d), null).Role); }
        finally { Directory.Delete(d, true); }
    }

    [Fact]
    public void CostumesFolder_IsVisual_EvenWithoutCategory()
    {
        var d = MakeDir("Costumes");
        try { Assert.Equal(ModRole.VisualOverride, ContentRoleClassifier.Classify(Mod(d), null).Role); }
        finally { Directory.Delete(d, true); }
    }

    [Fact]
    public void Category_UsedWhenNoFolderSignal()
    {
        var d = Make_EmptyDir();
        try { Assert.Equal(ModRole.BaseLayer, ContentRoleClassifier.Classify(Mod(d), "Characters").Role); }
        finally { Directory.Delete(d, true); }
    }

    [Fact]
    public void FileReplacingMod_WithoutCategory_IsVisual()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"crc-{Guid.NewGuid():N}");
        var full = Path.Combine(dir, "P5REssentials", "CPK", "BASE.CPK", "tex.dds");
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, "x");
        try { Assert.Equal(ModRole.VisualOverride, ContentRoleClassifier.Classify(Mod(dir), null).Role); }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void NoSignal_IsUnknown()
    {
        var d = Make_EmptyDir();
        try { Assert.Equal(ModRole.Unknown, ContentRoleClassifier.Classify(Mod(d), null).Role); }
        finally { Directory.Delete(d, true); }
    }

    private static string Make_EmptyDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"crc-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter ContentRoleClassifierTests`
Expected: FAIL — `ContentRoleClassifier` が存在しない。

- [ ] **Step 3: Write minimal implementation**

```csharp
// src/ReloadedHelper.Core/ContentRoleClassifier.cs
using System.IO;

namespace ReloadedHelper.Core;

public sealed record RoleDecision(ModRole Role, string Reason);

// MODの中身（フォルダ構成）を一次情報に役割を決める。
// カテゴリは GameBanana 照合できたMODにしか付かないため、中身を優先して判定する。
public static class ContentRoleClassifier
{
    public static RoleDecision Classify(ModInfo mod, string? category)
    {
        if (mod.IsLibrary)
            return new(ModRole.Library, "ライブラリ指定のため最前に配置");

        var folder = mod.FolderPath;
        if (HasSubdir(folder, "BGME"))
            return new(ModRole.Music, "BGMEフォルダがあるため音楽として配置");
        if (HasSubdir(folder, "Costumes"))
            return new(ModRole.VisualOverride, "衣装フォルダがあるため見た目として後方に配置");

        var byCategory = ModRoleClassifier.Classify(mod, category);
        if (byCategory != ModRole.Unknown)
            return new(byCategory, $"カテゴリ「{category}」のため{ModLayer.Label(byCategory)}に配置");

        if (ModContentScanner.Scan(folder, mod.ModId).Paths.Count > 0)
            return new(ModRole.VisualOverride, "ゲームファイルを上書きするMODのため見た目として後方に配置");

        return new(ModRole.Unknown, "役割を判定できないため末尾に配置");
    }

    private static bool HasSubdir(string folder, string name)
    {
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return false;
        return Directory.GetDirectories(folder)
            .Any(d => string.Equals(Path.GetFileName(d), name, StringComparison.OrdinalIgnoreCase));
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter ContentRoleClassifierTests`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/ReloadedHelper.Core/ContentRoleClassifier.cs tests/ReloadedHelper.Core.Tests/ContentRoleClassifierTests.cs
git commit -m "feat: 中身ベースの役割判定 ContentRoleClassifier を追加"
```

---

### Task 3: 層別フル整列＋全MOD配置理由（LoadOrderOptimizer）

**Files:**
- Modify: `src/ReloadedHelper.Core/LoadOrderOptimizer.cs`
- Modify: `tests/ReloadedHelper.Core.Tests/LoadOrderOptimizerTests.cs`（既存期待値の更新＋新テスト）
- Modify: `tests/ReloadedHelper.Core.Tests/AutoSortCoordinatorTests.cs`（`OptimizeResult` の引数追加に追従）

**Interfaces:**
- Consumes: `ModLayer.Rank`（Task 1）、既存 `LoadOrderSorter.Sort`、`ModRole`、`PreferenceStore`、`FileConflict`。
- Produces:
  - `record ModPlacement(string ModId, int LayerRank, string LayerLabel, string Reason)`
  - `OptimizeResult` に4番目 `IReadOnlyList<ModPlacement> Placements` を追加。
  - `Optimize(...)` に末尾の省略可能引数 `IReadOnlyDictionary<string, string>? roleReasons = null` を追加（既存呼び出しは非破壊）。

**設計メモ:** 初期順を「役割の層ランクで安定整列 → 依存ソート」に変える。これにより Visual は常に Base より後ろに来るので、従来「衝突移動で動かしていた」ケースの多くは層整列だけで正しくなり、`Reasons`（移動記録）は空になる。配置の説明は全MOD分を `Placements` に出すので、移動が起きなくても各MODに理由が付く。

- [ ] **Step 1: Update existing tests to the new layered behavior（まず失敗させる）**

`tests/ReloadedHelper.Core.Tests/LoadOrderOptimizerTests.cs` の以下3メソッドを置き換える:

```csharp
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
```

`tests/ReloadedHelper.Core.Tests/AutoSortCoordinatorTests.cs` の2か所の `new OptimizeResult(...)` に4番目の引数を追加:

```csharp
        var result = new OptimizeResult(
            new[] { "a", "b" },
            new[] { new PlacementReason("a", "b", "msg") },
            Array.Empty<(string, string)>(),
            Array.Empty<ModPlacement>());
```

```csharp
        var empty = new OptimizeResult(Array.Empty<string>(),
            Array.Empty<PlacementReason>(), Array.Empty<(string, string)>(),
            Array.Empty<ModPlacement>());
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter LoadOrderOptimizerTests`
Expected: FAIL — `OptimizeResult` に `Placements` が無い／4引数コンストラクタが無い（コンパイルエラー）。

- [ ] **Step 3: Implement layered ordering and placements**

`src/ReloadedHelper.Core/LoadOrderOptimizer.cs` を次のように変更する。

レコード定義部（ファイル上部）に `ModPlacement` を追加し、`OptimizeResult` に4番目を追加:

```csharp
public sealed record PlacementReason(string MovedModId, string AgainstModId, string Message);

public sealed record ModPlacement(string ModId, int LayerRank, string LayerLabel, string Reason);

public sealed record OptimizeResult(
    IReadOnlyList<string> Order,
    IReadOnlyList<PlacementReason> Reasons,
    IReadOnlyList<(string A, string B)> Unresolved,
    IReadOnlyList<ModPlacement> Placements);
```

`Optimize` のシグネチャに省略可能引数を追加:

```csharp
    public static OptimizeResult Optimize(
        string appId,
        IReadOnlyList<string> currentOrder,
        IReadOnlyDictionary<string, IReadOnlyList<string>> dependenciesOf,
        IReadOnlyList<FileConflict> conflicts,
        IReadOnlyDictionary<string, ModRole> rolesByMod,
        PreferenceStore prefs,
        IReadOnlyDictionary<string, string>? roleReasons = null)
    {
```

メソッド冒頭の「1. 依存を満たす基準順」を、層整列を挟む形に置き換える:

```csharp
        // 0. 役割の層ランクで全MODを安定整列（弱い土台が前、見た目が後ろ）。同層は元順を維持。
        var layered = currentOrder
            .Select((id, i) => (id, i, rank: ModLayer.Rank(rolesByMod.GetValueOrDefault(id, ModRole.Unknown))))
            .OrderBy(x => x.rank).ThenBy(x => x.i)
            .Select(x => x.id)
            .ToList();

        // 1. 依存を満たす基準順（層整列を初期順として依存ソート）
        var order = LoadOrderSorter.Sort(layered, dependenciesOf).ToList();
```

メソッド末尾の `return` の直前に全MOD配置理由を組み立て、return を差し替える:

```csharp
        var placements = order.Select(id =>
        {
            var role = rolesByMod.GetValueOrDefault(id, ModRole.Unknown);
            var reason = roleReasons != null && roleReasons.TryGetValue(id, out var r) && !string.IsNullOrWhiteSpace(r)
                ? r
                : $"{ModLayer.Label(role)}として配置";
            return new ModPlacement(id, ModLayer.Rank(role), ModLayer.Label(role), reason);
        }).ToList();

        return new OptimizeResult(order, reasons, unresolved, placements);
```

（旧 `return new OptimizeResult(order, reasons, unresolved);` は削除する。）

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter LoadOrderOptimizerTests`
Expected: PASS（更新3メソッド＋既存の `Preference_OverridesRole` / `Unknown_VsUnknown_IsUnresolved_NoMove` / `Unresolved_IsDeduplicated_AcrossMultipleConflictingFiles` も PASS）

- [ ] **Step 5: Add a test for explicit roleReasons passthrough**

`LoadOrderOptimizerTests.cs` に追加:

```csharp
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
```

- [ ] **Step 6: Run tests to verify it passes**

Run: `dotnet test --filter LoadOrderOptimizerTests`
Expected: PASS

- [ ] **Step 7: Commit**

```bash
git add src/ReloadedHelper.Core/LoadOrderOptimizer.cs tests/ReloadedHelper.Core.Tests/LoadOrderOptimizerTests.cs tests/ReloadedHelper.Core.Tests/AutoSortCoordinatorTests.cs
git commit -m "feat: 層別フル整列と全MOD配置理由を LoadOrderOptimizer に実装"
```

---

### Task 4: スキャナのカバレッジ拡張（ModContentScanner）

**Files:**
- Modify: `src/ReloadedHelper.Core/ModContentScanner.cs`
- Modify: `tests/ReloadedHelper.Core.Tests/ModContentScannerTests.cs`

**Interfaces:**
- Consumes: なし（純粋なファイル走査）。
- Produces: 既存 `ModOverrides ModContentScanner.Scan(string, string)`（戻り型不変、検出ルートを追加）。

**設計メモ:** Persona 系で実ファイルを上書きする主な redirect ルートを追加し、競合検出の対象を広げる。既存2ルートは維持（非回帰）。追加: `BGME`、`Costumes`、`P5REssentials/MOD`、`AwbEmulator/AWB`。

- [ ] **Step 1: Write the failing test**

`ModContentScannerTests.cs` に追加:

```csharp
    [Fact]
    public void Scan_collects_bgme_and_costumes_paths()
    {
        var dir = MakeMod(
            ("BGME/Persona", "music.pme"),
            ("Costumes/Joker/01", "model.gmd"));
        try
        {
            var paths = ModContentScanner.Scan(dir, "m4").Paths;
            Assert.Contains("bgme/persona/music.pme", paths);
            Assert.Contains("costumes/joker/01/model.gmd", paths);
        }
        finally { Directory.Delete(dir, true); }
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter ModContentScannerTests`
Expected: FAIL — `BGME`/`Costumes` 配下が検出されず Paths に含まれない。

- [ ] **Step 3: Implement — RedirectRoots を拡張**

`src/ReloadedHelper.Core/ModContentScanner.cs` の `RedirectRoots` を差し替える:

```csharp
    // 拡張ポイント: ゲームファイルを置き換える redirect ルート（相対・小文字比較）
    private static readonly string[] RedirectRoots =
    {
        "P5REssentials/CPK",
        "P5REssentials/MOD",
        "FEmulator/AWB",
        "AwbEmulator/AWB",
        "BGME",
        "Costumes",
    };
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter ModContentScannerTests`
Expected: PASS（既存の `Scan_collects_cpk_and_awb_paths_normalized_lowercase` 等も PASS）

- [ ] **Step 5: Commit**

```bash
git add src/ReloadedHelper.Core/ModContentScanner.cs tests/ReloadedHelper.Core.Tests/ModContentScannerTests.cs
git commit -m "feat: ModContentScanner の認識ルートを拡張し競合検出を広げる"
```

---

### Task 5: MainViewModel 配線（中身ベース役割＋配置理由を各行へ）

**Files:**
- Modify: `src/ReloadedHelper.Core/Models.cs`（`ModLoadEntry` に `PlacementReason`）
- Modify: `src/ReloadedHelper.Core/MainViewModel.cs`
- Test: `tests/ReloadedHelper.Core.Tests/AutoSortWiringTests.cs`（既存の `BuildRoles` テストは維持、`BuildRoleDecisions` の新テストを追加）

**Interfaces:**
- Consumes: `ContentRoleClassifier.Classify`（Task 2）、`ModPlacement`／`Optimize` の `roleReasons` 引数（Task 3）。
- Produces:
  - `ModLoadEntry` に `string? PlacementReason = null`。
  - `MainViewModel.BuildRoleDecisions(entries, catalog) -> IReadOnlyDictionary<string, RoleDecision>`（internal static）。
  - 既存 `BuildRoles` は `BuildRoleDecisions` から Role を取り出す形に変更（戻り型・名は不変）。

- [ ] **Step 1: Write the failing test**

`AutoSortWiringTests.cs` に追加:

```csharp
    [Fact]
    public void BuildRoleDecisions_GivesRoleAndReason()
    {
        var catalog = new Dictionary<string, ModInfo>
        {
            ["lib"] = new("lib", "Lib", "", "1", "", Array.Empty<string>(), Array.Empty<string>(),
                Array.Empty<string>(), Array.Empty<string>(), null, null, null, null, "", IsLibrary: true),
        };
        var entries = new[] { new ModLoadEntry(0, "lib", catalog["lib"], true, null, true) };

        var decisions = MainViewModel.BuildRoleDecisions(entries, catalog);

        Assert.Equal(ModRole.Library, decisions["lib"].Role);
        Assert.False(string.IsNullOrWhiteSpace(decisions["lib"].Reason));
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter AutoSortWiringTests`
Expected: FAIL — `BuildRoleDecisions` が存在しない。

- [ ] **Step 3: Add PlacementReason to ModLoadEntry**

`src/ReloadedHelper.Core/Models.cs` の `ModLoadEntry` 定義に末尾フィールドを追加:

```csharp
public sealed record ModLoadEntry(
    int Order,
    string ModId,
    ModInfo? Info,
    bool Enabled,
    string? Category = null,
    bool IsLibrary = false,
    string? PlacementReason = null)
{
```

- [ ] **Step 4: Implement BuildRoleDecisions and rewire BuildRoles**

`src/ReloadedHelper.Core/MainViewModel.cs` の既存 `BuildRoles` を次の2メソッドに置き換える:

```csharp
    internal static IReadOnlyDictionary<string, RoleDecision> BuildRoleDecisions(
        IReadOnlyList<ModLoadEntry> entries,
        IReadOnlyDictionary<string, ModInfo> catalog)
    {
        var map = new Dictionary<string, RoleDecision>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in entries)
        {
            var info = e.Info ?? (catalog.TryGetValue(e.ModId, out var ci) ? ci : null);
            map[e.ModId] = info is null
                ? new RoleDecision(ModRole.Unknown, "情報が無いため末尾に配置")
                : ContentRoleClassifier.Classify(info, e.Category);
        }
        return map;
    }

    internal static IReadOnlyDictionary<string, ModRole> BuildRoles(
        IReadOnlyList<ModLoadEntry> entries,
        IReadOnlyDictionary<string, ModInfo> catalog)
        => BuildRoleDecisions(entries, catalog)
            .ToDictionary(kv => kv.Key, kv => kv.Value.Role, StringComparer.OrdinalIgnoreCase);
```

- [ ] **Step 5: Wire decisions + placements into RunAutoSort**

`MainViewModel.RunAutoSort` 内、`var roles = BuildRoles(...)` の行を置き換え、`Optimize` 呼び出しに `roleReasons` を渡す。さらに配置理由を保持するフィールドを使う。

クラスのフィールド追加（`private bool _inReload;` の近く）:

```csharp
    private IReadOnlyDictionary<string, string> _lastPlacementReasons =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
```

`RunAutoSort` 内の置き換え（`var diagResult = GameDiagnostics.Run(...)` の直後あたり）:

```csharp
        var decisions = BuildRoleDecisions(_allEntries, _catalog);
        var roles = decisions.ToDictionary(kv => kv.Key, kv => kv.Value.Role, StringComparer.OrdinalIgnoreCase);
        var roleReasons = decisions.ToDictionary(kv => kv.Key, kv => kv.Value.Reason, StringComparer.OrdinalIgnoreCase);
```

`Optimize` 呼び出しに引数を追加:

```csharp
        var result = LoadOrderOptimizer.Optimize(appId, game.SortedMods, depMap, diagResult.Conflicts, roles, _prefs, roleReasons);
```

`_coordinator.Apply(...)` のあとに配置理由を保存し、`RebuildEntries()` で使えるようにする。`RebuildEntries();` の直前に:

```csharp
        _lastPlacementReasons = result.Placements.ToDictionary(
            p => p.ModId, p => p.Reason, StringComparer.OrdinalIgnoreCase);
```

- [ ] **Step 6: Inject reasons in RebuildEntries**

`MainViewModel.RebuildEntries` を、各エントリに保存済み理由を載せる形に変更:

```csharp
    private void RebuildEntries()
    {
        var built = SelectedGame is null
            ? Array.Empty<ModLoadEntry>()
            : LoadOrderBuilder.Build(SelectedGame, _catalog, _userData);
        _allEntries = built
            .Select(e => _lastPlacementReasons.TryGetValue(e.ModId, out var r)
                ? e with { PlacementReason = r }
                : e)
            .ToList();
        ApplyFilter();
    }
```

- [ ] **Step 7: Run the full test suite**

Run: `dotnet test`
Expected: PASS（`AutoSortWiringTests` 含む全テスト）

- [ ] **Step 8: Commit**

```bash
git add src/ReloadedHelper.Core/Models.cs src/ReloadedHelper.Core/MainViewModel.cs tests/ReloadedHelper.Core.Tests/AutoSortWiringTests.cs
git commit -m "feat: 中身ベース役割と配置理由を MainViewModel に配線"
```

---

### Task 6: UI — 詳細パネルに配置理由を表示

**Files:**
- Modify: `src/ReloadedHelper.App/Views/ModListView.xaml`

**Interfaces:**
- Consumes: `ModLoadEntry.PlacementReason`（Task 5）。バインドのみ、コードビハインド変更なし。

**設計メモ:** 右の詳細パネル（`DataContext="{Binding SelectedEntry}"`、約413行目）に、`PlacementReason` が null のとき非表示になる行を1つ足す。Core 側にロジックは無く表示のみのため、検証はビルド＋実機目視。

- [ ] **Step 1: Add the placement reason block**

`src/ReloadedHelper.App/Views/ModListView.xaml` の詳細パネル内、`CategoryLabel` を表示している `TextBlock`（約478行目）の直後に追加:

```xml
                            <!-- 自動並び替えの配置理由（PlacementReason が null のとき非表示） -->
                            <TextBlock Text="{Binding PlacementReason}"
                                       TextWrapping="Wrap"
                                       Margin="0,6,0,0"
                                       Foreground="{DynamicResource TextSecondaryBrush}"
                                       FontSize="{DynamicResource FontSizeSmall}">
                                <TextBlock.Style>
                                    <Style TargetType="TextBlock">
                                        <Style.Triggers>
                                            <DataTrigger Binding="{Binding PlacementReason}" Value="{x:Null}">
                                                <Setter Property="Visibility" Value="Collapsed"/>
                                            </DataTrigger>
                                        </Style.Triggers>
                                    </Style>
                                </TextBlock.Style>
                            </TextBlock>
```

> 注: `TextSecondaryBrush` / `FontSizeSmall` が `Themes/` に無い場合は、同パネル内の既存 `TextBlock`（例: `Info.ModAuthor` 行）が使っている `Foreground` / `FontSize` のリソース名に合わせること。

- [ ] **Step 2: Build the app**

Run: `dotnet build reloaded-helper.slnx`
Expected: ビルド成功（XAML エラーなし）。

- [ ] **Step 3: Manual verification**

`dotnet publish` 済み、または `dotnet run --project src/ReloadedHelper.App` で起動し、MODを選択 → 詳細パネルに「○○のため△△に配置」が出ること、理由の無い行では何も表示されないことを目視確認。

- [ ] **Step 4: Commit**

```bash
git add src/ReloadedHelper.App/Views/ModListView.xaml
git commit -m "feat: MOD詳細パネルに自動並び替えの配置理由を表示"
```

---

### Task 7: 全体結合の確認（回帰）

**Files:**
- なし（検証のみ）

- [ ] **Step 1: Run the full suite**

Run: `dotnet test`
Expected: 全 PASS。

- [ ] **Step 2: Build release**

Run: `dotnet build reloaded-helper.slnx -c Release`
Expected: 成功。

- [ ] **Step 3: Commit（必要なら）**

変更が無ければスキップ。

---

## Self-Review

**1. Spec coverage:**
- 全体整列（全MODを層別に並べる）→ Task 3。
- 中身ベース役割分類（カテゴリ無しでも役割が付く）→ Task 2、配線は Task 5。
- 全MODに配置理由 → Task 3（`ModPlacement`）＋ Task 5（行へ注入）＋ Task 6（表示）。
- スキャナのカバレッジ拡張 → Task 4。
- 既存バックアップ／変更なし時は書込まない → 既存挙動を `RunAutoSort` で維持（変更なし）。
- 非ゴール（sounds/wips/GitHub取得、プレビュー承認、他ゲーム汎用化）→ 着手しない。

**2. Placeholder scan:** TBD/TODO なし。各コードステップに実コードあり。

**3. Type consistency:** `RoleDecision(Role, Reason)`、`ModPlacement(ModId, LayerRank, LayerLabel, Reason)`、`OptimizeResult` 4引数、`Optimize(..., roleReasons = null)`、`ModLoadEntry(..., PlacementReason = null)`、`BuildRoleDecisions`/`BuildRoles` の戻り型 — Task 間で一致。
