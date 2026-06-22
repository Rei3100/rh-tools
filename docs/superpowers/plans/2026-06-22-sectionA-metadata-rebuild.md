# セクションA：データ取得・翻訳の作り直し 実装計画

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** GameBanana からの取得を実際に動く状態にし、カテゴリ・作者・URL欠落補完・全件強制取り直し・逐次反映・更新レポート（見える化）を実装する。

**Architecture:** Core 層に取得パイプライン（`GameBananaClient` 修正＋`GameRegistry`＋`ModMetadataRefresher`）を組み、App 層は1件処理ごとに UI へ逐次反映する。実行時に外部 AI を一切呼ばず、GameBanana 公開 API と Google 翻訳の無料エンドポイントのみ使用。

**Tech Stack:** C# / .NET 10 / WPF、System.Text.Json、xUnit。HTTP は `FakeHttpMessageHandler`（既存）でモック。

## Global Constraints

- ランタイム NuGet 追加禁止（System.Text.Json のみ可）。テスト用 xUnit は可。
- ユーザーデータは `%APPDATA%\ReloadedHelper` 以外に保存禁止。
- 実行時に Claude／外部 AI を呼ばない。
- 失敗を握りつぶさない（必ずレポートに理由を残す）。
- 自動修正の前提（このセクションでは ModConfig.json 上書き）はユーザー合意済み・バックアップなし。
- 確定値：GameBanana ゲームID … P5R(`p5r.exe`)=`16951` / P4G(`p4g.exe`)=`17755`,`8263` / P5S(`p5s.exe`)=`9099`。
- GameBanana エンドポイント（実機確認済み）：
  - Fetch: `https://gamebanana.com/apiv11/Mod/{id}?_csvProperties=_sName,_sText,_aCategory,_aGame,_aSubmitter` → オブジェクト（`_sName`,`_sText`(HTML混じり),`_aCategory._sName`,`_aGame._idRow`,`_aSubmitter._sName`）。
  - Search: `https://gamebanana.com/apiv11/Util/Search/Results?_sSearchString={q}&_idGameRow={gameId}&_sModelName=Mod&_nPage=1` → `{"_aRecords":[{"_idRow":..,"_sName":".."}]}`。

---

## ファイル構成

| 区分 | パス | 役割 |
|------|------|------|
| 変更 | `src/ReloadedHelper.Core/GameBananaClient.cs` | エンドポイント・パース修正、作者取得、`IGameBananaSource` 実装 |
| 変更 | `src/ReloadedHelper.Core/Models.cs` | `GameBananaModInfo` に `Author` 追加 |
| 新規 | `src/ReloadedHelper.Core/GameRegistry.cs` | AppId→GameBanana ゲームID（内蔵） |
| 変更 | `src/ReloadedHelper.Core/UserData.cs` | `ModUserData` に `OriginalName`/`OriginalDescription`/`Author` 追加 |
| 新規 | `src/ReloadedHelper.Core/HtmlText.cs` | HTML タグ除去（翻訳前処理） |
| 変更 | `src/ReloadedHelper.Core/TranslationService.cs` | `ITranslator` 実装 |
| 新規 | `src/ReloadedHelper.Core/IMetadataSources.cs` | `IGameBananaSource`・`ITranslator` 定義 |
| 新規 | `src/ReloadedHelper.Core/ModMetadataRefresher.cs` | 1件分の取得・翻訳パイプライン＋結果 |
| 新規 | `src/ReloadedHelper.Core/UpdateReportLog.cs` | 更新レポートのファイル追記 |
| 変更 | `src/ReloadedHelper.Core/ModConfigUpdater.cs` | `Write` に作者上書き引数追加 |
| 変更 | `src/ReloadedHelper.Core/MainViewModel.cs` | `ApplyMetadataToRow` 逐次反映メソッド追加 |
| 変更 | `src/ReloadedHelper.App/App.xaml.cs` | 取得処理を refresher 経由に。逐次反映・レポート・`Reload()` 使用へ |
| 変更 | `src/ReloadedHelper.App/Views/ModListView.xaml(.cs)` | 「全件強制取り直し」ボタン＋更新レポートパネル |
| 変更 | `tests/ReloadedHelper.Core.Tests/GameBananaClientTests.cs` | 新形式に書き換え |
| 新規 | `tests/ReloadedHelper.Core.Tests/GameRegistryTests.cs` 他 | 各新規クラスのテスト |

---

### Task A1: GameBananaClient.FetchAsync を apiv11 オブジェクト形式に修正＋作者取得

**Files:**
- Modify: `src/ReloadedHelper.Core/Models.cs`（`GameBananaModInfo`）
- Modify: `src/ReloadedHelper.Core/GameBananaClient.cs:7,21-70`
- Test: `tests/ReloadedHelper.Core.Tests/GameBananaClientTests.cs:17-44`

**Interfaces:**
- Produces: `record GameBananaModInfo(string Name, string Text, string? Category, string GameId, string? Author)`、`Task<GameBananaModInfo?> GameBananaClient.FetchAsync(string gbId, CancellationToken ct = default)`

- [ ] **Step 1: `GameBananaModInfo` に `Author` を追加**

`Models.cs` の既存定義（現状 `GameBananaClient.cs` 内にある `public sealed record GameBananaModInfo(string Name, string Text, string? Category, string GameId);` を `Models.cs` へ移し）を次に変更：

```csharp
public sealed record GameBananaModInfo(string Name, string Text, string? Category, string GameId, string? Author);
```

（`GameBananaClient.cs:7` の旧定義は削除する。）

- [ ] **Step 2: Fetch のテストを新形式に書き換え（失敗するテスト）**

`GameBananaClientTests.cs` の `FetchAsync_parses_profile_page_response` を置換：

```csharp
[Fact]
public async Task FetchAsync_parses_apiv11_object_response()
{
    var json = """
    {"_sName":"Persona 5 2016 Beta Lavenza",
     "_sText":"This mod restores Lavenza's old beta model.<br><br>Warning",
     "_aCategory":{"_sName":"Characters"},
     "_aGame":{"_idRow":16951},
     "_aSubmitter":{"_sName":"lonelycrow"}}
    """;
    var handler = new FakeHttpMessageHandler(json);
    var client = new GameBananaClient(new System.Net.Http.HttpClient(handler));

    var result = await client.FetchAsync("491359");

    Assert.NotNull(result);
    Assert.Equal("Persona 5 2016 Beta Lavenza", result!.Name);
    Assert.StartsWith("This mod restores", result.Text);
    Assert.Equal("Characters", result.Category);
    Assert.Equal("16951", result.GameId);
    Assert.Equal("lonelycrow", result.Author);
    Assert.Contains("gamebanana.com/apiv11/Mod/491359", handler.LastRequestUri);
    Assert.DoesNotContain("api.gamebanana.com", handler.LastRequestUri);
}
```

- [ ] **Step 3: テスト失敗を確認**

Run: `dotnet test --filter FetchAsync_parses_apiv11_object_response`
Expected: FAIL（旧 URL／旧パース）

- [ ] **Step 4: `FetchAsync` と `ParseModInfo` を実装**

`GameBananaClient.cs` を次に変更：

```csharp
public async Task<GameBananaModInfo?> FetchAsync(string gbId, CancellationToken ct = default)
{
    var url = $"https://gamebanana.com/apiv11/Mod/{gbId}" +
              "?_csvProperties=_sName,_sText,_aCategory,_aGame,_aSubmitter";
    try
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(ApiTimeout);
        var json = await http.GetStringAsync(url, cts.Token);
        return ParseModInfo(json);
    }
    catch { return null; }
}

private static GameBananaModInfo? ParseModInfo(string json)
{
    try
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) return null;

        var name = GetStr(root, "_sName") ?? "";
        var text = GetStr(root, "_sText") ?? "";
        string? category = root.TryGetProperty("_aCategory", out var cat) && cat.ValueKind == JsonValueKind.Object
            ? GetStr(cat, "_sName") : null;
        string gameId = "";
        if (root.TryGetProperty("_aGame", out var game) && game.ValueKind == JsonValueKind.Object
            && game.TryGetProperty("_idRow", out var gid))
            gameId = gid.ValueKind == JsonValueKind.Number ? gid.GetInt64().ToString() : (gid.GetString() ?? "");
        string? author = root.TryGetProperty("_aSubmitter", out var sub) && sub.ValueKind == JsonValueKind.Object
            ? GetStr(sub, "_sName") : null;

        return new GameBananaModInfo(name, text, category, gameId, author);
    }
    catch (JsonException) { return null; }
}

private static string? GetStr(JsonElement el, string name) =>
    el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
```

- [ ] **Step 5: テスト合格を確認**

Run: `dotnet test --filter FetchAsync`
Expected: PASS（`FetchAsync_returns_null_on_failure` も維持）

- [ ] **Step 6: コミット**

```bash
git add src/ReloadedHelper.Core/Models.cs src/ReloadedHelper.Core/GameBananaClient.cs tests/ReloadedHelper.Core.Tests/GameBananaClientTests.cs
git commit -m "fix: GameBananaClient.FetchAsync uses apiv11 object format + author"
```

---

### Task A2: GameBananaClient.SearchAsync を apiv11 `_aRecords` 形式に修正

**Files:**
- Modify: `src/ReloadedHelper.Core/GameBananaClient.cs:35-104`
- Test: `tests/ReloadedHelper.Core.Tests/GameBananaClientTests.cs:46-82`

**Interfaces:**
- Produces: `Task<(string GbId, string GbGameId)?> SearchAsync(string modName, string gbGameId, CancellationToken ct = default)`（シグネチャ不変、URL とパースのみ変更）

- [ ] **Step 1: Search のテストを新形式に書き換え（失敗するテスト）**

`SearchAsync_returns_best_match_above_80_percent` と `_returns_null_when_no_match...` と `_returns_null_on_empty_results` の JSON を `_aRecords` 形式へ：

```csharp
[Fact]
public async Task SearchAsync_returns_best_match_above_80_percent()
{
    var json = """{"_aRecords":[{"_idRow":123456,"_sName":"CRI FileSystem V2 Hook"}]}""";
    var handler = new FakeHttpMessageHandler(json);
    var client = new GameBananaClient(new System.Net.Http.HttpClient(handler));

    var result = await client.SearchAsync("CRI FileSystem V2 Hook", "8809");

    Assert.NotNull(result);
    Assert.Equal("123456", result!.Value.GbId);
    Assert.Equal("8809", result.Value.GbGameId);
    Assert.Contains("_sSearchString=", handler.LastRequestUri);
    Assert.Contains("_idGameRow=8809", handler.LastRequestUri);
}

[Fact]
public async Task SearchAsync_returns_null_when_no_match_above_threshold()
{
    var json = """{"_aRecords":[{"_idRow":1,"_sName":"Completely Different Mod"}]}""";
    var client = new GameBananaClient(new System.Net.Http.HttpClient(new FakeHttpMessageHandler(json)));
    Assert.Null(await client.SearchAsync("My Unique Mod Name", "8809"));
}

[Fact]
public async Task SearchAsync_returns_null_on_empty_results()
{
    var json = """{"_aRecords":[]}""";
    var client = new GameBananaClient(new System.Net.Http.HttpClient(new FakeHttpMessageHandler(json)));
    Assert.Null(await client.SearchAsync("Any Mod", "8809"));
}
```

- [ ] **Step 2: テスト失敗を確認**

Run: `dotnet test --filter SearchAsync`
Expected: FAIL

- [ ] **Step 3: `SearchAsync` の URL と `ParseSearchResult` を実装**

```csharp
public async Task<(string GbId, string GbGameId)?> SearchAsync(
    string modName, string gbGameId, CancellationToken ct = default)
{
    var q = Uri.EscapeDataString(modName);
    var url = $"https://gamebanana.com/apiv11/Util/Search/Results" +
              $"?_sSearchString={q}&_idGameRow={gbGameId}&_sModelName=Mod&_nPage=1";
    try
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(ApiTimeout);
        var json = await http.GetStringAsync(url, cts.Token);
        return ParseSearchResult(json, modName, gbGameId);
    }
    catch { return null; }
}

private static (string GbId, string GbGameId)? ParseSearchResult(
    string json, string modName, string gbGameId)
{
    try
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("_aRecords", out var arr) ||
            arr.ValueKind != JsonValueKind.Array) return null;

        string? bestId = null;
        double bestScore = 0;
        foreach (var item in arr.EnumerateArray())
        {
            if (!item.TryGetProperty("_idRow", out var idProp)) continue;
            if (!item.TryGetProperty("_sName", out var nameProp)) continue;
            var id = idProp.ValueKind == JsonValueKind.Number
                ? idProp.GetInt64().ToString() : idProp.GetString() ?? "";
            var score = Similarity(modName, nameProp.GetString() ?? "");
            if (score > bestScore) { bestScore = score; bestId = id; }
        }
        if (bestScore >= SimilarityThreshold && bestId is not null)
            return (bestId, gbGameId);
        return null;
    }
    catch (JsonException) { return null; }
}
```

（`Similarity`/`Normalize`/`LevenshteinDistance`/`SimilarityThreshold` は既存のまま流用。）

- [ ] **Step 4: テスト合格を確認**

Run: `dotnet test --filter SearchAsync`
Expected: PASS

- [ ] **Step 5: コミット**

```bash
git add src/ReloadedHelper.Core/GameBananaClient.cs tests/ReloadedHelper.Core.Tests/GameBananaClientTests.cs
git commit -m "fix: GameBananaClient.SearchAsync uses apiv11 _aRecords format"
```

---

### Task A3: GameRegistry（内蔵ゲームID）

**Files:**
- Create: `src/ReloadedHelper.Core/GameRegistry.cs`
- Test: `tests/ReloadedHelper.Core.Tests/GameRegistryTests.cs`

**Interfaces:**
- Produces: `IReadOnlyList<string> GameRegistry.GameIdsFor(IEnumerable<string> appIds)`（マッチした AppId に対応する GameBanana ゲームID群を重複なしで返す）

- [ ] **Step 1: 失敗するテスト**

```csharp
namespace ReloadedHelper.Core.Tests;

public class GameRegistryTests
{
    [Fact]
    public void GameIdsFor_returns_ids_for_known_appids()
    {
        Assert.Equal(new[] { "16951" }, GameRegistry.GameIdsFor(new[] { "p5r.exe" }));
        Assert.Equal(new[] { "17755", "8263" }, GameRegistry.GameIdsFor(new[] { "p4g.exe" }));
        Assert.Equal(new[] { "9099" }, GameRegistry.GameIdsFor(new[] { "P5S.EXE" })); // 大文字小文字無視
    }

    [Fact]
    public void GameIdsFor_returns_empty_for_unknown()
    {
        Assert.Empty(GameRegistry.GameIdsFor(new[] { "unknown.exe" }));
    }

    [Fact]
    public void GameIdsFor_dedupes_across_appids()
    {
        var ids = GameRegistry.GameIdsFor(new[] { "p5r.exe", "p5r.exe" });
        Assert.Equal(new[] { "16951" }, ids);
    }
}
```

- [ ] **Step 2: テスト失敗を確認**

Run: `dotnet test --filter GameRegistry`
Expected: FAIL（型なし）

- [ ] **Step 3: 実装**

```csharp
namespace ReloadedHelper.Core;

public static class GameRegistry
{
    // AppId（小文字）→ GameBanana ゲームID（複数可・優先順）
    private static readonly Dictionary<string, string[]> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        ["p5r.exe"] = new[] { "16951" },
        ["p4g.exe"] = new[] { "17755", "8263" },
        ["p5s.exe"] = new[] { "9099" },
    };

    public static IReadOnlyList<string> GameIdsFor(IEnumerable<string> appIds)
    {
        var result = new List<string>();
        foreach (var appId in appIds)
            if (Map.TryGetValue(appId, out var ids))
                foreach (var id in ids)
                    if (!result.Contains(id)) result.Add(id);
        return result;
    }
}
```

- [ ] **Step 4: テスト合格を確認**

Run: `dotnet test --filter GameRegistry`
Expected: PASS

- [ ] **Step 5: コミット**

```bash
git add src/ReloadedHelper.Core/GameRegistry.cs tests/ReloadedHelper.Core.Tests/GameRegistryTests.cs
git commit -m "feat: add GameRegistry with built-in GameBanana game ids"
```

---

### Task A4: ModUserData に OriginalName/OriginalDescription/Author 追加

**Files:**
- Modify: `src/ReloadedHelper.Core/UserData.cs:6-18`
- Test: `tests/ReloadedHelper.Core.Tests/UserDataTests.cs`（無ければ作成）

**Interfaces:**
- Produces: `ModUserData.OriginalName`/`OriginalDescription`/`Author`（すべて `string?`）

- [ ] **Step 1: 失敗するテスト（ラウンドトリップ）**

`UserDataTests.cs`（無ければ新規）に追加：

```csharp
using System.IO;
namespace ReloadedHelper.Core.Tests;

public class UserDataTests
{
    [Fact]
    public void Roundtrip_preserves_new_fields()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ud-{Guid.NewGuid():N}.json");
        try
        {
            var file = new UserDataFile();
            file.Mods["m1"] = new ModUserData
            {
                OriginalName = "Beta Lavenza",
                OriginalDescription = "restores model",
                Author = "lonelycrow"
            };
            UserDataStore.Save(path, file);
            var loaded = UserDataStore.Load(path);

            Assert.Equal("Beta Lavenza", loaded.Mods["m1"].OriginalName);
            Assert.Equal("restores model", loaded.Mods["m1"].OriginalDescription);
            Assert.Equal("lonelycrow", loaded.Mods["m1"].Author);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
```

- [ ] **Step 2: テスト失敗を確認**

Run: `dotnet test --filter Roundtrip_preserves_new_fields`
Expected: FAIL（プロパティなし）

- [ ] **Step 3: フィールド追加**

`UserData.cs` の `ModUserData` に追加：

```csharp
    // セクションA 追加
    public string? OriginalName        { get; set; }
    public string? OriginalDescription { get; set; }
    public string? Author              { get; set; }
```

- [ ] **Step 4: テスト合格を確認**

Run: `dotnet test --filter Roundtrip_preserves_new_fields`
Expected: PASS

- [ ] **Step 5: コミット**

```bash
git add src/ReloadedHelper.Core/UserData.cs tests/ReloadedHelper.Core.Tests/UserDataTests.cs
git commit -m "feat: add OriginalName/OriginalDescription/Author to ModUserData"
```

---

### Task A5: HtmlText（HTML タグ除去）

**Files:**
- Create: `src/ReloadedHelper.Core/HtmlText.cs`
- Test: `tests/ReloadedHelper.Core.Tests/HtmlTextTests.cs`

**Interfaces:**
- Produces: `string HtmlText.Strip(string html)`（`<br>` を改行に、その他タグ除去、HTML エンティティ `&amp; &lt; &gt; &quot; &#39;` を復元、前後空白除去）

- [ ] **Step 1: 失敗するテスト**

```csharp
namespace ReloadedHelper.Core.Tests;

public class HtmlTextTests
{
    [Theory]
    [InlineData("Hello<br><br>World", "Hello\n\nWorld")]
    [InlineData("<b>Bold</b> text", "Bold text")]
    [InlineData("A &amp; B &lt;tag&gt;", "A & B <tag>")]
    [InlineData("  spaced  ", "spaced")]
    public void Strip_removes_tags_and_decodes(string input, string expected)
    {
        Assert.Equal(expected, HtmlText.Strip(input));
    }
}
```

- [ ] **Step 2: テスト失敗を確認**

Run: `dotnet test --filter HtmlText`
Expected: FAIL

- [ ] **Step 3: 実装**

```csharp
using System.Text.RegularExpressions;

namespace ReloadedHelper.Core;

public static class HtmlText
{
    public static string Strip(string html)
    {
        if (string.IsNullOrEmpty(html)) return html ?? "";
        var s = Regex.Replace(html, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, "<[^>]+>", "");
        s = s.Replace("&amp;", "&").Replace("&lt;", "<").Replace("&gt;", ">")
             .Replace("&quot;", "\"").Replace("&#39;", "'");
        return s.Trim();
    }
}
```

- [ ] **Step 4: テスト合格を確認**

Run: `dotnet test --filter HtmlText`
Expected: PASS

- [ ] **Step 5: コミット**

```bash
git add src/ReloadedHelper.Core/HtmlText.cs tests/ReloadedHelper.Core.Tests/HtmlTextTests.cs
git commit -m "feat: add HtmlText.Strip for cleaning GameBanana description"
```

---

### Task A6: ソース用インターフェース＋既存クラスへの実装

**Files:**
- Create: `src/ReloadedHelper.Core/IMetadataSources.cs`
- Modify: `src/ReloadedHelper.Core/GameBananaClient.cs`（`: IGameBananaSource`、インスタンス版 `ExtractIdFromUrl` 追加）
- Modify: `src/ReloadedHelper.Core/TranslationService.cs:6`（`: ITranslator`）

**Interfaces:**
- Produces:
```csharp
public interface IGameBananaSource
{
    string? ExtractId(string? url);
    Task<(string GbId, string GbGameId)?> SearchAsync(string modName, string gbGameId, CancellationToken ct = default);
    Task<GameBananaModInfo?> FetchAsync(string gbId, CancellationToken ct = default);
}
public interface ITranslator
{
    Task<string> TranslateAsync(string text, string targetLang, CancellationToken ct = default);
}
```

- [ ] **Step 1: インターフェース定義を作成**

`IMetadataSources.cs` に上記 2 インターフェースをそのまま記述（`namespace ReloadedHelper.Core;`）。

- [ ] **Step 2: GameBananaClient に実装を追加**

`GameBananaClient` 宣言を `public sealed class GameBananaClient(HttpClient http) : IGameBananaSource` に変更し、インスタンス版を追加（既存 static はそのまま残す）：

```csharp
public string? ExtractId(string? url) => ExtractIdFromUrl(url);
```

（`SearchAsync`・`FetchAsync` は既存シグネチャがそのままインターフェースを満たす。）

- [ ] **Step 3: TranslationService に実装を宣言**

`public sealed class TranslationService(HttpClient http, TimeSpan? requestDelay = null) : ITranslator`（メソッドは既存のまま）。

- [ ] **Step 4: ビルド確認**

Run: `dotnet build reloaded-helper.slnx`
Expected: 0 errors

- [ ] **Step 5: コミット**

```bash
git add src/ReloadedHelper.Core/IMetadataSources.cs src/ReloadedHelper.Core/GameBananaClient.cs src/ReloadedHelper.Core/TranslationService.cs
git commit -m "feat: add IGameBananaSource/ITranslator interfaces"
```

---

### Task A7: ModMetadataRefresher（1件分の取得・翻訳パイプライン）

**Files:**
- Create: `src/ReloadedHelper.Core/ModMetadataRefresher.cs`
- Test: `tests/ReloadedHelper.Core.Tests/ModMetadataRefresherTests.cs`

**Interfaces:**
- Consumes: `IGameBananaSource`, `ITranslator`（Task A6）, `GameRegistry`（A3）, `HtmlText`（A5）, `GlossaryProvider.Apply`（既存）, `ModInfo`/`ModUserData`（既存・A4）
- Produces:
```csharp
public enum RefreshStatus { GbMatched, TranslatedOnly, Failed }
public sealed record MetadataRefreshResult(
    string ModId, RefreshStatus Status, string? GbId,
    string JaName, string JaDesc, string? Category, string? Author, string Reason);

public sealed class ModMetadataRefresher(IGameBananaSource gb, ITranslator tr)
{
    public Task<MetadataRefreshResult> RefreshAsync(ModInfo mod, ModUserData ud, CancellationToken ct = default);
}
```
`RefreshAsync` は副作用として `ud.OriginalName`/`ud.OriginalDescription` を未設定時にスナップショットする。

- [ ] **Step 1: 失敗するテスト（GB一致経路）**

`ModMetadataRefresherTests.cs`：

```csharp
namespace ReloadedHelper.Core.Tests;

public class ModMetadataRefresherTests
{
    private static ModInfo Mod(string id, string name, string desc = "", string? url = null,
        string[]? appIds = null) =>
        new(id, name, "", "1.0.0", desc, Array.Empty<string>(), Array.Empty<string>(),
            Array.Empty<string>(), appIds ?? new[] { "p5r.exe" }, url, null, null, null, "C:\\x");

    // テスト用フェイク
    private sealed class FakeGb : IGameBananaSource
    {
        public string? IdFromUrl; public (string, string)? SearchHit; public GameBananaModInfo? FetchResult;
        public string? LastSearchName;
        public string? ExtractId(string? url) => IdFromUrl;
        public Task<(string GbId, string GbGameId)?> SearchAsync(string n, string g, CancellationToken ct = default)
        { LastSearchName = n; return Task.FromResult(SearchHit); }
        public Task<GameBananaModInfo?> FetchAsync(string id, CancellationToken ct = default)
            => Task.FromResult(FetchResult);
    }
    private sealed class FakeTr : ITranslator
    {
        public Task<string> TranslateAsync(string t, string lang, CancellationToken ct = default)
            => Task.FromResult("[訳]" + t);
    }

    [Fact]
    public async Task GbMatched_uses_gamebanana_data()
    {
        var gb = new FakeGb
        {
            IdFromUrl = "491359",
            FetchResult = new GameBananaModInfo("Beta Lavenza", "restores model<br>warn", "Skin", "16951", "lonelycrow")
        };
        var sut = new ModMetadataRefresher(gb, new FakeTr());
        var ud = new ModUserData();

        var r = await sut.RefreshAsync(Mod("m1", "ベータ ラヴェンツァ", url: "https://gamebanana.com/mods/491359"), ud);

        Assert.Equal(RefreshStatus.GbMatched, r.Status);
        Assert.Equal("491359", r.GbId);
        Assert.Equal("[訳]Beta Lavenza", r.JaName);
        Assert.Equal("[訳]restores model\nwarn", r.JaDesc); // HTML 除去後に翻訳
        Assert.Equal("Skin", r.Category);
        Assert.Equal("lonelycrow", r.Author);
    }

    [Fact]
    public async Task NoMatch_falls_back_to_translation_only_and_snapshots_original()
    {
        var gb = new FakeGb { IdFromUrl = null, SearchHit = null };
        var sut = new ModMetadataRefresher(gb, new FakeTr());
        var ud = new ModUserData();

        var r = await sut.RefreshAsync(Mod("m2", "Cool English Name", "english desc"), ud);

        Assert.Equal(RefreshStatus.TranslatedOnly, r.Status);
        Assert.Null(r.GbId);
        Assert.Equal("[訳]Cool English Name", r.JaName);
        Assert.Equal("Cool English Name", ud.OriginalName);       // スナップショット
        Assert.Equal("english desc", ud.OriginalDescription);
    }

    [Fact]
    public async Task Search_uses_english_modid_when_name_is_japanese()
    {
        var gb = new FakeGb { IdFromUrl = null, SearchHit = ("777", "16951") };
        var sut = new ModMetadataRefresher(gb, new FakeTr());
        // 現在名が日本語、OriginalName 未設定 → 検索キーは ModId
        await sut.RefreshAsync(Mod("P5R.CostumeFramework", "コスチューム"), new ModUserData());
        Assert.Equal("P5R.CostumeFramework", gb.LastSearchName);
    }
}
```

- [ ] **Step 2: テスト失敗を確認**

Run: `dotnet test --filter ModMetadataRefresher`
Expected: FAIL（型なし）

- [ ] **Step 3: 実装**

```csharp
namespace ReloadedHelper.Core;

public enum RefreshStatus { GbMatched, TranslatedOnly, Failed }

public sealed record MetadataRefreshResult(
    string ModId, RefreshStatus Status, string? GbId,
    string JaName, string JaDesc, string? Category, string? Author, string Reason);

public sealed class ModMetadataRefresher(IGameBananaSource gb, ITranslator tr)
{
    public async Task<MetadataRefreshResult> RefreshAsync(
        ModInfo mod, ModUserData ud, CancellationToken ct = default)
    {
        // 英語原本を未設定時にスナップショット
        ud.OriginalName ??= mod.ModName;
        ud.OriginalDescription ??= mod.ModDescription;

        // ── GB ID の決定 ──
        var gbId = ud.GameBananaId
                   ?? gb.ExtractId(ud.UrlOverride)
                   ?? gb.ExtractId(mod.ProjectUrl);

        if (gbId is null)
        {
            var key = SearchKey(ud, mod);
            foreach (var gameId in GameRegistry.GameIdsFor(mod.SupportedAppIds))
            {
                var hit = await gb.SearchAsync(key, gameId, ct);
                if (hit is not null) { gbId = hit.Value.GbId; break; }
            }
        }

        // ── GB 一致時：GB データを翻訳 ──
        if (gbId is not null)
        {
            var info = await gb.FetchAsync(gbId, ct);
            if (info is not null)
            {
                var name = ApplyGlossary(await tr.TranslateAsync(info.Name, "ja", ct), mod);
                var desc = ApplyGlossary(await tr.TranslateAsync(HtmlText.Strip(info.Text), "ja", ct), mod);
                return new MetadataRefreshResult(mod.ModId, RefreshStatus.GbMatched, gbId,
                    name, desc, info.Category, info.Author, "GB一致");
            }
            // フェッチ失敗 → 翻訳フォールバックへ（理由を残す）
            var fn = ApplyGlossary(await tr.TranslateAsync(ud.OriginalName ?? mod.ModName, "ja", ct), mod);
            var fd = ApplyGlossary(await tr.TranslateAsync(ud.OriginalDescription ?? mod.ModDescription, "ja", ct), mod);
            return new MetadataRefreshResult(mod.ModId, RefreshStatus.Failed, gbId,
                fn, fd, null, null, "GB取得失敗(翻訳のみ適用)");
        }

        // ── マッチなし → 翻訳のみ ──
        var jn = ApplyGlossary(await tr.TranslateAsync(ud.OriginalName ?? mod.ModName, "ja", ct), mod);
        var jd = ApplyGlossary(await tr.TranslateAsync(ud.OriginalDescription ?? mod.ModDescription, "ja", ct), mod);
        return new MetadataRefreshResult(mod.ModId, RefreshStatus.TranslatedOnly, null,
            jn, jd, null, null, "GB未一致(翻訳のみ)");
    }

    private static string SearchKey(ModUserData ud, ModInfo mod)
    {
        var name = ud.OriginalName;
        if (!string.IsNullOrWhiteSpace(name) && IsMostlyAscii(name)) return name;
        return mod.ModId;
    }

    private static bool IsMostlyAscii(string s)
    {
        int ascii = s.Count(c => c < 128);
        return s.Length > 0 && (double)ascii / s.Length >= 0.7;
    }

    private static string ApplyGlossary(string text, ModInfo mod)
    {
        foreach (var appId in mod.SupportedAppIds)
            text = GlossaryProvider.Apply(text, appId);
        return text;
    }
}
```

- [ ] **Step 4: テスト合格を確認**

Run: `dotnet test --filter ModMetadataRefresher`
Expected: PASS（3件）

- [ ] **Step 5: コミット**

```bash
git add src/ReloadedHelper.Core/ModMetadataRefresher.cs tests/ReloadedHelper.Core.Tests/ModMetadataRefresherTests.cs
git commit -m "feat: add ModMetadataRefresher pipeline (URL/search/fetch/translate)"
```

---

### Task A8: UpdateReportLog（更新レポートのファイル追記）

**Files:**
- Create: `src/ReloadedHelper.Core/UpdateReportLog.cs`
- Test: `tests/ReloadedHelper.Core.Tests/UpdateReportLogTests.cs`

**Interfaces:**
- Consumes: `MetadataRefreshResult`（A7）
- Produces:
```csharp
public static class UpdateReportLog
{
    public static string DefaultPath { get; }              // %APPDATA%\ReloadedHelper\update-log.txt
    public static string Format(MetadataRefreshResult r);  // 1行テキスト
    public static void Append(string path, IEnumerable<MetadataRefreshResult> results); // ヘッダ(日時)+各行を追記
}
```

- [ ] **Step 1: 失敗するテスト**

```csharp
using System.IO;
namespace ReloadedHelper.Core.Tests;

public class UpdateReportLogTests
{
    [Fact]
    public void Format_includes_modid_status_reason()
    {
        var r = new MetadataRefreshResult("m1", RefreshStatus.GbMatched, "491359",
            "名前", "説明", "Skin", "author", "GB一致");
        var line = UpdateReportLog.Format(r);
        Assert.Contains("m1", line);
        Assert.Contains("GB一致", line);
        Assert.Contains("491359", line);
    }

    [Fact]
    public void Append_writes_lines_to_file()
    {
        var path = Path.Combine(Path.GetTempPath(), $"log-{Guid.NewGuid():N}.txt");
        try
        {
            UpdateReportLog.Append(path, new[]
            {
                new MetadataRefreshResult("m1", RefreshStatus.GbMatched, "1", "", "", null, null, "GB一致"),
                new MetadataRefreshResult("m2", RefreshStatus.Failed, null, "", "", null, null, "GB取得失敗"),
            });
            var text = File.ReadAllText(path);
            Assert.Contains("m1", text);
            Assert.Contains("m2", text);
            Assert.Contains("GB取得失敗", text);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
```

- [ ] **Step 2: テスト失敗を確認**

Run: `dotnet test --filter UpdateReportLog`
Expected: FAIL

- [ ] **Step 3: 実装**

```csharp
using System.IO;
using System.Text;

namespace ReloadedHelper.Core;

public static class UpdateReportLog
{
    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                     "ReloadedHelper", "update-log.txt");

    public static string Format(MetadataRefreshResult r)
    {
        var id = r.GbId is null ? "" : $" gb={r.GbId}";
        return $"[{r.Status}] {r.ModId}{id} — {r.Reason}";
    }

    public static void Append(string path, IEnumerable<MetadataRefreshResult> results)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var sb = new StringBuilder();
        sb.AppendLine($"=== {DateTime.Now:yyyy-MM-dd HH:mm:ss} 更新 ===");
        foreach (var r in results) sb.AppendLine(Format(r));
        File.AppendAllText(path, sb.ToString());
    }
}
```

- [ ] **Step 4: テスト合格を確認**

Run: `dotnet test --filter UpdateReportLog`
Expected: PASS

- [ ] **Step 5: コミット**

```bash
git add src/ReloadedHelper.Core/UpdateReportLog.cs tests/ReloadedHelper.Core.Tests/UpdateReportLogTests.cs
git commit -m "feat: add UpdateReportLog for visible refresh results"
```

---

### Task A9: 処理対象の選別（全件強制 vs 差分）

**Files:**
- Create: `src/ReloadedHelper.Core/RefreshSelector.cs`
- Test: `tests/ReloadedHelper.Core.Tests/RefreshSelectorTests.cs`

**Interfaces:**
- Produces:
```csharp
public static class RefreshSelector
{
    // force=true: 全件。force=false: userdata 未登録 or FetchedVersion != 現バージョン のみ
    public static IReadOnlyList<ModInfo> Select(
        IEnumerable<ModInfo> catalog, UserDataFile userData, bool force);
}
```

- [ ] **Step 1: 失敗するテスト**

```csharp
namespace ReloadedHelper.Core.Tests;

public class RefreshSelectorTests
{
    private static ModInfo Mod(string id, string ver) =>
        new(id, id, "", ver, "", Array.Empty<string>(), Array.Empty<string>(),
            Array.Empty<string>(), new[] { "p5r.exe" }, null, null, null, null, "C:\\x");

    [Fact]
    public void Force_selects_all()
    {
        var cat = new[] { Mod("a", "1.0"), Mod("b", "1.0") };
        var ud = new UserDataFile();
        ud.Mods["a"] = new ModUserData { FetchedVersion = "1.0" };
        Assert.Equal(2, RefreshSelector.Select(cat, ud, force: true).Count);
    }

    [Fact]
    public void Incremental_selects_new_and_version_changed_only()
    {
        var cat = new[] { Mod("a", "1.0"), Mod("b", "2.0"), Mod("c", "1.0") };
        var ud = new UserDataFile();
        ud.Mods["a"] = new ModUserData { FetchedVersion = "1.0" }; // 変化なし → 除外
        ud.Mods["b"] = new ModUserData { FetchedVersion = "1.0" }; // 変化あり → 対象
        // c は未登録 → 対象
        var ids = RefreshSelector.Select(cat, ud, force: false).Select(m => m.ModId).ToList();
        Assert.Equal(new[] { "b", "c" }, ids);
    }
}
```

- [ ] **Step 2: テスト失敗を確認**

Run: `dotnet test --filter RefreshSelector`
Expected: FAIL

- [ ] **Step 3: 実装**

```csharp
namespace ReloadedHelper.Core;

public static class RefreshSelector
{
    public static IReadOnlyList<ModInfo> Select(
        IEnumerable<ModInfo> catalog, UserDataFile userData, bool force)
    {
        var result = new List<ModInfo>();
        foreach (var mod in catalog)
        {
            if (force) { result.Add(mod); continue; }
            if (!userData.Mods.TryGetValue(mod.ModId, out var ud)) { result.Add(mod); continue; }
            if (ud.FetchedVersion != mod.ModVersion) result.Add(mod);
        }
        return result;
    }
}
```

- [ ] **Step 4: テスト合格を確認**

Run: `dotnet test --filter RefreshSelector`
Expected: PASS

- [ ] **Step 5: コミット**

```bash
git add src/ReloadedHelper.Core/RefreshSelector.cs tests/ReloadedHelper.Core.Tests/RefreshSelectorTests.cs
git commit -m "feat: add RefreshSelector (force-all vs incremental)"
```

---

### Task A10: ModConfigUpdater に作者上書きを追加

**Files:**
- Modify: `src/ReloadedHelper.Core/ModConfigUpdater.cs:15-59`
- Test: `tests/ReloadedHelper.Core.Tests/ModConfigUpdaterTests.cs`

**Interfaces:**
- Produces: `void ModConfigUpdater.Write(string modFolderPath, string japaneseName, string japaneseDescription, string? author = null)`（`author` が非 null なら `ModAuthor` を上書き）

- [ ] **Step 1: 失敗するテスト**

`ModConfigUpdaterTests.cs` に追加：

```csharp
[Fact]
public void Write_overwrites_author_when_provided()
{
    var dir = Path.Combine(Path.GetTempPath(), $"mc-{Guid.NewGuid():N}");
    Directory.CreateDirectory(dir);
    try
    {
        File.WriteAllText(Path.Combine(dir, "ModConfig.json"),
            """{"ModId":"x","ModName":"Old","ModAuthor":"OldAuthor","ModDescription":"d"}""");
        ModConfigUpdater.Write(dir, "新名", "新説明", "lonelycrow");
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "ModConfig.json")));
        Assert.Equal("lonelycrow", doc.RootElement.GetProperty("ModAuthor").GetString());
        Assert.Equal("新名", doc.RootElement.GetProperty("ModName").GetString());
    }
    finally { Directory.Delete(dir, true); }
}
```

- [ ] **Step 2: テスト失敗を確認**

Run: `dotnet test --filter Write_overwrites_author_when_provided`
Expected: FAIL（引数なし／コンパイルエラー）

- [ ] **Step 3: 実装**

`Write` のシグネチャに `string? author = null` を追加。`ModName`/`ModDescription` と同様に `ModAuthor` を処理：

```csharp
public static void Write(string modFolderPath, string japaneseName, string japaneseDescription, string? author = null)
{
    var configPath = Path.Combine(modFolderPath, "ModConfig.json");
    if (!File.Exists(configPath)) return;
    try
    {
        var original = File.ReadAllText(configPath);
        using var doc = JsonDocument.Parse(original);
        using var ms = new System.IO.MemoryStream();
        using (var writer = new Utf8JsonWriter(ms, WriterOptions))
        {
            writer.WriteStartObject();
            bool wroteName = false, wroteDesc = false, wroteAuthor = false;
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.Name == "ModName") { writer.WriteString("ModName", japaneseName); wroteName = true; }
                else if (prop.Name == "ModDescription") { writer.WriteString("ModDescription", japaneseDescription); wroteDesc = true; }
                else if (prop.Name == "ModAuthor" && author is not null) { writer.WriteString("ModAuthor", author); wroteAuthor = true; }
                else prop.WriteTo(writer);
            }
            if (!wroteName) writer.WriteString("ModName", japaneseName);
            if (!wroteDesc) writer.WriteString("ModDescription", japaneseDescription);
            if (author is not null && !wroteAuthor) writer.WriteString("ModAuthor", author);
            writer.WriteEndObject();
        }
        File.WriteAllBytes(configPath, ms.ToArray());
    }
    catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException) { }
}
```

- [ ] **Step 4: テスト合格を確認**

Run: `dotnet test --filter ModConfigUpdater`
Expected: PASS（既存テストも維持）

- [ ] **Step 5: コミット**

```bash
git add src/ReloadedHelper.Core/ModConfigUpdater.cs tests/ReloadedHelper.Core.Tests/ModConfigUpdaterTests.cs
git commit -m "feat: ModConfigUpdater can overwrite ModAuthor"
```

---

### Task A11: MainViewModel.ApplyMetadataToRow（逐次反映）

**Files:**
- Modify: `src/ReloadedHelper.Core/MainViewModel.cs`
- Test: `tests/ReloadedHelper.Core.Tests/MainViewModelTests.cs`（無ければ作成）

**Interfaces:**
- Produces: `void MainViewModel.ApplyMetadataToRow(string modId, string jaName, string jaDesc, string? category, string? author)`（`Entries` 内の該当行を更新済みエントリで差し替える。該当なしなら何もしない）

- [ ] **Step 1: 失敗するテスト**

```csharp
namespace ReloadedHelper.Core.Tests;

public class MainViewModelTests
{
    private static ModInfo Info(string id, string name) =>
        new(id, name, "old", "1.0", "olddesc", Array.Empty<string>(), Array.Empty<string>(),
            Array.Empty<string>(), new[] { "p5r.exe" }, null, null, null, null, "C:\\x");

    [Fact]
    public void ApplyMetadataToRow_replaces_matching_entry()
    {
        var vm = new MainViewModel();
        vm.Entries.Add(new ModLoadEntry(1, "m1", Info("m1", "Old"), true));
        vm.Entries.Add(new ModLoadEntry(2, "m2", Info("m2", "Keep"), true));

        vm.ApplyMetadataToRow("m1", "新名", "新説明", "Skin", "author");

        var e = vm.Entries.First(x => x.ModId == "m1");
        Assert.Equal("新名", e.Info!.ModName);
        Assert.Equal("新説明", e.Info.ModDescription);
        Assert.Equal("author", e.Info.ModAuthor);
        Assert.Equal("Skin", e.Category);
        Assert.Equal("Keep", vm.Entries.First(x => x.ModId == "m2").Info!.ModName); // 無関係は不変
    }

    [Fact]
    public void ApplyMetadataToRow_ignores_unknown_id()
    {
        var vm = new MainViewModel();
        vm.Entries.Add(new ModLoadEntry(1, "m1", Info("m1", "Old"), true));
        vm.ApplyMetadataToRow("nope", "x", "y", null, null); // 例外を投げない
        Assert.Single(vm.Entries);
    }
}
```

- [ ] **Step 2: テスト失敗を確認**

Run: `dotnet test --filter ApplyMetadataToRow`
Expected: FAIL

- [ ] **Step 3: 実装**

`MainViewModel` に追加：

```csharp
public void ApplyMetadataToRow(string modId, string jaName, string jaDesc, string? category, string? author)
{
    for (int i = 0; i < Entries.Count; i++)
    {
        var e = Entries[i];
        if (!string.Equals(e.ModId, modId, StringComparison.OrdinalIgnoreCase)) continue;
        var newInfo = e.Info is null
            ? null
            : e.Info with { ModName = jaName, ModDescription = jaDesc, ModAuthor = author ?? e.Info.ModAuthor };
        Entries[i] = e with { Info = newInfo, Category = category ?? e.Category };
        break;
    }
}
```

- [ ] **Step 4: テスト合格を確認**

Run: `dotnet test --filter ApplyMetadataToRow`
Expected: PASS

- [ ] **Step 5: コミット**

```bash
git add src/ReloadedHelper.Core/MainViewModel.cs tests/ReloadedHelper.Core.Tests/MainViewModelTests.cs
git commit -m "feat: MainViewModel.ApplyMetadataToRow for incremental UI reflection"
```

---

### Task A12: App.xaml.cs を新パイプライン＋逐次反映＋レポートに接続

**Files:**
- Modify: `src/ReloadedHelper.App/App.xaml.cs:138-272`（`RefreshModMetadataAsync` 全面差し替え）
- Modify: `src/ReloadedHelper.App/Views/ModListView.xaml`（「全件強制取り直し」ボタン＋レポートパネル）
- Modify: `src/ReloadedHelper.App/Views/ModListView.xaml.cs`（ボタンハンドラ）
- Modify: `src/ReloadedHelper.Core/MainViewModel.cs`（`ForceRefreshAction` プロパティ、`ObservableCollection<string> UpdateReportLines` 追加）

**Interfaces:**
- Consumes: `ModMetadataRefresher`(A7), `RefreshSelector`(A9), `UpdateReportLog`(A8), `GameBananaClient`/`TranslationService`(A6), `MainViewModel.ApplyMetadataToRow`(A11), `MainViewModel.Reload`(既存)
- Produces: `MainViewModel.ForceRefreshAction : Func<Task>?`、`MainViewModel.UpdateReportLines : ObservableCollection<string>`

> このタスクは UI を含むため、ユニットテストではなく **手動検証**（実機起動）で確認する。

- [ ] **Step 1: MainViewModel に強制更新フックとレポート行を追加**

```csharp
public Func<Task>? ForceRefreshAction { get; set; }
public ObservableCollection<string> UpdateReportLines { get; } = new();
```

- [ ] **Step 2: `RefreshModMetadataAsync` を差し替え**

`App.xaml.cs` の現 `RefreshModMetadataAsync`（138-272 行）を次に置換。引数 `bool force` と `targetModIds` を持つ：

```csharp
private static async Task RefreshModMetadataAsync(
    MainViewModel modListVm, ReloadedInstall install,
    bool force, IReadOnlyList<string>? targetModIds = null)
{
    bool started = false;
    Current.Dispatcher.Invoke(() =>
    {
        if (modListVm.IsUpdating) return;
        modListVm.IsUpdating = true; started = true;
        modListVm.UpdateReportLines.Clear();
    });
    if (!started) return;

    var results = new List<MetadataRefreshResult>();
    try
    {
        using var http = new System.Net.Http.HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("rh-tools/1.0");
        var refresher = new ModMetadataRefresher(new GameBananaClient(http), new TranslationService(http));

        var userData = UserDataStore.Load(UserDataStore.DefaultPath);
        var catalog = Current.Dispatcher.Invoke(() => modListVm.AllMods);

        var pool = targetModIds is null
            ? RefreshSelector.Select(catalog.Values, userData, force)
            : catalog.Values.Where(m => targetModIds.Contains(m.ModId, StringComparer.OrdinalIgnoreCase)).ToList();

        int total = pool.Count, processed = 0;
        if (total == 0) return;

        foreach (var mod in pool)
        {
            processed++;
            Current.Dispatcher.Invoke(() => modListVm.UpdateProgress = $"更新中 {processed}/{total} 件...");

            userData.Mods.TryGetValue(mod.ModId, out var modData);
            modData ??= new ModUserData();

            var r = await refresher.RefreshAsync(mod, modData);
            results.Add(r);

            ModConfigUpdater.Write(mod.FolderPath, r.JaName, r.JaDesc, r.Author);

            modData.GameBananaId = r.GbId;
            modData.Category = r.Category;
            modData.Author = r.Author;
            modData.TranslatedName = r.JaName;
            modData.TranslatedDescription = r.JaDesc;
            modData.FetchedAt = DateTime.UtcNow;
            modData.FetchedVersion = mod.ModVersion;
            userData.Mods[mod.ModId] = modData;

            // 1件ごとに即 UI 反映（タブは切り替わらない）
            Current.Dispatcher.Invoke(() =>
            {
                modListVm.ApplyMetadataToRow(mod.ModId, r.JaName, r.JaDesc, r.Category, r.Author);
                modListVm.UpdateReportLines.Add(UpdateReportLog.Format(r));
            });
        }

        UserDataStore.Save(UserDataStore.DefaultPath, userData);
        UpdateReportLog.Append(UpdateReportLog.DefaultPath, results);

        // 最後に整合のため SelectedGame を保持して再読込
        Current.Dispatcher.Invoke(() => modListVm.Reload());
    }
    catch (Exception ex)
    {
        Current.Dispatcher.Invoke(() =>
            modListVm.UpdateReportLines.Add($"[エラー] 更新中に問題が発生: {ex.Message}"));
    }
    finally
    {
        Current.Dispatcher.Invoke(() =>
        {
            modListVm.IsUpdating = false;
            modListVm.UpdateProgress = "";
        });
    }
}
```

- [ ] **Step 3: 起動時フックの配線を更新**

`OnStartup`（82-85 行）の配線を置換：

```csharp
modListVm.RefreshAction = () => RefreshModMetadataAsync(modListVm, install, force: false);
modListVm.ForceRefreshAction = () => RefreshModMetadataAsync(modListVm, install, force: true);
modListVm.RefreshSelectedAction = ids =>
    Task.Run(() => RefreshModMetadataAsync(modListVm, install, force: true, ids));
_ = Task.Run(() => RefreshModMetadataAsync(modListVm, install, force: false));
```

- [ ] **Step 4: ModListView に「全件強制取り直し」ボタンとレポートパネルを追加**

`ModListView.xaml` のヘッダ部（既存「今すぐ更新」ボタン付近）に、本体UIの既存スタイルに合わせて追加（配色はリソース流用）：

```xml
<Button x:Name="ForceRefreshButton" Content="全件強制取り直し"
        Click="ForceRefreshButton_Click" Margin="8,0,0,0"/>
```

レポートは `IsUpdating` 中に表示する小パネル（`UpdateReportLines` を `ItemsControl` でバインド、`UpdateProgress` をラベル表示）。既存のダーク配色リソースを使用すること。

`ModListView.xaml.cs` に追加：

```csharp
private async void ForceRefreshButton_Click(object sender, RoutedEventArgs e)
{
    if (DataContext is MainViewModel vm && !vm.IsUpdating && vm.ForceRefreshAction is { } action)
        await action();
}
```

- [ ] **Step 5: ビルド**

Run: `dotnet build reloaded-helper.slnx`
Expected: 0 errors

- [ ] **Step 6: 手動検証（実機）**

1. アプリ起動 → `C:\FreeSoft\Reloaded-II` を読み込み。
2. 「全件強制取り直し」を押す。
3. 確認項目：
   - リストが**1件ずつ順に**日本語名・カテゴリバッジ・作者に更新される（最後まで待たされない）。
   - 処理中に見ているゲームタブが**勝手に切り替わらない**。
   - `%APPDATA%\ReloadedHelper\update-log.txt` に `[GbMatched]`/`[TranslatedOnly]`/`[Failed]` が記録される。
   - 既知 URL を持つ MOD（例 Beta.LAVENZA）が `GbMatched gb=491359` になりカテゴリが付く。
4. 再起動 → 差分のみ処理（変化なしなら即終了）。

- [ ] **Step 7: コミット**

```bash
git add src/ReloadedHelper.App/App.xaml.cs src/ReloadedHelper.App/Views/ModListView.xaml src/ReloadedHelper.App/Views/ModListView.xaml.cs src/ReloadedHelper.Core/MainViewModel.cs
git commit -m "feat: wire metadata rebuild — force refresh, incremental reflection, report panel"
```

---

## Self-Review（計画 vs 仕様）

- **A-1 API修正** → Task A1, A2 ✅
- **A-2 ゲームID内蔵** → Task A3 ✅
- **A-3 英語原本で安定検索** → Task A4（OriginalName）, A7（SearchKey）✅
- **A-4 全件強制取り直し→差分** → Task A9, A12 ✅
- **A-5 見える化（レポート）** → Task A8, A12（レポートパネル）✅
- **A-6 逐次反映＋タブ切替防止** → Task A11, A12（ApplyMetadataToRow / Reload）✅
- **A-7 カテゴリ・作者** → Task A1（作者取得）, A10（ModAuthor書込）, 既存 CategoryLabel ✅
- 型整合：`MetadataRefreshResult`/`RefreshStatus`/`GameBananaModInfo(+Author)`/各シグネチャは全タスクで一貫。
- プレースホルダ：UI のレイアウト詳細（A12 Step4）は既存 XAML スタイル流用を指示済み（実装者が現行配色リソースに合わせる）。
