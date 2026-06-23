# 自動並び替え：種類ベース分類への改訂 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 粗い5役割(ModRole)を、名前・ID・説明文・フォルダ・カテゴリから判定する細かい11種類(ModType)に置き換え、種類ごとに固まった並びにする。

**Architecture:** 新 `ModType`(11種) と `ModTypeClassifier`(キーワード+カテゴリ+フォルダ判定) を追加し、`LoadOrderOptimizer` と `MainViewModel` を ModType ベースへ移行。層別フル整列の枠組みは維持。旧 `ModRole`/`ModRoleClassifier`/`ContentRoleClassifier`/`ModLayer` は撤去。

**Tech Stack:** C# / .NET 10 / WPF。テスト xUnit。ランタイム追加パッケージなし。

## Global Constraints

- ランタイム NuGet パッケージ追加禁止（System.Text.Json のみ可）。xUnit は可。
- ユーザーデータは `%APPDATA%\ReloadedHelper` 以外に保存禁止。
- ロジックは `src/ReloadedHelper.Core/`、UI は `src/ReloadedHelper.App/`、テストは `tests/ReloadedHelper.Core.Tests/`。
- ビルド `dotnet build reloaded-helper.slnx` / テスト `dotnet test`。
- ユーザー向け文字列はすべて日本語。
- 既存挙動維持：書込前に必ずバックアップ、順序が変わらなければ書込・履歴は作らない（配置理由は常に保存・反映）。
- 有効/無効は分けない（混在のまま）。

## File Structure

- `src/ReloadedHelper.Core/ModType.cs`（新規）— `ModType` enum ＋ `ModTypeInfo.Rank/Label`。
- `src/ReloadedHelper.Core/ModTypeClassifier.cs`（新規）— `TypeDecision` ＋ `ModTypeClassifier.Classify`。
- `src/ReloadedHelper.Core/LoadOrderOptimizer.cs`（変更）— ModRole→ModType。
- `src/ReloadedHelper.Core/MainViewModel.cs`（変更）— ModType 配線。
- 撤去：`ModRoleClassifier.cs`(ModRole定義含む)・`ContentRoleClassifier.cs`(RoleDecision定義含む)・`ModLayer.cs` と各テスト。
- テスト：`ModTypeTests.cs`・`ModTypeClassifierTests.cs`（新規）、`LoadOrderOptimizerTests.cs`・`AutoSortWiringTests.cs`（書き換え）。

---

### Task 1: ModType 種類とランク・ラベル

**Files:**
- Create: `src/ReloadedHelper.Core/ModType.cs`
- Test: `tests/ReloadedHelper.Core.Tests/ModTypeTests.cs`

**Interfaces:**
- Produces: `enum ModType { Library, Gameplay, Battle, Event, Music, Model, Costume, SkinTexture, Portrait, Ui, Unknown }`、`int ModTypeInfo.Rank(ModType)`、`string ModTypeInfo.Label(ModType)`。

- [ ] **Step 1: Write the failing test**

```csharp
// tests/ReloadedHelper.Core.Tests/ModTypeTests.cs
using ReloadedHelper.Core;
using Xunit;

namespace ReloadedHelper.Core.Tests;

public class ModTypeTests
{
    [Theory]
    [InlineData(ModType.Library, 0)]
    [InlineData(ModType.Gameplay, 1)]
    [InlineData(ModType.Battle, 2)]
    [InlineData(ModType.Event, 3)]
    [InlineData(ModType.Music, 4)]
    [InlineData(ModType.Model, 5)]
    [InlineData(ModType.Costume, 6)]
    [InlineData(ModType.SkinTexture, 7)]
    [InlineData(ModType.Portrait, 8)]
    [InlineData(ModType.Ui, 9)]
    [InlineData(ModType.Unknown, 10)]
    public void Rank_FrontToBack(ModType t, int expected)
        => Assert.Equal(expected, ModTypeInfo.Rank(t));

    [Fact]
    public void Label_IsNonEmptyForAll()
    {
        foreach (ModType t in System.Enum.GetValues<ModType>())
            Assert.False(string.IsNullOrWhiteSpace(ModTypeInfo.Label(t)));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter ModTypeTests`
Expected: FAIL — `ModType`/`ModTypeInfo` 未定義（コンパイルエラー）。

- [ ] **Step 3: Write minimal implementation**

```csharp
// src/ReloadedHelper.Core/ModType.cs
namespace ReloadedHelper.Core;

// MODの種類。ランクが小さいほどリスト前方（土台）・大きいほど後方（見た目の上書き）。
public enum ModType
{
    Library, Gameplay, Battle, Event, Music, Model, Costume, SkinTexture, Portrait, Ui, Unknown
}

public static class ModTypeInfo
{
    public static int Rank(ModType t) => t switch
    {
        ModType.Library => 0,
        ModType.Gameplay => 1,
        ModType.Battle => 2,
        ModType.Event => 3,
        ModType.Music => 4,
        ModType.Model => 5,
        ModType.Costume => 6,
        ModType.SkinTexture => 7,
        ModType.Portrait => 8,
        ModType.Ui => 9,
        _ => 10, // Unknown
    };

    public static string Label(ModType t) => t switch
    {
        ModType.Library => "ライブラリ/前提",
        ModType.Gameplay => "ゲーム機能・修正",
        ModType.Battle => "戦闘・ボス",
        ModType.Event => "イベント・ストーリー",
        ModType.Music => "音楽・BGM",
        ModType.Model => "モデル",
        ModType.Costume => "衣装",
        ModType.SkinTexture => "スキン・テクスチャ",
        ModType.Portrait => "立ち絵",
        ModType.Ui => "UI",
        _ => "判定不能",
    };
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter ModTypeTests`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/ReloadedHelper.Core/ModType.cs tests/ReloadedHelper.Core.Tests/ModTypeTests.cs
git commit -m "feat: 種類タクソノミ ModType とランク・ラベルを追加"
```

---

### Task 2: ModTypeClassifier（種類判定）

**Files:**
- Create: `src/ReloadedHelper.Core/ModTypeClassifier.cs`
- Test: `tests/ReloadedHelper.Core.Tests/ModTypeClassifierTests.cs`

**Interfaces:**
- Consumes: `ModInfo`、`ModType`/`ModTypeInfo.Label`（Task 1）、`ModContentScanner.Scan`。
- Produces: `record TypeDecision(ModType Type, string Reason)`、`TypeDecision ModTypeClassifier.Classify(ModInfo mod, string? category)`。

判定優先順（上から最初に当たったもの）:
1. `IsLibrary` → Library
2. フォルダ `BGME/` → Music、`Costumes/` → Costume
3. 特定カテゴリ（CategoryMap）→ 対応種類
4. キーワード（ModId+ModName+ModDescription、優先順 Music→Portrait→Costume→SkinTexture→Model→Ui→Battle→Event→Gameplay）
5. カテゴリ Characters/Character → Model
6. 曖昧カテゴリ（Other/Misc・QOL・Fixes・Cheats・Events・Bomb/Defuse）→ Gameplay
7. ファイル上書きあり（ModContentScanner の Paths>0）→ SkinTexture
8. それ以外 → Unknown

- [ ] **Step 1: Write the failing test**

```csharp
// tests/ReloadedHelper.Core.Tests/ModTypeClassifierTests.cs
using System.IO;
using ReloadedHelper.Core;
using Xunit;

namespace ReloadedHelper.Core.Tests;

public class ModTypeClassifierTests
{
    private static ModInfo Mod(string id, string name = "", string desc = "",
        string folder = "", bool lib = false) => new(
        id, name, "", "1.0", desc, System.Array.Empty<string>(), System.Array.Empty<string>(),
        System.Array.Empty<string>(), System.Array.Empty<string>(), null, null, null, null, folder, lib);

    private static string EmptyDir()
    {
        var d = Path.Combine(Path.GetTempPath(), $"mtc-{System.Guid.NewGuid():N}");
        Directory.CreateDirectory(d);
        return d;
    }

    private static string DirWith(string sub)
    {
        var d = EmptyDir();
        Directory.CreateDirectory(Path.Combine(d, sub.Replace('/', Path.DirectorySeparatorChar)));
        return d;
    }

    [Fact]
    public void Library_Wins()
        => Assert.Equal(ModType.Library, ModTypeClassifier.Classify(Mod("x", lib: true), "Skins").Type);

    [Fact]
    public void BgmeFolder_IsMusic()
    {
        var d = DirWith("BGME");
        try { Assert.Equal(ModType.Music, ModTypeClassifier.Classify(Mod("x", folder: d), null).Type); }
        finally { Directory.Delete(d, true); }
    }

    [Fact]
    public void CostumesFolder_IsCostume()
    {
        var d = DirWith("Costumes");
        try { Assert.Equal(ModType.Costume, ModTypeClassifier.Classify(Mod("x", folder: d), null).Type); }
        finally { Directory.Delete(d, true); }
    }

    [Theory]
    [InlineData("Skins", ModType.SkinTexture)]
    [InlineData("Textures", ModType.SkinTexture)]
    [InlineData("Portraits", ModType.Portrait)]
    [InlineData("Models", ModType.Model)]
    [InlineData("Personas", ModType.Model)]
    [InlineData("User Interface", ModType.Ui)]
    [InlineData("Battles", ModType.Battle)]
    [InlineData("Cutscenes / FMV", ModType.Event)]
    public void SpecificCategory_Maps(string cat, ModType expected)
    {
        var d = EmptyDir();
        try { Assert.Equal(expected, ModTypeClassifier.Classify(Mod("x", folder: d), cat).Type); }
        finally { Directory.Delete(d, true); }
    }

    [Theory]
    [InlineData("p5rpc.music.foundalight", "光を見つけた", ModType.Music)]
    [InlineData("x", "戦闘BGM差し替え", ModType.Music)]
    [InlineData("p5rpc.cheats.darts", "チート", ModType.Gameplay)]
    [InlineData("x", "Black Mask Boss Animations", ModType.Battle)]
    [InlineData("x", "PS4 cutscenes restored", ModType.Event)]
    [InlineData("p5rpc.skin.longhairann", "ロングヘアのアン", ModType.SkinTexture)]
    public void Keywords_Classify(string id, string name, ModType expected)
    {
        var d = EmptyDir();
        try { Assert.Equal(expected, ModTypeClassifier.Classify(Mod(id, name, folder: d), null).Type); }
        finally { Directory.Delete(d, true); }
    }

    [Fact]
    public void VagueCategory_IsGameplay()
    {
        var d = EmptyDir();
        try { Assert.Equal(ModType.Gameplay, ModTypeClassifier.Classify(Mod("x", folder: d), "Other/Misc").Type); }
        finally { Directory.Delete(d, true); }
    }

    [Fact]
    public void CharactersCategory_IsModel()
    {
        var d = EmptyDir();
        try { Assert.Equal(ModType.Model, ModTypeClassifier.Classify(Mod("x", folder: d), "Characters").Type); }
        finally { Directory.Delete(d, true); }
    }

    [Fact]
    public void FileReplaceOnly_IsSkinTexture()
    {
        var d = EmptyDir();
        var f = Path.Combine(d, "P5REssentials", "CPK", "BASE.CPK", "t.dds");
        Directory.CreateDirectory(Path.GetDirectoryName(f)!);
        File.WriteAllText(f, "x");
        try { Assert.Equal(ModType.SkinTexture, ModTypeClassifier.Classify(Mod("x", folder: d), null).Type); }
        finally { Directory.Delete(d, true); }
    }

    [Fact]
    public void NoSignal_IsUnknown()
    {
        var d = EmptyDir();
        try
        {
            var r = ModTypeClassifier.Classify(Mod("x", folder: d), null);
            Assert.Equal(ModType.Unknown, r.Type);
            Assert.False(string.IsNullOrWhiteSpace(r.Reason));
        }
        finally { Directory.Delete(d, true); }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter ModTypeClassifierTests`
Expected: FAIL — `ModTypeClassifier`/`TypeDecision` 未定義。

- [ ] **Step 3: Write minimal implementation**

```csharp
// src/ReloadedHelper.Core/ModTypeClassifier.cs
using System.IO;

namespace ReloadedHelper.Core;

public sealed record TypeDecision(ModType Type, string Reason);

// MODの種類を、フォルダ・カテゴリ・名前/説明のキーワードから判定する。
// GameBananaカテゴリは欠落しがちなので、名前・IDの語からも種類を拾う。
public static class ModTypeClassifier
{
    // 名前・説明から種類を拾うキーワード。優先順（最初に当たったものを採用）。
    private static readonly (ModType Type, string[] Keywords)[] KeywordRules =
    {
        (ModType.Music, new[] { "music", "bgm", "sound", "ost", "soundtrack", "song", "サウンド", "音楽" }),
        (ModType.Portrait, new[] { "portrait", "bustup", "立ち絵", "ポートレート", "バストアップ" }),
        (ModType.Costume, new[] { "costume", "outfit", "衣装", "コスチューム", "swimsuit", "水着" }),
        (ModType.SkinTexture, new[] { "skin", "texture", "スキン", "テクスチャ", "retexture", "recolor", "リテクスチャ" }),
        (ModType.Model, new[] { "model", "モデル", "mesh" }),
        (ModType.Ui, new[] { "interface", "hud", "カットイン", "cutin", "cut-in", "dualsense" }),
        (ModType.Battle, new[] { "battle", "boss", "bossfight", "戦闘", "ボス", "encounter" }),
        (ModType.Event, new[] { "cutscene", "fmv", "story", "event", "イベント", "ストーリー", "カットシーン" }),
        (ModType.Gameplay, new[] { "cheat", "fix", "patch", "qol", "tweak", "disable", "difficulty", "修正", "パッチ", "チート" }),
    };

    // 信頼できる特定カテゴリ → 種類。
    private static readonly Dictionary<string, ModType> CategoryMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["skins"] = ModType.SkinTexture, ["skin"] = ModType.SkinTexture,
        ["textures"] = ModType.SkinTexture, ["texture"] = ModType.SkinTexture, ["texture packs"] = ModType.SkinTexture,
        ["portraits"] = ModType.Portrait, ["portrait"] = ModType.Portrait,
        ["models"] = ModType.Model, ["model packs"] = ModType.Model, ["personas"] = ModType.Model, ["persona"] = ModType.Model,
        ["user interface"] = ModType.Ui, ["ui"] = ModType.Ui,
        ["battles"] = ModType.Battle,
        ["cutscenes / fmv"] = ModType.Event, ["cutscenes"] = ModType.Event,
        ["sound"] = ModType.Music, ["music"] = ModType.Music,
    };

    // 役割がぼやけたカテゴリ → ゲーム機能（土台寄り）。
    private static readonly HashSet<string> VagueGameplay = new(StringComparer.OrdinalIgnoreCase)
    {
        "other/misc", "qol", "fixes", "cheats", "events", "bomb/defuse",
    };

    public static TypeDecision Classify(ModInfo mod, string? category)
    {
        if (mod.IsLibrary)
            return new(ModType.Library, "ライブラリ指定のため前方に配置");

        var folder = mod.FolderPath;
        if (HasSubdir(folder, "BGME"))
            return new(ModType.Music, "BGMEフォルダがあるため音楽として配置");
        if (HasSubdir(folder, "Costumes"))
            return new(ModType.Costume, "衣装フォルダがあるため衣装として配置");

        var cat = category?.Trim();
        if (!string.IsNullOrEmpty(cat) && CategoryMap.TryGetValue(cat, out var byCat))
            return new(byCat, $"カテゴリ「{cat}」のため{ModTypeInfo.Label(byCat)}に配置");

        var blob = $"{mod.ModId} {mod.ModName} {mod.ModDescription}".ToLowerInvariant();
        foreach (var (type, kws) in KeywordRules)
            if (kws.Any(k => blob.Contains(k)))
                return new(type, $"名前・説明から{ModTypeInfo.Label(type)}と判定");

        if (!string.IsNullOrEmpty(cat) &&
            (cat.Equals("Characters", StringComparison.OrdinalIgnoreCase) ||
             cat.Equals("Character", StringComparison.OrdinalIgnoreCase)))
            return new(ModType.Model, "カテゴリ「Characters」のためモデルに配置");

        if (!string.IsNullOrEmpty(cat) && VagueGameplay.Contains(cat))
            return new(ModType.Gameplay, $"カテゴリ「{cat}」のためゲーム機能に配置");

        if (ModContentScanner.Scan(folder, mod.ModId).Paths.Count > 0)
            return new(ModType.SkinTexture, "ゲームファイルを上書きするため見た目として配置");

        return new(ModType.Unknown, "手がかりがないため末尾に配置");
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

Run: `dotnet test --filter ModTypeClassifierTests`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/ReloadedHelper.Core/ModTypeClassifier.cs tests/ReloadedHelper.Core.Tests/ModTypeClassifierTests.cs
git commit -m "feat: 種類判定 ModTypeClassifier を追加"
```

---

### Task 3: LoadOrderOptimizer と MainViewModel を ModType へ移行

ModRole→ModType の署名変更は呼び出し側も同時に直さないとビルドが通らないため、最適化器・ビューモデル・両テストを1タスクでまとめて移行する。

**Files:**
- Modify: `src/ReloadedHelper.Core/LoadOrderOptimizer.cs`
- Modify: `src/ReloadedHelper.Core/MainViewModel.cs`
- Modify: `tests/ReloadedHelper.Core.Tests/LoadOrderOptimizerTests.cs`
- Modify: `tests/ReloadedHelper.Core.Tests/AutoSortWiringTests.cs`

**Interfaces:**
- Consumes: `ModType`/`ModTypeInfo`（Task 1）、`TypeDecision`/`ModTypeClassifier.Classify`（Task 2）。
- Produces:
  - `Optimize(..., IReadOnlyDictionary<string, ModType> typesByMod, PreferenceStore prefs, IReadOnlyDictionary<string, string>? typeReasons = null)`
  - `MainViewModel.BuildTypeDecisions(entries, catalog) -> IReadOnlyDictionary<string, TypeDecision>`（internal static）

- [ ] **Step 1: Rewrite the optimizer tests to ModType（まず失敗させる）**

`tests/ReloadedHelper.Core.Tests/LoadOrderOptimizerTests.cs` を全置換:

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
```

- [ ] **Step 2: Rewrite AutoSortWiringTests to ModType**

`tests/ReloadedHelper.Core.Tests/AutoSortWiringTests.cs` を全置換:

```csharp
using ReloadedHelper.Core;
using Xunit;

namespace ReloadedHelper.Core.Tests;

public class AutoSortWiringTests
{
    [Fact]
    public void BuildTypeDecisions_GivesTypeAndReason()
    {
        var catalog = new Dictionary<string, ModInfo>
        {
            ["lib"] = new("lib", "Lib", "", "1", "", System.Array.Empty<string>(), System.Array.Empty<string>(),
                System.Array.Empty<string>(), System.Array.Empty<string>(), null, null, null, null, "", IsLibrary: true),
        };
        var entries = new[] { new ModLoadEntry(0, "lib", catalog["lib"], true, null, true) };

        var decisions = MainViewModel.BuildTypeDecisions(entries, catalog);

        Assert.Equal(ModType.Library, decisions["lib"].Type);
        Assert.False(string.IsNullOrWhiteSpace(decisions["lib"].Reason));
    }

    [Fact]
    public void BuildTypeDecisions_NoInfo_IsUnknown()
    {
        var catalog = new Dictionary<string, ModInfo>();
        var entries = new[] { new ModLoadEntry(0, "ghost", null, true, null, false) };

        var decisions = MainViewModel.BuildTypeDecisions(entries, catalog);

        Assert.Equal(ModType.Unknown, decisions["ghost"].Type);
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test --filter "LoadOrderOptimizerTests|AutoSortWiringTests"`
Expected: FAIL — `Optimize` が ModType 辞書を受け付けない／`BuildTypeDecisions` 未定義（コンパイルエラー）。

- [ ] **Step 4: Migrate LoadOrderOptimizer**

`src/ReloadedHelper.Core/LoadOrderOptimizer.cs` の `Optimize` 署名を変更:

```csharp
    public static OptimizeResult Optimize(
        string appId,
        IReadOnlyList<string> currentOrder,
        IReadOnlyDictionary<string, IReadOnlyList<string>> dependenciesOf,
        IReadOnlyList<FileConflict> conflicts,
        IReadOnlyDictionary<string, ModType> typesByMod,
        PreferenceStore prefs,
        IReadOnlyDictionary<string, string>? typeReasons = null)
    {
```

層整列の rank 取得を差し替え:

```csharp
        // 0. 種類ランクで全MODを安定整列（弱い土台が前、見た目が後ろ）。同種は元順を維持。
        var layered = currentOrder
            .Select((id, i) => (id, i, rank: ModTypeInfo.Rank(typesByMod.GetValueOrDefault(id, ModType.Unknown))))
            .OrderBy(x => x.rank).ThenBy(x => x.i)
            .Select(x => x.id)
            .ToList();
```

勝者決定の呼び出しを `DecideByType` に変更（2か所）:

```csharp
            string? winner = c.ModIds.Count == 2
                ? prefs.GetWinner(appId, c.ModIds[0], c.ModIds[1]) ?? DecideByType(c.ModIds, typesByMod)
                : DecideByType(c.ModIds, typesByMod);
```

placements の組み立てを差し替え:

```csharp
        var placements = order.Select(id =>
        {
            var type = typesByMod.GetValueOrDefault(id, ModType.Unknown);
            var reason = typeReasons != null && typeReasons.TryGetValue(id, out var r) && !string.IsNullOrWhiteSpace(r)
                ? r
                : $"{ModTypeInfo.Label(type)}として配置";
            return new ModPlacement(id, ModTypeInfo.Rank(type), ModTypeInfo.Label(type), reason);
        }).ToList();
```

`DecideByRole` メソッド全体を `DecideByType` に置換:

```csharp
    // 競合する全MODのうち、種類ランクが最大で一意なものを勝者に（後ろ＝勝ち）。
    // 同点・最大が Unknown なら自動判断しない（＝要確認）。
    private static string? DecideByType(IReadOnlyList<string> mods, IReadOnlyDictionary<string, ModType> types)
    {
        var ranked = mods
            .Select(m => (mod: m, type: types.GetValueOrDefault(m, ModType.Unknown)))
            .Select(x => (x.mod, x.type, rank: ModTypeInfo.Rank(x.type)))
            .OrderByDescending(x => x.rank)
            .ToList();

        var top = ranked[0];
        if (top.type == ModType.Unknown) return null;
        if (ranked.Count(x => x.rank == top.rank) > 1) return null;
        return top.mod;
    }
```

- [ ] **Step 5: Migrate MainViewModel**

`src/ReloadedHelper.Core/MainViewModel.cs`:

(a) `RunAutoSort` 内、`decisions`/`roles`/`roleReasons` の3行を置き換え:

```csharp
        var decisions = BuildTypeDecisions(_allEntries, _catalog);
        var types = decisions.ToDictionary(kv => kv.Key, kv => kv.Value.Type, StringComparer.OrdinalIgnoreCase);
        var typeReasons = decisions.ToDictionary(kv => kv.Key, kv => kv.Value.Reason, StringComparer.OrdinalIgnoreCase);
```

(b) `Optimize` 呼び出しの引数 `roles, _prefs, roleReasons` を `types, _prefs, typeReasons` に変更:

```csharp
        var result = LoadOrderOptimizer.Optimize(appId, game.SortedMods, depMap, diagResult.Conflicts, types, _prefs, typeReasons);
```

(c) 末尾の `BuildRoleDecisions` と `BuildRoles` の2メソッドを、次の1メソッドに置き換え:

```csharp
    internal static IReadOnlyDictionary<string, TypeDecision> BuildTypeDecisions(
        IReadOnlyList<ModLoadEntry> entries,
        IReadOnlyDictionary<string, ModInfo> catalog)
    {
        var map = new Dictionary<string, TypeDecision>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in entries)
        {
            var info = e.Info ?? (catalog.TryGetValue(e.ModId, out var ci) ? ci : null);
            map[e.ModId] = info is null
                ? new TypeDecision(ModType.Unknown, "情報が無いため末尾に配置")
                : ModTypeClassifier.Classify(info, e.Category);
        }
        return map;
    }
```

- [ ] **Step 6: Run focused tests, then full suite**

Run: `dotnet test --filter "LoadOrderOptimizerTests|AutoSortWiringTests"`
Expected: PASS
Run: `dotnet test`
Expected: 旧 ModRole 系テスト（ModRoleClassifierTests/ContentRoleClassifierTests/ModLayerTests）はまだ存在し PASS。新規・移行分も PASS。全体 PASS。

- [ ] **Step 7: Commit**

```bash
git add src/ReloadedHelper.Core/LoadOrderOptimizer.cs src/ReloadedHelper.Core/MainViewModel.cs tests/ReloadedHelper.Core.Tests/LoadOrderOptimizerTests.cs tests/ReloadedHelper.Core.Tests/AutoSortWiringTests.cs
git commit -m "feat: 並び替えを ModType ベースに移行（optimizer/viewmodel）"
```

---

### Task 4: 旧 ModRole 系の撤去

**Files:**
- Delete: `src/ReloadedHelper.Core/ModRoleClassifier.cs`（`ModRole` enum 定義含む）
- Delete: `src/ReloadedHelper.Core/ContentRoleClassifier.cs`（`RoleDecision` 定義含む）
- Delete: `src/ReloadedHelper.Core/ModLayer.cs`
- Delete: `tests/ReloadedHelper.Core.Tests/ModRoleClassifierTests.cs`
- Delete: `tests/ReloadedHelper.Core.Tests/ContentRoleClassifierTests.cs`
- Delete: `tests/ReloadedHelper.Core.Tests/ModLayerTests.cs`

**Interfaces:**
- これらは Task 3 完了後どこからも参照されない（参照は autosort クラスタ内に限定。Task 3 で全移行済み）。

- [ ] **Step 1: Confirm no remaining references**

Run: `git grep -n "ModRole\|ModLayer\|ModRoleClassifier\|ContentRoleClassifier\|RoleDecision\|BuildRoles\|BuildRoleDecisions" -- src tests`
Expected: 出力なし（docs は対象外）。もし `src`/`tests` に残っていれば、それは Task 3 の移行漏れ。先に直す。

- [ ] **Step 2: Delete the orphaned files**

```bash
git rm src/ReloadedHelper.Core/ModRoleClassifier.cs \
       src/ReloadedHelper.Core/ContentRoleClassifier.cs \
       src/ReloadedHelper.Core/ModLayer.cs \
       tests/ReloadedHelper.Core.Tests/ModRoleClassifierTests.cs \
       tests/ReloadedHelper.Core.Tests/ContentRoleClassifierTests.cs \
       tests/ReloadedHelper.Core.Tests/ModLayerTests.cs
```

- [ ] **Step 3: Build and run full suite**

Run: `dotnet build reloaded-helper.slnx`
Expected: 0 エラー。
Run: `dotnet test`
Expected: 全 PASS（ModType 系・既存の非 autosort テスト含む）。

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "refactor: 旧 ModRole/ModLayer 系を撤去（ModType へ一本化）"
```

---

### Task 5: 実データ受け入れ検証（コントローラー実施・手動）

**Files:** なし（検証のみ）

このタスクはサブエージェントではなくコントローラー（あなた）が実施する。v1 の失敗（未検証のまま実機へ）を繰り返さないための必須ゲート。

- [ ] **Step 1: Full suite + release build**

Run: `dotnet test` → 全 PASS、`dotnet build reloaded-helper.slnx -c Release` → 0 エラー。

- [ ] **Step 2: 実データ・シミュレーション**

ユーザー実機の `C:\FreeSoft\Reloaded-II\Apps\p5r.exe\AppConfig.json`(217 MOD) と `%APPDATA%\ReloadedHelper\userdata.json`(カテゴリ) に対し、新 `ModTypeClassifier` 相当の判定を流し、`ModType` 分布を集計する。受け入れ基準：**Unknown ≤ 10%**、各種類が固まって並ぶこと。サンプルの並びを目視し、明らかな誤判定（フレームワークが見た目に入る等）がないこと。

- [ ] **Step 3: 実機起動確認**

App を起動（バージョン 0.9.0 で更新ポップアップは出ない）し、MOD一覧が種類ごとに整列され、各MODに種類ベースの理由が出ることを目視。ユーザーに確認を依頼する。

- [ ] **Step 4: 結果を記録**

`.superpowers/sdd/progress.md` に結果（Unknown率・誤判定の有無・ユーザー確認結果）を1行で追記。

---

## Self-Review

**1. Spec coverage（v2 spec 各節）:**
- §3 種類とレイヤー順 → Task 1（ModType/Rank/Label）。
- §4 分類アルゴリズム（IsLibrary/フォルダ/特定カテゴリ/キーワード/Characters/曖昧カテゴリ/ファイル上書き/Unknown）→ Task 2（全分岐をテスト）。
- §5 整列とデータフロー（ModType ランクで層整列・DecideByType・Placements・VM 配線）→ Task 3。
- §5 旧分類器の統合・置換 → Task 4（撤去）。
- §6 受け入れ検証（ユニット＋実データ＋非回帰）→ Task 2/3 のユニット、Task 5 の実データ・実機。
- §2 非ゴール（オンライン取得・プレビュー・他ゲーム）→ 着手しない。
- 既存挙動維持（バックアップ・変更なし時スキップ・配置理由常時保存）→ Task 3 は RunAutoSort のその部分を変更しない。

**2. Placeholder scan:** TBD/TODO なし。各コードステップに実コードあり。

**3. Type consistency:** `ModType`(11値)、`ModTypeInfo.Rank/Label`、`TypeDecision(Type,Reason)`、`Optimize(..., typesByMod, prefs, typeReasons=null)`、`DecideByType`、`BuildTypeDecisions` — Task 1〜4 で一致。`ModPlacement`/`OptimizeResult` のフィールド名（LayerRank/LayerLabel）は据え置き（中身は ModType ランク/ラベル）で、Task 3 のテストもその名で参照。
