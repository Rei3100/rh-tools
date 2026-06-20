# Phase 2.5 + Phase 3 実装計画

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** UIバグ修正・自動並び替え・自動アップデートを実装し、rh-tools をデイリーユース可能にする

**Architecture:** トポロジカルソート（Kahn's algorithm）を Core に実装、App 起動時に全ゲームのロードオーダーを自動書き換え。設定・ヘルプはオーバーレイ方式に変更して MOD 画面を見ながら拡大率をリアルタイム変更できるようにする。GitHub Releases を使った自己更新機能も追加。

**Tech Stack:** C# 13 / .NET 10 / WPF / System.Text.Json / System.Net.Http（標準ライブラリのみ）/ xUnit

## Global Constraints

- ランタイム NuGet パッケージ追加禁止（System.Text.Json・System.Net.Http は .NET 標準 → OK）
- テスト専用の xUnit は可
- ユーザーデータ保存先: `%APPDATA%\ReloadedHelper` のみ
- AppConfig.json 書き換え前に必ずバックアップを作成する
- 自動アップデートのインストール先: `C:\FreeSoft\ReloadedHelper\ReloadedHelper.App.exe`
- GitHub リポジトリ: `Rei3100/rh-tools`（Public）

## Files

### 新規作成
- `src/ReloadedHelper.Core/LoadOrderSorter.cs` — トポロジカルソートのロジック（副作用なし）
- `src/ReloadedHelper.Core/AppConfigWriter.cs` — AppConfig.json の EnabledMods を書き換える
- `src/ReloadedHelper.Core/LoadOrderBackupService.cs` — バックアップの保存・復元（直近3世代）
- `src/ReloadedHelper.Core/UpdateChecker.cs` — GitHub Releases API で最新版を確認
- `tests/ReloadedHelper.Core.Tests/LoadOrderSorterTests.cs`
- `tests/ReloadedHelper.Core.Tests/AppConfigWriterTests.cs`
- `tests/ReloadedHelper.Core.Tests/LoadOrderBackupServiceTests.cs`
- `.github/workflows/release.yml` — tag トリガー・GitHub Release 作成

### 変更
- `src/ReloadedHelper.App/App.xaml.cs` — Mutex・起動最大化・自動並び替え・自動アップデート
- `src/ReloadedHelper.App/MainWindow.xaml.cs` — トレイ復帰最大化・オーバーレイ閉じる・Escape キー
- `src/ReloadedHelper.App/MainWindow.xaml` — ContentControl からオーバーレイ方式に変更
- `src/ReloadedHelper.App/ShellViewModel.cs` — IsOverlayVisible・CurrentOverlayView 追加
- `src/ReloadedHelper.App/Views/SettingsView.xaml` — フォルダパスのフォント修正

---

## Task 1: ウィンドウ管理（起動最大化・トレイ復帰最大化・多重起動禁止）

**Files:**
- Modify: `src/ReloadedHelper.App/App.xaml.cs`
- Modify: `src/ReloadedHelper.App/MainWindow.xaml.cs`
- Modify: `src/ReloadedHelper.App/MainWindow.xaml`

**Interfaces:**
- 変更なし（内部挙動の修正のみ）

- [ ] **Step 1: App.xaml.cs に Mutex・最大化を追加**

`OnStartup` の先頭に追加し、末尾の `window.Show()` を `window.Show()` 直前に最大化処理を追加する。

```csharp
// App.xaml.cs の先頭 using に追加:
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

// App クラス内に追加（クラスレベル）:
private static Mutex? _appMutex;

[DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
[DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
private const int SW_RESTORE = 9;

private static void ActivateExistingInstance()
{
    var currentId = Environment.ProcessId;
    var name = Path.GetFileNameWithoutExtension(
        Environment.ProcessPath ?? "ReloadedHelper.App");
    foreach (var proc in Process.GetProcessesByName(name))
    {
        if (proc.Id == currentId) continue;
        var hwnd = proc.MainWindowHandle;
        if (hwnd == IntPtr.Zero) continue;
        ShowWindow(hwnd, SW_RESTORE);
        SetForegroundWindow(hwnd);
        return;
    }
}
```

`OnStartup` の **先頭**（base.OnStartup より前）に追加:

```csharp
_appMutex = new Mutex(true, "Global\\ReloadedHelper_v1", out bool createdNew);
if (!createdNew)
{
    ActivateExistingInstance();
    Shutdown();
    return;
}
```

`OnStartup` 末尾の `window.Show()` を以下に置き換え:

```csharp
window.WindowState = WindowState.Maximized;
window.Show();
```

- [ ] **Step 2: MainWindow.xaml.cs の RestoreWindow を最大化に修正**

`MainWindow.xaml.cs` の `RestoreWindow()` メソッドを探して修正する。現在は `WindowState.Normal` → `WindowState.Maximized` に変更:

```csharp
private void RestoreWindow()
{
    Show();
    WindowState = WindowState.Maximized;   // ← Normal から変更
    Activate();
    TaskbarIcon.Visibility = Visibility.Collapsed;
}
```

- [ ] **Step 3: ビルドして動作確認**

```powershell
dotnet build reloaded-helper.slnx --configuration Release --no-restore
```

期待: ビルドが通る。アプリを起動すると最大化、2回目の起動で既存ウィンドウが前面に出る。

- [ ] **Step 4: コミット**

```powershell
git add src/ReloadedHelper.App/App.xaml.cs src/ReloadedHelper.App/MainWindow.xaml.cs
git commit -m "feat: maximize on start, single-instance mutex, restore-from-tray maximizes"
```

---

## Task 2: 設定バグ修正 + 設定・ヘルプをオーバーレイ化

**Files:**
- Modify: `src/ReloadedHelper.App/Views/SettingsView.xaml`
- Modify: `src/ReloadedHelper.App/ShellViewModel.cs`
- Modify: `src/ReloadedHelper.App/MainWindow.xaml`
- Modify: `src/ReloadedHelper.App/MainWindow.xaml.cs`

**Interfaces:**
- `ShellViewModel` に `IsOverlayVisible (bool)` と `CurrentOverlayView (object?)` を追加
- `MainWindow` は `CloseOverlay_Click` ハンドラを追加

- [ ] **Step 1: SettingsView.xaml のフォルダパス TextBox のフォントを修正**

フォルダパス表示 TextBox に `FontFamily="Consolas"` を追加（BIZ UDPGothic は `\` を `¥` に描画するため）。

```xml
<!-- SettingsView.xaml: Reloaded-II フォルダの TextBox を以下に変更 -->
<TextBox Grid.Column="0"
         Text="{Binding ReloadedInstallPath, Mode=OneWay}"
         IsReadOnly="True"
         FontFamily="Consolas"
         Margin="0,0,8,0" Height="32"/>
```

- [ ] **Step 2: ShellViewModel に IsOverlayVisible と CurrentOverlayView を追加**

```csharp
// ShellViewModel.cs — CurrentView プロパティの setter 末尾に Notify 追加:
set
{
    if (_currentView == value) return;
    _currentView = value;
    Notify();
    Notify(nameof(IsModListActive));
    Notify(nameof(IsSettingsActive));
    Notify(nameof(IsHelpActive));
    Notify(nameof(IsOverlayVisible));      // ← 追加
    Notify(nameof(CurrentOverlayView));    // ← 追加
}

// 以下のプロパティを追加（既存プロパティの下に）:
public bool IsOverlayVisible => IsSettingsActive || IsHelpActive;

public object? CurrentOverlayView =>
    IsSettingsActive ? SettingsVm :
    IsHelpActive ? (object?)HelpVm :
    null;
```

- [ ] **Step 3: MainWindow.xaml をオーバーレイ方式に変更**

現在の `ContentControl` が Grid.Column="1" に配置されている部分を、以下の構造に置き換える。

```xml
<!-- MainWindow.xaml: Grid.Column="1" の部分を以下に全置換 -->
<Grid Grid.Column="1">

    <!-- 常に表示される MOD 一覧 -->
    <views:ModListView DataContext="{Binding ModListVm}"/>

    <!-- 設定・ヘルプ オーバーレイ -->
    <Grid Visibility="{Binding IsOverlayVisible,
                       Converter={StaticResource BoolToVisConverter}}">

        <!-- 半透明バックドロップ -->
        <Rectangle Fill="Black" Opacity="0.45"/>

        <!-- パネル本体 -->
        <Border Width="560" MaxHeight="620"
                Background="{DynamicResource BgBarBrush}"
                BorderBrush="{DynamicResource BorderInputBrush}" BorderThickness="1"
                CornerRadius="12"
                HorizontalAlignment="Center" VerticalAlignment="Center">
            <Grid>
                <Grid.RowDefinitions>
                    <RowDefinition Height="Auto"/>
                    <RowDefinition Height="*"/>
                </Grid.RowDefinitions>

                <!-- 閉じるボタン -->
                <Button Grid.Row="0" Content="✕"
                        Width="32" Height="32"
                        HorizontalAlignment="Right" Margin="8"
                        Click="CloseOverlay_Click"
                        Style="{StaticResource NavButtonStyle}"/>

                <!-- コンテンツ（設定 or ヘルプ） -->
                <ContentControl Grid.Row="1"
                                Content="{Binding CurrentOverlayView}">
                    <ContentControl.Resources>
                        <DataTemplate DataType="{x:Type core:SettingsViewModel}">
                            <views:SettingsView/>
                        </DataTemplate>
                        <DataTemplate DataType="{x:Type app:HelpViewModel}">
                            <views:HelpView/>
                        </DataTemplate>
                    </ContentControl.Resources>
                </ContentControl>
            </Grid>
        </Border>
    </Grid>
</Grid>
```

また `Window.Resources` に BoolToVisibilityConverter を追加する（なければ追加）:

```xml
<Window.Resources>
    <BooleanToVisibilityConverter x:Key="BoolToVisConverter"/>
</Window.Resources>
```

- [ ] **Step 4: MainWindow.xaml.cs に CloseOverlay_Click と Escape キーを追加**

```csharp
// MainWindow.xaml.cs に追加:
private void CloseOverlay_Click(object sender, RoutedEventArgs e)
    => _shell.ShowModList();

protected override void OnKeyDown(System.Windows.Input.KeyEventArgs e)
{
    if (e.Key == System.Windows.Input.Key.Escape && _shell.IsOverlayVisible)
    {
        _shell.ShowModList();
        e.Handled = true;
    }
    base.OnKeyDown(e);
}
```

- [ ] **Step 5: ビルドして動作確認**

```powershell
dotnet build reloaded-helper.slnx --configuration Release --no-restore
```

期待:
- アプリ起動 → MOD 一覧が表示される
- 設定アイコンをクリック → MOD 一覧の上にオーバーレイで設定が表示される
- Escape / ✕ ボタンで閉じる
- 拡大率スライダーを動かすと MOD 一覧がリアルタイムで拡縮する
- フォルダパスがバックスラッシュで正しく表示される

- [ ] **Step 6: コミット**

```powershell
git add src/ReloadedHelper.App/Views/SettingsView.xaml `
        src/ReloadedHelper.App/ShellViewModel.cs `
        src/ReloadedHelper.App/MainWindow.xaml `
        src/ReloadedHelper.App/MainWindow.xaml.cs
git commit -m "feat: settings/help as overlay, live zoom preview, fix folder path font"
```

---

## Task 3: LoadOrderSorter (TDD)

**Files:**
- Create: `src/ReloadedHelper.Core/LoadOrderSorter.cs`
- Create: `tests/ReloadedHelper.Core.Tests/LoadOrderSorterTests.cs`

**Interfaces:**
- Produces:
  ```csharp
  public static class LoadOrderSorter
  {
      public static IReadOnlyList<string> Sort(
          IReadOnlyList<string> currentOrder,
          IReadOnlyDictionary<string, IReadOnlyList<string>> dependenciesOf);
  }
  ```
- `currentOrder`: 現在のロードオーダー（上から順に）
- `dependenciesOf`: modId → そのMODが依存しているmodIdのリスト
- 戻り値: 依存関係を満たした新しいロードオーダー（依存先が上）

- [ ] **Step 1: テストファイルを作成**

```csharp
// tests/ReloadedHelper.Core.Tests/LoadOrderSorterTests.cs
using ReloadedHelper.Core;

namespace ReloadedHelper.Core.Tests;

public class LoadOrderSorterTests
{
    private static IReadOnlyDictionary<string, IReadOnlyList<string>> NoDeps =>
        new Dictionary<string, IReadOnlyList<string>>();

    [Fact]
    public void NoDependencies_PreservesCurrentOrder()
    {
        var order = new[] { "ModA", "ModB", "ModC" };
        var result = LoadOrderSorter.Sort(order, NoDeps);
        Assert.Equal(order, result);
    }

    [Fact]
    public void SimpleDependency_DependencyPlacedFirst()
    {
        // ModA depends on ModB → ModB must come before ModA
        var order = new[] { "ModA", "ModB" };
        var deps = new Dictionary<string, IReadOnlyList<string>>
        {
            ["ModA"] = new[] { "ModB" }
        };
        var result = LoadOrderSorter.Sort(order, deps);
        Assert.Equal(new[] { "ModB", "ModA" }, result);
    }

    [Fact]
    public void TransitiveDependency_AllSortedCorrectly()
    {
        // C depends on B, B depends on A → sorted: A, B, C
        var order = new[] { "C", "B", "A" };
        var deps = new Dictionary<string, IReadOnlyList<string>>
        {
            ["C"] = new[] { "B" },
            ["B"] = new[] { "A" }
        };
        var result = LoadOrderSorter.Sort(order, deps);
        Assert.Equal(new[] { "A", "B", "C" }, result);
    }

    [Fact]
    public void IndependentMods_PreserveRelativeOrder()
    {
        // ModC depends on ModA. ModB is independent.
        // Original: ModB(0), ModC(1), ModA(2)
        // ModB no deps → ready at idx 0; ModA no deps → ready at idx 2
        // ModC deps satisfied after ModA
        // Expected: ModB, ModA, ModC
        var order = new[] { "ModB", "ModC", "ModA" };
        var deps = new Dictionary<string, IReadOnlyList<string>>
        {
            ["ModC"] = new[] { "ModA" }
        };
        var result = LoadOrderSorter.Sort(order, deps);
        Assert.Equal(new[] { "ModB", "ModA", "ModC" }, result);
    }

    [Fact]
    public void CircularDependency_ReturnsAllModsWithoutThrowing()
    {
        var order = new[] { "ModA", "ModB" };
        var deps = new Dictionary<string, IReadOnlyList<string>>
        {
            ["ModA"] = new[] { "ModB" },
            ["ModB"] = new[] { "ModA" }
        };
        var result = LoadOrderSorter.Sort(order, deps);
        Assert.Equal(2, result.Count);
        Assert.Contains("ModA", result);
        Assert.Contains("ModB", result);
    }

    [Fact]
    public void UninstalledDependency_IsIgnored()
    {
        // ModA depends on LibX which is not in the list
        var order = new[] { "ModA", "ModB" };
        var deps = new Dictionary<string, IReadOnlyList<string>>
        {
            ["ModA"] = new[] { "LibX" }
        };
        var result = LoadOrderSorter.Sort(order, deps);
        // LibX not installed → no constraint on ModA → original order preserved
        Assert.Equal(new[] { "ModA", "ModB" }, result);
    }
}
```

- [ ] **Step 2: テストを実行して失敗を確認**

```powershell
dotnet test tests/ReloadedHelper.Core.Tests/ --filter "LoadOrderSorterTests" -v minimal
```

期待: `error CS0234: The type or namespace name 'LoadOrderSorter' does not exist`

- [ ] **Step 3: LoadOrderSorter を実装**

```csharp
// src/ReloadedHelper.Core/LoadOrderSorter.cs
namespace ReloadedHelper.Core;

public static class LoadOrderSorter
{
    /// <summary>
    /// Returns modIds in load order where dependencies come before dependents.
    /// Preserves relative order among mods with no dependency relationship.
    /// Circular dependencies are appended at the end in original order.
    /// </summary>
    public static IReadOnlyList<string> Sort(
        IReadOnlyList<string> currentOrder,
        IReadOnlyDictionary<string, IReadOnlyList<string>> dependenciesOf)
    {
        var allMods = new HashSet<string>(currentOrder, StringComparer.OrdinalIgnoreCase);
        // Map each modId to its original index (for stable ordering)
        var indexMap = currentOrder
            .Select((id, i) => (id, i))
            .ToDictionary(x => x.id, x => x.i, StringComparer.OrdinalIgnoreCase);

        // Build: dep → list of mods that depend on dep
        var dependents = allMods.ToDictionary(
            m => m, _ => new List<string>(), StringComparer.OrdinalIgnoreCase);
        var inDegree = allMods.ToDictionary(
            m => m, _ => 0, StringComparer.OrdinalIgnoreCase);

        foreach (var mod in allMods)
        {
            if (!dependenciesOf.TryGetValue(mod, out var deps)) continue;
            foreach (var dep in deps)
            {
                if (!allMods.Contains(dep)) continue; // not installed → ignore
                dependents[dep].Add(mod);
                inDegree[mod]++;
            }
        }

        // Kahn's algorithm with original-index priority for stable ordering
        var comparer = Comparer<(int idx, string id)>.Create((a, b) =>
        {
            int c = a.idx.CompareTo(b.idx);
            return c != 0 ? c : StringComparer.OrdinalIgnoreCase.Compare(a.id, b.id);
        });
        var ready = new SortedSet<(int idx, string id)>(comparer);
        foreach (var mod in allMods.Where(m => inDegree[m] == 0))
            ready.Add((indexMap[mod], mod));

        var result = new List<string>(allMods.Count);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        while (ready.Count > 0)
        {
            var item = ready.Min;
            ready.Remove(item);
            result.Add(item.id);
            visited.Add(item.id);

            foreach (var dependent in dependents[item.id])
            {
                inDegree[dependent]--;
                if (inDegree[dependent] == 0)
                    ready.Add((indexMap.GetValueOrDefault(dependent, int.MaxValue), dependent));
            }
        }

        // Append circular-dep remainder in original order
        foreach (var mod in currentOrder)
            if (!visited.Contains(mod))
                result.Add(mod);

        return result.AsReadOnly();
    }
}
```

- [ ] **Step 4: テストを実行して全パスを確認**

```powershell
dotnet test tests/ReloadedHelper.Core.Tests/ --filter "LoadOrderSorterTests" -v normal
```

期待: 全6テスト PASS

- [ ] **Step 5: コミット**

```powershell
git add src/ReloadedHelper.Core/LoadOrderSorter.cs `
        tests/ReloadedHelper.Core.Tests/LoadOrderSorterTests.cs
git commit -m "feat(core): LoadOrderSorter — topological sort preserving relative order"
```

---

## Task 4: AppConfigWriter + LoadOrderBackupService (TDD)

**Files:**
- Create: `src/ReloadedHelper.Core/LoadOrderBackupService.cs`
- Create: `src/ReloadedHelper.Core/AppConfigWriter.cs`
- Create: `tests/ReloadedHelper.Core.Tests/AppConfigWriterTests.cs`
- Create: `tests/ReloadedHelper.Core.Tests/LoadOrderBackupServiceTests.cs`

**Interfaces:**
- Produces:
  ```csharp
  public static class LoadOrderBackupService
  {
      public static void Backup(string configPath, string appId);
      public static IReadOnlyList<string> ListBackups(string appId);
      public static void Restore(string backupPath, string configPath);
  }

  public static class AppConfigWriter
  {
      public static void WriteOrder(
          string configPath, string appId, IReadOnlyList<string> newEnabledMods);
  }
  ```

- [ ] **Step 1: バックアップサービスのテストを作成**

```csharp
// tests/ReloadedHelper.Core.Tests/LoadOrderBackupServiceTests.cs
using ReloadedHelper.Core;

namespace ReloadedHelper.Core.Tests;

public class LoadOrderBackupServiceTests : IDisposable
{
    private readonly string _tmpConfig = Path.GetTempFileName();

    public LoadOrderBackupServiceTests()
        => File.WriteAllText(_tmpConfig, """{"EnabledMods":["A","B"]}""");

    public void Dispose() => File.Delete(_tmpConfig);

    [Fact]
    public void Backup_CreatesFileInBackupDirectory()
    {
        LoadOrderBackupService.Backup(_tmpConfig, "test-app-backup1");
        var backups = LoadOrderBackupService.ListBackups("test-app-backup1");
        Assert.Single(backups);
        Assert.EndsWith(".json", backups[0]);
    }

    [Fact]
    public void Backup_KeepsOnlyLatestThree()
    {
        for (int i = 0; i < 5; i++)
        {
            Thread.Sleep(10); // ensure different timestamps
            LoadOrderBackupService.Backup(_tmpConfig, "test-app-prune");
        }
        var backups = LoadOrderBackupService.ListBackups("test-app-prune");
        Assert.Equal(3, backups.Count);
    }

    [Fact]
    public void Restore_CopiesBackupToConfigPath()
    {
        LoadOrderBackupService.Backup(_tmpConfig, "test-app-restore");
        var backup = LoadOrderBackupService.ListBackups("test-app-restore")[0];

        var dest = Path.GetTempFileName();
        File.WriteAllText(dest, "{}");
        LoadOrderBackupService.Restore(backup, dest);

        Assert.Equal("""{"EnabledMods":["A","B"]}""", File.ReadAllText(dest));
        File.Delete(dest);
    }
}
```

- [ ] **Step 2: AppConfigWriter のテストを作成**

```csharp
// tests/ReloadedHelper.Core.Tests/AppConfigWriterTests.cs
using ReloadedHelper.Core;

namespace ReloadedHelper.Core.Tests;

public class AppConfigWriterTests : IDisposable
{
    private readonly string _tmp = Path.GetTempFileName();

    public void Dispose() => File.Delete(_tmp);

    [Fact]
    public void WriteOrder_UpdatesEnabledModsPreservingOtherFields()
    {
        File.WriteAllText(_tmp, """
            {
              "AppId": "p5r.exe",
              "AppName": "P5R",
              "EnabledMods": ["ModA", "ModB", "ModC"],
              "SortedMods": ["ModA", "ModB", "ModC"]
            }
            """);

        AppConfigWriter.WriteOrder(_tmp, "p5r.exe", new[] { "ModC", "ModA", "ModB" });

        var result = AppConfigParser.Parse(File.ReadAllText(_tmp), Path.GetTempPath());
        Assert.Equal(new[] { "ModC", "ModA", "ModB" }, result.EnabledMods);
        Assert.Equal("P5R", result.AppName); // other fields preserved
    }

    [Fact]
    public void WriteOrder_CreatesBackupBeforeWriting()
    {
        File.WriteAllText(_tmp, """{"AppId":"test","EnabledMods":["A","B"]}""");
        var appId = "writer-backup-test";

        AppConfigWriter.WriteOrder(_tmp, appId, new[] { "B", "A" });

        var backups = LoadOrderBackupService.ListBackups(appId);
        Assert.Single(backups);
        // Backup contains original order
        var original = AppConfigParser.Parse(File.ReadAllText(backups[0]), Path.GetTempPath());
        Assert.Equal(new[] { "A", "B" }, original.EnabledMods);
    }
}
```

- [ ] **Step 3: テストを実行して失敗を確認**

```powershell
dotnet test tests/ReloadedHelper.Core.Tests/ --filter "AppConfigWriterTests|LoadOrderBackupServiceTests" -v minimal
```

期待: コンパイルエラー（クラスが未定義）

- [ ] **Step 4: LoadOrderBackupService を実装**

```csharp
// src/ReloadedHelper.Core/LoadOrderBackupService.cs
namespace ReloadedHelper.Core;

public static class LoadOrderBackupService
{
    private static string BackupDir(string appId) => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ReloadedHelper", "backups", appId);

    public static void Backup(string configPath, string appId)
    {
        var dir = BackupDir(appId);
        Directory.CreateDirectory(dir);
        var stamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss-fff");
        File.Copy(configPath, Path.Combine(dir, $"{stamp}.json"), overwrite: true);
        Prune(dir);
    }

    private static void Prune(string dir)
    {
        var old = Directory.GetFiles(dir, "*.json")
            .OrderByDescending(f => f)
            .Skip(3);
        foreach (var f in old) File.Delete(f);
    }

    public static IReadOnlyList<string> ListBackups(string appId) =>
        Directory.Exists(BackupDir(appId))
            ? Directory.GetFiles(BackupDir(appId), "*.json")
                       .OrderByDescending(f => f)
                       .ToArray()
            : Array.Empty<string>();

    public static void Restore(string backupPath, string configPath) =>
        File.Copy(backupPath, configPath, overwrite: true);
}
```

- [ ] **Step 5: AppConfigWriter を実装**

```csharp
// src/ReloadedHelper.Core/AppConfigWriter.cs
using System.Text.Json;

namespace ReloadedHelper.Core;

public static class AppConfigWriter
{
    public static void WriteOrder(
        string configPath, string appId, IReadOnlyList<string> newEnabledMods)
    {
        LoadOrderBackupService.Backup(configPath, appId);

        var original = File.ReadAllBytes(configPath);
        using var doc = JsonDocument.Parse(original);

        using var ms = new System.IO.MemoryStream();
        using var writer = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true });

        writer.WriteStartObject();
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            if (prop.Name == "EnabledMods")
            {
                writer.WritePropertyName("EnabledMods");
                writer.WriteStartArray();
                foreach (var mod in newEnabledMods) writer.WriteStringValue(mod);
                writer.WriteEndArray();
            }
            else
            {
                prop.WriteTo(writer);
            }
        }
        writer.WriteEndObject();
        writer.Flush();

        File.WriteAllBytes(configPath, ms.ToArray());
    }
}
```

- [ ] **Step 6: テストを実行して全パスを確認**

```powershell
dotnet test tests/ReloadedHelper.Core.Tests/ --filter "AppConfigWriterTests|LoadOrderBackupServiceTests" -v normal
```

期待: 全テスト PASS

- [ ] **Step 7: コミット**

```powershell
git add src/ReloadedHelper.Core/LoadOrderBackupService.cs `
        src/ReloadedHelper.Core/AppConfigWriter.cs `
        tests/ReloadedHelper.Core.Tests/LoadOrderBackupServiceTests.cs `
        tests/ReloadedHelper.Core.Tests/AppConfigWriterTests.cs
git commit -m "feat(core): AppConfigWriter and LoadOrderBackupService (backup + write load order)"
```

---

## Task 5: 自動並び替えの統合（App 起動フロー）

**Files:**
- Modify: `src/ReloadedHelper.App/App.xaml.cs`

**Interfaces:**
- Consumes:
  - `LoadOrderSorter.Sort(IReadOnlyList<string>, IReadOnlyDictionary<string, IReadOnlyList<string>>)`
  - `AppConfigWriter.WriteOrder(string configPath, string appId, IReadOnlyList<string>)`
  - `GameInfo.FolderPath` — AppConfig.json のディレクトリパス
  - `GameInfo.EnabledMods` — 現在の有効 MOD リスト（並び替え前）
  - `ModInfo.Dependencies` — そのMODの依存先リスト
  - `MainViewModel.Games` — 読み込み済みゲーム一覧
  - `MainViewModel.AllMods` — 全MOD情報（後述: 現在 `_catalog` として private のため public プロパティを追加）

- [ ] **Step 1: MainViewModel に AllMods プロパティを追加**

`src/ReloadedHelper.Core/MainViewModel.cs` を開き、`_catalog` フィールドの型（`ModCatalog` または `IReadOnlyDictionary<string, ModInfo>`）を確認する。以下のような public プロパティを追加する（実際の型に合わせて調整）:

```csharp
// MainViewModel.cs に追加（_catalog フィールドが ModCatalog 型の場合）:
public IReadOnlyDictionary<string, ModInfo> AllMods => _catalog.All;
```

`ModCatalog` に `All` プロパティがない場合は `_catalog` の実際の API を確認して追記する。

- [ ] **Step 2: App.xaml.cs に ApplySortAllGames メソッドを追加**

```csharp
// App.xaml.cs のクラス内に追加:
private static void ApplySortAllGames(MainViewModel mainVm, ReloadedInstall install)
{
    // Build dependency map: modId → list of modIds it depends on
    var depMap = mainVm.AllMods.ToDictionary(
        kv => kv.Key,
        kv => (IReadOnlyList<string>)kv.Value.Dependencies,
        StringComparer.OrdinalIgnoreCase);

    bool anyChanged = false;
    foreach (var game in mainVm.Games)
    {
        if (game.EnabledMods.Count == 0) continue;

        var sorted = LoadOrderSorter.Sort(game.EnabledMods, depMap);

        // Check if order actually changed
        bool changed = !sorted.SequenceEqual(
            game.EnabledMods, StringComparer.OrdinalIgnoreCase);
        if (!changed) continue;

        var configPath = Path.Combine(game.FolderPath, "AppConfig.json");
        if (!File.Exists(configPath)) continue;

        AppConfigWriter.WriteOrder(configPath, game.AppId, sorted);
        anyChanged = true;
    }

    if (anyChanged)
    {
        // Reload from disk so UI shows the sorted order
        mainVm.LoadFrom(install);
    }
}
```

- [ ] **Step 3: OnStartup で LoadFrom の直後に ApplySortAllGames を呼ぶ**

`OnStartup` の中の `mainVm.LoadFrom(install);` の直後に追加:

```csharp
mainVm.LoadFrom(install);
ApplySortAllGames(mainVm, install);   // ← 追加
```

- [ ] **Step 4: ビルドして確認**

```powershell
dotnet build reloaded-helper.slnx --configuration Release --no-restore
```

期待: ビルドが通る。

- [ ] **Step 5: 動作確認**

アプリを起動して以下を確認する:
- Reloaded-II の `Apps\p5r.exe\AppConfig.json` が書き換えられている（バックアップが `%APPDATA%\ReloadedHelper\backups\p5r.exe\` に存在する）
- 依存関係のある MOD が正しく「依存先が上」になっている（Reloaded-II 本体と比較して確認）

- [ ] **Step 6: コミット**

```powershell
git add src/ReloadedHelper.Core/MainViewModel.cs src/ReloadedHelper.App/App.xaml.cs
git commit -m "feat: auto-sort load order on startup using dependency topology"
```

---

## Task 6: 自動アップデート

**Files:**
- Create: `src/ReloadedHelper.Core/UpdateChecker.cs`
- Modify: `src/ReloadedHelper.App/App.xaml.cs`

**Interfaces:**
- Produces:
  ```csharp
  public sealed class UpdateChecker(HttpClient http)
  {
      public async Task<(string? Version, string? DownloadUrl)> CheckAsync();
  }
  ```

- [ ] **Step 1: UpdateChecker を実装**

```csharp
// src/ReloadedHelper.Core/UpdateChecker.cs
using System.Net.Http;
using System.Text.Json;

namespace ReloadedHelper.Core;

public sealed class UpdateChecker(HttpClient http)
{
    private const string ApiUrl =
        "https://api.github.com/repos/Rei3100/rh-tools/releases/latest";

    public async Task<(string? Version, string? DownloadUrl)> CheckAsync()
    {
        try
        {
            http.DefaultRequestHeaders.TryAddWithoutValidation(
                "User-Agent", "ReloadedHelper/1.0");
            var json = await http.GetStringAsync(ApiUrl);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var tag = root.GetProperty("tag_name").GetString()?.TrimStart('v');
            string? url = null;

            if (root.TryGetProperty("assets", out var assets))
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    var name = asset.GetProperty("name").GetString() ?? "";
                    if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        url = asset.GetProperty("browser_download_url").GetString();
                        break;
                    }
                }
            }

            return (tag, url);
        }
        catch
        {
            return (null, null);
        }
    }
}
```

- [ ] **Step 2: App.xaml.cs に CheckAndApplyUpdateAsync を追加**

```csharp
// App.xaml.cs に using 追加:
using System.Net.Http;
using System.Reflection;

// クラス内に追加:
private static async Task CheckAndApplyUpdateAsync()
{
    try
    {
        using var http = new HttpClient();
        var checker = new UpdateChecker(http);
        var (latestVer, downloadUrl) = await checker.CheckAsync();
        if (latestVer is null || downloadUrl is null) return;

        var currentVer = Assembly.GetExecutingAssembly()
                                 .GetName().Version?.ToString(3) ?? "0.0.0";
        if (latestVer == currentVer) return;

        // Download new exe
        var tempPath = Path.Combine(Path.GetTempPath(), "rh-tools-update.exe");
        var data = await http.GetByteArrayAsync(downloadUrl);
        await File.WriteAllBytesAsync(tempPath, data);

        // Apply update: PowerShell replaces exe after this process exits
        var currentExe = Environment.ProcessPath
                         ?? Path.Combine(
                             AppContext.BaseDirectory, "ReloadedHelper.App.exe");
        var script =
            $"Start-Sleep -Milliseconds 1500; " +
            $"Copy-Item -Path '{tempPath}' -Destination '{currentExe}' -Force; " +
            $"Start-Process -FilePath '{currentExe}'";

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "powershell",
            Arguments = $"-WindowStyle Hidden -Command \"{script}\"",
            UseShellExecute = true,
            WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
        });

        Current.Dispatcher.Invoke(() =>
        {
            MessageBox.Show(
                $"v{latestVer} に更新します。アプリを再起動します。",
                "rh-tools 更新",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Current.Shutdown();
        });
    }
    catch { /* 更新失敗は無視して起動を続ける */ }
}
```

- [ ] **Step 3: OnStartup でバックグラウンド更新チェックを起動**

`OnStartup` の末尾（`window.Show()` の後）に追加:

```csharp
window.Show();
_ = Task.Run(CheckAndApplyUpdateAsync); // ← バックグラウンドで更新確認
```

- [ ] **Step 4: ビルド確認**

```powershell
dotnet build reloaded-helper.slnx --configuration Release --no-restore
```

期待: ビルドが通る。

- [ ] **Step 5: コミット**

```powershell
git add src/ReloadedHelper.Core/UpdateChecker.cs src/ReloadedHelper.App/App.xaml.cs
git commit -m "feat: auto-update from GitHub Releases on startup"
```

---

## Task 7: GitHub Actions — tag トリガー・リリースビルド

**Files:**
- Create: `.github/workflows/release.yml`
- Modify: `src/ReloadedHelper.App/ReloadedHelper.App.csproj` (Version プロパティを追加)

**Interfaces:**
- 変更なし（CI/CD の追加）

- [ ] **Step 1: csproj に Version プロパティのプレースホルダーを追加**

`src/ReloadedHelper.App/ReloadedHelper.App.csproj` の `<PropertyGroup>` に追加:

```xml
<Version>0.1.0</Version>
<AssemblyVersion>0.1.0.0</AssemblyVersion>
<FileVersion>0.1.0.0</FileVersion>
```

これは CI で `-p:Version=X.Y.Z` で上書きされる。

- [ ] **Step 2: release.yml を作成**

```yaml
# .github/workflows/release.yml
name: release

on:
  push:
    tags:
      - 'v[0-9]*'

permissions:
  contents: write

jobs:
  release:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v4

      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'

      - name: Extract version from tag
        shell: pwsh
        run: |
          $ver = $env:GITHUB_REF_NAME -replace '^v', ''
          echo "VERSION=$ver" >> $env:GITHUB_ENV

      - run: dotnet restore

      - run: dotnet build --configuration Release --no-restore

      - run: dotnet test --configuration Release --no-build

      - name: Publish single-file exe
        shell: pwsh
        run: |
          dotnet publish src/ReloadedHelper.App/ReloadedHelper.App.csproj `
            -c Release -r win-x64 --self-contained true `
            -p:PublishSingleFile=true `
            -p:IncludeNativeLibrariesForSelfExtract=true `
            -p:Version=$env:VERSION `
            -o publish

      - name: Create GitHub Release
        uses: softprops/action-gh-release@v2
        with:
          files: publish/ReloadedHelper.App.exe
          generate_release_notes: true
```

- [ ] **Step 3: コミットして初回タグを作成**

```powershell
git add .github/workflows/release.yml `
        src/ReloadedHelper.App/ReloadedHelper.App.csproj
git commit -m "ci: add tag-triggered release workflow with auto-versioning"

# 初回リリースタグ
git tag v0.3.0
git push origin main --tags
```

- [ ] **Step 4: GitHub Actions の結果を確認**

GitHub → Actions → release workflow が green になり、Releases に `v0.3.0` と `ReloadedHelper.App.exe` が添付されていることを確認。

- [ ] **Step 5: ローカルに exe をダウンロードして配置**

GitHub の Releases からダウンロードした `ReloadedHelper.App.exe` を `C:\FreeSoft\ReloadedHelper\` に配置。以降は自動アップデートで更新される。

---

## 全体確認チェックリスト

- [ ] 起動時にウィンドウが最大化されている
- [ ] タスクトレイから復帰時も最大化される
- [ ] 2重起動で既存ウィンドウが最前面に出る（2回目の exe は終了する）
- [ ] 設定アイコン → オーバーレイ表示、Escape / ✕ で閉じる
- [ ] 拡大率スライダーを動かしながら MOD リストが即座に拡縮する
- [ ] フォルダパスが `\` で正しく表示される
- [ ] `%APPDATA%\ReloadedHelper\backups\` にバックアップが作成されている
- [ ] 依存関係のある MOD が「依存先が上」になっている（Reloaded-II 本体と比較）
- [ ] GitHub に `v0.x.x` タグを push すると Release が作成される
- [ ] `C:\FreeSoft\ReloadedHelper\ReloadedHelper.App.exe` を起動すると自動更新が動く（新バージョンがある場合）
