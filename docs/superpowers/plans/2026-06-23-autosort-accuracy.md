# 自動並び替え 精度オーバーホール Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 各MODが「ゲームの何を触るか」を解析し、競合を役割ルールとユーザーの好みで解いて、有効化/起動/削除のたびに読み込み順を自動最適化するエンジンを実装する。

**Architecture:** `IModAnalyzer` 群がMODを「資源キー(file:/song:/costume:)→触るMOD」へ正規化 → `ConflictDetector`(汎用化)が競合検出 → `ModRoleClassifier` がカテゴリ/パスから役割を推定 → `LoadOrderOptimizer` が「依存 > 好み(`PreferenceStore`) > 役割」の優先度で順序+配置理由を算出 → `AutoSortCoordinator` が発動制御・バックアップ・履歴記録。`ModDiagnostics` は新資源・新原因を提示。

**Tech Stack:** C# / .NET 10 / WPF。Core 層は WPF 非依存。テストは xUnit。JSON は System.Text.Json のみ。

## Global Constraints

- ランタイム NuGet パッケージ追加禁止（System.Text.Json のみ可。テスト用 xUnit は可）。
- ユーザーデータは `%APPDATA%\ReloadedHelper` 以外に保存禁止。
- 実行時に Claude/外部AIを一切呼ばない（知識はソフト内のルールに持つ）。
- 失敗・未対応を握りつぶさない（必ず記録）。自動修正前にバックアップ、ワンタップ復元。
- Core: `src/ReloadedHelper.Core/`、App: `src/ReloadedHelper.App/`、Test: `tests/ReloadedHelper.Core.Tests/`。
- ビルド確認: `dotnet build reloaded-helper.slnx`、テスト: `dotnet test`。

---

## ファイル構成（このプランで作成/変更）

- Create: `src/ReloadedHelper.Core/Analyzers/IModAnalyzer.cs` — 解析の共通インターフェース＋資源モデル `ModResources`。
- Modify: `src/ReloadedHelper.Core/ModContentScanner.cs` → `FileReplaceAnalyzer` として IModAnalyzer 実装（既存ロジック維持）。
- Create: `src/ReloadedHelper.Core/Analyzers/BgmeAnalyzer.cs` — `song:*` 抽出。
- Create: `src/ReloadedHelper.Core/Analyzers/CostumeAnalyzer.cs` — `costume:*` 抽出。
- Create: `src/ReloadedHelper.Core/Analyzers/StructureAnalyzer.cs` — 構造警告。
- Create: `src/ReloadedHelper.Core/Analyzers/ModAnalysis.cs` — 全アナライザを束ねる集約サービス。
- Modify: `src/ReloadedHelper.Core/ConflictDetector.cs` — 資源キー汎用化（既に汎用なので資源型を渡せるよう調整）。
- Create: `src/ReloadedHelper.Core/ModRoleClassifier.cs` — カテゴリ/パス→役割。
- Create: `src/ReloadedHelper.Core/PreferenceStore.cs` — 競合ペアの勝敗を永続化。
- Create: `src/ReloadedHelper.Core/LoadOrderOptimizer.cs` — 依存+好み+役割で順序+配置理由。
- Create: `src/ReloadedHelper.Core/AutoSortCoordinator.cs` — 発動・バックアップ・履歴。
- Modify: `src/ReloadedHelper.Core/ModDiagnostics.cs` — 役割考慮・構造警告・曲ID被りの提示。
- Modify: `src/ReloadedHelper.Core/GameDiagnostics.cs` — 新アナライザ集約を入力に。
- Create/Modify (App): 診断パネルに「自動配置履歴」＋「元に戻す」導線。
- Test: 各 Core クラスに対応するテストファイル。

> **Note:** 既存 `ModContentScanner`/`ConflictDetector` は D+E で実装済み・実機検証済み。互換を壊さないよう、リファクタは「移行テストが緑」を保ちながら行う。

---

## Task 1: 資源モデルとアナライザ基盤

**Files:**
- Create: `src/ReloadedHelper.Core/Analyzers/IModAnalyzer.cs`
- Test: `tests/ReloadedHelper.Core.Tests/Analyzers/ModResourcesTests.cs`

**Interfaces:**
- Produces:
  - `enum ResourceKind { File, Song, Costume }`
  - `readonly record struct ResourceKey(ResourceKind Kind, string Value)` with `public override string ToString()` → `"file:..."` 等。
  - `sealed record ModResources(string ModId, IReadOnlyList<ResourceKey> Resources)`
  - `interface IModAnalyzer { IReadOnlyList<ResourceKey> Analyze(ModInfo mod); }`

- [ ] **Step 1: Write the failing test**

```csharp
using ReloadedHelper.Core.Analyzers;
using Xunit;

namespace ReloadedHelper.Core.Tests.Analyzers;

public class ModResourcesTests
{
    [Fact]
    public void ResourceKey_ToString_UsesKindPrefix()
    {
        Assert.Equal("file:p5ressentials/cpk/a.bin",
            new ResourceKey(ResourceKind.File, "p5ressentials/cpk/a.bin").ToString());
        Assert.Equal("song:12000", new ResourceKey(ResourceKind.Song, "12000").ToString());
        Assert.Equal("costume:joker/0", new ResourceKey(ResourceKind.Costume, "joker/0").ToString());
    }

    [Fact]
    public void ResourceKey_EqualityIsValueBased()
    {
        Assert.Equal(new ResourceKey(ResourceKind.Song, "1"), new ResourceKey(ResourceKind.Song, "1"));
        Assert.NotEqual(new ResourceKey(ResourceKind.Song, "1"), new ResourceKey(ResourceKind.File, "1"));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter ModResourcesTests`
Expected: FAIL (型 `ResourceKey` 未定義でコンパイルエラー)

- [ ] **Step 3: Write minimal implementation**

```csharp
namespace ReloadedHelper.Core.Analyzers;

public enum ResourceKind { File, Song, Costume }

public readonly record struct ResourceKey(ResourceKind Kind, string Value)
{
    private string Prefix => Kind switch
    {
        ResourceKind.File => "file",
        ResourceKind.Song => "song",
        ResourceKind.Costume => "costume",
        _ => "unknown",
    };

    public override string ToString() => $"{Prefix}:{Value}";
}

public sealed record ModResources(string ModId, IReadOnlyList<ResourceKey> Resources);

public interface IModAnalyzer
{
    // 解析できなければ空リストを返す（例外で握りつぶさない）。
    IReadOnlyList<ResourceKey> Analyze(ModInfo mod);
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter ModResourcesTests`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/ReloadedHelper.Core/Analyzers/IModAnalyzer.cs tests/ReloadedHelper.Core.Tests/Analyzers/ModResourcesTests.cs
git commit -m "feat: アナライザ基盤(ResourceKey/IModAnalyzer)"
```

---

## Task 2: FileReplaceAnalyzer（既存スキャナを IModAnalyzer 化）

**Files:**
- Modify: `src/ReloadedHelper.Core/ModContentScanner.cs`
- Create: `src/ReloadedHelper.Core/Analyzers/FileReplaceAnalyzer.cs`
- Test: `tests/ReloadedHelper.Core.Tests/Analyzers/FileReplaceAnalyzerTests.cs`

**Interfaces:**
- Consumes: `IModAnalyzer`, `ResourceKey`, `ModInfo`。既存 `ModContentScanner.Scan(folderPath, modId)` を内部利用。
- Produces: `sealed class FileReplaceAnalyzer : IModAnalyzer`。`file:<相対パス小文字>` を返す。

- [ ] **Step 1: Write the failing test**

```csharp
using System.IO;
using ReloadedHelper.Core;
using ReloadedHelper.Core.Analyzers;
using Xunit;

namespace ReloadedHelper.Core.Tests.Analyzers;

public class FileReplaceAnalyzerTests
{
    private static ModInfo Mod(string folder) => new(
        "m", "M", "", "1.0", "", Array.Empty<string>(), Array.Empty<string>(),
        Array.Empty<string>(), Array.Empty<string>(), null, null, null, null, folder);

    [Fact]
    public void Analyze_ReturnsFileResources_ForRedirectFiles()
    {
        var tmp = Directory.CreateTempSubdirectory().FullName;
        var cpk = Path.Combine(tmp, "P5REssentials", "CPK", "sub");
        Directory.CreateDirectory(cpk);
        File.WriteAllText(Path.Combine(cpk, "Hair.bin"), "x");

        var res = new FileReplaceAnalyzer().Analyze(Mod(tmp));

        Assert.Contains(new ResourceKey(ResourceKind.File, "p5ressentials/cpk/sub/hair.bin"), res);
    }

    [Fact]
    public void Analyze_EmptyForNonExistentFolder()
        => Assert.Empty(new FileReplaceAnalyzer().Analyze(Mod(@"C:\nope\missing")));
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter FileReplaceAnalyzerTests`
Expected: FAIL (型 `FileReplaceAnalyzer` 未定義)

- [ ] **Step 3: Write minimal implementation**

`ModContentScanner` の `Scan` はそのまま残し、ラップする：

```csharp
namespace ReloadedHelper.Core.Analyzers;

public sealed class FileReplaceAnalyzer : IModAnalyzer
{
    public IReadOnlyList<ResourceKey> Analyze(ModInfo mod)
    {
        var overrides = ModContentScanner.Scan(mod.FolderPath, mod.ModId);
        var result = new List<ResourceKey>(overrides.Paths.Count);
        foreach (var p in overrides.Paths)
            result.Add(new ResourceKey(ResourceKind.File, p));
        return result;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter FileReplaceAnalyzerTests`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/ReloadedHelper.Core/Analyzers/FileReplaceAnalyzer.cs tests/ReloadedHelper.Core.Tests/Analyzers/FileReplaceAnalyzerTests.cs
git commit -m "feat: FileReplaceAnalyzer(既存スキャナの IModAnalyzer 化)"
```

---

## Task 3: ModRoleClassifier（役割分類）

**Files:**
- Create: `src/ReloadedHelper.Core/ModRoleClassifier.cs`
- Test: `tests/ReloadedHelper.Core.Tests/ModRoleClassifierTests.cs`

**Interfaces:**
- Consumes: `ModInfo`（`Tags` にカテゴリ文字列が入る想定。`ModLoadEntry.Category` と同じ値域: "Sound","Skin","Texture","UI","Gameplay Mechanics","Misc","Quality Of Life" 等）, `IsLibrary`。
- Produces:
  - `enum ModRole { VisualOverride, BaseLayer, Music, Library, Unknown }`
  - `static class ModRoleClassifier { static ModRole Classify(ModInfo mod, string? category); }`

- [ ] **Step 1: Write the failing test**

```csharp
using ReloadedHelper.Core;
using Xunit;

namespace ReloadedHelper.Core.Tests;

public class ModRoleClassifierTests
{
    private static ModInfo Mod(bool lib = false, params string[] tags) => new(
        "m","M","","1.0","", tags, Array.Empty<string>(), Array.Empty<string>(),
        Array.Empty<string>(), null, null, null, null, "", lib);

    [Theory]
    [InlineData("Skin", ModRole.VisualOverride)]
    [InlineData("Texture", ModRole.VisualOverride)]
    [InlineData("UI", ModRole.VisualOverride)]
    [InlineData("Sound", ModRole.Music)]
    [InlineData("Gameplay Mechanics", ModRole.BaseLayer)]
    [InlineData("Misc", ModRole.Unknown)]
    [InlineData(null, ModRole.Unknown)]
    public void Classify_ByCategory(string? category, ModRole expected)
        => Assert.Equal(expected, ModRoleClassifier.Classify(Mod(), category));

    [Fact]
    public void Classify_LibraryBeatsCategory()
        => Assert.Equal(ModRole.Library, ModRoleClassifier.Classify(Mod(lib: true), "Skin"));
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter ModRoleClassifierTests`
Expected: FAIL (型未定義)

- [ ] **Step 3: Write minimal implementation**

```csharp
namespace ReloadedHelper.Core;

public enum ModRole { VisualOverride, BaseLayer, Music, Library, Unknown }

public static class ModRoleClassifier
{
    // カテゴリ→役割の知識テーブル（拡張ポイント：値を足すだけで賢くなる）。
    public static ModRole Classify(ModInfo mod, string? category)
    {
        if (mod.IsLibrary) return ModRole.Library;
        return category switch
        {
            "Skin" or "Texture" or "UI" => ModRole.VisualOverride,
            "Sound" => ModRole.Music,
            "Gameplay Mechanics" => ModRole.BaseLayer,
            _ => ModRole.Unknown,
        };
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter ModRoleClassifierTests`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/ReloadedHelper.Core/ModRoleClassifier.cs tests/ReloadedHelper.Core.Tests/ModRoleClassifierTests.cs
git commit -m "feat: ModRoleClassifier(カテゴリ→役割の知識テーブル)"
```

---

## Task 4: PreferenceStore（好みの記憶 / 永続化）

**Files:**
- Create: `src/ReloadedHelper.Core/PreferenceStore.cs`
- Test: `tests/ReloadedHelper.Core.Tests/PreferenceStoreTests.cs`

**Interfaces:**
- Produces:
  - `sealed class PreferenceStore`（テスト容易化のため保存先ディレクトリを ctor で受ける）。
  - `PreferenceStore(string dir)`
  - `void SetWinner(string appId, string modA, string modB, string winnerModId)` — ペア{A,B}の勝者を記録（順不同で同一視）。
  - `string? GetWinner(string appId, string modA, string modB)` — 記録が無ければ null。
  - `void Save()` / 読み込みは ctor で実施。
  - 保存形式: `<dir>/preferences.json`。本番は `%APPDATA%\ReloadedHelper`。

- [ ] **Step 1: Write the failing test**

```csharp
using System.IO;
using ReloadedHelper.Core;
using Xunit;

namespace ReloadedHelper.Core.Tests;

public class PreferenceStoreTests
{
    [Fact]
    public void SetThenGet_ReturnsWinner_OrderInsensitive()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var store = new PreferenceStore(dir);
        store.SetWinner("p5r", "hairMod", "bodyMod", "hairMod");

        Assert.Equal("hairMod", store.GetWinner("p5r", "hairMod", "bodyMod"));
        Assert.Equal("hairMod", store.GetWinner("p5r", "bodyMod", "hairMod")); // 順不同
        Assert.Null(store.GetWinner("p5r", "hairMod", "other"));
    }

    [Fact]
    public void Persists_AcrossInstances()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        new PreferenceStore(dir).SetWinner("p5r", "a", "b", "b");
        Assert.Equal("b", new PreferenceStore(dir).GetWinner("p5r", "a", "b"));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter PreferenceStoreTests`
Expected: FAIL (型未定義)

- [ ] **Step 3: Write minimal implementation**

```csharp
using System.IO;
using System.Text.Json;

namespace ReloadedHelper.Core;

public sealed class PreferenceStore
{
    private readonly string _path;
    // key: "appId|min|max"(小文字), value: winnerModId
    private readonly Dictionary<string, string> _winners;

    public PreferenceStore(string dir)
    {
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "preferences.json");
        _winners = File.Exists(_path)
            ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(_path))
              ?? new(StringComparer.Ordinal)
            : new(StringComparer.Ordinal);
    }

    private static string Key(string appId, string a, string b)
    {
        var x = a.ToLowerInvariant();
        var y = b.ToLowerInvariant();
        var (lo, hi) = string.CompareOrdinal(x, y) <= 0 ? (x, y) : (y, x);
        return $"{appId.ToLowerInvariant()}|{lo}|{hi}";
    }

    public string? GetWinner(string appId, string a, string b)
        => _winners.TryGetValue(Key(appId, a, b), out var w) ? w : null;

    public void SetWinner(string appId, string a, string b, string winnerModId)
    {
        _winners[Key(appId, a, b)] = winnerModId;
        Save();
    }

    public void Save() =>
        File.WriteAllText(_path, JsonSerializer.Serialize(_winners,
            new JsonSerializerOptions { WriteIndented = true }));
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter PreferenceStoreTests`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/ReloadedHelper.Core/PreferenceStore.cs tests/ReloadedHelper.Core.Tests/PreferenceStoreTests.cs
git commit -m "feat: PreferenceStore(競合ペアの勝敗を永続化)"
```

---

## Task 5: ConflictDetector の資源汎用化

**Files:**
- Modify: `src/ReloadedHelper.Core/ConflictDetector.cs`
- Test: `tests/ReloadedHelper.Core.Tests/ConflictDetectorResourceTests.cs`

**Interfaces:**
- Consumes: `ModResources`, `ResourceKey`。
- Produces: 既存 `FileConflict(string PathKey, IReadOnlyList<string> ModIds, string WinnerModId)` は維持。新規オーバーロード `Detect(IReadOnlyList<ModResources> orderedEnabled)` を追加（`ResourceKey.ToString()` を PathKey に使う）。既存の `Detect(IReadOnlyList<ModOverrides>)` も互換のため残す。

> **互換性:** 既存テスト（D+E）が緑のままであること。既存シグネチャは削除しない。

- [ ] **Step 1: Write the failing test**

```csharp
using ReloadedHelper.Core;
using ReloadedHelper.Core.Analyzers;
using Xunit;

namespace ReloadedHelper.Core.Tests;

public class ConflictDetectorResourceTests
{
    private static ModResources R(string id, params ResourceKey[] keys) => new(id, keys);

    [Fact]
    public void Detect_SongConflict_LastWins()
    {
        var song = new ResourceKey(ResourceKind.Song, "12000");
        var conflicts = ConflictDetector.Detect(new[]
        {
            R("a", song),
            R("b", song),
        });

        var c = Assert.Single(conflicts);
        Assert.Equal("song:12000", c.PathKey);
        Assert.Equal("b", c.WinnerModId); // 読み込み順で後＝勝者
        Assert.Equal(new[] { "a", "b" }, c.ModIds);
    }

    [Fact]
    public void Detect_NoConflict_WhenDistinctResources()
    {
        var conflicts = ConflictDetector.Detect(new[]
        {
            R("a", new ResourceKey(ResourceKind.File, "x")),
            R("b", new ResourceKey(ResourceKind.File, "y")),
        });
        Assert.Empty(conflicts);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter ConflictDetectorResourceTests`
Expected: FAIL (オーバーロード未定義)

- [ ] **Step 3: Write minimal implementation**

`ConflictDetector` に追加（既存メソッドは残す）：

```csharp
using ReloadedHelper.Core.Analyzers;

// ...既存 using/namespace 内に追加...

public static IReadOnlyList<FileConflict> Detect(IReadOnlyList<ModResources> orderedEnabled)
{
    var byKey = new Dictionary<string, List<string>>(StringComparer.Ordinal);
    foreach (var mr in orderedEnabled)
        foreach (var rk in mr.Resources)
        {
            var k = rk.ToString();
            if (!byKey.TryGetValue(k, out var list)) byKey[k] = list = new List<string>();
            if (!list.Contains(mr.ModId)) list.Add(mr.ModId);
        }

    var result = new List<FileConflict>();
    foreach (var (key, mods) in byKey)
        if (mods.Count > 1)
            result.Add(new FileConflict(key, mods, mods[^1]));
    return result;
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter ConflictDetectorResourceTests` then `dotnet test`
Expected: PASS（新規テスト＋既存テスト全緑）

- [ ] **Step 5: Commit**

```bash
git add src/ReloadedHelper.Core/ConflictDetector.cs tests/ReloadedHelper.Core.Tests/ConflictDetectorResourceTests.cs
git commit -m "feat: ConflictDetector を資源キー汎用に(オーバーロード追加)"
```

---

## Task 6: LoadOrderOptimizer（決定エンジン本体）

**Files:**
- Create: `src/ReloadedHelper.Core/LoadOrderOptimizer.cs`
- Test: `tests/ReloadedHelper.Core.Tests/LoadOrderOptimizerTests.cs`

**Interfaces:**
- Consumes: `LoadOrderSorter.Sort`（依存トポロジカル）、`FileConflict`、`PreferenceStore`、`ModRoleClassifier`/`ModRole`。
- Produces:
  - `sealed record PlacementReason(string MovedModId, string OverWinnerOrLoser, string Message)`
  - `sealed record OptimizeResult(IReadOnlyList<string> Order, IReadOnlyList<PlacementReason> Reasons, IReadOnlyList<(string A, string B)> Unresolved)`
  - `static class LoadOrderOptimizer`:
    `OptimizeResult Optimize(string appId, IReadOnlyList<string> currentOrder, IReadOnlyDictionary<string, IReadOnlyList<string>> dependenciesOf, IReadOnlyList<FileConflict> conflicts, IReadOnlyDictionary<string, ModRole> rolesByMod, PreferenceStore prefs)`

**ロジック（優先度: 依存 > 好み > 役割）:**
1. まず `LoadOrderSorter.Sort(currentOrder, dependenciesOf)` で依存を満たす基準順を得る。
2. 競合ごとに「勝つべきMOD」を決める：
   - `prefs.GetWinner` があればそれ。
   - 無ければ役割ルール: VisualOverride > BaseLayer なら Visual が勝者。役割で甲乙つかない（同役割 or 両方 Unknown）なら **Unresolved に積む（順序は触らない）**。
3. 勝者は敗者より「後」に来るよう、依存を壊さない範囲で安定的に並べ替える（勝者を敗者の直後以降へ移動）。依存制約に反する入れ替えはスキップし Unresolved に積む。
4. 動かした各ペアを `PlacementReason` に記録。

- [ ] **Step 1: Write the failing test**

```csharp
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
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter LoadOrderOptimizerTests`
Expected: FAIL (型未定義)

- [ ] **Step 3: Write minimal implementation**

```csharp
namespace ReloadedHelper.Core;

public sealed record PlacementReason(string MovedModId, string AgainstModId, string Message);

public sealed record OptimizeResult(
    IReadOnlyList<string> Order,
    IReadOnlyList<PlacementReason> Reasons,
    IReadOnlyList<(string A, string B)> Unresolved);

public static class LoadOrderOptimizer
{
    public static OptimizeResult Optimize(
        string appId,
        IReadOnlyList<string> currentOrder,
        IReadOnlyDictionary<string, IReadOnlyList<string>> dependenciesOf,
        IReadOnlyList<FileConflict> conflicts,
        IReadOnlyDictionary<string, ModRole> rolesByMod,
        PreferenceStore prefs)
    {
        // 1. 依存を満たす基準順
        var order = LoadOrderSorter.Sort(currentOrder, dependenciesOf).ToList();
        var reasons = new List<PlacementReason>();
        var unresolved = new List<(string A, string B)>();

        // 依存制約: dep は dependent より前。入れ替えがこれを壊さないか確認用。
        bool DependsOn(string mod, string maybeDep)
            => dependenciesOf.TryGetValue(mod, out var d) &&
               d.Contains(maybeDep, StringComparer.OrdinalIgnoreCase);

        foreach (var c in conflicts)
        {
            if (c.ModIds.Count != 2) continue; // 多重競合は当面ペア単位のみ自動化
            var a = c.ModIds[0];
            var b = c.ModIds[1];

            string? winner = prefs.GetWinner(appId, a, b) ?? DecideByRole(a, b, rolesByMod);
            if (winner is null) { unresolved.Add((a, b)); continue; }

            var loser = string.Equals(winner, a, StringComparison.OrdinalIgnoreCase) ? b : a;

            int wi = IndexOf(order, winner), li = IndexOf(order, loser);
            if (wi < 0 || li < 0) continue;
            if (wi > li) continue; // すでに勝者が後ろ＝OK

            // winner を loser の後ろへ移動したいが、依存を壊すなら諦める
            if (DependsOn(loser, winner)) { unresolved.Add((a, b)); continue; }

            order.RemoveAt(wi);
            li = IndexOf(order, loser);
            order.Insert(li + 1, winner);
            reasons.Add(new PlacementReason(winner, loser,
                $"「{winner}」を「{loser}」より後ろに配置しました（{winner} の上書きを反映）。"));
        }

        return new OptimizeResult(order, reasons, unresolved);
    }

    private static string? DecideByRole(string a, string b, IReadOnlyDictionary<string, ModRole> roles)
    {
        var ra = roles.GetValueOrDefault(a, ModRole.Unknown);
        var rb = roles.GetValueOrDefault(b, ModRole.Unknown);
        int Rank(ModRole r) => r switch
        {
            ModRole.VisualOverride => 2, // 勝たせたい
            ModRole.BaseLayer => 0,      // 負けてよい
            _ => 1,
        };
        if (ra == rb) return null;
        if (ra == ModRole.Unknown || rb == ModRole.Unknown) return null;
        return Rank(ra) > Rank(rb) ? a : Rank(rb) > Rank(ra) ? b : null;
    }

    private static int IndexOf(List<string> list, string id)
        => list.FindIndex(x => string.Equals(x, id, StringComparison.OrdinalIgnoreCase));
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter LoadOrderOptimizerTests`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/ReloadedHelper.Core/LoadOrderOptimizer.cs tests/ReloadedHelper.Core.Tests/LoadOrderOptimizerTests.cs
git commit -m "feat: LoadOrderOptimizer(依存>好み>役割で順序+配置理由)"
```

---

## Task 7: ModAnalysis（全アナライザ集約 + 配置履歴の型）

**Files:**
- Create: `src/ReloadedHelper.Core/Analyzers/ModAnalysis.cs`
- Test: `tests/ReloadedHelper.Core.Tests/Analyzers/ModAnalysisTests.cs`

**Interfaces:**
- Consumes: `IModAnalyzer` 群, `ModInfo`, `ModResources`。
- Produces:
  - `sealed class ModAnalysis`：`ModAnalysis(IReadOnlyList<IModAnalyzer> analyzers)`、`ModResources Analyze(ModInfo mod)`（全アナライザの資源を結合。1つが例外でも他は継続し、失敗は `IReadOnlyList<string> Failures` に記録）。
  - `IReadOnlyList<string> Failures { get; }`

- [ ] **Step 1: Write the failing test**

```csharp
using ReloadedHelper.Core;
using ReloadedHelper.Core.Analyzers;
using Xunit;

namespace ReloadedHelper.Core.Tests.Analyzers;

public class ModAnalysisTests
{
    private sealed class FakeAnalyzer(ResourceKey[] keys) : IModAnalyzer
    {
        public IReadOnlyList<ResourceKey> Analyze(ModInfo mod) => keys;
    }
    private sealed class ThrowingAnalyzer : IModAnalyzer
    {
        public IReadOnlyList<ResourceKey> Analyze(ModInfo mod) => throw new InvalidOperationException("boom");
    }
    private static ModInfo Mod() => new("m","M","","1","",
        Array.Empty<string>(),Array.Empty<string>(),Array.Empty<string>(),Array.Empty<string>(),
        null,null,null,null,"");

    [Fact]
    public void Analyze_CombinesAllAnalyzerResources()
    {
        var a = new ModAnalysis(new IModAnalyzer[]
        {
            new FakeAnalyzer(new[]{ new ResourceKey(ResourceKind.File,"x") }),
            new FakeAnalyzer(new[]{ new ResourceKey(ResourceKind.Song,"1") }),
        });
        var res = a.Analyze(Mod());
        Assert.Equal(2, res.Resources.Count);
    }

    [Fact]
    public void Analyze_RecordsFailure_ButContinues()
    {
        var a = new ModAnalysis(new IModAnalyzer[]
        {
            new ThrowingAnalyzer(),
            new FakeAnalyzer(new[]{ new ResourceKey(ResourceKind.File,"x") }),
        });
        var res = a.Analyze(Mod());
        Assert.Single(res.Resources);
        Assert.Single(a.Failures);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter ModAnalysisTests`
Expected: FAIL (型未定義)

- [ ] **Step 3: Write minimal implementation**

```csharp
namespace ReloadedHelper.Core.Analyzers;

public sealed class ModAnalysis
{
    private readonly IReadOnlyList<IModAnalyzer> _analyzers;
    private readonly List<string> _failures = new();

    public ModAnalysis(IReadOnlyList<IModAnalyzer> analyzers) => _analyzers = analyzers;

    public IReadOnlyList<string> Failures => _failures;

    public ModResources Analyze(ModInfo mod)
    {
        var all = new List<ResourceKey>();
        foreach (var analyzer in _analyzers)
        {
            try { all.AddRange(analyzer.Analyze(mod)); }
            catch (Exception ex)
            {
                _failures.Add($"{mod.ModId}: {analyzer.GetType().Name} 失敗: {ex.Message}");
            }
        }
        return new ModResources(mod.ModId, all);
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter ModAnalysisTests`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/ReloadedHelper.Core/Analyzers/ModAnalysis.cs tests/ReloadedHelper.Core.Tests/Analyzers/ModAnalysisTests.cs
git commit -m "feat: ModAnalysis(全アナライザ集約・失敗を握りつぶさない)"
```

---

## Task 8: AutoSortCoordinator（発動・バックアップ・履歴）

**Files:**
- Create: `src/ReloadedHelper.Core/AutoSortCoordinator.cs`
- Test: `tests/ReloadedHelper.Core.Tests/AutoSortCoordinatorTests.cs`

**Interfaces:**
- Consumes: `LoadOrderOptimizer`, `PlacementReason`, `OptimizeResult`。
- Produces:
  - `enum AutoSortTrigger { Startup, ToggleEnable, Delete, ForcedRefresh }`
  - `sealed record AutoSortHistoryEntry(DateTime At, AutoSortTrigger Trigger, IReadOnlyList<PlacementReason> Reasons, IReadOnlyList<(string A, string B)> Unresolved)`
  - `sealed class AutoSortCoordinator`：
    - ctor: `AutoSortCoordinator(string historyDir)`（履歴を `<historyDir>/autosort-history.json` に保存）。
    - `AutoSortHistoryEntry Apply(AutoSortTrigger trigger, OptimizeResult result, Action<IReadOnlyList<string>> applyOrder, Action backup)` — backup()→applyOrder(result.Order)→履歴追記、を順に行い、エントリを返す。
    - `IReadOnlyList<AutoSortHistoryEntry> History { get; }`（新しい順）。
  - 副作用（実ファイル書き込み・実バックアップ）は呼び出し側のデリゲートに委譲＝テスト可能。

- [ ] **Step 1: Write the failing test**

```csharp
using System.IO;
using ReloadedHelper.Core;
using Xunit;

namespace ReloadedHelper.Core.Tests;

public class AutoSortCoordinatorTests
{
    [Fact]
    public void Apply_BacksUpThenApplies_AndRecordsHistory()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var coord = new AutoSortCoordinator(dir);
        var calls = new List<string>();
        var result = new OptimizeResult(
            new[] { "a", "b" },
            new[] { new PlacementReason("a", "b", "msg") },
            Array.Empty<(string, string)>());

        var entry = coord.Apply(AutoSortTrigger.ToggleEnable, result,
            applyOrder: o => calls.Add("apply:" + string.Join(",", o)),
            backup: () => calls.Add("backup"));

        Assert.Equal(new[] { "backup", "apply:a,b" }, calls); // バックアップが先
        Assert.Single(entry.Reasons);
        Assert.Single(coord.History);
    }

    [Fact]
    public void History_Persists_NewestFirst()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var empty = new OptimizeResult(Array.Empty<string>(),
            Array.Empty<PlacementReason>(), Array.Empty<(string, string)>());
        var c1 = new AutoSortCoordinator(dir);
        c1.Apply(AutoSortTrigger.Startup, empty, _ => { }, () => { });

        var c2 = new AutoSortCoordinator(dir); // 再読み込み
        Assert.Single(c2.History);
        Assert.Equal(AutoSortTrigger.Startup, c2.History[0].Trigger);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter AutoSortCoordinatorTests`
Expected: FAIL (型未定義)

- [ ] **Step 3: Write minimal implementation**

```csharp
using System.IO;
using System.Text.Json;

namespace ReloadedHelper.Core;

public enum AutoSortTrigger { Startup, ToggleEnable, Delete, ForcedRefresh }

public sealed record AutoSortHistoryEntry(
    DateTime At,
    AutoSortTrigger Trigger,
    IReadOnlyList<PlacementReason> Reasons,
    IReadOnlyList<UnresolvedPair> Unresolved);

public sealed record UnresolvedPair(string A, string B);

public sealed class AutoSortCoordinator
{
    private readonly string _path;
    private readonly List<AutoSortHistoryEntry> _history;
    private const int MaxEntries = 50;

    public AutoSortCoordinator(string historyDir)
    {
        Directory.CreateDirectory(historyDir);
        _path = Path.Combine(historyDir, "autosort-history.json");
        _history = File.Exists(_path)
            ? JsonSerializer.Deserialize<List<AutoSortHistoryEntry>>(File.ReadAllText(_path)) ?? new()
            : new();
    }

    public IReadOnlyList<AutoSortHistoryEntry> History => _history;

    public AutoSortHistoryEntry Apply(
        AutoSortTrigger trigger,
        OptimizeResult result,
        Action<IReadOnlyList<string>> applyOrder,
        Action backup)
    {
        backup();
        applyOrder(result.Order);

        var entry = new AutoSortHistoryEntry(
            DateTime.Now, trigger, result.Reasons,
            result.Unresolved.Select(u => new UnresolvedPair(u.A, u.B)).ToList());

        _history.Insert(0, entry);
        if (_history.Count > MaxEntries) _history.RemoveRange(MaxEntries, _history.Count - MaxEntries);
        File.WriteAllText(_path, JsonSerializer.Serialize(_history,
            new JsonSerializerOptions { WriteIndented = true }));
        return entry;
    }
}
```

> **Note:** `OptimizeResult.Unresolved` は `(string A, string B)` タプルだが、JSON 直列化のため履歴側は `UnresolvedPair` レコードへ変換している。

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter AutoSortCoordinatorTests`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/ReloadedHelper.Core/AutoSortCoordinator.cs tests/ReloadedHelper.Core.Tests/AutoSortCoordinatorTests.cs
git commit -m "feat: AutoSortCoordinator(発動・バックアップ先行・履歴永続化)"
```

---

## Task 9: BGME 音楽MODの実機形式調査（コード前の必須調査）

**Files:**
- Create: `docs/superpowers/notes/bgme-format.md`（調査結果メモ）

**目的:** `BgmeAnalyzer` のパース対象を実物で確定する。形式を捏造しない。

- [ ] **Step 1: 実機の BGME 系MODを特定**

実機 MOD ルート `C:\FreeSoft\Reloaded-II` 配下で、BGME（BGM Event/Persona Music）依存のMODフォルダを探す。
Run（Bash）: `ls "C:/FreeSoft/Reloaded-II/Mods" | grep -i -E "bgme|music|bgm|song"`
見つからなければ、各MODの `ModConfig.json` の Dependencies に BGME フレームワーク（例 `p5rpc.modloader` や `BGME.Framework.*`）を含むものを `grep -rl -i bgme "C:/FreeSoft/Reloaded-II/Mods"` で探す。

- [ ] **Step 2: 曲ID定義ファイルの実体を記録**

特定したMODフォルダ内で、曲を定義しているファイル（`.pme`／`bgm.json`／`Music/` 配下の設定など実在するもの）を開き、**実際の曲ID表記**を確認する。
Read で中身を確認し、`docs/superpowers/notes/bgme-format.md` に以下を記録：
- 定義ファイルの相対パス（例 `BGME/Music.pme`）
- 曲IDの抽出方法（行フォーマット・JSONキー・正規表現）
- 1MOD複数曲のケースの例

- [ ] **Step 3: Commit（調査メモ）**

```bash
git add docs/superpowers/notes/bgme-format.md
git commit -m "docs: BGME 音楽MODの実機ファイル形式 調査メモ"
```

> **依存:** Task 10 は本メモの「曲ID抽出方法」に従って実装する。形式が複数あればメモに全列挙し、Task 10 でそれぞれにケースを足す。

---

## Task 10: BgmeAnalyzer（曲ID抽出）

**Files:**
- Create: `src/ReloadedHelper.Core/Analyzers/BgmeAnalyzer.cs`
- Test: `tests/ReloadedHelper.Core.Tests/Analyzers/BgmeAnalyzerTests.cs`

**Interfaces:**
- Consumes: `IModAnalyzer`, `ResourceKey`, `ModInfo`, および Task 9 のメモ（曲ID抽出方法）。
- Produces: `sealed class BgmeAnalyzer : IModAnalyzer`。`song:<曲ID>` を返す。定義ファイルが無ければ空。

> **実装メモ:** 下記テストとコードは「BGME 定義が `BGME/Music.pme` にあり、各行 `<曲ID> = <ファイル>` 形式（`#` はコメント）」という**仮の形式**で書いている。Task 9 のメモが別形式を示したら、`ExtractSongIds` のパース部とテストの入力をメモの実形式に差し替えること（資源化のI/Fは不変）。

- [ ] **Step 1: Write the failing test**

```csharp
using System.IO;
using ReloadedHelper.Core;
using ReloadedHelper.Core.Analyzers;
using Xunit;

namespace ReloadedHelper.Core.Tests.Analyzers;

public class BgmeAnalyzerTests
{
    private static ModInfo Mod(string folder) => new("m","M","","1","",
        Array.Empty<string>(),Array.Empty<string>(),Array.Empty<string>(),Array.Empty<string>(),
        null,null,null,null,folder);

    [Fact]
    public void Analyze_ExtractsSongIds_FromDefinitionFile()
    {
        var tmp = Directory.CreateTempSubdirectory().FullName;
        var bgme = Path.Combine(tmp, "BGME");
        Directory.CreateDirectory(bgme);
        File.WriteAllText(Path.Combine(bgme, "Music.pme"),
            "# comment\n12000 = battle.adx\n12001 = boss.adx\n");

        var res = new BgmeAnalyzer().Analyze(Mod(tmp));

        Assert.Contains(new ResourceKey(ResourceKind.Song, "12000"), res);
        Assert.Contains(new ResourceKey(ResourceKind.Song, "12001"), res);
    }

    [Fact]
    public void Analyze_EmptyWhenNoBgmeFolder()
        => Assert.Empty(new BgmeAnalyzer().Analyze(Mod(Directory.CreateTempSubdirectory().FullName)));
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter BgmeAnalyzerTests`
Expected: FAIL (型未定義)

- [ ] **Step 3: Write minimal implementation**

```csharp
using System.IO;
using System.Text.RegularExpressions;

namespace ReloadedHelper.Core.Analyzers;

public sealed class BgmeAnalyzer : IModAnalyzer
{
    // Task 9 のメモに合わせた抽出。仮形式: "<曲ID> = <file>"（# コメント）。
    private static readonly Regex Line = new(@"^\s*(\d+)\s*=", RegexOptions.Compiled);

    public IReadOnlyList<ResourceKey> Analyze(ModInfo mod)
    {
        var dir = Path.Combine(mod.FolderPath, "BGME");
        if (!Directory.Exists(dir)) return Array.Empty<ResourceKey>();

        var result = new List<ResourceKey>();
        foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            foreach (var raw in File.ReadLines(file))
            {
                if (raw.TrimStart().StartsWith('#')) continue;
                var m = Line.Match(raw);
                if (m.Success) result.Add(new ResourceKey(ResourceKind.Song, m.Groups[1].Value));
            }
        return result;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter BgmeAnalyzerTests`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/ReloadedHelper.Core/Analyzers/BgmeAnalyzer.cs tests/ReloadedHelper.Core.Tests/Analyzers/BgmeAnalyzerTests.cs
git commit -m "feat: BgmeAnalyzer(曲ID抽出)"
```

---

## Task 11: コスチュームフレームワークの実機形式調査

**Files:**
- Create: `docs/superpowers/notes/costume-format.md`

**目的:** `CostumeAnalyzer` のパース対象を実物で確定する。

- [ ] **Step 1: 実機のコスチューム系MODを特定**

Run（Bash）: `grep -rl -i -E "costume|outfit" "C:/FreeSoft/Reloaded-II/Mods" --include=ModConfig.json`
コスチュームフレームワーク（例 `Costume Framework` 系）に依存するMODを探す。

- [ ] **Step 2: 登録ファイルの実体を記録**

登録定義（`config.json`／`costumes/` 配下のメタデータ等、実在するもの）を開き、**キャラ・枠ID の表記**を確認。`docs/superpowers/notes/costume-format.md` に記録：
- 登録ファイルの相対パス
- コスチューム枠キー（`<キャラ>/<枠番号>` 等）の抽出方法
- 「一部しかランダムにならない」現象に関係しそうな枠の網羅性メモ（観察のみ。解決は将来）

- [ ] **Step 3: Commit**

```bash
git add docs/superpowers/notes/costume-format.md
git commit -m "docs: コスチュームフレームワークの実機形式 調査メモ"
```

---

## Task 12: CostumeAnalyzer（コスチューム枠抽出）

**Files:**
- Create: `src/ReloadedHelper.Core/Analyzers/CostumeAnalyzer.cs`
- Test: `tests/ReloadedHelper.Core.Tests/Analyzers/CostumeAnalyzerTests.cs`

**Interfaces:**
- Consumes: `IModAnalyzer`, Task 11 のメモ。
- Produces: `sealed class CostumeAnalyzer : IModAnalyzer`。`costume:<キャラ/枠>` を返す。

> **実装メモ:** 下記は「`Costumes/<character>/<slot>/` というフォルダ階層で1枠＝1フォルダ」という**仮形式**。Task 11 のメモが JSON 定義等を示したら、`Analyze` のパース部とテスト入力をメモの実形式へ差し替える（I/F不変）。

- [ ] **Step 1: Write the failing test**

```csharp
using System.IO;
using ReloadedHelper.Core;
using ReloadedHelper.Core.Analyzers;
using Xunit;

namespace ReloadedHelper.Core.Tests.Analyzers;

public class CostumeAnalyzerTests
{
    private static ModInfo Mod(string folder) => new("m","M","","1","",
        Array.Empty<string>(),Array.Empty<string>(),Array.Empty<string>(),Array.Empty<string>(),
        null,null,null,null,folder);

    [Fact]
    public void Analyze_ExtractsCostumeSlots()
    {
        var tmp = Directory.CreateTempSubdirectory().FullName;
        Directory.CreateDirectory(Path.Combine(tmp, "Costumes", "Joker", "0"));
        Directory.CreateDirectory(Path.Combine(tmp, "Costumes", "Joker", "1"));

        var res = new CostumeAnalyzer().Analyze(Mod(tmp));

        Assert.Contains(new ResourceKey(ResourceKind.Costume, "joker/0"), res);
        Assert.Contains(new ResourceKey(ResourceKind.Costume, "joker/1"), res);
    }

    [Fact]
    public void Analyze_EmptyWhenNoCostumesFolder()
        => Assert.Empty(new CostumeAnalyzer().Analyze(Mod(Directory.CreateTempSubdirectory().FullName)));
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter CostumeAnalyzerTests`
Expected: FAIL (型未定義)

- [ ] **Step 3: Write minimal implementation**

```csharp
using System.IO;

namespace ReloadedHelper.Core.Analyzers;

public sealed class CostumeAnalyzer : IModAnalyzer
{
    public IReadOnlyList<ResourceKey> Analyze(ModInfo mod)
    {
        var root = Path.Combine(mod.FolderPath, "Costumes");
        if (!Directory.Exists(root)) return Array.Empty<ResourceKey>();

        var result = new List<ResourceKey>();
        foreach (var charDir in Directory.GetDirectories(root))
        {
            var character = Path.GetFileName(charDir).ToLowerInvariant();
            foreach (var slotDir in Directory.GetDirectories(charDir))
            {
                var slot = Path.GetFileName(slotDir).ToLowerInvariant();
                result.Add(new ResourceKey(ResourceKind.Costume, $"{character}/{slot}"));
            }
        }
        return result;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter CostumeAnalyzerTests`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/ReloadedHelper.Core/Analyzers/CostumeAnalyzer.cs tests/ReloadedHelper.Core.Tests/Analyzers/CostumeAnalyzerTests.cs
git commit -m "feat: CostumeAnalyzer(コスチューム枠抽出)"
```

---

## Task 13: StructureAnalyzer（置き場所チェック）

**Files:**
- Create: `src/ReloadedHelper.Core/Analyzers/StructureAnalyzer.cs`
- Test: `tests/ReloadedHelper.Core.Tests/Analyzers/StructureAnalyzerTests.cs`

**Interfaces:**
- Consumes: `ModInfo`。
- Produces:
  - `sealed record StructureWarning(string ModId, string Message)`
  - `static class StructureAnalyzer { static IReadOnlyList<StructureWarning> Check(ModInfo mod); }`
- ロジック: MODフォルダ直下に既知の redirect ルート（`P5REssentials/CPK`, `FEmulator/AWB`, `BGME`, `Costumes` 等）も、また `ModConfig.json` 以外の有効なファイルも一切無い＝「中身が空 or 置き場所が変」を警告。1階層深い場所に redirect ルートが埋もれている（例 `<mod>/<余分>/P5REssentials/CPK`）も警告。

- [ ] **Step 1: Write the failing test**

```csharp
using System.IO;
using ReloadedHelper.Core;
using ReloadedHelper.Core.Analyzers;
using Xunit;

namespace ReloadedHelper.Core.Tests.Analyzers;

public class StructureAnalyzerTests
{
    private static ModInfo Mod(string folder) => new("m","M","","1","",
        Array.Empty<string>(),Array.Empty<string>(),Array.Empty<string>(),Array.Empty<string>(),
        null,null,null,null,folder);

    [Fact]
    public void Check_WarnsWhenRedirectRootNested_OneLevelTooDeep()
    {
        var tmp = Directory.CreateTempSubdirectory().FullName;
        // 余分な階層の下に CPK が埋もれている
        Directory.CreateDirectory(Path.Combine(tmp, "ExtraFolder", "P5REssentials", "CPK"));

        var warnings = StructureAnalyzer.Check(Mod(tmp));

        Assert.NotEmpty(warnings);
    }

    [Fact]
    public void Check_NoWarning_WhenRootAtTopLevel()
    {
        var tmp = Directory.CreateTempSubdirectory().FullName;
        Directory.CreateDirectory(Path.Combine(tmp, "P5REssentials", "CPK"));

        Assert.Empty(StructureAnalyzer.Check(Mod(tmp)));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter StructureAnalyzerTests`
Expected: FAIL (型未定義)

- [ ] **Step 3: Write minimal implementation**

```csharp
using System.IO;

namespace ReloadedHelper.Core.Analyzers;

public sealed record StructureWarning(string ModId, string Message);

public static class StructureAnalyzer
{
    private static readonly string[] KnownRoots =
        { "P5REssentials", "FEmulator", "BGME", "Costumes" };

    public static IReadOnlyList<StructureWarning> Check(ModInfo mod)
    {
        var warnings = new List<StructureWarning>();
        if (!Directory.Exists(mod.FolderPath)) return warnings;

        var topNames = Directory.GetDirectories(mod.FolderPath)
            .Select(d => Path.GetFileName(d))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        bool hasRootAtTop = KnownRoots.Any(topNames.Contains);
        if (hasRootAtTop) return warnings; // 正常

        // トップに無いが、1階層下に既知ルートが埋もれていないか
        foreach (var sub in Directory.GetDirectories(mod.FolderPath))
            foreach (var root in KnownRoots)
                if (Directory.Exists(Path.Combine(sub, root)))
                {
                    warnings.Add(new StructureWarning(mod.ModId,
                        $"MODの中身が1階層深い場所（{Path.GetFileName(sub)}）に入っています。" +
                        $"このままでは認識されない可能性があります。"));
                    return warnings;
                }
        return warnings;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter StructureAnalyzerTests`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/ReloadedHelper.Core/Analyzers/StructureAnalyzer.cs tests/ReloadedHelper.Core.Tests/Analyzers/StructureAnalyzerTests.cs
git commit -m "feat: StructureAnalyzer(置き場所チェック)"
```

---

## Task 14: ModDiagnostics 拡張（構造警告・曲ID被りの提示）

**Files:**
- Modify: `src/ReloadedHelper.Core/ModDiagnostics.cs`
- Test: `tests/ReloadedHelper.Core.Tests/ModDiagnosticsStructureTests.cs`

**Interfaces:**
- Consumes: 既存 `Analyze(GameInfo, catalog, conflicts)`、`StructureWarning`。
- Produces: 既存 `Analyze` に省略可能引数 `IReadOnlyList<StructureWarning>? structureWarnings = null` を追加（既存呼び出し互換）。構造警告を `Diagnostic(modId, Warning, message)` として追加。曲ID被り（PathKey が `song:` で始まる競合）は既存の競合集約ロジックでそのまま Info 化されるため、メッセージ文言だけ「曲」が分かるよう調整。

- [ ] **Step 1: Write the failing test**

```csharp
using ReloadedHelper.Core;
using ReloadedHelper.Core.Analyzers;
using Xunit;

namespace ReloadedHelper.Core.Tests;

public class ModDiagnosticsStructureTests
{
    private static GameInfo Game() => new("p5r","P5R","",null,
        new[]{"m"}, new[]{"m"}, "");
    private static Dictionary<string, ModInfo> Catalog() => new()
    {
        ["m"] = new("m","M","","1","",Array.Empty<string>(),Array.Empty<string>(),
            Array.Empty<string>(), new[]{"p5r"}, null,null,null,null,""),
    };

    [Fact]
    public void Analyze_IncludesStructureWarnings()
    {
        var warnings = new[] { new StructureWarning("m", "置き場所が変です") };
        var result = ModDiagnostics.Analyze(Game(), Catalog(),
            Array.Empty<FileConflict>(), warnings);

        Assert.Contains(result, d => d.ModId == "m"
            && d.Severity == DiagnosticSeverity.Warning
            && d.Message.Contains("置き場所"));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter ModDiagnosticsStructureTests`
Expected: FAIL (引数の数が違う/型未定義)

- [ ] **Step 3: Write minimal implementation**

`ModDiagnostics.Analyze` のシグネチャと末尾に追加：

```csharp
using ReloadedHelper.Core.Analyzers;

// シグネチャ変更:
public static IReadOnlyList<Diagnostic> Analyze(
    GameInfo game,
    IReadOnlyDictionary<string, ModInfo> catalog,
    IReadOnlyList<FileConflict> conflicts,
    IReadOnlyList<StructureWarning>? structureWarnings = null)
{
    // ...既存処理はそのまま...

    // 競合集約ループの「上書き」メッセージ生成箇所で、PathKey 接頭辞で文言を分岐：
    //   song: → 「同じ曲」/ それ以外 → 「ファイル」
    // （既存ループ内の文言を下記の通り変更）

    // 末尾、return の直前に構造警告を追加：
    if (structureWarnings is not null)
        foreach (var w in structureWarnings)
            result.Add(new Diagnostic(w.ModId, DiagnosticSeverity.Warning, w.Message));

    return result;
}
```

既存の競合 Info 文言生成を、資源種別で分岐する形に変更（`pairCount` ループは競合単位の集計だが、文言は資源種別が混在し得る。ここでは簡潔さ優先で「ファイル」を「項目」に一般化する）：

```csharp
foreach (var (key, count) in pairCount)
    result.Add(new Diagnostic(key.Loser, DiagnosticSeverity.Info,
        $"このMODの {count} 個の項目が「{DisplayName(catalog, key.Winner)}」に上書きされています（読み込み順で後のMODが優先）。意図しない場合は順序を入れ替えてください。"));
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter ModDiagnosticsStructureTests` then `dotnet test`
Expected: PASS（既存診断テストも緑。既存呼び出しは省略引数で互換）

- [ ] **Step 5: Commit**

```bash
git add src/ReloadedHelper.Core/ModDiagnostics.cs tests/ReloadedHelper.Core.Tests/ModDiagnosticsStructureTests.cs
git commit -m "feat: ModDiagnostics に構造警告・資源汎用の上書き提示"
```

---

## Task 15: GameDiagnostics を全アナライザ集約に切替

**Files:**
- Modify: `src/ReloadedHelper.Core/GameDiagnostics.cs`
- Test: `tests/ReloadedHelper.Core.Tests/GameDiagnosticsIntegrationTests.cs`

**Interfaces:**
- Consumes: `ModAnalysis`（Task 7）, `FileReplaceAnalyzer`/`BgmeAnalyzer`/`CostumeAnalyzer`/`StructureAnalyzer`, `ConflictDetector.Detect(IReadOnlyList<ModResources>)`。
- Produces: `GameDiagnostics.Run` を、全アナライザで `ModResources` を作り→ `ConflictDetector` 汎用版＋構造警告を集約する形へ変更。戻り値 `GameDiagnosticsResult` は不変（互換）。

- [ ] **Step 1: Write the failing test**

```csharp
using System.IO;
using ReloadedHelper.Core;
using Xunit;

namespace ReloadedHelper.Core.Tests;

public class GameDiagnosticsIntegrationTests
{
    [Fact]
    public void Run_DetectsFileConflict_AcrossTwoMods()
    {
        var root = Directory.CreateTempSubdirectory().FullName;
        string MakeMod(string id)
        {
            var f = Path.Combine(root, id, "P5REssentials", "CPK");
            Directory.CreateDirectory(f);
            File.WriteAllText(Path.Combine(f, "hair.bin"), "x");
            return Path.Combine(root, id);
        }
        var aFolder = MakeMod("a");
        var bFolder = MakeMod("b");

        ModInfo Info(string id, string folder) => new(id, id, "", "1", "",
            Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(),
            new[] { "p5r" }, null, null, null, null, folder);

        var catalog = new Dictionary<string, ModInfo>
        {
            ["a"] = Info("a", aFolder),
            ["b"] = Info("b", bFolder),
        };
        var game = new GameInfo("p5r", "P5R", "", null,
            new[] { "a", "b" }, new[] { "a", "b" }, "");

        var result = GameDiagnostics.Run(game, catalog);

        Assert.Contains(result.Conflicts, c => c.PathKey.Contains("hair.bin"));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter GameDiagnosticsIntegrationTests`
Expected: 既存実装でも通る可能性あり。**まず実行**し、緑ならこのタスクは「リファクタで緑維持」を確認する位置づけ。赤なら次ステップで実装。

- [ ] **Step 3: Write implementation（全アナライザ集約へ）**

```csharp
using ReloadedHelper.Core.Analyzers;

namespace ReloadedHelper.Core;

public sealed record GameDiagnosticsResult(
    IReadOnlyList<FileConflict> Conflicts, IReadOnlyList<Diagnostic> Diagnostics);

public static class GameDiagnostics
{
    private static IReadOnlyList<IModAnalyzer> Analyzers() => new IModAnalyzer[]
    {
        new FileReplaceAnalyzer(),
        new BgmeAnalyzer(),
        new CostumeAnalyzer(),
    };

    public static GameDiagnosticsResult Run(GameInfo game, IReadOnlyDictionary<string, ModInfo> catalog)
    {
        var enabled = new HashSet<string>(game.EnabledMods, StringComparer.OrdinalIgnoreCase);
        var analysis = new ModAnalysis(Analyzers());

        var ordered = new List<ModResources>();
        var structureWarnings = new List<StructureWarning>();
        foreach (var modId in game.SortedMods)               // 読み込み順
        {
            if (!enabled.Contains(modId)) continue;
            if (!catalog.TryGetValue(modId, out var info)) continue;
            ordered.Add(analysis.Analyze(info));
            structureWarnings.AddRange(StructureAnalyzer.Check(info));
        }

        var conflicts = ConflictDetector.Detect(ordered);
        var diagnostics = ModDiagnostics.Analyze(game, catalog, conflicts, structureWarnings);
        return new GameDiagnosticsResult(conflicts, diagnostics);
    }
}
```

- [ ] **Step 4: Run tests**

Run: `dotnet test`
Expected: PASS（新規統合テスト＋既存全テスト緑）

- [ ] **Step 5: Commit**

```bash
git add src/ReloadedHelper.Core/GameDiagnostics.cs tests/ReloadedHelper.Core.Tests/GameDiagnosticsIntegrationTests.cs
git commit -m "feat: GameDiagnostics を全アナライザ集約＋構造警告に拡張"
```

---

## Task 16: App 配線 — 自動並び替えの発動と履歴/復元UI

**Files:**
- Modify: `src/ReloadedHelper.Core/MainViewModel.cs`（並び替え呼び出し箇所＝有効/無効切替・削除・起動）
- Modify: App 側の診断ウィンドウ（`DiagnosticsWindow`）に「自動配置履歴」リスト＋「元に戻す」ボタン
- Test: `tests/ReloadedHelper.Core.Tests/AutoSortWiringTests.cs`（ViewModel の純ロジック部のみ。UI は目視）

**Interfaces:**
- Consumes: `AutoSortCoordinator`, `LoadOrderOptimizer`, `PreferenceStore`, `ModRoleClassifier`, `GameDiagnostics`, `LoadOrderBackupService`。
- Produces: MainViewModel に `void RunAutoSort(AutoSortTrigger trigger)`。役割辞書・依存辞書・競合を組み立て→ `LoadOrderOptimizer.Optimize`→ `AutoSortCoordinator.Apply`（backup= `LoadOrderBackupService.Backup`、applyOrder= 実際のロードオーダー書き込み）。発動点：起動完了時(Startup)・`ToggleEnabled`(ToggleEnable)・MOD削除(Delete)。

> **既存呼び出し:** D+E 後 `ToggleEnabled → Reload()` がある（メモ参照）。`Reload()` の直後に `RunAutoSort(AutoSortTrigger.ToggleEnable)` を差し込む。`MainViewModel` の既存の並び替え/保存メソッド名は実装時に grep で確認（`Sort(` 周辺）。

- [ ] **Step 1: 既存の並び替え呼び出し箇所を特定**

Run: `grep -n -E "LoadOrderSorter|RunAutoSort|ToggleEnabled|SortedMods|Reload\(" src/ReloadedHelper.Core/MainViewModel.cs`
発動点（ToggleEnabled・削除・起動）と、ロードオーダーを実書き込みする既存メソッドを把握する。

- [ ] **Step 2: Write the failing test（依存・役割辞書の組み立てロジック）**

ViewModel から純粋関数を切り出してテストする。`MainViewModel` に `internal static IReadOnlyDictionary<string, ModRole> BuildRoles(IReadOnlyList<ModLoadEntry> entries, IReadOnlyDictionary<string, ModInfo> catalog)` を追加し、これをテスト：

```csharp
using ReloadedHelper.Core;
using Xunit;

namespace ReloadedHelper.Core.Tests;

public class AutoSortWiringTests
{
    [Fact]
    public void BuildRoles_UsesCategoryAndLibraryFlag()
    {
        var catalog = new Dictionary<string, ModInfo>
        {
            ["skin"] = new("skin","Skin","","1","",Array.Empty<string>(),Array.Empty<string>(),
                Array.Empty<string>(),Array.Empty<string>(),null,null,null,null,""),
            ["lib"] = new("lib","Lib","","1","",Array.Empty<string>(),Array.Empty<string>(),
                Array.Empty<string>(),Array.Empty<string>(),null,null,null,null,"", IsLibrary:true),
        };
        var entries = new[]
        {
            new ModLoadEntry(0, "skin", catalog["skin"], true, "Skin", false),
            new ModLoadEntry(1, "lib", catalog["lib"], true, null, true),
        };

        var roles = MainViewModel.BuildRoles(entries, catalog);

        Assert.Equal(ModRole.VisualOverride, roles["skin"]);
        Assert.Equal(ModRole.Library, roles["lib"]);
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test --filter AutoSortWiringTests`
Expected: FAIL (`BuildRoles` 未定義)

- [ ] **Step 4: Implement BuildRoles と RunAutoSort 配線**

`MainViewModel` に追加：

```csharp
internal static IReadOnlyDictionary<string, ModRole> BuildRoles(
    IReadOnlyList<ModLoadEntry> entries,
    IReadOnlyDictionary<string, ModInfo> catalog)
{
    var roles = new Dictionary<string, ModRole>(StringComparer.OrdinalIgnoreCase);
    foreach (var e in entries)
    {
        var info = e.Info ?? (catalog.TryGetValue(e.ModId, out var ci) ? ci : null);
        if (info is null) { roles[e.ModId] = ModRole.Unknown; continue; }
        roles[e.ModId] = ModRoleClassifier.Classify(info, e.Category);
    }
    return roles;
}
```

`RunAutoSort` は、依存辞書（既存の依存取得経路を流用）・役割・現在順・競合（`GameDiagnostics.Run` の Conflicts）を集めて `LoadOrderOptimizer.Optimize` → `AutoSortCoordinator.Apply`。`applyOrder` は既存のロードオーダー保存メソッドへ、`backup` は `LoadOrderBackupService.Backup(configPath, appId)` へ接続する（具体メソッド名は Step 1 の grep 結果に合わせる）。Coordinator/PreferenceStore の保存先は `%APPDATA%\ReloadedHelper`。

- [ ] **Step 5: 発動点に差し込み＋診断UIに履歴/復元**

- 起動完了処理の末尾に `RunAutoSort(AutoSortTrigger.Startup)`。
- `ToggleEnabled` の `Reload()` 後に `RunAutoSort(AutoSortTrigger.ToggleEnable)`。
- MOD削除処理後に `RunAutoSort(AutoSortTrigger.Delete)`。
- `DiagnosticsWindow` に `AutoSortCoordinator.History` をバインドした「自動配置履歴」セクションと、`LoadOrderBackupService.ListBackups`/`Restore` を使う「元に戻す」ボタンを追加。

- [ ] **Step 6: Run tests + build**

Run: `dotnet test` then `dotnet build reloaded-helper.slnx`
Expected: PASS / ビルド成功

- [ ] **Step 7: Commit**

```bash
git add src/ReloadedHelper.Core/MainViewModel.cs src/ReloadedHelper.App tests/ReloadedHelper.Core.Tests/AutoSortWiringTests.cs
git commit -m "feat: 自動並び替えの発動配線＋診断UIに配置履歴/復元"
```

---

## Task 17: 手動ドラッグを好みとして学習

**Files:**
- Modify: `src/ReloadedHelper.Core/MainViewModel.cs`（手動並び替え確定の箇所）
- Test: `tests/ReloadedHelper.Core.Tests/ManualReorderLearningTests.cs`

**Interfaces:**
- Consumes: `PreferenceStore`, 競合情報（`GameDiagnostics.Run`）。
- Produces: `internal static IReadOnlyList<(string Winner, string Loser)> LearnFromManualOrder(IReadOnlyList<string> newOrder, IReadOnlyList<FileConflict> conflicts)` — 手動で確定した順序を見て、競合ペアについて「後ろに来た方＝勝者」を割り出し、`PreferenceStore.SetWinner` 用のペア列を返す。

- [ ] **Step 1: Write the failing test**

```csharp
using ReloadedHelper.Core;
using Xunit;

namespace ReloadedHelper.Core.Tests;

public class ManualReorderLearningTests
{
    [Fact]
    public void LearnFromManualOrder_LaterModIsWinner()
    {
        var conflicts = new[] { new FileConflict("file:hair", new[] { "a", "b" }, "b") };
        // ユーザーが手で a を後ろにした → a を勝たせたい
        var learned = MainViewModel.LearnFromManualOrder(new[] { "b", "a" }, conflicts);

        Assert.Contains(("a", "b"), learned); // (Winner=a, Loser=b)
    }

    [Fact]
    public void LearnFromManualOrder_IgnoresNonConflicting()
    {
        var learned = MainViewModel.LearnFromManualOrder(
            new[] { "x", "y" }, Array.Empty<FileConflict>());
        Assert.Empty(learned);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter ManualReorderLearningTests`
Expected: FAIL (`LearnFromManualOrder` 未定義)

- [ ] **Step 3: Write implementation**

```csharp
internal static IReadOnlyList<(string Winner, string Loser)> LearnFromManualOrder(
    IReadOnlyList<string> newOrder,
    IReadOnlyList<FileConflict> conflicts)
{
    int Idx(string id) => ((List<string>)new List<string>(newOrder))
        .FindIndex(x => string.Equals(x, id, StringComparison.OrdinalIgnoreCase));

    var learned = new List<(string, string)>();
    foreach (var c in conflicts)
    {
        if (c.ModIds.Count != 2) continue;
        var a = c.ModIds[0];
        var b = c.ModIds[1];
        int ia = Idx(a), ib = Idx(b);
        if (ia < 0 || ib < 0) continue;
        // 後ろ（index大）が勝者
        if (ia > ib) learned.Add((a, b));
        else if (ib > ia) learned.Add((b, a));
    }
    return learned;
}
```

手動並び替え確定箇所で、この戻り値を `prefs.SetWinner(appId, winner, loser, winner)` で記録する配線を追加（発動は手動ドラッグ確定時のみ）。

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter ManualReorderLearningTests`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/ReloadedHelper.Core/MainViewModel.cs tests/ReloadedHelper.Core.Tests/ManualReorderLearningTests.cs
git commit -m "feat: 手動ドラッグを好み(PreferenceStore)として学習"
```

---

## Task 18: 全体結合の実機スモーク＋仕上げ

**Files:**
- なし（検証＋必要なら微修正）

- [ ] **Step 1: 全テスト＋ビルド**

Run: `dotnet test` then `dotnet build reloaded-helper.slnx`
Expected: 全緑・ビルド成功

- [ ] **Step 2: ローカル実機ビルド（自動更新抑止）**

Run: `dotnet publish src/ReloadedHelper.App -r win-x64 -c Release /p:PublishSingleFile=true /p:SelfContained=true -p:Version=9.9.9 -o publish/`
（`-p:Version=9.9.9` で installed 版への自動更新を抑止。メモ [[project-reliability-overhaul]] の方針）

- [ ] **Step 3: 実機スモーク（P5R）**

publish/ のexeを起動し、以下を目視確認：
- 起動時に自動並び替えが走り、診断パネルの「自動配置履歴」に Startup エントリが出る。
- 髪型MODを有効化→ToggleEnable エントリが出て、競合相手より後ろに配置される。
- 「元に戻す」で直前バックアップに戻せる。
- 判断できない競合がある場合の挙動（Unresolved として履歴に出る）を確認。

- [ ] **Step 4: 進捗メモ更新**

`%この会話のメモリ%` の `project-reliability-overhaul.md` または新規 `project-autosort.md` に「自動並び替え精度オーバーホール = 実装完了・実機検証結果」を追記。

- [ ] **Step 5: ブランチ完了**

`superpowers:finishing-a-development-branch` に従い、main へのマージ/PR を判断。

---

## Self-Review メモ（計画作成者による確認）

- **Spec coverage:** アナライザ基盤(T1) / 4アナライザ(T2,10,12,13) / 役割分類(T3) / 決定エンジン(T6) / 好み記憶(T4,17) / 発動&履歴&復元(T8,16) / 診断拡張(T14,15) / 実機形式調査(T9,11) — 仕様の全節に対応タスクあり。
- **未知形式の扱い:** BGME/コスチュームは調査タスク(T9,T11)を実装前に置き、仮形式である旨を実装タスクに明記。捏造を避ける。
- **互換性:** 既存 `ConflictDetector`/`ModDiagnostics`/`GameDiagnostics` はオーバーロード/省略引数で後方互換を維持し、各タスクで `dotnet test` 全緑を確認。
- **型整合:** `ResourceKey`/`ModResources`/`OptimizeResult`/`PlacementReason`/`AutoSortTrigger`/`ModRole` は定義タスクと利用タスクで名称一致を確認済み。`Unresolved` のタプル→`UnresolvedPair` 変換は T8 に明記。
