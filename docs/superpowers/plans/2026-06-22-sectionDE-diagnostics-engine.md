# セクションD+E：診断・自動解決エンジン（第1弾＝静的解析の土台）実装計画

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** MODのファイル中身を解析して「ファイル競合（どのMODに上書きされているか）」「なぜ効かない（依存無効・対象ゲーム違い・上書き負け）」を**ソフト自力で**検出し、日本語で提示する診断エンジンの土台を作る。実行時に外部AIを呼ばず、ルール＋ファイル解析だけで動く拡張型。

**Architecture:** Core 層に純粋関数群を積む — `ModContentScanner`（redirectルート配下の上書きパス抽出）→ `ConflictDetector`（同一パスを奪うMODの検出・後勝ち判定）→ `ModDiagnostics`（依存/対象ゲーム/競合の診断メッセージ生成）→ `GameDiagnostics`（1ゲーム分の統合実行）。App 層は診断結果を一覧表示する `DiagnosticsWindow`。安全な並び順は既存の依存トポロジーソート（起動時）に任せ、競合は警告として可視化（自動入れ替えはしない＝迷うものは警告）。

**Tech Stack:** C# / .NET 10 / WPF、System.IO、xUnit。Core はファイル解析（temp ディレクトリでテスト）。UI は手動検証。

## Global Constraints

- ランタイム NuGet 追加禁止（System.Text.Json のみ）。テスト用 xUnit は可。
- ユーザーデータは %APPDATA%\ReloadedHelper 以外に保存禁止（このセクションは読み取り専用解析＝書き込みなし）。
- **実行時に外部 AI を呼ばない**。ルールとファイル解析のみ。拡張ポイント（redirectルート/診断ルール）を追加しやすく保つ。
- 自動修正はこのセクションでは行わない（競合は警告表示のみ。安全な並び順は既存の起動時ソートが担当）。バックアップ前提の自動修正は将来の反復。
- 新規 UI は既存テーマ流用：`BgMainBrush`/`BgInputBrush`/`BorderInputBrush`/`AccentBrush`/`TextBodyBrush`/`TextLabelBrush`/`TextMetaBrush`/`MainFont`。新規色なし（警告色は既存 `#C0392B` を流用可）。
- 確定事実（実機調査）：redirectルートは `P5REssentials/CPK/...`（167 MODが使用）と `FEmulator/AWB/...`（97 MOD）。配下の相対パスが「置き換えるゲームファイル」。アーカイブ名の大文字小文字は揺れる（`BASE.CPK` vs `Base.cpk`）ため**小文字正規化して比較**。Reloaded は**読み込み順で後のMODが優先**（後勝ち）。

## スコープ外（将来の反復）

- クラッシュ／Windowsイベントログ参照（追加ライブラリ制約のため別途）。
- BGME音楽スクリプトの曲ID単位の競合（今回は file-replacement ルートのみ）。
- 競合の自動並び替え修正（今回は警告のみ）。
- コスチュームフレームワークの登録範囲解析。

---

## ファイル構成

| 区分 | パス | 役割 |
|------|------|------|
| 新規 | `src/ReloadedHelper.Core/ModContentScanner.cs` | redirectルート配下の上書きパス抽出 |
| 新規 | `src/ReloadedHelper.Core/ConflictDetector.cs` | 同一パスを奪うMODの検出・後勝ち判定 |
| 新規 | `src/ReloadedHelper.Core/ModDiagnostics.cs` | 依存/対象ゲーム/競合の診断生成（日本語） |
| 新規 | `src/ReloadedHelper.Core/GameDiagnostics.cs` | 1ゲーム分の統合実行（スキャン→競合→診断） |
| 新規 | `src/ReloadedHelper.App/Views/DiagnosticsWindow.xaml`(.cs) | 診断結果一覧（背景スキャン→表示） |
| 変更 | `src/ReloadedHelper.App/Views/ModListView.xaml`(.cs) | ヘッダに「診断」ボタン |
| 新規 | `tests/ReloadedHelper.Core.Tests/ModContentScannerTests.cs` 他 | 各 Core クラスのテスト |

---

### Task D1: ModContentScanner（上書きパス抽出）

**Files:**
- Create: `src/ReloadedHelper.Core/ModContentScanner.cs`
- Test: `tests/ReloadedHelper.Core.Tests/ModContentScannerTests.cs`

**Interfaces:**
- Produces:
```csharp
public sealed record ModOverrides(string ModId, IReadOnlyList<string> Paths);
public static class ModContentScanner
{
    // mod フォルダ内の既知 redirect ルート配下の全ファイルを、
    // "<root小文字>/<相対パス小文字・/区切り>" の集合として返す。フォルダ無しなら空。
    public static ModOverrides Scan(string modFolderPath, string modId);
}
```

- [ ] **Step 1: 失敗するテスト**

```csharp
using System.IO;
namespace ReloadedHelper.Core.Tests;

public class ModContentScannerTests
{
    private static string MakeMod(params (string rel, string name)[] files)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"mod-{Guid.NewGuid():N}");
        foreach (var (rel, name) in files)
        {
            var full = Path.Combine(dir, rel.Replace('/', Path.DirectorySeparatorChar), name);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, "x");
        }
        return dir;
    }

    [Fact]
    public void Scan_collects_cpk_and_awb_paths_normalized_lowercase()
    {
        var dir = MakeMod(
            ("P5REssentials/CPK/BASE.CPK/Bustup", "B100_000_00.BIN"),
            ("FEmulator/AWB/BGM_42.AWB", "851_x.adx"),
            ("Readme", "notes.txt"));            // redirect 外は無視
        try
        {
            var o = ModContentScanner.Scan(dir, "m1");
            Assert.Equal("m1", o.ModId);
            Assert.Contains("p5ressentials/cpk/base.cpk/bustup/b100_000_00.bin", o.Paths);
            Assert.Contains("femulator/awb/bgm_42.awb/851_x.adx", o.Paths);
            Assert.Equal(2, o.Paths.Count);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Scan_returns_empty_when_no_redirect_roots()
    {
        var dir = MakeMod(("Something", "a.dll"));
        try { Assert.Empty(ModContentScanner.Scan(dir, "m2").Paths); }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Scan_missing_folder_is_empty()
    {
        Assert.Empty(ModContentScanner.Scan(Path.Combine(Path.GetTempPath(), "nope-xyz"), "m3").Paths);
    }
}
```

- [ ] **Step 2: テスト失敗を確認**

Run: `dotnet test --filter ModContentScanner`
Expected: FAIL（型なし）

- [ ] **Step 3: 実装**

```csharp
using System.IO;

namespace ReloadedHelper.Core;

public sealed record ModOverrides(string ModId, IReadOnlyList<string> Paths);

public static class ModContentScanner
{
    // 拡張ポイント: ゲームファイルを置き換える redirect ルート（相対・小文字比較）
    private static readonly string[] RedirectRoots = { "P5REssentials/CPK", "FEmulator/AWB" };

    public static ModOverrides Scan(string modFolderPath, string modId)
    {
        var paths = new List<string>();
        if (Directory.Exists(modFolderPath))
        {
            foreach (var root in RedirectRoots)
            {
                var rootDir = ResolveCaseInsensitive(modFolderPath, root);
                if (rootDir is null) continue;
                var rootKey = root.ToLowerInvariant();
                foreach (var file in Directory.EnumerateFiles(rootDir, "*", SearchOption.AllDirectories))
                {
                    var rel = Path.GetRelativePath(rootDir, file).Replace('\\', '/').ToLowerInvariant();
                    paths.Add($"{rootKey}/{rel}");
                }
            }
        }
        return new ModOverrides(modId, paths);
    }

    // "A/B" を modFolderPath 配下から大文字小文字無視で解決。無ければ null。
    private static string? ResolveCaseInsensitive(string baseDir, string relative)
    {
        var current = baseDir;
        foreach (var seg in relative.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!Directory.Exists(current)) return null;
            var match = Directory.GetDirectories(current)
                .FirstOrDefault(d => string.Equals(Path.GetFileName(d), seg, StringComparison.OrdinalIgnoreCase));
            if (match is null) return null;
            current = match;
        }
        return Directory.Exists(current) ? current : null;
    }
}
```

- [ ] **Step 4: テスト合格を確認**

Run: `dotnet test --filter ModContentScanner`
Expected: PASS

- [ ] **Step 5: コミット**

```bash
git add src/ReloadedHelper.Core/ModContentScanner.cs tests/ReloadedHelper.Core.Tests/ModContentScannerTests.cs
git commit -m "feat: ModContentScanner extracts redirect-root override paths"
```

---

### Task D2: ConflictDetector（競合検出・後勝ち判定）

**Files:**
- Create: `src/ReloadedHelper.Core/ConflictDetector.cs`
- Test: `tests/ReloadedHelper.Core.Tests/ConflictDetectorTests.cs`

**Interfaces:**
- Consumes: `ModOverrides`（D1）
- Produces:
```csharp
public sealed record FileConflict(string PathKey, IReadOnlyList<string> ModIds, string WinnerModId);
public static class ConflictDetector
{
    // orderedEnabled は読み込み順（index 0 = 最初に読み込み＝優先度低）。
    // 同一パスを 2 つ以上の MOD が持てば競合。WinnerModId = その並びで最後の MOD（後勝ち）。
    public static IReadOnlyList<FileConflict> Detect(IReadOnlyList<ModOverrides> orderedEnabled);
}
```

- [ ] **Step 1: 失敗するテスト**

```csharp
namespace ReloadedHelper.Core.Tests;

public class ConflictDetectorTests
{
    [Fact]
    public void Detect_finds_shared_path_winner_is_last_in_order()
    {
        var a = new ModOverrides("A", new[] { "p/x.bin", "p/a-only.bin" });
        var b = new ModOverrides("B", new[] { "p/x.bin" });
        var conflicts = ConflictDetector.Detect(new[] { a, b }); // A 先, B 後

        var c = Assert.Single(conflicts);
        Assert.Equal("p/x.bin", c.PathKey);
        Assert.Equal(new[] { "A", "B" }, c.ModIds);
        Assert.Equal("B", c.WinnerModId); // 後勝ち
    }

    [Fact]
    public void Detect_no_conflict_when_paths_disjoint()
    {
        var a = new ModOverrides("A", new[] { "p/a.bin" });
        var b = new ModOverrides("B", new[] { "p/b.bin" });
        Assert.Empty(ConflictDetector.Detect(new[] { a, b }));
    }
}
```

- [ ] **Step 2: テスト失敗を確認**

Run: `dotnet test --filter ConflictDetector`
Expected: FAIL

- [ ] **Step 3: 実装**

```csharp
namespace ReloadedHelper.Core;

public sealed record FileConflict(string PathKey, IReadOnlyList<string> ModIds, string WinnerModId);

public static class ConflictDetector
{
    public static IReadOnlyList<FileConflict> Detect(IReadOnlyList<ModOverrides> orderedEnabled)
    {
        var byPath = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var mo in orderedEnabled)
            foreach (var p in mo.Paths)
            {
                if (!byPath.TryGetValue(p, out var list)) byPath[p] = list = new List<string>();
                if (!list.Contains(mo.ModId)) list.Add(mo.ModId);
            }

        var result = new List<FileConflict>();
        foreach (var (path, mods) in byPath)
            if (mods.Count > 1)
                result.Add(new FileConflict(path, mods, mods[^1])); // 読み込み順で最後＝優先＝勝者
        return result;
    }
}
```

- [ ] **Step 4: テスト合格を確認**

Run: `dotnet test --filter ConflictDetector`
Expected: PASS

- [ ] **Step 5: コミット**

```bash
git add src/ReloadedHelper.Core/ConflictDetector.cs tests/ReloadedHelper.Core.Tests/ConflictDetectorTests.cs
git commit -m "feat: ConflictDetector finds shared override paths (last-wins)"
```

---

### Task D3: ModDiagnostics（依存・対象ゲーム・競合の診断生成）

**Files:**
- Create: `src/ReloadedHelper.Core/ModDiagnostics.cs`
- Test: `tests/ReloadedHelper.Core.Tests/ModDiagnosticsTests.cs`

**Interfaces:**
- Consumes: `GameInfo`, `ModInfo`（既存）, `FileConflict`（D2）
- Produces:
```csharp
public enum DiagnosticSeverity { Info, Warning }
public sealed record Diagnostic(string ModId, DiagnosticSeverity Severity, string Message);
public static class ModDiagnostics
{
    public static IReadOnlyList<Diagnostic> Analyze(
        GameInfo game,
        IReadOnlyDictionary<string, ModInfo> catalog,
        IReadOnlyList<FileConflict> conflicts);
}
```
診断ルール（有効MODのみ対象）：
1. **対象ゲーム違い**: `SupportedAppIds` が非空かつ `game.AppId` を含まない → Warning。
2. **依存未導入**: `Dependencies` の各依存が catalog に無い → Warning。
3. **依存無効**: 依存が catalog にあるが有効でない → Warning。
4. **上書き負け**: 競合で勝者でないMOD → Info（「N個のファイルが『勝者』に上書き」）。(loser,winner) ごとに集約。

- [ ] **Step 1: 失敗するテスト**

```csharp
namespace ReloadedHelper.Core.Tests;

public class ModDiagnosticsTests
{
    private static ModInfo Mod(string id, string[] appIds, string[]? deps = null) =>
        new(id, id, "", "1.0", "", Array.Empty<string>(), deps ?? Array.Empty<string>(),
            Array.Empty<string>(), appIds, null, null, null, null, "C:\\x");

    private static GameInfo Game(string appId, string[] enabled) =>
        new(appId, appId, "", null, enabled, enabled, "C:\\g");

    [Fact]
    public void Warns_on_appid_mismatch()
    {
        var cat = new Dictionary<string, ModInfo> { ["A"] = Mod("A", new[] { "p4g.exe" }) };
        var diags = ModDiagnostics.Analyze(Game("p5r.exe", new[] { "A" }), cat, Array.Empty<FileConflict>());
        Assert.Contains(diags, d => d.ModId == "A" && d.Severity == DiagnosticSeverity.Warning && d.Message.Contains("向け"));
    }

    [Fact]
    public void Warns_on_disabled_dependency()
    {
        var cat = new Dictionary<string, ModInfo>
        {
            ["A"] = Mod("A", new[] { "p5r.exe" }, new[] { "Lib" }),
            ["Lib"] = Mod("Lib", new[] { "p5r.exe" }),
        };
        // Lib は有効リストに無い
        var diags = ModDiagnostics.Analyze(Game("p5r.exe", new[] { "A" }), cat, Array.Empty<FileConflict>());
        Assert.Contains(diags, d => d.ModId == "A" && d.Severity == DiagnosticSeverity.Warning && d.Message.Contains("無効"));
    }

    [Fact]
    public void Warns_on_missing_dependency()
    {
        var cat = new Dictionary<string, ModInfo> { ["A"] = Mod("A", new[] { "p5r.exe" }, new[] { "Gone" }) };
        var diags = ModDiagnostics.Analyze(Game("p5r.exe", new[] { "A" }), cat, Array.Empty<FileConflict>());
        Assert.Contains(diags, d => d.ModId == "A" && d.Message.Contains("見つかりません"));
    }

    [Fact]
    public void Info_on_overwritten_loser_aggregated()
    {
        var cat = new Dictionary<string, ModInfo>
        {
            ["A"] = Mod("A", new[] { "p5r.exe" }),
            ["B"] = Mod("B", new[] { "p5r.exe" }),
        };
        var conflicts = new[]
        {
            new FileConflict("p/x.bin", new[] { "A", "B" }, "B"),
            new FileConflict("p/y.bin", new[] { "A", "B" }, "B"),
        };
        var diags = ModDiagnostics.Analyze(Game("p5r.exe", new[] { "A", "B" }), cat, conflicts);
        var loserDiag = Assert.Single(diags, d => d.ModId == "A" && d.Severity == DiagnosticSeverity.Info);
        Assert.Contains("2", loserDiag.Message);     // 2 ファイル集約
        Assert.Contains("B", loserDiag.Message);
    }
}
```

- [ ] **Step 2: テスト失敗を確認**

Run: `dotnet test --filter ModDiagnostics`
Expected: FAIL

- [ ] **Step 3: 実装**

```csharp
namespace ReloadedHelper.Core;

public enum DiagnosticSeverity { Info, Warning }

public sealed record Diagnostic(string ModId, DiagnosticSeverity Severity, string Message);

public static class ModDiagnostics
{
    public static IReadOnlyList<Diagnostic> Analyze(
        GameInfo game,
        IReadOnlyDictionary<string, ModInfo> catalog,
        IReadOnlyList<FileConflict> conflicts)
    {
        var enabled = new HashSet<string>(game.EnabledMods, StringComparer.OrdinalIgnoreCase);
        var result = new List<Diagnostic>();

        foreach (var modId in game.EnabledMods)
        {
            if (!catalog.TryGetValue(modId, out var info)) continue;

            if (info.SupportedAppIds.Count > 0 &&
                !info.SupportedAppIds.Contains(game.AppId, StringComparer.OrdinalIgnoreCase))
            {
                result.Add(new Diagnostic(modId, DiagnosticSeverity.Warning,
                    $"このMODは「{game.DisplayName}」向けではない可能性があります（対象: {string.Join(", ", info.SupportedAppIds)}）。"));
            }

            foreach (var dep in info.Dependencies)
            {
                if (!catalog.ContainsKey(dep))
                    result.Add(new Diagnostic(modId, DiagnosticSeverity.Warning,
                        $"必要な依存MOD「{dep}」が見つかりません。動作しない可能性があります。"));
                else if (!enabled.Contains(dep))
                    result.Add(new Diagnostic(modId, DiagnosticSeverity.Warning,
                        $"必要な依存MOD「{DisplayName(catalog, dep)}」が無効です。有効にしてください。"));
            }
        }

        // 競合の集約：(敗者, 勝者) ごとにファイル数を数える
        var pairCount = new Dictionary<(string Loser, string Winner), int>();
        foreach (var c in conflicts)
            foreach (var m in c.ModIds)
                if (!string.Equals(m, c.WinnerModId, StringComparison.OrdinalIgnoreCase))
                {
                    var key = (m, c.WinnerModId);
                    pairCount[key] = pairCount.TryGetValue(key, out var n) ? n + 1 : 1;
                }

        foreach (var (key, count) in pairCount)
            result.Add(new Diagnostic(key.Loser, DiagnosticSeverity.Info,
                $"このMODの {count} 個のファイルが「{DisplayName(catalog, key.Winner)}」に上書きされています（読み込み順で後のMODが優先）。意図しない場合は順序を入れ替えてください。"));

        return result;
    }

    private static string DisplayName(IReadOnlyDictionary<string, ModInfo> catalog, string modId) =>
        catalog.TryGetValue(modId, out var info) && info.ModName.Length > 0 ? info.ModName : modId;
}
```

- [ ] **Step 4: テスト合格を確認**

Run: `dotnet test --filter ModDiagnostics`
Expected: PASS（4件）

- [ ] **Step 5: コミット**

```bash
git add src/ReloadedHelper.Core/ModDiagnostics.cs tests/ReloadedHelper.Core.Tests/ModDiagnosticsTests.cs
git commit -m "feat: ModDiagnostics — appid/dependency/overwrite findings (JA)"
```

---

### Task D4: GameDiagnostics（1ゲーム分の統合実行）

**Files:**
- Create: `src/ReloadedHelper.Core/GameDiagnostics.cs`
- Test: `tests/ReloadedHelper.Core.Tests/GameDiagnosticsTests.cs`

**Interfaces:**
- Consumes: `ModContentScanner`(D1), `ConflictDetector`(D2), `ModDiagnostics`(D3), `GameInfo`/`ModInfo`（既存）
- Produces:
```csharp
public sealed record GameDiagnosticsResult(
    IReadOnlyList<FileConflict> Conflicts, IReadOnlyList<Diagnostic> Diagnostics);
public static class GameDiagnostics
{
    // 有効MODを読み込み順（game.SortedMods 順）にスキャン→競合→診断。
    public static GameDiagnosticsResult Run(GameInfo game, IReadOnlyDictionary<string, ModInfo> catalog);
}
```

- [ ] **Step 1: 失敗するテスト（実フォルダ）**

```csharp
using System.IO;
namespace ReloadedHelper.Core.Tests;

public class GameDiagnosticsTests
{
    private static string MakeModWithFile(string overrideRel)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"gmod-{Guid.NewGuid():N}");
        var full = Path.Combine(dir, overrideRel.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, "x");
        return dir;
    }

    private static ModInfo Mod(string id, string folder) =>
        new(id, id, "", "1.0", "", Array.Empty<string>(), Array.Empty<string>(),
            Array.Empty<string>(), new[] { "p5r.exe" }, null, null, null, null, folder);

    [Fact]
    public void Run_detects_conflict_between_two_enabled_mods()
    {
        var dirA = MakeModWithFile("P5REssentials/CPK/Base.cpk/x.bin");
        var dirB = MakeModWithFile("P5REssentials/CPK/BASE.CPK/X.BIN"); // 大文字違い＝同一パス扱い
        try
        {
            var cat = new Dictionary<string, ModInfo> { ["A"] = Mod("A", dirA), ["B"] = Mod("B", dirB) };
            var game = new GameInfo("p5r.exe", "P5R", "", null,
                new[] { "A", "B" }, new[] { "A", "B" }, "C:\\g"); // SortedMods=[A,B] → B 後勝ち
            var res = GameDiagnostics.Run(game, cat);

            Assert.NotEmpty(res.Conflicts);
            Assert.Equal("B", res.Conflicts[0].WinnerModId);
            Assert.Contains(res.Diagnostics, d => d.ModId == "A" && d.Severity == DiagnosticSeverity.Info);
        }
        finally { Directory.Delete(dirA, true); Directory.Delete(dirB, true); }
    }
}
```

- [ ] **Step 2: テスト失敗を確認**

Run: `dotnet test --filter GameDiagnostics`
Expected: FAIL

- [ ] **Step 3: 実装**

```csharp
namespace ReloadedHelper.Core;

public sealed record GameDiagnosticsResult(
    IReadOnlyList<FileConflict> Conflicts, IReadOnlyList<Diagnostic> Diagnostics);

public static class GameDiagnostics
{
    public static GameDiagnosticsResult Run(GameInfo game, IReadOnlyDictionary<string, ModInfo> catalog)
    {
        var enabled = new HashSet<string>(game.EnabledMods, StringComparer.OrdinalIgnoreCase);
        var ordered = new List<ModOverrides>();
        foreach (var modId in game.SortedMods)               // 読み込み順
        {
            if (!enabled.Contains(modId)) continue;
            if (!catalog.TryGetValue(modId, out var info)) continue;
            ordered.Add(ModContentScanner.Scan(info.FolderPath, modId));
        }
        var conflicts = ConflictDetector.Detect(ordered);
        var diagnostics = ModDiagnostics.Analyze(game, catalog, conflicts);
        return new GameDiagnosticsResult(conflicts, diagnostics);
    }
}
```

- [ ] **Step 4: テスト合格を確認**

Run: `dotnet test --filter GameDiagnostics`
Expected: PASS

- [ ] **Step 5: コミット**

```bash
git add src/ReloadedHelper.Core/GameDiagnostics.cs tests/ReloadedHelper.Core.Tests/GameDiagnosticsTests.cs
git commit -m "feat: GameDiagnostics ties scanner+conflicts+diagnostics per game"
```

---

### Task D5: DiagnosticsWindow ＋「診断」ボタン

**Files:**
- Create: `src/ReloadedHelper.App/Views/DiagnosticsWindow.xaml`(.cs)
- Modify: `src/ReloadedHelper.App/Views/ModListView.xaml`（ヘッダに「診断」ボタン）
- Modify: `src/ReloadedHelper.App/Views/ModListView.xaml.cs`（ハンドラ）

**Interfaces:**
- Consumes: `GameDiagnostics.Run`(D4), `MainViewModel.SelectedGame`/`AllMods`（既存）
- Produces: `public DiagnosticsWindow(GameInfo game, IReadOnlyDictionary<string, ModInfo> catalog)`

> UI のため手動検証。スキャンは件数が多く重いので**バックグラウンド実行**して完了後に表示する。

- [ ] **Step 1: DiagnosticsWindow.xaml を作成**

```xml
<Window x:Class="ReloadedHelper.App.Views.DiagnosticsWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="診断" Width="640" Height="600"
        WindowStartupLocation="CenterOwner"
        Background="{DynamicResource BgMainBrush}"
        FontFamily="{DynamicResource MainFont}">
    <Grid Margin="20">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
        </Grid.RowDefinitions>

        <TextBlock x:Name="HeaderText" Grid.Row="0" FontSize="16" FontWeight="Bold"
                   Foreground="{DynamicResource TextPrimaryBrush}" Margin="0,0,0,12"/>

        <TextBlock x:Name="StatusText" Grid.Row="1" Text="解析中..."
                   Foreground="{DynamicResource TextLabelBrush}" VerticalAlignment="Top"/>

        <ScrollViewer Grid.Row="1" VerticalScrollBarVisibility="Auto">
            <ItemsControl x:Name="FindingsList">
                <ItemsControl.ItemTemplate>
                    <DataTemplate>
                        <Border Background="{DynamicResource BgCardBrush}" CornerRadius="6"
                                Padding="12,10" Margin="0,0,0,8">
                            <StackPanel>
                                <TextBlock Text="{Binding Title}" FontWeight="Bold"
                                           Foreground="{Binding TitleBrush}" TextWrapping="Wrap"/>
                                <TextBlock Text="{Binding Message}" Margin="0,4,0,0"
                                           Foreground="{DynamicResource TextBodyBrush}" TextWrapping="Wrap"/>
                            </StackPanel>
                        </Border>
                    </DataTemplate>
                </ItemsControl.ItemTemplate>
            </ItemsControl>
        </ScrollViewer>
    </Grid>
</Window>
```

- [ ] **Step 2: DiagnosticsWindow.xaml.cs を作成**

```csharp
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using ReloadedHelper.Core;

namespace ReloadedHelper.App.Views;

public partial class DiagnosticsWindow : Window
{
    public sealed record Row(string Title, string Message, Brush TitleBrush);

    private readonly ObservableCollection<Row> _rows = new();
    private readonly GameInfo _game;
    private readonly IReadOnlyDictionary<string, ModInfo> _catalog;

    public DiagnosticsWindow(GameInfo game, IReadOnlyDictionary<string, ModInfo> catalog)
    {
        _game = game;
        _catalog = catalog;
        InitializeComponent();
        HeaderText.Text = $"{game.DisplayName} の診断";
        FindingsList.ItemsSource = _rows;
        _ = RunAsync();
    }

    private async Task RunAsync()
    {
        var result = await Task.Run(() => GameDiagnostics.Run(_game, _catalog));

        var warnBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#C0392B")!); // 既存の警告赤を流用
        var infoBrush = (Brush)FindResource("TextLabelBrush");

        foreach (var d in result.Diagnostics
                     .OrderByDescending(d => d.Severity)) // Warning 優先
        {
            var modName = _catalog.TryGetValue(d.ModId, out var info) && info.ModName.Length > 0
                ? info.ModName : d.ModId;
            var title = (d.Severity == DiagnosticSeverity.Warning ? "⚠ " : "・ ") + modName;
            _rows.Add(new Row(title, d.Message,
                d.Severity == DiagnosticSeverity.Warning ? warnBrush : infoBrush));
        }

        StatusText.Text = _rows.Count == 0 ? "問題は見つかりませんでした。" : "";
        StatusText.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
}
```

- [ ] **Step 3: ModListView に「診断」ボタンを追加**

`ModListView.xaml` のヘッダのボタン群（「全件強制取り直し」付近）に既存ボタンと同じスタイルで追加：
```xml
<Button x:Name="DiagnoseButton" Content="診断" Click="DiagnoseButton_Click" Margin="8,0,0,0"/>
```
（周囲の既存ヘッダボタンと同じ属性／リソースに合わせること。）

`ModListView.xaml.cs` に追加：
```csharp
private void DiagnoseButton_Click(object sender, RoutedEventArgs e)
{
    if (DataContext is not MainViewModel vm || vm.SelectedGame is not { } game) return;
    var win = new DiagnosticsWindow(game, vm.AllMods) { Owner = Window.GetWindow(this) };
    win.ShowDialog();
}
```

- [ ] **Step 4: ビルド**

Run: `dotnet build reloaded-helper.slnx`
Expected: 0 errors

- [ ] **Step 5: 手動検証（コントローラが実施）**

`-p:Version=9.9.9` ビルドで起動：
1. ゲームタブを選び「診断」を押す → 「解析中...」の後に診断一覧が出る。
2. 競合があるMOD（同種スキン等）に「N個のファイルが『〜』に上書きされています」が出る。
3. 依存が無効なMODに「依存『〜』が無効です」警告。対象ゲーム違いに警告。
4. 問題なしなら「問題は見つかりませんでした。」。

- [ ] **Step 6: コミット**

```bash
git add src/ReloadedHelper.App/Views/DiagnosticsWindow.xaml src/ReloadedHelper.App/Views/DiagnosticsWindow.xaml.cs src/ReloadedHelper.App/Views/ModListView.xaml src/ReloadedHelper.App/Views/ModListView.xaml.cs
git commit -m "feat: DiagnosticsWindow + 診断 button (conflicts + why-not-working)"
```

---

## Self-Review（計画 vs 仕様）

- **E-1 MOD内容スキャナ** → Task D1（redirect ルート抽出）✅
- **E-2 競合・被り検出** → Task D2（同一パス・後勝ち）✅
- **E-3「なぜ効かない」診断** → Task D3（依存無効/未導入・対象ゲーム違い・上書き負け）✅
- **E-4 読み込み順** → 既存の起動時依存ソートが安全な自動修正を担当。競合は D3/D5 で警告（迷うものは警告）✅
- **統合・UI** → Task D4（統合）・D5（診断画面＋ボタン）✅
- **E-5 クラッシュ検知** → 今回スコープ外（明記済み、将来の反復）。
- 型整合：`ModOverrides`/`FileConflict`/`Diagnostic`/`DiagnosticSeverity`/`GameDiagnosticsResult`/各シグネチャは全タスクで一貫。`GameInfo`/`ModInfo` の実コンストラクタ引数順（テスト内）に注意。
- 制約：書き込みなし（読み取り専用解析）。実行時AI非依存。新規色なし（警告は既存 `#C0392B` を流用）。拡張ポイント（RedirectRoots 配列・診断ルール追加）を明示。
