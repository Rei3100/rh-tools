# Phase 5 MOD管理強化 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** IsLibrary バグ修正・フレームワーク強制有効化・個別編集/削除ダイアログ・複数選択更新・強制再取得・トレイ配色修正を実装する。

**Architecture:** Core（ロジック層）の変更でIsLibraryを読み取り、フレームワーク系MODを常時有効化する。App層に `ModEditWindow`（新規WPFウィンドウ）と `RecycleBinHelper`（Win32 P/Invoke）を追加し、ModListViewを複数選択・右クリック対応に拡張する。

**Tech Stack:** C# / .NET 10 / WPF / System.Text.Json / xUnit / Win32 P/Invoke (shell32.dll)

## Global Constraints

- ランタイム NuGet パッケージ追加禁止（System.Text.Json のみ可）
- ユーザーデータは `%APPDATA%\ReloadedHelper` 以外に保存禁止
- テストは `dotnet test`、ビルドは `dotnet build reloaded-helper.slnx` で確認

---

## File Map

| ファイル | 変更種別 | 内容 |
|---------|---------|------|
| `src/ReloadedHelper.Core/Models.cs` | 変更 | `ModInfo.IsLibrary` / `ModLoadEntry.IsLibrary` 追加、`CategoryLabel` 更新 |
| `src/ReloadedHelper.Core/ModConfigParser.cs` | 変更 | `IsLibrary` フィールド読み取り |
| `src/ReloadedHelper.Core/LoadOrder.cs` | 変更 | `Build()` で IsLibrary → 常時 Enabled |
| `src/ReloadedHelper.Core/MainViewModel.cs` | 変更 | `ToggleEnabled` ブロック / `Reload()` / `RefreshSelectedAction` |
| `src/ReloadedHelper.Core/AppConfigWriter.cs` | 変更 | `RemoveMod()` 追加 |
| `src/ReloadedHelper.App/App.xaml.cs` | 変更 | `RefreshModMetadataAsync` 引数追加・バージョンチェック削除、`ApplySortAllGames` IsLibrary 対応 |
| `src/ReloadedHelper.App/MainWindow.xaml` | 変更 | トレイ ContextMenu 配色 |
| `src/ReloadedHelper.App/RecycleBinHelper.cs` | 新規 | ゴミ箱送り P/Invoke |
| `src/ReloadedHelper.App/Views/ModEditWindow.xaml` | 新規 | 個別編集ダイアログ XAML |
| `src/ReloadedHelper.App/Views/ModEditWindow.xaml.cs` | 新規 | 個別編集ダイアログ ロジック |
| `src/ReloadedHelper.App/Views/ModListView.xaml` | 変更 | 「…」列、右クリックメニュー、複数選択、「選択中を更新」、IsLibrary トグル非表示 |
| `src/ReloadedHelper.App/Views/ModListView.xaml.cs` | 変更 | 新ハンドラ群 |
| `tests/.../ModConfigParserIsLibraryTests.cs` | 新規 | IsLibrary 読み取りテスト |
| `tests/.../LoadOrderBuilderIsLibraryTests.cs` | 新規 | IsLibrary 強制有効テスト |
| `tests/.../AppConfigWriterRemoveModTests.cs` | 新規 | RemoveMod テスト |

---

### Task 1: IsLibrary — Core モデルとパーサー

**Files:**
- Modify: `src/ReloadedHelper.Core/Models.cs`
- Modify: `src/ReloadedHelper.Core/ModConfigParser.cs`
- Create: `tests/ReloadedHelper.Core.Tests/ModConfigParserIsLibraryTests.cs`

**Interfaces:**
- Produces: `ModInfo.IsLibrary bool`、`ModLoadEntry.IsLibrary bool`、`ModLoadEntry.CategoryLabel` が IsLibrary 時 `"フレームワーク"` を返す

---

- [ ] **Step 1: テストファイルを作成（まだ失敗する）**

```csharp
// tests/ReloadedHelper.Core.Tests/ModConfigParserIsLibraryTests.cs
namespace ReloadedHelper.Core.Tests;

public class ModConfigParserIsLibraryTests
{
    [Fact]
    public void Parse_WhenIsLibraryTrue_ReturnsTrue()
    {
        const string json = """
            {
                "ModId": "lib.mod", "ModName": "Lib", "ModAuthor": "A",
                "ModVersion": "1.0", "ModDescription": "Desc",
                "IsLibrary": true
            }
            """;
        var result = ModConfigParser.Parse(json, @"C:\mods\lib");
        Assert.True(result.IsLibrary);
    }

    [Fact]
    public void Parse_WhenIsLibraryMissing_ReturnsFalse()
    {
        const string json = """
            { "ModId": "normal.mod", "ModName": "Normal" }
            """;
        var result = ModConfigParser.Parse(json, @"C:\mods\normal");
        Assert.False(result.IsLibrary);
    }

    [Fact]
    public void Parse_WhenIsLibraryFalse_ReturnsFalse()
    {
        const string json = """
            { "ModId": "normal.mod", "ModName": "Normal", "IsLibrary": false }
            """;
        var result = ModConfigParser.Parse(json, @"C:\mods\normal");
        Assert.False(result.IsLibrary);
    }

    [Fact]
    public void ModLoadEntry_WhenIsLibraryTrue_CategoryLabelIsFramework()
    {
        var entry = new ModLoadEntry(1, "lib.mod", null, true, "Sound", IsLibrary: true);
        Assert.Equal("フレームワーク", entry.CategoryLabel);
    }

    [Fact]
    public void ModLoadEntry_WhenIsLibraryFalse_CategoryLabelUsesCategory()
    {
        var entry = new ModLoadEntry(1, "sound.mod", null, true, "Sound", IsLibrary: false);
        Assert.Equal("サウンド", entry.CategoryLabel);
    }

    [Fact]
    public void ModLoadEntry_WhenIsLibraryFalse_NullCategory_CategoryLabelIsNull()
    {
        var entry = new ModLoadEntry(1, "mod", null, true, null, IsLibrary: false);
        Assert.Null(entry.CategoryLabel);
    }
}
```

- [ ] **Step 2: テストが失敗することを確認**

```
dotnet test tests/ReloadedHelper.Core.Tests --filter "ModConfigParserIsLibraryTests"
```

`ModInfo` に `IsLibrary` が存在しないためコンパイルエラーになることを確認する。

- [ ] **Step 3: `Models.cs` を変更 — `ModInfo` と `ModLoadEntry` に `IsLibrary` 追加**

```csharp
// src/ReloadedHelper.Core/Models.cs
using System.IO;

namespace ReloadedHelper.Core;

public sealed record ModInfo(
    string ModId,
    string ModName,
    string ModAuthor,
    string ModVersion,
    string ModDescription,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> Dependencies,
    IReadOnlyList<string> OptionalDependencies,
    IReadOnlyList<string> SupportedAppIds,
    string? ProjectUrl,
    string? GitHubUserName,
    string? GitHubRepositoryName,
    string? IconFileName,
    string FolderPath,
    bool IsLibrary = false)
{
    public string? IconPath =>
        string.IsNullOrEmpty(IconFileName) ? null : Path.Combine(FolderPath, IconFileName);

    public string DisplayName => string.IsNullOrEmpty(ModName) ? ModId : ModName;
}

public sealed record GameInfo(
    string AppId,
    string AppName,
    string AppLocation,
    string? IconFileName,
    IReadOnlyList<string> EnabledMods,
    IReadOnlyList<string> SortedMods,
    string FolderPath)
{
    public string? IconPath =>
        string.IsNullOrEmpty(IconFileName) ? null : Path.Combine(FolderPath, IconFileName);

    public string DisplayName => string.IsNullOrEmpty(AppName) ? AppId : AppName;
}

public sealed record ModLoadEntry(
    int Order,
    string ModId,
    ModInfo? Info,
    bool Enabled,
    string? Category = null,
    bool IsLibrary = false)
{
    public string DisplayName =>
        Info is { ModName.Length: > 0 } ? Info.ModName : ModId;

    public string? CategoryLabel =>
        IsLibrary ? "フレームワーク" :
        Category switch
        {
            "Sound"              => "サウンド",
            "Skin"               => "スキン",
            "Texture"            => "テクスチャ",
            "UI"                 => "UI",
            "Gameplay Mechanics" => "ゲームプレイ",
            "Misc"               => "その他",
            "Quality Of Life"    => "QOL",
            null                 => null,
            _                    => Category,
        };
}
```

- [ ] **Step 4: `ModConfigParser.cs` を変更 — `GetBool` 追加と `IsLibrary` 読み取り**

```csharp
// src/ReloadedHelper.Core/ModConfigParser.cs
using System.Text.Json;

namespace ReloadedHelper.Core;

public static class ModConfigParser
{
    public static ModInfo Parse(string json, string folderPath)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var modId          = GetString(root, "ModId")          ?? "";
        var modName        = GetString(root, "ModName")        ?? "";
        var modAuthor      = GetString(root, "ModAuthor")      ?? "";
        var modVersion     = GetString(root, "ModVersion")     ?? "";
        var modDescription = GetString(root, "ModDescription") ?? "";

        var tags                 = GetStringArray(root, "Tags");
        var dependencies         = GetStringArray(root, "ModDependencies");
        var optionalDependencies = GetStringArray(root, "OptionalDependencies");
        var supportedAppIds      = GetStringArray(root, "SupportedAppId");

        var projectUrl = GetString(root, "ProjectUrl");
        if (string.IsNullOrWhiteSpace(projectUrl)) projectUrl = null;

        string? gitHubUserName        = null;
        string? gitHubRepositoryName  = null;
        if (root.TryGetProperty("PluginData", out var pluginData) &&
            pluginData.TryGetProperty("GitHubRelease", out var githubRelease))
        {
            gitHubUserName       = GetString(githubRelease, "UserName");
            gitHubRepositoryName = GetString(githubRelease, "RepositoryName");
        }

        var iconFileName = GetString(root, "ModIcon");
        if (string.IsNullOrEmpty(iconFileName)) iconFileName = null;

        var isLibrary = GetBool(root, "IsLibrary");

        return new ModInfo(
            modId, modName, modAuthor, modVersion, modDescription,
            tags, dependencies, optionalDependencies, supportedAppIds,
            projectUrl, gitHubUserName, gitHubRepositoryName, iconFileName, folderPath,
            isLibrary);
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var prop)) return null;
        if (prop.ValueKind != JsonValueKind.String) return null;
        return prop.GetString();
    }

    private static IReadOnlyList<string> GetStringArray(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var prop)) return Array.Empty<string>();
        if (prop.ValueKind != JsonValueKind.Array) return Array.Empty<string>();

        var result = new List<string>();
        foreach (var item in prop.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                var str = item.GetString();
                if (str != null) result.Add(str);
            }
        }
        return result;
    }

    private static bool GetBool(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var prop)) return false;
        return prop.ValueKind == JsonValueKind.True;
    }
}
```

- [ ] **Step 5: テストが通ることを確認**

```
dotnet test tests/ReloadedHelper.Core.Tests --filter "ModConfigParserIsLibraryTests"
```

Expected: 6 tests PASS

- [ ] **Step 6: 既存テストが壊れていないことを確認**

```
dotnet test tests/ReloadedHelper.Core.Tests
```

Expected: 全テスト PASS

- [ ] **Step 7: ビルドチェック**

```
dotnet build reloaded-helper.slnx
```

Expected: Build succeeded, 0 errors

- [ ] **Step 8: コミット**

```
git add src/ReloadedHelper.Core/Models.cs src/ReloadedHelper.Core/ModConfigParser.cs tests/ReloadedHelper.Core.Tests/ModConfigParserIsLibraryTests.cs
git commit -m "feat: add IsLibrary to ModInfo and ModLoadEntry, update CategoryLabel"
```

---

### Task 2: フレームワーク強制有効化 + AppConfigWriter.RemoveMod

**Files:**
- Modify: `src/ReloadedHelper.Core/LoadOrder.cs`
- Modify: `src/ReloadedHelper.Core/MainViewModel.cs`
- Modify: `src/ReloadedHelper.Core/AppConfigWriter.cs`
- Create: `tests/ReloadedHelper.Core.Tests/LoadOrderBuilderIsLibraryTests.cs`
- Create: `tests/ReloadedHelper.Core.Tests/AppConfigWriterRemoveModTests.cs`

**Interfaces:**
- Consumes: `ModInfo.IsLibrary` (Task 1)
- Produces: `LoadOrderBuilder.Build()` が IsLibrary=true の MOD を常時 Enabled=true にする。`MainViewModel.Reload()` メソッド。`AppConfigWriter.RemoveMod(configPath, appId, modId)` メソッド。

---

- [ ] **Step 1: テストファイルを作成**

```csharp
// tests/ReloadedHelper.Core.Tests/LoadOrderBuilderIsLibraryTests.cs
namespace ReloadedHelper.Core.Tests;

public class LoadOrderBuilderIsLibraryTests
{
    private static ModInfo MakeModInfo(string id, bool isLibrary) => new ModInfo(
        id, id, "Author", "1.0", "Desc",
        Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(),
        Array.Empty<string>(), null, null, null, null, @"C:\mods\" + id,
        isLibrary);

    private static GameInfo MakeGame(
        IReadOnlyList<string> enabled,
        IReadOnlyList<string> sorted) => new GameInfo(
            "game.exe", "Game", @"C:\game.exe", null,
            enabled, sorted, @"C:\game");

    [Fact]
    public void Build_IsLibraryTrue_AlwaysEnabled_EvenIfNotInEnabledMods()
    {
        var catalog = new Dictionary<string, ModInfo>
        {
            ["lib.mod"] = MakeModInfo("lib.mod", isLibrary: true)
        };
        var game = MakeGame(
            enabled: Array.Empty<string>(),  // lib.mod は EnabledMods に含まれていない
            sorted:  new[] { "lib.mod" });

        var result = LoadOrderBuilder.Build(game, catalog);

        Assert.Single(result);
        Assert.True(result[0].Enabled);
        Assert.True(result[0].IsLibrary);
    }

    [Fact]
    public void Build_IsLibraryFalse_DisabledIfNotInEnabledMods()
    {
        var catalog = new Dictionary<string, ModInfo>
        {
            ["normal.mod"] = MakeModInfo("normal.mod", isLibrary: false)
        };
        var game = MakeGame(
            enabled: Array.Empty<string>(),
            sorted:  new[] { "normal.mod" });

        var result = LoadOrderBuilder.Build(game, catalog);

        Assert.Single(result);
        Assert.False(result[0].Enabled);
        Assert.False(result[0].IsLibrary);
    }

    [Fact]
    public void Build_IsLibraryTrue_CategoryLabelIsFramework()
    {
        var catalog = new Dictionary<string, ModInfo>
        {
            ["lib.mod"] = MakeModInfo("lib.mod", isLibrary: true)
        };
        var game = MakeGame(
            enabled: Array.Empty<string>(),
            sorted:  new[] { "lib.mod" });

        var result = LoadOrderBuilder.Build(game, catalog);

        Assert.Equal("フレームワーク", result[0].CategoryLabel);
    }
}
```

```csharp
// tests/ReloadedHelper.Core.Tests/AppConfigWriterRemoveModTests.cs
namespace ReloadedHelper.Core.Tests;

public class AppConfigWriterRemoveModTests : IDisposable
{
    private readonly string _tmp = Path.GetTempFileName();

    public void Dispose()
    {
        File.Delete(_tmp);
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ReloadedHelper", "backups", "remove-test");
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public void RemoveMod_RemovesFromBothLists_PreservesOthers()
    {
        File.WriteAllText(_tmp, """
            {
              "AppId": "remove-test",
              "AppName": "TestGame",
              "EnabledMods": ["ModA", "ModB", "ModC"],
              "SortedMods":  ["ModA", "ModB", "ModC"]
            }
            """);

        AppConfigWriter.RemoveMod(_tmp, "remove-test", "ModB");

        var result = AppConfigParser.Parse(File.ReadAllText(_tmp), Path.GetTempPath());
        Assert.Equal(new[] { "ModA", "ModC" }, result.EnabledMods);
        Assert.Equal(new[] { "ModA", "ModC" }, result.SortedMods);
        Assert.Equal("TestGame", result.AppName);
    }

    [Fact]
    public void RemoveMod_CaseInsensitive()
    {
        File.WriteAllText(_tmp, """
            {
              "AppId": "remove-test",
              "EnabledMods": ["ModA", "MODB"],
              "SortedMods":  ["ModA", "MODB"]
            }
            """);

        AppConfigWriter.RemoveMod(_tmp, "remove-test", "modb");

        var result = AppConfigParser.Parse(File.ReadAllText(_tmp), Path.GetTempPath());
        Assert.Equal(new[] { "ModA" }, result.EnabledMods);
        Assert.Equal(new[] { "ModA" }, result.SortedMods);
    }

    [Fact]
    public void RemoveMod_WhenModNotPresent_NoChange()
    {
        File.WriteAllText(_tmp, """
            {
              "AppId": "remove-test",
              "EnabledMods": ["ModA"],
              "SortedMods":  ["ModA"]
            }
            """);

        AppConfigWriter.RemoveMod(_tmp, "remove-test", "NonExistent");

        var result = AppConfigParser.Parse(File.ReadAllText(_tmp), Path.GetTempPath());
        Assert.Equal(new[] { "ModA" }, result.EnabledMods);
        Assert.Equal(new[] { "ModA" }, result.SortedMods);
    }
}
```

- [ ] **Step 2: テストが失敗することを確認**

```
dotnet test tests/ReloadedHelper.Core.Tests --filter "LoadOrderBuilderIsLibraryTests|AppConfigWriterRemoveModTests"
```

Expected: コンパイルエラー or FAIL

- [ ] **Step 3: `LoadOrder.cs` を変更**

```csharp
// src/ReloadedHelper.Core/LoadOrder.cs
namespace ReloadedHelper.Core;

public enum FilterMode { All, EnabledOnly, DisabledOnly }

public static class LoadOrderBuilder
{
    public static IReadOnlyList<ModLoadEntry> Build(
        GameInfo game,
        IReadOnlyDictionary<string, ModInfo> catalog,
        UserDataFile? userData = null)
    {
        var enabled = new HashSet<string>(game.EnabledMods, StringComparer.Ordinal);
        var list = new List<ModLoadEntry>(game.SortedMods.Count);
        for (int i = 0; i < game.SortedMods.Count; i++)
        {
            var id        = game.SortedMods[i];
            catalog.TryGetValue(id, out var info);
            var category  = userData?.Mods.GetValueOrDefault(id)?.Category;
            var isLibrary = info?.IsLibrary ?? false;
            var isEnabled = enabled.Contains(id) || isLibrary;
            list.Add(new ModLoadEntry(i + 1, id, info, isEnabled, category, isLibrary));
        }
        return list;
    }
}

public static class ModFilter
{
    public static IReadOnlyList<ModLoadEntry> Filter(
        IReadOnlyList<ModLoadEntry> entries, string? search,
        FilterMode mode = FilterMode.All)
    {
        IEnumerable<ModLoadEntry> result = entries;

        result = mode switch
        {
            FilterMode.EnabledOnly  => result.Where(e => e.Enabled),
            FilterMode.DisabledOnly => result.Where(e => !e.Enabled),
            _                       => result
        };

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            result = result.Where(e =>
                e.ModId.Contains(s, StringComparison.OrdinalIgnoreCase) ||
                e.DisplayName.Contains(s, StringComparison.OrdinalIgnoreCase));
        }

        return result.ToList();
    }
}
```

- [ ] **Step 4: `AppConfigWriter.cs` に `RemoveMod` を追加**

```csharp
// src/ReloadedHelper.Core/AppConfigWriter.cs
// 既存の WriteOrder と WriteEnabledAndSorted の後に追加：

    public static void RemoveMod(string configPath, string appId, string modId)
    {
        LoadOrderBackupService.Backup(configPath, appId);

        var original = File.ReadAllBytes(configPath);
        using var doc = JsonDocument.Parse(original);

        using var ms     = new System.IO.MemoryStream();
        using var writer = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true });

        writer.WriteStartObject();
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            switch (prop.Name)
            {
                case "EnabledMods":
                case "SortedMods":
                    writer.WritePropertyName(prop.Name);
                    writer.WriteStartArray();
                    foreach (var item in prop.Value.EnumerateArray())
                    {
                        var id = item.GetString();
                        if (id != null && !string.Equals(id, modId, StringComparison.OrdinalIgnoreCase))
                            writer.WriteStringValue(id);
                    }
                    writer.WriteEndArray();
                    break;
                default:
                    prop.WriteTo(writer);
                    break;
            }
        }
        writer.WriteEndObject();
        writer.Flush();

        File.WriteAllBytes(configPath, ms.ToArray());
    }
```

- [ ] **Step 5: `MainViewModel.cs` に変更を追加**

`ToggleEnabled` の先頭に `IsLibrary` ガードを追加し、`Reload()` メソッドを追加する：

```csharp
// src/ReloadedHelper.Core/MainViewModel.cs の変更点

// (1) RefreshSelectedAction プロパティを追加（RefreshAction の直後）:
public Func<IReadOnlyList<string>, Task>? RefreshSelectedAction { get; set; }

// (2) ToggleEnabled の先頭に追加:
public void ToggleEnabled(ModLoadEntry entry)
{
    if (entry.IsLibrary) return;  // フレームワーク MOD はトグル不可
    if (SelectedGame is null || _install is null) return;
    // ... 以降は変更なし

// (3) Reload() メソッドを追加（LoadFrom の後あたり）:
public void Reload()
{
    if (_install is null) return;
    var prevId = SelectedGame?.AppId;
    LoadFrom(_install);
    if (prevId is not null)
    {
        var restored = Games.FirstOrDefault(g => g.AppId == prevId);
        if (restored is not null) SelectedGame = restored;
    }
}
```

- [ ] **Step 6: テストが通ることを確認**

```
dotnet test tests/ReloadedHelper.Core.Tests --filter "LoadOrderBuilderIsLibraryTests|AppConfigWriterRemoveModTests"
```

Expected: 全テスト PASS

- [ ] **Step 7: 全テスト確認**

```
dotnet test tests/ReloadedHelper.Core.Tests
```

Expected: 全テスト PASS

- [ ] **Step 8: ビルドチェック**

```
dotnet build reloaded-helper.slnx
```

- [ ] **Step 9: コミット**

```
git add src/ReloadedHelper.Core/LoadOrder.cs src/ReloadedHelper.Core/MainViewModel.cs src/ReloadedHelper.Core/AppConfigWriter.cs tests/ReloadedHelper.Core.Tests/LoadOrderBuilderIsLibraryTests.cs tests/ReloadedHelper.Core.Tests/AppConfigWriterRemoveModTests.cs
git commit -m "feat: framework mods always enabled, add Reload() and AppConfigWriter.RemoveMod"
```

---

### Task 3: 強制再取得 + RefreshSelectedAction + ApplySortAllGames IsLibrary 対応

**Files:**
- Modify: `src/ReloadedHelper.App/App.xaml.cs`

**Interfaces:**
- Consumes: `MainViewModel.RefreshSelectedAction` (Task 2), `ModInfo.IsLibrary` (Task 1)
- Produces: `RefreshModMetadataAsync(modListVm, install, targetModIds)` — null で全件、非null で指定 MOD のみ処理。`modListVm.RefreshSelectedAction` 登録済み。

---

- [ ] **Step 1: `App.xaml.cs` を変更**

以下の変更をすべて `App.xaml.cs` に適用する：

**① `OnStartup` に `RefreshSelectedAction` の登録を追加**（`modListVm.RefreshAction = ...` の直後）：

```csharp
modListVm.RefreshAction = () => Task.Run(() => RefreshModMetadataAsync(modListVm, install));
modListVm.RefreshSelectedAction = ids =>
    Task.Run(() => RefreshModMetadataAsync(modListVm, install, ids));
_ = Task.Run(() => RefreshModMetadataAsync(modListVm, install));
```

（元の `_ = Task.Run(() => RefreshModMetadataAsync(modListVm, install));` と `modListVm.RefreshAction = ...` を上記3行に置き換える）

**② `RefreshModMetadataAsync` のシグネチャと `toProcess` を変更**：

```csharp
private static async Task RefreshModMetadataAsync(
    MainViewModel modListVm,
    ReloadedInstall install,
    IReadOnlyList<string>? targetModIds = null)
{
    bool started = false;
    Current.Dispatcher.Invoke(() =>
    {
        if (modListVm.IsUpdating) return;
        modListVm.IsUpdating = true;
        started = true;
    });
    if (!started) return;

    try
    {
        using var http = new System.Net.Http.HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("rh-tools/1.0");
        var gbClient   = new GameBananaClient(http);
        var translator = new TranslationService(http);
        var userData   = UserDataStore.Load(UserDataStore.DefaultPath);
        var catalog    = Current.Dispatcher.Invoke(() => modListVm.AllMods);

        // targetModIds が null なら全件、指定があればその MOD のみ
        var toProcess = targetModIds is null
            ? catalog.Values.ToList()
            : catalog.Values
                .Where(m => targetModIds.Contains(m.ModId, StringComparer.OrdinalIgnoreCase))
                .ToList();

        int total = toProcess.Count;
        if (total == 0) return;

        // 以降は既存コードと同じ（gameIdCache 構築〜foreach ループ〜Save〜LoadFrom）
        // ... [既存コードをそのまま保持]
    }
    catch { }
    finally
    {
        Current.Dispatcher.Invoke(() =>
        {
            modListVm.IsUpdating     = false;
            modListVm.UpdateProgress = "";
        });
    }
}
```

> **注意:** `toProcess` の生成部分（元の `var toProcess = catalog.Values.Where(...)` ブロック）を上記コードで**丸ごと置き換える**。ループ内の処理（gbId取得・翻訳・保存）は変更しない。

**③ `ApplySortAllGames` を変更** — IsLibrary MOD を常に enabled グループに含め、`WriteOrder` → `WriteEnabledAndSorted` に変更：

```csharp
private static void ApplySortAllGames(MainViewModel mainVm, ReloadedInstall install)
{
    var catalog = mainVm.AllMods;
    var depMap  = catalog.ToDictionary(
        kv => kv.Key,
        kv => (IReadOnlyList<string>)kv.Value.Dependencies,
        StringComparer.OrdinalIgnoreCase);

    bool anyChanged = false;
    foreach (var game in mainVm.Games)
    {
        if (game.SortedMods.Count == 0) continue;

        var enabledSet = new HashSet<string>(game.EnabledMods, StringComparer.OrdinalIgnoreCase);

        // IsLibrary MOD は常に enabled グループへ
        var enabledGroup = game.SortedMods
            .Where(id => enabledSet.Contains(id) ||
                         (catalog.TryGetValue(id, out var m) && m.IsLibrary))
            .ToList();
        var disabledGroup = game.SortedMods
            .Where(id => !enabledGroup.Contains(id, StringComparer.OrdinalIgnoreCase))
            .ToList();

        var sortedEnabled  = LoadOrderSorter.Sort(enabledGroup,  depMap);
        var sortedDisabled = LoadOrderSorter.Sort(disabledGroup, depMap);
        var newSorted  = sortedEnabled.Concat(sortedDisabled).ToList();
        var newEnabled = sortedEnabled.ToList();

        var configPath = Path.Combine(game.FolderPath, "AppConfig.json");
        if (!File.Exists(configPath)) continue;

        bool sortChanged    = !newSorted.SequenceEqual(game.SortedMods,  StringComparer.OrdinalIgnoreCase);
        bool enabledChanged = !newEnabled.SequenceEqual(game.EnabledMods, StringComparer.OrdinalIgnoreCase);
        if (!sortChanged && !enabledChanged) continue;

        AppConfigWriter.WriteEnabledAndSorted(configPath, game.AppId, newEnabled, newSorted);
        anyChanged = true;
    }

    if (anyChanged) mainVm.LoadFrom(install);
}
```

- [ ] **Step 2: ビルドチェック**

```
dotnet build reloaded-helper.slnx
```

Expected: Build succeeded, 0 errors

- [ ] **Step 3: コミット**

```
git add src/ReloadedHelper.App/App.xaml.cs
git commit -m "feat: force re-fetch all mods, add RefreshSelectedAction, fix ApplySortAllGames for IsLibrary"
```

---

### Task 4: タスクトレイ ContextMenu 配色修正

**Files:**
- Modify: `src/ReloadedHelper.App/MainWindow.xaml`

---

- [ ] **Step 1: `MainWindow.xaml` の ContextMenu を変更**

```xml
<!-- 変更前 -->
<tb:TaskbarIcon.ContextMenu>
    <ContextMenu>
        <MenuItem Header="表示" Click="TrayShow_Click"/>
        <MenuItem Header="終了" Click="TrayExit_Click"/>
    </ContextMenu>
</tb:TaskbarIcon.ContextMenu>

<!-- 変更後 -->
<tb:TaskbarIcon.ContextMenu>
    <ContextMenu Background="{DynamicResource BgBarBrush}"
                 BorderBrush="{DynamicResource BorderInputBrush}"
                 BorderThickness="1">
        <MenuItem Header="表示" Click="TrayShow_Click"
                  Foreground="{DynamicResource TextBodyBrush}"
                  Background="{DynamicResource BgBarBrush}"/>
        <Separator Background="{DynamicResource BgSeparatorBrush}"/>
        <MenuItem Header="終了" Click="TrayExit_Click"
                  Foreground="{DynamicResource TextBodyBrush}"
                  Background="{DynamicResource BgBarBrush}"/>
    </ContextMenu>
</tb:TaskbarIcon.ContextMenu>
```

- [ ] **Step 2: ビルドチェック**

```
dotnet build reloaded-helper.slnx
```

- [ ] **Step 3: 目視確認**

アプリを起動してタスクトレイアイコンを右クリックし、「表示」と「終了」が白文字でダーク背景に表示されることを確認する。

- [ ] **Step 4: コミット**

```
git add src/ReloadedHelper.App/MainWindow.xaml
git commit -m "fix: apply dark theme colors to tray context menu"
```

---

### Task 5: RecycleBinHelper + ModEditWindow

**Files:**
- Create: `src/ReloadedHelper.App/RecycleBinHelper.cs`
- Create: `src/ReloadedHelper.App/Views/ModEditWindow.xaml`
- Create: `src/ReloadedHelper.App/Views/ModEditWindow.xaml.cs`

**Interfaces:**
- Consumes: `MainViewModel.Reload()` (Task 2)、`MainViewModel.RefreshSelectedAction` (Task 3)、`AppConfigWriter.RemoveMod` (Task 2)、`ModLoadEntry.IsLibrary` (Task 1)
- Produces: `RecycleBinHelper.SendToRecycleBin(path)`、`ModEditWindow(ModLoadEntry, MainViewModel)` ダイアログ

---

- [ ] **Step 1: `RecycleBinHelper.cs` を作成**

```csharp
// src/ReloadedHelper.App/RecycleBinHelper.cs
using System.Runtime.InteropServices;

namespace ReloadedHelper.App;

internal static class RecycleBinHelper
{
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref SHFILEOPSTRUCT op);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr  hwnd;
        public uint    wFunc;
        public string  pFrom;
        public string? pTo;
        public ushort  fFlags;
        public bool    fAnyOperationsAborted;
        public IntPtr  hNameMappings;
        public string? lpszProgressTitle;
    }

    private const uint   FO_DELETE          = 0x0003;
    private const ushort FOF_ALLOWUNDO      = 0x0040;
    private const ushort FOF_NOCONFIRMATION = 0x0010;
    private const ushort FOF_NOERRORUI      = 0x0400;

    /// <summary>指定フォルダ（またはファイル）をゴミ箱に送る。</summary>
    public static bool SendToRecycleBin(string path)
    {
        var op = new SHFILEOPSTRUCT
        {
            wFunc  = FO_DELETE,
            pFrom  = path + "\0\0",
            fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_NOERRORUI
        };
        return SHFileOperation(ref op) == 0;
    }
}
```

- [ ] **Step 2: `ModEditWindow.xaml` を作成**

```xml
<!-- src/ReloadedHelper.App/Views/ModEditWindow.xaml -->
<Window x:Class="ReloadedHelper.App.Views.ModEditWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="MOD 編集" Width="480" SizeToContent="Height"
        ResizeMode="NoResize" WindowStartupLocation="CenterOwner"
        Background="{DynamicResource BgMainBrush}"
        FontFamily="{DynamicResource MainFont}">

    <StackPanel Margin="20">

        <!-- MOD名 -->
        <TextBlock Text="MOD名（日本語）" Foreground="{DynamicResource TextLabelBrush}"
                   FontSize="{DynamicResource FontSizeLabel}" Margin="0,0,0,4"/>
        <TextBox x:Name="TbName" Margin="0,0,0,12" Padding="8,6"
                 Background="{DynamicResource BgInputBrush}"
                 Foreground="{DynamicResource TextBodyBrush}"
                 BorderBrush="{DynamicResource BorderInputBrush}" BorderThickness="1"
                 FontSize="{DynamicResource FontSizeCardBody}"/>

        <!-- 説明 -->
        <TextBlock Text="説明（日本語）" Foreground="{DynamicResource TextLabelBrush}"
                   FontSize="{DynamicResource FontSizeLabel}" Margin="0,0,0,4"/>
        <TextBox x:Name="TbDescription" Height="90" Margin="0,0,0,12" Padding="8,6"
                 AcceptsReturn="True" TextWrapping="Wrap" VerticalScrollBarVisibility="Auto"
                 Background="{DynamicResource BgInputBrush}"
                 Foreground="{DynamicResource TextBodyBrush}"
                 BorderBrush="{DynamicResource BorderInputBrush}" BorderThickness="1"
                 FontSize="{DynamicResource FontSizeCardBody}"/>

        <!-- URL -->
        <TextBlock Text="GameBanana URL" Foreground="{DynamicResource TextLabelBrush}"
                   FontSize="{DynamicResource FontSizeLabel}" Margin="0,0,0,4"/>
        <TextBox x:Name="TbUrl" Margin="0,0,0,12" Padding="8,6"
                 Background="{DynamicResource BgInputBrush}"
                 Foreground="{DynamicResource TextBodyBrush}"
                 BorderBrush="{DynamicResource BorderInputBrush}" BorderThickness="1"
                 FontSize="{DynamicResource FontSizeCardBody}"/>

        <!-- メモ -->
        <TextBlock Text="メモ" Foreground="{DynamicResource TextLabelBrush}"
                   FontSize="{DynamicResource FontSizeLabel}" Margin="0,0,0,4"/>
        <TextBox x:Name="TbNotes" Height="60" Margin="0,0,0,12" Padding="8,6"
                 AcceptsReturn="True" TextWrapping="Wrap"
                 Background="{DynamicResource BgInputBrush}"
                 Foreground="{DynamicResource TextBodyBrush}"
                 BorderBrush="{DynamicResource BorderInputBrush}" BorderThickness="1"
                 FontSize="{DynamicResource FontSizeCardBody}"/>

        <!-- GameBanana ID -->
        <TextBlock Text="GameBanana ID（自動取得）" Foreground="{DynamicResource TextLabelBrush}"
                   FontSize="{DynamicResource FontSizeLabel}" Margin="0,0,0,4"/>
        <TextBox x:Name="TbGbId" Margin="0,0,0,16" Padding="8,6"
                 Background="{DynamicResource BgInputBrush}"
                 Foreground="{DynamicResource TextBodyBrush}"
                 BorderBrush="{DynamicResource BorderInputBrush}" BorderThickness="1"
                 FontSize="{DynamicResource FontSizeCardBody}"/>

        <!-- ボタン行 -->
        <StackPanel Orientation="Horizontal" HorizontalAlignment="Right" Margin="0,0,0,12">
            <Button x:Name="BtnRefresh" Content="個別更新"
                    Click="BtnRefresh_Click" Padding="12,6" Margin="0,0,8,0"
                    Background="{DynamicResource BgInputBrush}"
                    Foreground="{DynamicResource TextBodyBrush}"
                    BorderBrush="{DynamicResource BorderInputBrush}" BorderThickness="1"
                    Cursor="Hand"/>
            <Button x:Name="BtnSave" Content="保存"
                    Click="BtnSave_Click" Padding="12,6" Margin="0,0,8,0"
                    Background="{DynamicResource AccentBrush}"
                    Foreground="White" BorderThickness="0" Cursor="Hand"/>
            <Button Content="キャンセル"
                    Click="BtnCancel_Click" Padding="12,6"
                    Background="{DynamicResource BgInputBrush}"
                    Foreground="{DynamicResource TextBodyBrush}"
                    BorderBrush="{DynamicResource BorderInputBrush}" BorderThickness="1"
                    Cursor="Hand"/>
        </StackPanel>

        <!-- 区切り -->
        <Border Height="1" Background="{DynamicResource BgSeparatorBrush}" Margin="0,0,0,12"/>

        <!-- 削除ボタン（IsLibrary の場合は非表示） -->
        <Button x:Name="BtnDelete" Content="このMODを削除（ゴミ箱へ）"
                Click="BtnDelete_Click" Padding="12,6" HorizontalAlignment="Stretch"
                Background="#C0392B" Foreground="White" BorderThickness="0" Cursor="Hand"/>
    </StackPanel>
</Window>
```

- [ ] **Step 3: `ModEditWindow.xaml.cs` を作成**

```csharp
// src/ReloadedHelper.App/Views/ModEditWindow.xaml.cs
using System.IO;
using System.Windows;
using ReloadedHelper.Core;

namespace ReloadedHelper.App.Views;

public partial class ModEditWindow : Window
{
    private readonly ModLoadEntry  _entry;
    private readonly MainViewModel _vm;

    public ModEditWindow(ModLoadEntry entry, MainViewModel vm)
    {
        _entry = entry;
        _vm    = vm;
        InitializeComponent();
        LoadFields();
    }

    private void LoadFields()
    {
        var ud = UserDataStore.Load(UserDataStore.DefaultPath);
        ud.Mods.TryGetValue(_entry.ModId, out var data);

        TbName.Text        = data?.TranslatedName        ?? _entry.Info?.ModName        ?? "";
        TbDescription.Text = data?.TranslatedDescription ?? _entry.Info?.ModDescription ?? "";
        TbUrl.Text         = data?.UrlOverride           ?? _entry.Info?.ProjectUrl      ?? "";
        TbNotes.Text       = data?.Notes                 ?? "";
        TbGbId.Text        = data?.GameBananaId          ?? "";

        // フレームワーク MOD は削除禁止
        BtnDelete.Visibility = _entry.IsLibrary ? Visibility.Collapsed : Visibility.Visible;
    }

    private void SaveFields()
    {
        var ud = UserDataStore.Load(UserDataStore.DefaultPath);
        if (!ud.Mods.TryGetValue(_entry.ModId, out var data))
            data = new ModUserData();

        data.TranslatedName        = TbName.Text.Trim();
        data.TranslatedDescription = TbDescription.Text.Trim();
        data.UrlOverride           = TbUrl.Text.Trim();
        data.Notes                 = TbNotes.Text.Trim();
        data.GameBananaId          = TbGbId.Text.Trim();

        ud.Mods[_entry.ModId] = data;
        UserDataStore.Save(UserDataStore.DefaultPath, ud);
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        SaveFields();
        _vm.Reload();
        Close();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e) => Close();

    private void BtnRefresh_Click(object sender, RoutedEventArgs e)
    {
        SaveFields();
        if (_vm.RefreshSelectedAction is not null)
            _ = _vm.RefreshSelectedAction(new[] { _entry.ModId });
        Close();
    }

    private void BtnDelete_Click(object sender, RoutedEventArgs e)
    {
        if (_entry.Info is null)
        {
            MessageBox.Show("MOD フォルダが見つかりません。", "削除エラー",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var name = _entry.DisplayName;
        var confirm = MessageBox.Show(
            $"「{name}」のフォルダをゴミ箱に移動します。よろしいですか？\n\n{_entry.Info.FolderPath}",
            "MOD 削除の確認",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.OK) return;

        // 全ゲームの AppConfig.json から除去
        foreach (var game in _vm.Games)
        {
            var configPath = Path.Combine(game.FolderPath, "AppConfig.json");
            if (File.Exists(configPath))
                AppConfigWriter.RemoveMod(configPath, game.AppId, _entry.ModId);
        }

        // UserData から除去
        var ud = UserDataStore.Load(UserDataStore.DefaultPath);
        ud.Mods.Remove(_entry.ModId);
        UserDataStore.Save(UserDataStore.DefaultPath, ud);

        // ゴミ箱へ
        var success = RecycleBinHelper.SendToRecycleBin(_entry.Info.FolderPath);
        if (!success)
        {
            MessageBox.Show("ゴミ箱への移動に失敗しました。", "削除エラー",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        _vm.Reload();
        Close();
    }
}
```

- [ ] **Step 4: ビルドチェック**

```
dotnet build reloaded-helper.slnx
```

Expected: Build succeeded, 0 errors

- [ ] **Step 5: コミット**

```
git add src/ReloadedHelper.App/RecycleBinHelper.cs src/ReloadedHelper.App/Views/ModEditWindow.xaml src/ReloadedHelper.App/Views/ModEditWindow.xaml.cs
git commit -m "feat: add RecycleBinHelper and ModEditWindow for per-mod edit/delete"
```

---

### Task 6: ModListView UI 全更新

**Files:**
- Modify: `src/ReloadedHelper.App/Views/ModListView.xaml`
- Modify: `src/ReloadedHelper.App/Views/ModListView.xaml.cs`

**Interfaces:**
- Consumes: `ModEditWindow(ModLoadEntry, MainViewModel)` (Task 5)、`ModLoadEntry.IsLibrary` (Task 1)、`MainViewModel.RefreshSelectedAction` (Task 3)

---

- [ ] **Step 1: `ModListView.xaml` を変更 — 5箇所**

**① ListBox に `SelectionMode="Extended"` と `SelectionChanged` を追加**

```xml
<!-- 変更前 -->
<ListBox ItemsSource="{Binding Entries}"
         SelectedItem="{Binding SelectedEntry}"
         ...>

<!-- 変更後 -->
<ListBox x:Name="ModListBox"
         ItemsSource="{Binding Entries}"
         SelectedItem="{Binding SelectedEntry}"
         SelectionMode="Extended"
         SelectionChanged="ModList_SelectionChanged"
         ...>
```

**② ツールバーに「選択中を更新」ボタンを追加**（「今すぐ更新」ボタンの前に挿入）

```xml
<!-- 「今すぐ更新」の前に追加 -->
<Button x:Name="RefreshSelectedButton"
        Content="選択中を更新 (0件)"
        Click="RefreshSelected_Click"
        Visibility="Collapsed"
        Padding="8,4" Margin="0,0,3,0" Cursor="Hand"
        Background="{DynamicResource BgInputBrush}"
        Foreground="{DynamicResource TextBodyBrush}"
        BorderBrush="{DynamicResource BorderInputBrush}" BorderThickness="1"
        FontSize="{DynamicResource FontSizeLabel}"/>
```

**③ DataTemplate の Grid に `ColumnDefinitions` を追加（6列目）**

```xml
<!-- 変更前 -->
<Grid Height="54">
    <Grid.ColumnDefinitions>
        <ColumnDefinition Width="36"/>   <!-- 順番 -->
        <ColumnDefinition Width="50"/>   <!-- サムネ -->
        <ColumnDefinition Width="*"/>    <!-- 名前 + 作者 -->
        <ColumnDefinition Width="Auto"/> <!-- カテゴリバッジ -->
        <ColumnDefinition Width="52"/>   <!-- トグル -->
    </Grid.ColumnDefinitions>

<!-- 変更後 -->
<Grid Height="54">
    <Grid.ColumnDefinitions>
        <ColumnDefinition Width="36"/>   <!-- 順番 -->
        <ColumnDefinition Width="50"/>   <!-- サムネ -->
        <ColumnDefinition Width="*"/>    <!-- 名前 + 作者 -->
        <ColumnDefinition Width="Auto"/> <!-- カテゴリバッジ -->
        <ColumnDefinition Width="52"/>   <!-- トグル -->
        <ColumnDefinition Width="36"/>   <!-- … ボタン -->
    </Grid.ColumnDefinitions>
```

**④ トグルボタンを IsLibrary 時に非表示にする**（既存の ToggleButton を置き換え）

```xml
<!-- 変更前 -->
<ToggleButton Grid.Column="4"
             IsChecked="{Binding Enabled, Mode=OneWay}"
             Click="ModToggle_Click"
             Style="{DynamicResource ToggleSwitchStyle}"
             HorizontalAlignment="Center" VerticalAlignment="Center"/>

<!-- 変更後 -->
<ToggleButton Grid.Column="4"
             IsChecked="{Binding Enabled, Mode=OneWay}"
             Click="ModToggle_Click"
             HorizontalAlignment="Center" VerticalAlignment="Center">
    <ToggleButton.Style>
        <Style TargetType="ToggleButton" BasedOn="{StaticResource ToggleSwitchStyle}">
            <Style.Triggers>
                <DataTrigger Binding="{Binding IsLibrary}" Value="True">
                    <Setter Property="Visibility" Value="Collapsed"/>
                </DataTrigger>
            </Style.Triggers>
        </Style>
    </ToggleButton.Style>
</ToggleButton>
```

**⑤ 「…」ボタンと右クリック ContextMenu を追加**（ToggleButton の後に追加）

```xml
<!-- 「…」ボタン（6列目） -->
<Button Grid.Column="5" Content="…"
        Click="EditButton_Click" Tag="{Binding}"
        Width="28" Height="28" Padding="0"
        HorizontalAlignment="Center" VerticalAlignment="Center"
        Background="Transparent" BorderThickness="0"
        Foreground="{DynamicResource TextMetaBrush}"
        Cursor="Hand" FontSize="14"/>
```

DataTemplate の `<Grid Height="54">` の直前（`<Grid.ColumnDefinitions>` の前）に右クリックメニューを追加：

```xml
<Grid Height="54">
    <Grid.ContextMenu>
        <ContextMenu>
            <MenuItem Header="編集..."              Click="EditMenu_Click"/>
            <MenuItem Header="このMODを更新"        Click="RefreshMenu_Click"/>
            <Separator/>
            <MenuItem Header="削除（ゴミ箱へ）"    Click="DeleteMenu_Click"/>
        </ContextMenu>
    </Grid.ContextMenu>
    <Grid.ColumnDefinitions>
```

- [ ] **Step 2: `ModListView.xaml.cs` を変更 — 既存コードに追加**

```csharp
// src/ReloadedHelper.App/Views/ModListView.xaml.cs
// 既存の using や namespace を保持し、以下のメソッドを追加

// ── 複数選択 ──
private void ModList_SelectionChanged(object sender, SelectionChangedEventArgs e)
{
    var count = ModListBox.SelectedItems.Count;
    RefreshSelectedButton.Content    = $"選択中を更新 ({count}件)";
    RefreshSelectedButton.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
}

private void RefreshSelected_Click(object sender, RoutedEventArgs e)
{
    if (DataContext is not MainViewModel vm) return;
    if (vm.RefreshSelectedAction is null) return;

    var ids = ModListBox.SelectedItems
        .OfType<ModLoadEntry>()
        .Select(entry => entry.ModId)
        .ToList();

    if (ids.Count == 0) return;
    _ = vm.RefreshSelectedAction(ids);
}

// ── 「…」ボタン ──
private void EditButton_Click(object sender, RoutedEventArgs e)
{
    if (sender is Button btn && btn.Tag is ModLoadEntry entry)
        OpenEditWindow(entry);
}

// ── 右クリックメニュー ──
private void EditMenu_Click(object sender, RoutedEventArgs e)
{
    if (GetContextMenuEntry(sender) is { } entry) OpenEditWindow(entry);
}

private void RefreshMenu_Click(object sender, RoutedEventArgs e)
{
    if (GetContextMenuEntry(sender) is not { } entry) return;
    if (DataContext is not MainViewModel vm) return;
    if (vm.RefreshSelectedAction is null) return;
    _ = vm.RefreshSelectedAction(new[] { entry.ModId });
}

private void DeleteMenu_Click(object sender, RoutedEventArgs e)
{
    if (GetContextMenuEntry(sender) is { } entry)
    {
        var win = new Views.ModEditWindow(entry, (MainViewModel)DataContext!);
        win.Owner = Window.GetWindow(this);
        // 削除ボタンを直接トリガーするのではなく、ウィンドウを開く
        win.ShowDialog();
    }
}

private void OpenEditWindow(ModLoadEntry entry)
{
    if (DataContext is not MainViewModel vm) return;
    var win = new Views.ModEditWindow(entry, vm);
    win.Owner = Window.GetWindow(this);
    win.ShowDialog();
    // ShowDialog は同期なので、閉じたあと済み（Reload は Window 内で呼ばれる）
}

private static ModLoadEntry? GetContextMenuEntry(object sender)
{
    if (sender is MenuItem item &&
        item.Parent is ContextMenu cm &&
        cm.PlacementTarget is Grid grid &&
        grid.DataContext is ModLoadEntry entry)
        return entry;
    return null;
}
```

- [ ] **Step 3: `ModListView.xaml.cs` の既存 using を確認・追加**

ファイル先頭の using に以下が必要なら追加：

```csharp
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using ReloadedHelper.Core;
```

- [ ] **Step 4: ビルドチェック**

```
dotnet build reloaded-helper.slnx
```

Expected: Build succeeded, 0 errors

- [ ] **Step 5: 全テスト確認**

```
dotnet test tests/ReloadedHelper.Core.Tests
```

Expected: 全テスト PASS

- [ ] **Step 6: 目視確認チェックリスト**

アプリを起動して以下を確認する：

- [ ] IsLibrary=true の MOD に「フレームワーク」バッジが表示され、トグルが非表示
- [ ] 起動時にフレームワーク MOD が有効として表示される
- [ ] カテゴリバッジが MOD カードに表示される（再取得完了後）
- [ ] 「…」ボタンクリックで ModEditWindow が開く
- [ ] 右クリックメニューに「編集…」「このMODを更新」「削除（ゴミ箱へ）」が表示される
- [ ] Ctrl+クリックで複数選択でき、「選択中を更新 (N件)」ボタンが現れる
- [ ] 「選択中を更新」でそのMODだけ更新が走る
- [ ] MOD 削除で確認ダイアログが出てゴミ箱に移動される
- [ ] タスクトレイ右クリックメニューが白文字・ダーク背景で表示される

- [ ] **Step 7: コミット**

```
git add src/ReloadedHelper.App/Views/ModListView.xaml src/ReloadedHelper.App/Views/ModListView.xaml.cs
git commit -m "feat: add edit button, right-click menu, multi-select update, framework toggle hide"
```

---

## Self-Review チェックリスト

- [x] **Spec coverage:** A（IsLibrary読み取り・カテゴリバッジ）→ Task 1,2,3。B（フレームワーク強制有効）→ Task 2,3。C（個別編集・削除・右クリック）→ Task 5,6。D（複数選択更新）→ Task 3,6。E（強制再取得）→ Task 3。F（トレイ配色）→ Task 4。全て対応済み。
- [x] **Placeholder scan:** 「TBD」「TODO」「実装は後で」なし。
- [x] **Type consistency:** `RefreshSelectedAction: Func<IReadOnlyList<string>, Task>?` — Task 2 で定義、Task 3 で登録、Task 5/6 で呼び出し。型一致。`AppConfigWriter.RemoveMod(string, string, string)` — Task 2 で定義、Task 5 で使用。引数一致。`MainViewModel.Reload()` — Task 2 で定義、Task 5 で使用。
