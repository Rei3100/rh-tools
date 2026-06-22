# Phase 4: GameBanana 全自動取得 + 日本語化 実装プラン

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** MOD の名前・説明文を GameBanana から自動取得して日本語翻訳し、カテゴリバッジを UI に表示する。

**Architecture:** 起動時にバックグラウンドで全 MOD を処理。GameBanana API で情報を取得し Google Translate 非公式 API で翻訳後、GlossaryProvider でゲーム固有用語を補正して ModConfig.json に直接書き込む。処理済み情報は userdata.json にキャッシュして次回起動時のスキップ判定に使う。

**Tech Stack:** C# 10+ / .NET 10 / WPF, System.Text.Json, xUnit, HttpClient (DI によるモック)

## Global Constraints

- NuGet 追加禁止（System.Text.Json のみ。テスト用 xUnit は既存）
- ユーザーデータ保存先: `%APPDATA%\ReloadedHelper\userdata.json` のみ
- 外部 HTTP: `translate.googleapis.com` と `api.gamebanana.com` のみ（無料・キー不要）
- バックアップなし（ModConfig.json は直接上書き）
- TDD: xUnit でユニットテスト → RED → GREEN → REFACTOR の順を守る
- コミットは各タスク完了ごと

---

## Task 1: UserData Phase 4 フィールド追加

**Files:**
- Modify: `src/ReloadedHelper.Core/UserData.cs`
- Modify: `tests/ReloadedHelper.Core.Tests/UserDataTests.cs`

**Interfaces:**
- Produces: `ModUserData.GameBananaId`, `.Category`, `.FetchedAt`, `.FetchedVersion` — すべて `string?` または `DateTime?`、後タスクで参照

- [ ] **Step 1: テストを書く（RED）**

`UserDataTests.cs` に以下を追加（既存テストの下）:

```csharp
[Fact]
public void Phase4_fields_roundtrip()
{
    var dir = Path.Combine(Path.GetTempPath(), "rh_ud4_" + Guid.NewGuid().ToString("N"));
    var path = Path.Combine(dir, "userdata.json");
    try
    {
        var file = new UserDataFile();
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        file.Mods["mod.a"] = new ModUserData
        {
            GameBananaId = "123456",
            Category = "Sound",
            FetchedAt = now,
            FetchedVersion = "1.2.3"
        };
        UserDataStore.Save(path, file);

        var loaded = UserDataStore.Load(path);
        var m = loaded.Mods["mod.a"];
        Assert.Equal("123456", m.GameBananaId);
        Assert.Equal("Sound", m.Category);
        Assert.Equal(now, m.FetchedAt);
        Assert.Equal("1.2.3", m.FetchedVersion);
    }
    finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
}
```

- [ ] **Step 2: テスト失敗を確認**

```
dotnet test tests/ReloadedHelper.Core.Tests --filter "Phase4_fields_roundtrip" -v minimal
```
Expected: FAIL (フィールドが存在しない)

- [ ] **Step 3: `UserData.cs` にフィールドを追加**

`ModUserData` クラスに以下を追加（既存フィールド 4 行の直後）:

```csharp
    // Phase 4 追加
    public string?   GameBananaId   { get; set; }
    public string?   Category       { get; set; }
    public DateTime? FetchedAt      { get; set; }
    public string?   FetchedVersion { get; set; }
```

- [ ] **Step 4: テスト通過を確認**

```
dotnet test tests/ReloadedHelper.Core.Tests --filter "Phase4_fields_roundtrip" -v minimal
```
Expected: PASS

- [ ] **Step 5: 全テストが通過することを確認**

```
dotnet test tests/ReloadedHelper.Core.Tests -v minimal
```
Expected: 全 PASS

- [ ] **Step 6: コミット**

```
git add src/ReloadedHelper.Core/UserData.cs tests/ReloadedHelper.Core.Tests/UserDataTests.cs
git commit -m "feat: add Phase 4 fields to ModUserData (GameBananaId, Category, FetchedAt, FetchedVersion)"
```

---

## Task 2: ModLoadEntry に Category 追加 + LoadOrderBuilder 拡張

**Files:**
- Modify: `src/ReloadedHelper.Core/Models.cs`
- Modify: `src/ReloadedHelper.Core/LoadOrder.cs`
- Modify: `tests/ReloadedHelper.Core.Tests/LoadOrderTests.cs`

**Interfaces:**
- Consumes: `UserDataFile` (Task 1 で拡張済み)
- Produces: `ModLoadEntry(... string? Category = null)` — Task 7, 9 でバインディング参照
- Produces: `ModLoadEntry.CategoryLabel` — Task 9 で XAML バインディング参照
- Produces: `LoadOrderBuilder.Build(GameInfo, IReadOnlyDictionary<string, ModInfo>, UserDataFile? userData = null)`

- [ ] **Step 1: テストを書く（RED）**

`LoadOrderTests.cs` に以下を追加:

```csharp
[Fact]
public void Build_injects_category_from_userdata()
{
    var catalog = new Dictionary<string, ModInfo>
    {
        ["a"] = Mod("a", "Alpha"),
    };
    var game = new GameInfo("p5r.exe", "P5R", "", null,
        EnabledMods: new[] { "a" },
        SortedMods:  new[] { "a" },
        FolderPath: @"C:\Apps\p5r.exe");
    var userData = new UserDataFile();
    userData.Mods["a"] = new ModUserData { Category = "Sound" };

    var entries = LoadOrderBuilder.Build(game, catalog, userData);

    Assert.Equal("Sound", entries[0].Category);
    Assert.Equal("サウンド", entries[0].CategoryLabel);
}

[Fact]
public void Build_without_userdata_leaves_category_null()
{
    var catalog = new Dictionary<string, ModInfo> { ["a"] = Mod("a", "Alpha") };
    var game = new GameInfo("p5r.exe", "P5R", "", null,
        EnabledMods: new[] { "a" },
        SortedMods:  new[] { "a" },
        FolderPath: @"C:\Apps\p5r.exe");

    var entries = LoadOrderBuilder.Build(game, catalog);

    Assert.Null(entries[0].Category);
    Assert.Null(entries[0].CategoryLabel);
}
```

- [ ] **Step 2: テスト失敗を確認**

```
dotnet test tests/ReloadedHelper.Core.Tests --filter "Build_injects_category_from_userdata|Build_without_userdata_leaves_category_null" -v minimal
```
Expected: FAIL

- [ ] **Step 3: `Models.cs` の `ModLoadEntry` を更新**

```csharp
public sealed record ModLoadEntry(int Order, string ModId, ModInfo? Info, bool Enabled, string? Category = null)
{
    public string DisplayName =>
        Info is { ModName.Length: > 0 } ? Info.ModName : ModId;

    public string? CategoryLabel => Category switch
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

- [ ] **Step 4: `LoadOrder.cs` の `LoadOrderBuilder.Build()` を更新**

```csharp
public static IReadOnlyList<ModLoadEntry> Build(
    GameInfo game,
    IReadOnlyDictionary<string, ModInfo> catalog,
    UserDataFile? userData = null)
{
    var enabled = new HashSet<string>(game.EnabledMods, StringComparer.Ordinal);
    var list = new List<ModLoadEntry>(game.SortedMods.Count);
    for (int i = 0; i < game.SortedMods.Count; i++)
    {
        var id = game.SortedMods[i];
        catalog.TryGetValue(id, out var info);
        var category = userData?.Mods.GetValueOrDefault(id)?.Category;
        list.Add(new ModLoadEntry(i + 1, id, info, enabled.Contains(id), category));
    }
    return list;
}
```

- [ ] **Step 5: テスト通過を確認**

```
dotnet test tests/ReloadedHelper.Core.Tests -v minimal
```
Expected: 全 PASS（既存テストへの影響なし。`Build(game, catalog)` は引数 2 個のまま動作する）

- [ ] **Step 6: コミット**

```
git add src/ReloadedHelper.Core/Models.cs src/ReloadedHelper.Core/LoadOrder.cs tests/ReloadedHelper.Core.Tests/LoadOrderTests.cs
git commit -m "feat: add Category/CategoryLabel to ModLoadEntry, extend LoadOrderBuilder.Build with optional userData"
```

---

## Task 3: GlossaryProvider 新規作成

**Files:**
- Create: `src/ReloadedHelper.Core/GlossaryProvider.cs`
- Create: `tests/ReloadedHelper.Core.Tests/GlossaryProviderTests.cs`

**Interfaces:**
- Produces: `GlossaryProvider.Apply(string text, string appId) : string` — Task 8 オーケストレーターで呼び出し

- [ ] **Step 1: テストを書く（RED）**

新規ファイル `tests/ReloadedHelper.Core.Tests/GlossaryProviderTests.cs`:

```csharp
namespace ReloadedHelper.Core.Tests;

public class GlossaryProviderTests
{
    [Fact]
    public void P5R_replaces_character_full_name_before_short_name()
    {
        var result = GlossaryProvider.Apply("Ryuji Sakamoto and Ryuji", "p5r.exe");
        Assert.Equal("坂本竜司 and 竜司", result);
    }

    [Fact]
    public void P5R_replaces_term()
    {
        var result = GlossaryProvider.Apply("The Phantom Thieves enter the Palace.", "p5r.exe");
        Assert.Equal("The 怪盗団 enter the パレス.", result);
    }

    [Fact]
    public void P4G_replaces_term()
    {
        var result = GlossaryProvider.Apply("The Investigation Team watches the Midnight Channel.", "p4g.exe");
        Assert.Equal("The 自称特別捜査隊 watches the マヨナカテレビ.", result);
    }

    [Fact]
    public void P5S_replaces_term()
    {
        var result = GlossaryProvider.Apply("Sophia enters the Jail.", "p5s.exe");
        Assert.Equal("ソフィア enters the 監獄.", result);
    }

    [Fact]
    public void Unknown_appid_returns_text_unchanged()
    {
        var result = GlossaryProvider.Apply("Persona", "unknown.exe");
        Assert.Equal("Persona", result);
    }

    [Fact]
    public void Does_not_replace_partial_word_match()
    {
        var result = GlossaryProvider.Apply("Personalise", "p5r.exe");
        Assert.Equal("Personalise", result);
    }
}
```

- [ ] **Step 2: テスト失敗を確認**

```
dotnet test tests/ReloadedHelper.Core.Tests --filter "GlossaryProviderTests" -v minimal
```
Expected: FAIL (クラスが存在しない)

- [ ] **Step 3: `GlossaryProvider.cs` を作成**

```csharp
using System.Text.RegularExpressions;

namespace ReloadedHelper.Core;

public static class GlossaryProvider
{
    private static readonly IReadOnlyDictionary<string, (string En, string Ja)[]> Terms =
        new Dictionary<string, (string, string)[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["p5r.exe"] =
            [
                // 複合名は必ず単独名より前に配置（長いものから置換して衝突を防ぐ）
                ("Ryuji Sakamoto",  "坂本竜司"),
                ("Ann Takamaki",    "高巻杏"),
                ("Yusuke Kitagawa", "喜多川祐介"),
                ("Makoto Niijima",  "新島真"),
                ("Futaba Sakura",   "佐倉双葉"),
                ("Haru Okumura",    "奥村春"),
                ("Goro Akechi",     "明智吾郎"),
                ("Phantom Thieves", "怪盗団"),
                ("Velvet Room",     "ベルベットルーム"),
                ("Metaverse",       "異世界"),
                ("Mementos",        "メメントス"),
                ("Confidant",       "コープ"),
                ("Cognitive",       "認知"),
                ("Palace",          "パレス"),
                ("Shadow",          "シャドウ"),
                ("Persona",         "ペルソナ"),
                ("Joker",           "ジョーカー"),
                ("Ryuji",           "竜司"),
                ("Ann",             "杏"),
                ("Yusuke",          "祐介"),
                ("Makoto",          "真"),
                ("Futaba",          "双葉"),
                ("Haru",            "春"),
                ("Morgana",         "モルガナ"),
                ("Akechi",          "明智"),
                ("Lavenza",         "ラヴェンツァ"),
                ("Igor",            "イゴール"),
                ("Sojiro",          "惣治郎"),
                ("Kasumi",          "かすみ"),
            ],
            ["p4g.exe"] =
            [
                ("Investigation Team", "自称特別捜査隊"),
                ("Yu Narukami",        "鳴上悠"),
                ("Midnight Channel",   "マヨナカテレビ"),
                ("Yosuke",             "陽介"),
                ("Chie",               "千枝"),
                ("Yukiko",             "雪子"),
                ("Kanji",              "完二"),
                ("Rise",               "りせ"),
                ("Teddie",             "クマ"),
                ("Naoto",              "直斗"),
            ],
            ["p5s.exe"] =
            [
                ("EMMA",     "エマ"),
                ("Jail",     "監獄"),
                ("Monarch",  "モナーク"),
                ("Sophia",   "ソフィア"),
                ("Zenkichi", "善吉"),
            ],
        };

    public static string Apply(string text, string appId)
    {
        if (!Terms.TryGetValue(appId, out var terms)) return text;
        foreach (var (en, ja) in terms)
            text = Regex.Replace(text, $@"\b{Regex.Escape(en)}\b", ja, RegexOptions.IgnoreCase);
        return text;
    }
}
```

- [ ] **Step 4: テスト通過を確認**

```
dotnet test tests/ReloadedHelper.Core.Tests --filter "GlossaryProviderTests" -v minimal
```
Expected: 全 PASS

- [ ] **Step 5: 全テスト確認**

```
dotnet test tests/ReloadedHelper.Core.Tests -v minimal
```

- [ ] **Step 6: コミット**

```
git add src/ReloadedHelper.Core/GlossaryProvider.cs tests/ReloadedHelper.Core.Tests/GlossaryProviderTests.cs
git commit -m "feat: add GlossaryProvider with P5R/P4G/P5S term dictionaries"
```

---

## Task 4: TranslationService 新規作成

**Files:**
- Create: `src/ReloadedHelper.Core/TranslationService.cs`
- Create: `tests/ReloadedHelper.Core.Tests/TestHelpers.cs`  (FakeHttpMessageHandler — Task 5 でも使用)
- Create: `tests/ReloadedHelper.Core.Tests/TranslationServiceTests.cs`

**Interfaces:**
- Produces: `TranslationService(HttpClient http, TimeSpan? requestDelay = null)`
- Produces: `TranslateAsync(string text, string targetLang, CancellationToken ct = default) : Task<string>`
  - 失敗時は元テキストを返す（例外を投げない）

- [ ] **Step 1: テストヘルパーを作成**

新規ファイル `tests/ReloadedHelper.Core.Tests/TestHelpers.cs`:

```csharp
using System.Net;
using System.Net.Http;

namespace ReloadedHelper.Core.Tests;

internal sealed class FakeHttpMessageHandler(string responseBody, HttpStatusCode status = HttpStatusCode.OK)
    : HttpMessageHandler
{
    public string? LastRequestUri { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct)
    {
        LastRequestUri = request.RequestUri?.ToString();
        return Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(responseBody, System.Text.Encoding.UTF8, "application/json")
        });
    }
}
```

- [ ] **Step 2: テストを書く（RED）**

新規ファイル `tests/ReloadedHelper.Core.Tests/TranslationServiceTests.cs`:

```csharp
namespace ReloadedHelper.Core.Tests;

public class TranslationServiceTests
{
    // Google Translate 非公式 API のレスポンス形式:
    // [[["翻訳テキスト", "原文", ...], ...], null, "en"]
    private static string TranslateResponse(string translated, string original) =>
        $"[[[{System.Text.Json.JsonSerializer.Serialize(translated)},{System.Text.Json.JsonSerializer.Serialize(original)}]],null,\"en\"]";

    [Fact]
    public async Task TranslateAsync_returns_translated_text()
    {
        var handler = new FakeHttpMessageHandler(TranslateResponse("ジョーカー", "Joker"));
        var http = new HttpClient(handler);
        var svc = new TranslationService(http, TimeSpan.Zero);

        var result = await svc.TranslateAsync("Joker", "ja");

        Assert.Equal("ジョーカー", result);
        Assert.Contains("translate.googleapis.com", handler.LastRequestUri);
        Assert.Contains("Joker", handler.LastRequestUri);
        Assert.Contains("tl=ja", handler.LastRequestUri);
    }

    [Fact]
    public async Task TranslateAsync_returns_original_text_on_http_failure()
    {
        var handler = new FakeHttpMessageHandler("", System.Net.HttpStatusCode.ServiceUnavailable);
        var http = new HttpClient(handler);
        var svc = new TranslationService(http, TimeSpan.Zero);

        var result = await svc.TranslateAsync("Hello", "ja");

        Assert.Equal("Hello", result);
    }

    [Fact]
    public async Task TranslateAsync_handles_multi_chunk_response()
    {
        // 長いテキストは複数チャンクに分割されて返ることがある
        var json = "[[[\"こんにちは\",\"Hello\"],[\"世界\",\"world\"]],null,\"en\"]";
        var handler = new FakeHttpMessageHandler(json);
        var http = new HttpClient(handler);
        var svc = new TranslationService(http, TimeSpan.Zero);

        var result = await svc.TranslateAsync("Hello world", "ja");

        Assert.Equal("こんにちは世界", result);
    }

    [Fact]
    public async Task TranslateAsync_returns_empty_for_empty_input()
    {
        var handler = new FakeHttpMessageHandler("");
        var http = new HttpClient(handler);
        var svc = new TranslationService(http, TimeSpan.Zero);

        var result = await svc.TranslateAsync("", "ja");

        Assert.Equal("", result);
    }
}
```

- [ ] **Step 3: テスト失敗を確認**

```
dotnet test tests/ReloadedHelper.Core.Tests --filter "TranslationServiceTests" -v minimal
```
Expected: FAIL

- [ ] **Step 4: `TranslationService.cs` を作成**

```csharp
using System.Net.Http;
using System.Text.Json;

namespace ReloadedHelper.Core;

public sealed class TranslationService(HttpClient http, TimeSpan? requestDelay = null)
{
    private readonly TimeSpan _delay = requestDelay ?? TimeSpan.FromMilliseconds(100);

    public async Task<string> TranslateAsync(string text, string targetLang, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(text)) return text;

        if (_delay > TimeSpan.Zero)
            await Task.Delay(_delay, ct);

        var encoded = Uri.EscapeDataString(text);
        var url = $"https://translate.googleapis.com/translate_a/single" +
                  $"?client=gtx&sl=en&tl={targetLang}&dt=t&q={encoded}";

        for (int attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(10));
                var response = await http.GetStringAsync(url, cts.Token);
                return ParseTranslation(response) ?? text;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
            {
                if (attempt == 1 || ct.IsCancellationRequested) return text;
                await Task.Delay(_delay > TimeSpan.Zero ? _delay : TimeSpan.FromMilliseconds(100), ct);
            }
        }
        return text;
    }

    private static string? ParseTranslation(string json)
    {
        // レスポンス形式: [[["翻訳テキスト","原文",...],...]],null,"en"]
        try
        {
            using var doc = JsonDocument.Parse(json);
            var sb = new System.Text.StringBuilder();
            foreach (var chunk in doc.RootElement[0].EnumerateArray())
            {
                if (chunk.GetArrayLength() > 0 && chunk[0].ValueKind == JsonValueKind.String)
                    sb.Append(chunk[0].GetString());
            }
            return sb.Length > 0 ? sb.ToString() : null;
        }
        catch (JsonException) { return null; }
    }
}
```

- [ ] **Step 5: テスト通過を確認**

```
dotnet test tests/ReloadedHelper.Core.Tests --filter "TranslationServiceTests" -v minimal
```
Expected: 全 PASS

- [ ] **Step 6: 全テスト確認**

```
dotnet test tests/ReloadedHelper.Core.Tests -v minimal
```

- [ ] **Step 7: コミット**

```
git add src/ReloadedHelper.Core/TranslationService.cs tests/ReloadedHelper.Core.Tests/TestHelpers.cs tests/ReloadedHelper.Core.Tests/TranslationServiceTests.cs
git commit -m "feat: add TranslationService using Google Translate unofficial API"
```

---

## Task 5: GameBananaClient 新規作成

**Files:**
- Create: `src/ReloadedHelper.Core/GameBananaClient.cs`
- Create: `tests/ReloadedHelper.Core.Tests/GameBananaClientTests.cs`

**Interfaces:**
- Produces: `GameBananaModInfo` record — `(string Name, string Text, string? Category, string GameId)`
- Produces: `GameBananaClient(HttpClient http)`
- Produces: `GameBananaClient.ExtractIdFromUrl(string? projectUrl) : string?` — static
- Produces: `FetchAsync(string gbId, CancellationToken ct = default) : Task<GameBananaModInfo?>`
- Produces: `SearchAsync(string modName, string gbGameId, CancellationToken ct = default) : Task<(string GbId, string GbGameId)?>`

**API レスポンス形式（実装・テストで仮定する形式）:**

ProfilePage フィールドクエリ:
```
GET https://api.gamebanana.com/apiv11/Mod/{id}/ProfilePage?fields=name%2Ctext%2CCategory().name%2CGame().id
```
レスポンス（フィールド順の JSON 配列）:
```json
["Mod Name", "Description text", "Sound", "8809"]
```
※ Game ID は文字列または数値、どちらも受け入れる

検索:
```
GET https://api.gamebanana.com/apiv11/Util/Search/Results?search_query=...&itemtype=Mod&gameid=...&page=1&nperpage=5
```
レスポンス（オブジェクト配列）:
```json
[{"_idRow": 123456, "_sName": "CRI FileSystem V2 Hook"}, {"_idRow": 789012, "_sName": "Other Mod"}]
```

- [ ] **Step 1: テストを書く（RED）**

新規ファイル `tests/ReloadedHelper.Core.Tests/GameBananaClientTests.cs`:

```csharp
namespace ReloadedHelper.Core.Tests;

public class GameBananaClientTests
{
    [Theory]
    [InlineData("https://gamebanana.com/mods/123456", "123456")]
    [InlineData("https://gamebanana.com/dl/123456",   "123456")]
    [InlineData("https://gamebanana.com/mods/123456?some=param", "123456")]
    [InlineData("https://example.com/other", null)]
    [InlineData(null, null)]
    [InlineData("", null)]
    public void ExtractIdFromUrl_parses_known_url_patterns(string? url, string? expected)
    {
        Assert.Equal(expected, GameBananaClient.ExtractIdFromUrl(url));
    }

    [Fact]
    public async Task FetchAsync_parses_profile_page_response()
    {
        // ProfilePage?fields=name,text,Category().name,Game().id → 順序通りの配列
        var json = """["CRI FileSystem V2 Hook","Hooks the CRI filesystem.","Sound","8809"]""";
        var handler = new FakeHttpMessageHandler(json);
        var client = new GameBananaClient(new System.Net.Http.HttpClient(handler));

        var result = await client.FetchAsync("123456");

        Assert.NotNull(result);
        Assert.Equal("CRI FileSystem V2 Hook", result!.Name);
        Assert.Equal("Hooks the CRI filesystem.", result.Text);
        Assert.Equal("Sound", result.Category);
        Assert.Equal("8809", result.GameId);
        Assert.Contains("api.gamebanana.com/apiv11/Mod/123456/ProfilePage", handler.LastRequestUri);
    }

    [Fact]
    public async Task FetchAsync_returns_null_on_failure()
    {
        var handler = new FakeHttpMessageHandler("", System.Net.HttpStatusCode.NotFound);
        var client = new GameBananaClient(new System.Net.Http.HttpClient(handler));

        var result = await client.FetchAsync("999");

        Assert.Null(result);
    }

    [Fact]
    public async Task SearchAsync_returns_best_match_above_80_percent()
    {
        // 完全一致 → 採用
        var json = """[{"_idRow": 123456, "_sName": "CRI FileSystem V2 Hook"}]""";
        var handler = new FakeHttpMessageHandler(json);
        var client = new GameBananaClient(new System.Net.Http.HttpClient(handler));

        var result = await client.SearchAsync("CRI FileSystem V2 Hook", "8809");

        Assert.NotNull(result);
        Assert.Equal("123456", result!.Value.GbId);
        Assert.Equal("8809", result!.Value.GbGameId);
    }

    [Fact]
    public async Task SearchAsync_returns_null_when_no_match_above_threshold()
    {
        var json = """[{"_idRow": 1, "_sName": "Completely Different Mod"}]""";
        var handler = new FakeHttpMessageHandler(json);
        var client = new GameBananaClient(new System.Net.Http.HttpClient(handler));

        var result = await client.SearchAsync("My Unique Mod Name", "8809");

        Assert.Null(result);
    }

    [Fact]
    public async Task SearchAsync_returns_null_on_empty_results()
    {
        var handler = new FakeHttpMessageHandler("[]");
        var client = new GameBananaClient(new System.Net.Http.HttpClient(handler));

        var result = await client.SearchAsync("Any Mod", "8809");

        Assert.Null(result);
    }
}
```

- [ ] **Step 2: テスト失敗を確認**

```
dotnet test tests/ReloadedHelper.Core.Tests --filter "GameBananaClientTests" -v minimal
```
Expected: FAIL

- [ ] **Step 3: `GameBananaClient.cs` を作成**

```csharp
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ReloadedHelper.Core;

public sealed record GameBananaModInfo(string Name, string Text, string? Category, string GameId);

public sealed class GameBananaClient(HttpClient http)
{
    private static readonly TimeSpan ApiTimeout = TimeSpan.FromSeconds(3);
    private const double SimilarityThreshold = 0.80;

    public static string? ExtractIdFromUrl(string? projectUrl)
    {
        if (string.IsNullOrEmpty(projectUrl)) return null;
        var m = Regex.Match(projectUrl, @"gamebanana\.com/(?:mods|dl)/(\d+)", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value : null;
    }

    public async Task<GameBananaModInfo?> FetchAsync(string gbId, CancellationToken ct = default)
    {
        var url = $"https://api.gamebanana.com/apiv11/Mod/{gbId}/ProfilePage" +
                  "?fields=name%2Ctext%2CCategory().name%2CGame().id";
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(ApiTimeout);
            var json = await http.GetStringAsync(url, cts.Token);
            return ParseModInfo(json);
        }
        catch { return null; }
    }

    public async Task<(string GbId, string GbGameId)?> SearchAsync(
        string modName, string gbGameId, CancellationToken ct = default)
    {
        var q = Uri.EscapeDataString(modName);
        var url = $"https://api.gamebanana.com/apiv11/Util/Search/Results" +
                  $"?search_query={q}&itemtype=Mod&gameid={gbGameId}&page=1&nperpage=5";
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(ApiTimeout);
            var json = await http.GetStringAsync(url, cts.Token);
            return ParseSearchResult(json, modName, gbGameId);
        }
        catch { return null; }
    }

    private static GameBananaModInfo? ParseModInfo(string json)
    {
        // 期待形式: ["name", "text", "Category", "gameId"]
        try
        {
            using var doc = JsonDocument.Parse(json);
            var arr = doc.RootElement;
            if (arr.ValueKind != JsonValueKind.Array || arr.GetArrayLength() < 4) return null;

            var name     = arr[0].GetString() ?? "";
            var text     = arr[1].GetString() ?? "";
            var category = arr[2].ValueKind == JsonValueKind.String ? arr[2].GetString() : null;
            var gameId   = arr[3].ValueKind == JsonValueKind.String
                ? arr[3].GetString() ?? ""
                : arr[3].GetInt64().ToString();

            return new GameBananaModInfo(name, text, category, gameId);
        }
        catch (JsonException) { return null; }
    }

    private static (string GbId, string GbGameId)? ParseSearchResult(
        string json, string modName, string gbGameId)
    {
        // 期待形式: [{"_idRow": 123, "_sName": "..."}, ...]
        try
        {
            using var doc = JsonDocument.Parse(json);
            var arr = doc.RootElement;
            if (arr.ValueKind != JsonValueKind.Array) return null;

            string? bestId = null;
            double bestScore = 0;

            foreach (var item in arr.EnumerateArray())
            {
                if (!item.TryGetProperty("_idRow", out var idProp)) continue;
                if (!item.TryGetProperty("_sName", out var nameProp)) continue;

                var id   = idProp.ValueKind == JsonValueKind.Number
                    ? idProp.GetInt64().ToString()
                    : idProp.GetString() ?? "";
                var name = nameProp.GetString() ?? "";
                var score = Similarity(modName, name);

                if (score > bestScore) { bestScore = score; bestId = id; }
            }

            if (bestScore >= SimilarityThreshold && bestId is not null)
                return (bestId, gbGameId);
            return null;
        }
        catch (JsonException) { return null; }
    }

    private static double Similarity(string a, string b)
    {
        var na = Normalize(a);
        var nb = Normalize(b);
        if (na.Length == 0 && nb.Length == 0) return 1.0;
        if (na.Length == 0 || nb.Length == 0) return 0.0;
        int dist = LevenshteinDistance(na, nb);
        return 1.0 - (double)dist / Math.Max(na.Length, nb.Length);
    }

    private static string Normalize(string s) =>
        new(s.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    private static int LevenshteinDistance(string a, string b)
    {
        int m = a.Length, n = b.Length;
        var d = new int[m + 1, n + 1];
        for (int i = 0; i <= m; i++) d[i, 0] = i;
        for (int j = 0; j <= n; j++) d[0, j] = j;
        for (int i = 1; i <= m; i++)
            for (int j = 1; j <= n; j++)
                d[i, j] = a[i - 1] == b[j - 1]
                    ? d[i - 1, j - 1]
                    : 1 + Math.Min(d[i - 1, j], Math.Min(d[i, j - 1], d[i - 1, j - 1]));
        return d[m, n];
    }
}
```

- [ ] **Step 4: テスト通過を確認**

```
dotnet test tests/ReloadedHelper.Core.Tests --filter "GameBananaClientTests" -v minimal
```
Expected: 全 PASS

- [ ] **Step 5: 全テスト確認**

```
dotnet test tests/ReloadedHelper.Core.Tests -v minimal
```

- [ ] **Step 6: コミット**

```
git add src/ReloadedHelper.Core/GameBananaClient.cs tests/ReloadedHelper.Core.Tests/GameBananaClientTests.cs
git commit -m "feat: add GameBananaClient with URL extraction, search, and mod info fetch"
```

---

## Task 6: ModConfigUpdater 新規作成

**Files:**
- Create: `src/ReloadedHelper.Core/ModConfigUpdater.cs`
- Create: `tests/ReloadedHelper.Core.Tests/ModConfigUpdaterTests.cs`

**Interfaces:**
- Produces: `ModConfigUpdater.Write(string modFolderPath, string japaneseName, string japaneseDescription) : void`

- [ ] **Step 1: テストを書く（RED）**

新規ファイル `tests/ReloadedHelper.Core.Tests/ModConfigUpdaterTests.cs`:

```csharp
using System.IO;
using System.Text.Json;

namespace ReloadedHelper.Core.Tests;

public class ModConfigUpdaterTests
{
    private static string NewTempDir()
    {
        var p = Path.Combine(Path.GetTempPath(), "rh_mcu_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(p);
        return p;
    }

    [Fact]
    public void Write_updates_name_and_description_preserves_other_fields()
    {
        var dir = NewTempDir();
        try
        {
            var json = """
                {
                  "ModId": "mod.test",
                  "ModName": "Test Mod",
                  "ModAuthor": "Author",
                  "ModVersion": "1.0.0",
                  "ModDescription": "English description",
                  "SupportedAppId": ["p5r.exe"]
                }
                """;
            File.WriteAllText(Path.Combine(dir, "ModConfig.json"), json);

            ModConfigUpdater.Write(dir, "テストMOD", "日本語説明");

            var written = File.ReadAllText(Path.Combine(dir, "ModConfig.json"));
            using var doc = JsonDocument.Parse(written);
            var root = doc.RootElement;
            Assert.Equal("テストMOD",   root.GetProperty("ModName").GetString());
            Assert.Equal("日本語説明",   root.GetProperty("ModDescription").GetString());
            Assert.Equal("mod.test",    root.GetProperty("ModId").GetString());
            Assert.Equal("Author",      root.GetProperty("ModAuthor").GetString());
            Assert.Equal("1.0.0",       root.GetProperty("ModVersion").GetString());
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Write_does_nothing_when_no_config_file()
    {
        var dir = NewTempDir();
        try
        {
            // No exception, no file created
            ModConfigUpdater.Write(dir, "名前", "説明");
            Assert.False(File.Exists(Path.Combine(dir, "ModConfig.json")));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Write_preserves_japanese_unicode_without_escaping()
    {
        var dir = NewTempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "ModConfig.json"),
                """{"ModId":"x","ModName":"X","ModDescription":"D"}""");

            ModConfigUpdater.Write(dir, "日本語名前", "日本語の説明文");

            var written = File.ReadAllText(Path.Combine(dir, "ModConfig.json"));
            // Unicode エスケープ（日 など）でなく生の日本語で保存される
            Assert.Contains("日本語名前", written);
            Assert.Contains("日本語の説明文", written);
        }
        finally { Directory.Delete(dir, true); }
    }
}
```

- [ ] **Step 2: テスト失敗を確認**

```
dotnet test tests/ReloadedHelper.Core.Tests --filter "ModConfigUpdaterTests" -v minimal
```
Expected: FAIL

- [ ] **Step 3: `ModConfigUpdater.cs` を作成**

```csharp
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace ReloadedHelper.Core;

public static class ModConfigUpdater
{
    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Indented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static void Write(string modFolderPath, string japaneseName, string japaneseDescription)
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
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (prop.Name == "ModName")
                        writer.WriteString("ModName", japaneseName);
                    else if (prop.Name == "ModDescription")
                        writer.WriteString("ModDescription", japaneseDescription);
                    else
                        prop.WriteTo(writer);
                }
                writer.WriteEndObject();
            }

            File.WriteAllBytes(configPath, ms.ToArray());
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // スキップ、アプリ起動は継続
        }
    }
}
```

- [ ] **Step 4: テスト通過を確認**

```
dotnet test tests/ReloadedHelper.Core.Tests --filter "ModConfigUpdaterTests" -v minimal
```
Expected: 全 PASS

- [ ] **Step 5: 全テスト確認**

```
dotnet test tests/ReloadedHelper.Core.Tests -v minimal
```

- [ ] **Step 6: コミット**

```
git add src/ReloadedHelper.Core/ModConfigUpdater.cs tests/ReloadedHelper.Core.Tests/ModConfigUpdaterTests.cs
git commit -m "feat: add ModConfigUpdater that overwrites ModName/ModDescription preserving all other fields"
```

---

## Task 7: MainViewModel に IsUpdating / UpdateProgress / RefreshAction を追加

**Files:**
- Modify: `src/ReloadedHelper.Core/MainViewModel.cs`
- Modify: `tests/ReloadedHelper.Core.Tests/MainViewModelTests.cs`

**Interfaces:**
- Consumes: `UserDataStore.Load()`, `LoadOrderBuilder.Build(..., userData)` (Task 1, 2 で追加済み)
- Produces: `MainViewModel.IsUpdating : bool` — Task 8, 9 で参照
- Produces: `MainViewModel.UpdateProgress : string` — Task 8, 9 で参照
- Produces: `MainViewModel.RefreshAction : Func<Task>?` — Task 8 でセット、Task 9 でハンドラが呼び出し

- [ ] **Step 1: テストを書く（RED）**

`MainViewModelTests.cs` に以下を追加（既存テストの下）:

```csharp
[Fact]
public void IsUpdating_fires_PropertyChanged()
{
    var vm = new MainViewModel();
    var changed = new List<string>();
    vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName ?? "");

    vm.IsUpdating = true;

    Assert.Contains("IsUpdating", changed);
}

[Fact]
public void UpdateProgress_fires_PropertyChanged()
{
    var vm = new MainViewModel();
    var changed = new List<string>();
    vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName ?? "");

    vm.UpdateProgress = "更新中 1/10 件...";

    Assert.Contains("UpdateProgress", changed);
    Assert.Equal("更新中 1/10 件...", vm.UpdateProgress);
}
```

- [ ] **Step 2: テスト失敗を確認**

```
dotnet test tests/ReloadedHelper.Core.Tests --filter "IsUpdating_fires|UpdateProgress_fires" -v minimal
```
Expected: FAIL

- [ ] **Step 3: `MainViewModel.cs` を更新**

ファイルの先頭付近の既存フィールド群（`_catalog`, `_allEntries`, `_install`）の下に追加:

```csharp
    private UserDataFile _userData = new();
```

`AllMods` プロパティの下に以下のプロパティを追加:

```csharp
    private bool _isUpdating;
    public bool IsUpdating
    {
        get => _isUpdating;
        set { if (_isUpdating != value) { _isUpdating = value; OnChanged(); } }
    }

    private string _updateProgress = "";
    public string UpdateProgress
    {
        get => _updateProgress;
        set { if (_updateProgress != value) { _updateProgress = value; OnChanged(); } }
    }

    public Func<Task>? RefreshAction { get; set; }
```

`LoadFrom()` メソッド内の `_catalog = ModCatalog.LoadAll(...)` の直後に追加:

```csharp
        _userData = UserDataStore.Load(UserDataStore.DefaultPath);
```

`RebuildEntries()` メソッド内の `LoadOrderBuilder.Build(SelectedGame, _catalog)` を以下に変更:

```csharp
        : LoadOrderBuilder.Build(SelectedGame, _catalog, _userData);
```

- [ ] **Step 4: テスト通過を確認**

```
dotnet test tests/ReloadedHelper.Core.Tests -v minimal
```
Expected: 全 PASS

- [ ] **Step 5: コミット**

```
git add src/ReloadedHelper.Core/MainViewModel.cs tests/ReloadedHelper.Core.Tests/MainViewModelTests.cs
git commit -m "feat: add IsUpdating, UpdateProgress, RefreshAction to MainViewModel; wire UserData into LoadOrderBuilder"
```

---

## Task 8: RefreshModMetadataAsync オーケストレーター追加

**Files:**
- Modify: `src/ReloadedHelper.App/App.xaml.cs`

このタスクは App 層のため xUnit テストなし。実装のみ。

**Interfaces:**
- Consumes: `GameBananaClient`, `TranslationService`, `GlossaryProvider`, `ModConfigUpdater`, `UserDataStore` (Tasks 3–6 で作成済み)
- Consumes: `MainViewModel.IsUpdating`, `.UpdateProgress`, `.RefreshAction` (Task 7 で追加済み)

- [ ] **Step 1: `App.xaml.cs` に `using` 追加**

ファイル冒頭の `using` ブロック（既存の行の末尾）に追加:

```csharp
using System.Text;
```

（※ `StringBuilder` はすでに `System.Text` 名前空間、かつ他の using で含まれる可能性があるため、重複なら追加不要）

- [ ] **Step 2: `OnStartup()` に RefreshAction のセットと Task.Run を追加**

`OnStartup()` 内の `_ = Task.Run(CheckAndApplyUpdateAsync);` の直後に以下を追加:

```csharp
        modListVm.RefreshAction = () => RefreshModMetadataAsync(modListVm, install);
        _ = Task.Run(() => RefreshModMetadataAsync(modListVm, install));
```

- [ ] **Step 3: `RefreshModMetadataAsync` メソッドを追加**

`CheckAndApplyUpdateAsync` メソッドの直後に追加:

```csharp
    private static async Task RefreshModMetadataAsync(MainViewModel modListVm, ReloadedInstall install)
    {
        try
        {
            using var http = new System.Net.Http.HttpClient();
            http.DefaultRequestHeaders.UserAgent.ParseAdd("rh-tools/1.0");
            var gbClient = new GameBananaClient(http);
            var translator = new TranslationService(http);

            var userData = UserDataStore.Load(UserDataStore.DefaultPath);
            var catalog = modListVm.AllMods;

            // 未処理 or バージョンが変わった MOD を列挙
            var toProcess = catalog.Values
                .Where(mod =>
                {
                    if (!userData.Mods.TryGetValue(mod.ModId, out var ud)) return true;
                    return ud.FetchedVersion != mod.ModVersion;
                })
                .ToList();

            int total = toProcess.Count;
            if (total == 0) return;

            // 既知の GameBanana ID を持つ MOD から AppId → GB game ID のキャッシュを構築
            var gameIdCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var mod in catalog.Values)
            {
                if (!userData.Mods.TryGetValue(mod.ModId, out var ud) || ud.GameBananaId is null) continue;
                foreach (var appId in mod.SupportedAppIds)
                {
                    if (gameIdCache.ContainsKey(appId)) continue;
                    var info = await gbClient.FetchAsync(ud.GameBananaId);
                    if (info is not null) { gameIdCache[appId] = info.GameId; break; }
                }
            }

            Current.Dispatcher.Invoke(() => modListVm.IsUpdating = true);
            int processed = 0;

            foreach (var mod in toProcess)
            {
                processed++;
                Current.Dispatcher.Invoke(() =>
                    modListVm.UpdateProgress = $"更新中 {processed}/{total} 件...");

                userData.Mods.TryGetValue(mod.ModId, out var modData);
                modData ??= new ModUserData();

                // ── Step 1: URL から直接 ID 抽出 ──
                var gbId = modData.GameBananaId ?? GameBananaClient.ExtractIdFromUrl(mod.ProjectUrl);

                // ── Step 2: 名前検索（キャッシュに game ID がある場合のみ）──
                if (gbId is null)
                {
                    foreach (var appId in mod.SupportedAppIds)
                    {
                        if (!gameIdCache.TryGetValue(appId, out var cachedGameId)) continue;
                        var found = await gbClient.SearchAsync(mod.ModName, cachedGameId);
                        if (found is not null) { gbId = found.Value.GbId; break; }
                    }
                }

                string jaName  = mod.ModName;
                string jaDesc  = mod.ModDescription;
                string? category = null;

                if (gbId is not null)
                {
                    var gbInfo = await gbClient.FetchAsync(gbId);
                    if (gbInfo is not null)
                    {
                        // game ID を各 AppId にキャッシュ
                        foreach (var appId in mod.SupportedAppIds)
                            gameIdCache.TryAdd(appId, gbInfo.GameId);

                        jaName  = await translator.TranslateAsync(gbInfo.Name, "ja");
                        jaDesc  = await translator.TranslateAsync(gbInfo.Text, "ja");
                        category = gbInfo.Category;
                    }
                }
                else
                {
                    // ── Step 3: マッチなし → 既存テキストを翻訳 ──
                    jaName = await translator.TranslateAsync(mod.ModName, "ja");
                    jaDesc = await translator.TranslateAsync(mod.ModDescription, "ja");
                }

                // GlossaryProvider で誤訳補正（サポート AppId のうち最初にマッチしたもの）
                foreach (var appId in mod.SupportedAppIds)
                {
                    jaName = GlossaryProvider.Apply(jaName, appId);
                    jaDesc = GlossaryProvider.Apply(jaDesc, appId);
                }

                // ModConfig.json に書き込み
                ModConfigUpdater.Write(mod.FolderPath, jaName, jaDesc);

                // userdata.json 更新
                modData.GameBananaId   = gbId;
                modData.Category       = category;
                modData.FetchedAt      = DateTime.UtcNow;
                modData.FetchedVersion = mod.ModVersion;
                modData.TranslatedName        = jaName;
                modData.TranslatedDescription = jaDesc;
                userData.Mods[mod.ModId] = modData;
            }

            UserDataStore.Save(UserDataStore.DefaultPath, userData);

            Current.Dispatcher.Invoke(() =>
            {
                modListVm.IsUpdating     = false;
                modListVm.UpdateProgress = "";
                modListVm.LoadFrom(install);
            });
        }
        catch { /* 更新失敗は無視して起動を継続 */ }
    }
```

- [ ] **Step 4: ビルドを確認**

```
dotnet build src/ReloadedHelper.App -v minimal
```
Expected: warnings 0, errors 0

- [ ] **Step 5: 全テスト確認**

```
dotnet test tests/ReloadedHelper.Core.Tests -v minimal
```
Expected: 全 PASS

- [ ] **Step 6: コミット**

```
git add src/ReloadedHelper.App/App.xaml.cs
git commit -m "feat: add RefreshModMetadataAsync background orchestrator in App.xaml.cs"
```

---

## Task 9: UI — カテゴリバッジ + 今すぐ更新ボタン + プログレスラベル

**Files:**
- Modify: `src/ReloadedHelper.App/Views/ModListView.xaml`
- Modify: `src/ReloadedHelper.App/Views/ModListView.xaml.cs`

このタスクは WPF UI 層のため xUnit テストなし。変更箇所を正確に示す。

**Interfaces:**
- Consumes: `MainViewModel.IsUpdating`, `.UpdateProgress`, `.RefreshAction`, `.EntryCountLabel` (Tasks 7 で追加済み)
- Consumes: `ModLoadEntry.CategoryLabel` (Task 2 で追加済み)

- [ ] **Step 1: ヘッダ部分のカウントラベルをプログレスラベルと切り替えられるよう変更**

`ModListView.xaml` の `<!-- 件数ラベル -->` TextBlock（1 つ）を、以下の 2 つの TextBlock に置き換える:

```xml
<!-- 件数ラベル（通常時） -->
<TextBlock Grid.Column="0"
           Text="{Binding EntryCountLabel, Mode=OneWay}"
           FontSize="{DynamicResource FontSizeLabel}"
           Foreground="{DynamicResource TextMetaBrush}"
           VerticalAlignment="Center"
           Margin="4,0,0,0">
    <TextBlock.Style>
        <Style TargetType="TextBlock">
            <Setter Property="Visibility" Value="Visible"/>
            <Style.Triggers>
                <DataTrigger Binding="{Binding IsUpdating}" Value="True">
                    <Setter Property="Visibility" Value="Collapsed"/>
                </DataTrigger>
            </Style.Triggers>
        </Style>
    </TextBlock.Style>
</TextBlock>

<!-- 更新プログレスラベル（更新中のみ表示） -->
<TextBlock Grid.Column="0"
           Text="{Binding UpdateProgress, Mode=OneWay}"
           FontSize="{DynamicResource FontSizeLabel}"
           Foreground="{DynamicResource AccentBrush}"
           VerticalAlignment="Center"
           Margin="4,0,0,0">
    <TextBlock.Style>
        <Style TargetType="TextBlock">
            <Setter Property="Visibility" Value="Collapsed"/>
            <Style.Triggers>
                <DataTrigger Binding="{Binding IsUpdating}" Value="True">
                    <Setter Property="Visibility" Value="Visible"/>
                </DataTrigger>
            </Style.Triggers>
        </Style>
    </TextBlock.Style>
</TextBlock>
```

- [ ] **Step 2: フィルタボタン群に「今すぐ更新」ボタンを追加**

`<!-- フィルタボタン：全て / 有効 / 無効 -->` の StackPanel 内、`<Button Content="無効" .../>` の直後に追加:

```xml
<Button Content="今すぐ更新" Click="RefreshButton_Click"
        Padding="8,4" Margin="6,0,0,0" Cursor="Hand"
        Background="{DynamicResource BgInputBrush}"
        Foreground="{DynamicResource TextBodyBrush}"
        BorderBrush="{DynamicResource BorderInputBrush}" BorderThickness="1"
        FontSize="{DynamicResource FontSizeLabel}"/>
```

- [ ] **Step 3: MOD カードのカラム定義にバッジ列を追加**

`<ListBox.ItemTemplate>` 内の `<DataTemplate>` の `<Grid Height="54">` の `<Grid.ColumnDefinitions>` を以下に変更:

```xml
<Grid.ColumnDefinitions>
    <ColumnDefinition Width="36"/>   <!-- 順番 -->
    <ColumnDefinition Width="50"/>   <!-- サムネ -->
    <ColumnDefinition Width="*"/>    <!-- 名前 + 作者 -->
    <ColumnDefinition Width="Auto"/> <!-- カテゴリバッジ -->
    <ColumnDefinition Width="52"/>   <!-- トグル -->
</Grid.ColumnDefinitions>
```

- [ ] **Step 4: カテゴリバッジを追加 + トグルの Grid.Column を 3 → 4 に更新**

`<!-- ON/OFF トグル -->` ToggleButton の直前に以下を挿入:

```xml
<!-- カテゴリバッジ（CategoryLabel が null のとき非表示） -->
<Border Grid.Column="3" CornerRadius="4" Padding="5,2" Margin="4,0"
        VerticalAlignment="Center" HorizontalAlignment="Center"
        Background="{DynamicResource AccentBrush}" Opacity="0.7">
    <Border.Style>
        <Style TargetType="Border">
            <Setter Property="Visibility" Value="Visible"/>
            <Style.Triggers>
                <DataTrigger Binding="{Binding CategoryLabel}" Value="{x:Null}">
                    <Setter Property="Visibility" Value="Collapsed"/>
                </DataTrigger>
            </Style.Triggers>
        </Style>
    </Border.Style>
    <TextBlock Text="{Binding CategoryLabel}"
               FontSize="10" FontWeight="SemiBold"
               Foreground="White"/>
</Border>
```

ToggleButton の `Grid.Column="3"` を `Grid.Column="4"` に変更:

```xml
<ToggleButton Grid.Column="4"
             IsChecked="{Binding Enabled, Mode=OneWay}"
             Click="ModToggle_Click"
             Style="{DynamicResource ToggleSwitchStyle}"
             HorizontalAlignment="Center" VerticalAlignment="Center"/>
```

- [ ] **Step 5: 詳細パネルにカテゴリ行を追加**

詳細パネル（右 280px の ScrollViewer 内）の `<!-- バージョン -->` StackPanel の直後、`<!-- URL -->` TextBlock の前に追加:

```xml
<!-- カテゴリ（null のとき非表示） -->
<StackPanel Orientation="Horizontal" Margin="0,0,0,6">
    <StackPanel.Style>
        <Style TargetType="StackPanel">
            <Setter Property="Visibility" Value="Visible"/>
            <Style.Triggers>
                <DataTrigger Binding="{Binding CategoryLabel}" Value="{x:Null}">
                    <Setter Property="Visibility" Value="Collapsed"/>
                </DataTrigger>
            </Style.Triggers>
        </Style>
    </StackPanel.Style>
    <TextBlock Text="カテゴリ: " FontSize="{DynamicResource FontSizeDetailBody}"
               Foreground="{DynamicResource TextLabelBrush}"/>
    <TextBlock Text="{Binding CategoryLabel}"
               FontSize="{DynamicResource FontSizeDetailBody}"
               Foreground="{DynamicResource TextDetailBrush}"/>
</StackPanel>
```

- [ ] **Step 6: `ModListView.xaml.cs` に `RefreshButton_Click` ハンドラを追加**

既存の `FilterDisabled_Click` メソッドの直後に追加:

```csharp
private async void RefreshButton_Click(object sender, RoutedEventArgs e)
{
    if (DataContext is MainViewModel vm && !vm.IsUpdating && vm.RefreshAction is { } action)
        await action();
}
```

- [ ] **Step 7: ビルドを確認**

```
dotnet build src/ReloadedHelper.App -v minimal
```
Expected: errors 0

- [ ] **Step 8: 全テスト確認**

```
dotnet test tests/ReloadedHelper.Core.Tests -v minimal
```
Expected: 全 PASS

- [ ] **Step 9: コミット**

```
git add src/ReloadedHelper.App/Views/ModListView.xaml src/ReloadedHelper.App/Views/ModListView.xaml.cs
git commit -m "feat: add category badge, update button, and progress label to ModListView"
```

---

## セルフレビュー（実装前チェックリスト）

設計書と照合した確認済み項目:

| 設計書セクション | 対応タスク |
|----------------|-----------|
| UserData 拡張（4 フィールド） | Task 1 ✓ |
| ModLoadEntry.Category | Task 2 ✓ |
| LoadOrderBuilder に UserDataFile | Task 2 ✓ |
| GlossaryProvider（P5R/P4G/P5S） | Task 3 ✓ |
| Google Translate 非公式 API + レート制限 100ms + リトライ | Task 4 ✓ |
| GameBanana URL 抽出 / 名前検索 80%+ / マッチなし翻訳のみ | Task 5 ✓ |
| ゲーム ID 自動検出とキャッシュ | Task 8 ✓ |
| ModConfig.json 更新（バックアップなし） | Task 6 ✓ |
| IsUpdating / UpdateProgress UI バインディング | Tasks 7, 9 ✓ |
| 今すぐ更新ボタン | Tasks 7(RefreshAction), 9 ✓ |
| カテゴリバッジ（MOD カード + 詳細パネル） | Tasks 2(CategoryLabel), 9 ✓ |
| 起動時バックグラウンド自動実行 | Task 8 ✓ |
| FetchedVersion でスキップ判定 | Task 8 ✓ |
| エラー時スキップ（3 秒タイムアウト、ログなし） | Tasks 4, 5, 8 ✓ |
| xUnit でサービス層をテスト（HttpClient モック） | Tasks 4, 5 ✓ |

**スコープ外（Phase 5 以降）:** カテゴリフィルタリング、翻訳手動修正 UI、MOD コンフィグ編集
