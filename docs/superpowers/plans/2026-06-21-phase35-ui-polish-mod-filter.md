# Phase 3.5 UI Polish & MOD Filter Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 9件のバグ修正・UX改善・機能追加で rh-tools をデイリーユース可能な品質に仕上げる。

**Architecture:** Core ロジック変更（FilterMode, ToggleEnabled, WriteEnabledAndSorted）を先に TDD で実装し、続いて App 層の UI 変更（XAML + code-behind）を行う。UI 変更はビルド＆目視確認。

**Tech Stack:** C# 13 / .NET 10 / WPF / System.Text.Json / xUnit

## Global Constraints

- ランタイム NuGet パッケージ追加禁止（System.Text.Json は .NET 標準 → OK）
- テスト用 xUnit は可
- ユーザーデータ保存先: `%APPDATA%\ReloadedHelper` のみ
- AppConfig.json 書き換え前に必ずバックアップを作成すること（既存 LoadOrderBackupService を使う）
- ビルド: `dotnet build reloaded-helper.slnx --configuration Release`
- テスト: `dotnet test`
- GitHub: `Rei3100/rh-tools`

## Files

### 変更（既存ファイル）
- `src/ReloadedHelper.Core/LoadOrder.cs` — FilterMode enum 追加、ModFilter.Filter() シグネチャ更新
- `src/ReloadedHelper.Core/AppConfigWriter.cs` — WriteEnabledAndSorted() 新メソッド追加
- `src/ReloadedHelper.Core/MainViewModel.cs` — `_install`、`FilterMode`、`EntryCountLabel`、`ToggleEnabled` 追加
- `src/ReloadedHelper.App/App.xaml.cs` — ApplySortAllGames() をグループ化対応に変更
- `src/ReloadedHelper.App/Views/SettingsView.xaml` — HorizontalAlignment="Stretch" に 1 行変更
- `src/ReloadedHelper.App/MainWindow.xaml` — TaskbarIcon XAML 化、overlay 移動、backdrop click、ToolTip 削除
- `src/ReloadedHelper.App/MainWindow.xaml.cs` — BuildTrayIcon() 削除、XAML 要素参照、ハンドラ追加
- `src/ReloadedHelper.App/Views/ModListView.xaml` — フィルタボタン、件数ラベル更新、ホイール、トグル Click
- `tests/ReloadedHelper.Core.Tests/FilterModeTests.cs` — FilterMode テスト（新規）
- `tests/ReloadedHelper.Core.Tests/AppConfigWriterTests.cs` — WriteEnabledAndSorted テスト（既存ファイルに追記）

### 新規作成
- `src/ReloadedHelper.App/Views/ModListView.xaml.cs` — code-behind（GameTabs_MouseWheel, ModToggle_Click, フィルタハンドラ）

---

## Task 1: FilterMode enum + ModFilter 更新（TDD）

**Files:**
- Modify: `src/ReloadedHelper.Core/LoadOrder.cs`
- Create: `tests/ReloadedHelper.Core.Tests/FilterModeTests.cs`

**Interfaces:**
- Produces:
  ```csharp
  public enum FilterMode { All, EnabledOnly, DisabledOnly }

  public static class ModFilter
  {
      public static IReadOnlyList<ModLoadEntry> Filter(
          IReadOnlyList<ModLoadEntry> entries, string? search,
          FilterMode mode = FilterMode.All)
  }
  ```

- [ ] **Step 1: テストファイルを作成**

```csharp
// tests/ReloadedHelper.Core.Tests/FilterModeTests.cs
using ReloadedHelper.Core;

namespace ReloadedHelper.Core.Tests;

public class FilterModeTests
{
    private static readonly ModLoadEntry[] SampleEntries =
    [
        new ModLoadEntry(1, "ModA", null, Enabled: true),
        new ModLoadEntry(2, "ModB", null, Enabled: false),
        new ModLoadEntry(3, "ModC", null, Enabled: true),
    ];

    [Theory]
    [InlineData(FilterMode.All, 3)]
    [InlineData(FilterMode.EnabledOnly, 2)]
    [InlineData(FilterMode.DisabledOnly, 1)]
    public void Filter_ByMode_ReturnsCorrectCount(FilterMode mode, int expected)
    {
        var result = ModFilter.Filter(SampleEntries, null, mode);
        Assert.Equal(expected, result.Count);
    }

    [Fact]
    public void Filter_EnabledOnly_ReturnsOnlyEnabledEntries()
    {
        var result = ModFilter.Filter(SampleEntries, null, FilterMode.EnabledOnly);
        Assert.All(result, e => Assert.True(e.Enabled));
    }

    [Fact]
    public void Filter_DisabledOnly_ReturnsOnlyDisabledEntries()
    {
        var result = ModFilter.Filter(SampleEntries, null, FilterMode.DisabledOnly);
        Assert.All(result, e => Assert.False(e.Enabled));
    }

    [Fact]
    public void Filter_ModeAndSearch_BothApplied()
    {
        // Enabled かつ名前が "A" を含む → ModA のみ
        var result = ModFilter.Filter(SampleEntries, "A", FilterMode.EnabledOnly);
        Assert.Single(result);
        Assert.Equal("ModA", result[0].ModId);
    }

    [Fact]
    public void Filter_DefaultMode_IsAll()
    {
        // デフォルト引数 = All
        var result = ModFilter.Filter(SampleEntries, null);
        Assert.Equal(3, result.Count);
    }
}
```

- [ ] **Step 2: テストを実行して失敗を確認**

```powershell
dotnet test tests/ReloadedHelper.Core.Tests/ --filter "FilterModeTests" -v minimal
```

期待: `error CS0246: The type or namespace name 'FilterMode' does not exist`

- [ ] **Step 3: LoadOrder.cs を更新**

`src/ReloadedHelper.Core/LoadOrder.cs` の現在の内容:
```csharp
namespace ReloadedHelper.Core;

public static class LoadOrderBuilder
{
    public static IReadOnlyList<ModLoadEntry> Build(GameInfo game, IReadOnlyDictionary<string, ModInfo> catalog)
    {
        var enabled = new HashSet<string>(game.EnabledMods, StringComparer.Ordinal);
        var list = new List<ModLoadEntry>(game.SortedMods.Count);
        for (int i = 0; i < game.SortedMods.Count; i++)
        {
            var id = game.SortedMods[i];
            catalog.TryGetValue(id, out var info);
            list.Add(new ModLoadEntry(i + 1, id, info, enabled.Contains(id)));
        }
        return list;
    }
}

public static class ModFilter
{
    public static IReadOnlyList<ModLoadEntry> Filter(IReadOnlyList<ModLoadEntry> entries, string? search)
    {
        if (string.IsNullOrWhiteSpace(search)) return entries;
        var s = search.Trim();
        return entries.Where(e =>
            e.ModId.Contains(s, StringComparison.OrdinalIgnoreCase) ||
            e.DisplayName.Contains(s, StringComparison.OrdinalIgnoreCase)).ToList();
    }
}
```

これを以下に置き換える:

```csharp
namespace ReloadedHelper.Core;

public enum FilterMode { All, EnabledOnly, DisabledOnly }

public static class LoadOrderBuilder
{
    public static IReadOnlyList<ModLoadEntry> Build(GameInfo game, IReadOnlyDictionary<string, ModInfo> catalog)
    {
        var enabled = new HashSet<string>(game.EnabledMods, StringComparer.Ordinal);
        var list = new List<ModLoadEntry>(game.SortedMods.Count);
        for (int i = 0; i < game.SortedMods.Count; i++)
        {
            var id = game.SortedMods[i];
            catalog.TryGetValue(id, out var info);
            list.Add(new ModLoadEntry(i + 1, id, info, enabled.Contains(id)));
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

- [ ] **Step 4: テストを実行して全パスを確認**

```powershell
dotnet test tests/ReloadedHelper.Core.Tests/ --filter "FilterModeTests" -v normal
```

期待: 全5テスト PASS

- [ ] **Step 5: コミット**

```powershell
git add src/ReloadedHelper.Core/LoadOrder.cs `
        tests/ReloadedHelper.Core.Tests/FilterModeTests.cs
git commit -m "feat(core): FilterMode enum and ModFilter enabled/disabled filter"
```

---

## Task 2: AppConfigWriter.WriteEnabledAndSorted（TDD）

**Files:**
- Modify: `src/ReloadedHelper.Core/AppConfigWriter.cs`
- Modify: `tests/ReloadedHelper.Core.Tests/AppConfigWriterTests.cs`

**Interfaces:**
- Produces:
  ```csharp
  public static class AppConfigWriter
  {
      // 既存
      public static void WriteOrder(string configPath, string appId,
          IReadOnlyList<string> newSortedMods)

      // 新規
      public static void WriteEnabledAndSorted(string configPath, string appId,
          IReadOnlyList<string> newEnabledMods,
          IReadOnlyList<string> newSortedMods)
  }
  ```

- [ ] **Step 1: 既存の AppConfigWriterTests.cs にテストを追記**

`tests/ReloadedHelper.Core.Tests/AppConfigWriterTests.cs` の末尾の `}` の前（クラス閉じ括弧の前）に追加:

```csharp
    [Fact]
    public void WriteEnabledAndSorted_UpdatesBothFields_PreservesOthers()
    {
        File.WriteAllText(_tmp, """
            {
              "AppId": "test.exe",
              "AppName": "TestGame",
              "EnabledMods": ["A", "B"],
              "SortedMods": ["A", "B", "C"]
            }
            """);

        AppConfigWriter.WriteEnabledAndSorted(_tmp, "test.exe",
            newEnabledMods: new[] { "B" },
            newSortedMods:  new[] { "B", "C", "A" });

        var result = AppConfigParser.Parse(File.ReadAllText(_tmp), Path.GetTempPath());
        Assert.Equal(new[] { "B" },          result.EnabledMods);
        Assert.Equal(new[] { "B", "C", "A" }, result.SortedMods);
        Assert.Equal("TestGame",              result.AppName); // other fields preserved
    }
```

- [ ] **Step 2: テストを実行して失敗を確認**

```powershell
dotnet test tests/ReloadedHelper.Core.Tests/ --filter "WriteEnabledAndSorted" -v minimal
```

期待: FAIL（メソッド未定義）

- [ ] **Step 3: AppConfigWriter.cs に WriteEnabledAndSorted を追加**

`src/ReloadedHelper.Core/AppConfigWriter.cs` の最後の `}` の前（クラス閉じ括弧の前）に追加:

```csharp
    public static void WriteEnabledAndSorted(
        string configPath, string appId,
        IReadOnlyList<string> newEnabledMods,
        IReadOnlyList<string> newSortedMods)
    {
        LoadOrderBackupService.Backup(configPath, appId);

        var original = File.ReadAllBytes(configPath);
        using var doc = JsonDocument.Parse(original);

        using var ms = new System.IO.MemoryStream();
        using var writer = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true });

        writer.WriteStartObject();
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            switch (prop.Name)
            {
                case "EnabledMods":
                    writer.WritePropertyName("EnabledMods");
                    writer.WriteStartArray();
                    foreach (var m in newEnabledMods) writer.WriteStringValue(m);
                    writer.WriteEndArray();
                    break;
                case "SortedMods":
                    writer.WritePropertyName("SortedMods");
                    writer.WriteStartArray();
                    foreach (var m in newSortedMods) writer.WriteStringValue(m);
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

- [ ] **Step 4: テストを実行して全パスを確認**

```powershell
dotnet test tests/ReloadedHelper.Core.Tests/ -v minimal
```

期待: 全テスト PASS（既存テストも含む）

- [ ] **Step 5: コミット**

```powershell
git add src/ReloadedHelper.Core/AppConfigWriter.cs `
        tests/ReloadedHelper.Core.Tests/AppConfigWriterTests.cs
git commit -m "feat(core): AppConfigWriter.WriteEnabledAndSorted writes both EnabledMods and SortedMods"
```

---

## Task 3: MainViewModel 更新（_install + FilterMode + ToggleEnabled）

**Files:**
- Modify: `src/ReloadedHelper.Core/MainViewModel.cs`

**Interfaces:**
- Consumes:
  - `FilterMode` (from Task 1)
  - `ModFilter.Filter(entries, search, mode)` (from Task 1)
  - `AppConfigWriter.WriteEnabledAndSorted(configPath, appId, enabled, sorted)` (from Task 2)
  - `LoadOrderSorter.Sort(order, depMap)` (既存)
  - `LoadOrderBackupService` (既存、AppConfigWriter 内部で呼ばれる)
- Produces:
  ```csharp
  // MainViewModel の新しいシグネチャ
  public FilterMode FilterMode { get; set; }   // setter で ApplyFilter() を呼ぶ
  public string EntryCountLabel { get; }        // 件数ラベル（読み取り専用プロパティ）
  public void ToggleEnabled(ModLoadEntry entry) // 有効/無効を切り替えてリロード
  ```

- [ ] **Step 1: MainViewModel.cs 全体を置き換える**

`src/ReloadedHelper.Core/MainViewModel.cs` を以下の内容に置き換える:

```csharp
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ReloadedHelper.Core;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private IReadOnlyDictionary<string, ModInfo> _catalog = new Dictionary<string, ModInfo>();
    private IReadOnlyList<ModLoadEntry> _allEntries = Array.Empty<ModLoadEntry>();
    private ReloadedInstall? _install;

    public IReadOnlyDictionary<string, ModInfo> AllMods => _catalog;

    public ObservableCollection<GameInfo> Games  { get; } = new();
    public ObservableCollection<ModLoadEntry> Entries { get; } = new();

    private GameInfo? _selectedGame;
    public GameInfo? SelectedGame
    {
        get => _selectedGame;
        set
        {
            if (!ReferenceEquals(_selectedGame, value))
            {
                _selectedGame = value;
                OnChanged();
                RebuildEntries();
            }
        }
    }

    private ModLoadEntry? _selectedEntry;
    public ModLoadEntry? SelectedEntry
    {
        get => _selectedEntry;
        set
        {
            if (!ReferenceEquals(_selectedEntry, value))
            {
                _selectedEntry = value;
                OnChanged();
            }
        }
    }

    private string _searchText = "";
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (_searchText != value)
            {
                _searchText = value ?? "";
                OnChanged();
                ApplyFilter();
            }
        }
    }

    private FilterMode _filterMode = FilterMode.All;
    public FilterMode FilterMode
    {
        get => _filterMode;
        set
        {
            if (_filterMode == value) return;
            _filterMode = value;
            OnChanged();
            ApplyFilter();
        }
    }

    public string EntryCountLabel
    {
        get
        {
            var total = _allEntries.Count;
            var shown = Entries.Count;
            return _filterMode switch
            {
                FilterMode.EnabledOnly  => $"読み込み順 ・ 有効 {shown} / 全 {total} 件",
                FilterMode.DisabledOnly => $"読み込み順 ・ 無効 {shown} / 全 {total} 件",
                _                       => $"読み込み順 ・ 全 {total} 件"
            };
        }
    }

    public void LoadFrom(ReloadedInstall install)
    {
        _install = install;
        _catalog = ModCatalog.LoadAll(install.ModsDir);
        Games.Clear();
        foreach (var g in GameCatalog.LoadAll(install.AppsDir)) Games.Add(g);
        SelectedGame = Games.Count > 0 ? Games[0] : null;
        if (SelectedGame is null) RebuildEntries();
    }

    public void ToggleEnabled(ModLoadEntry entry)
    {
        if (SelectedGame is null || _install is null) return;

        var game = SelectedGame;
        var enabledSet = game.EnabledMods.ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (enabledSet.Contains(entry.ModId)) enabledSet.Remove(entry.ModId);
        else enabledSet.Add(entry.ModId);

        var depMap = AllMods.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlyList<string>)kv.Value.Dependencies,
            StringComparer.OrdinalIgnoreCase);

        var enabledGroup  = game.SortedMods.Where(enabledSet.Contains).ToList();
        var disabledGroup = game.SortedMods.Where(id => !enabledSet.Contains(id)).ToList();
        var sortedEnabled  = LoadOrderSorter.Sort(enabledGroup, depMap);
        var sortedDisabled = LoadOrderSorter.Sort(disabledGroup, depMap);
        var newSorted  = sortedEnabled.Concat(sortedDisabled).ToList();
        var newEnabled = sortedEnabled.ToList();

        var configPath = Path.Combine(game.FolderPath, "AppConfig.json");
        if (!File.Exists(configPath)) return;

        AppConfigWriter.WriteEnabledAndSorted(configPath, game.AppId, newEnabled, newSorted);

        var prevId = game.AppId;
        LoadFrom(_install);
        var restored = Games.FirstOrDefault(g => g.AppId == prevId);
        if (restored is not null) SelectedGame = restored;
    }

    private void RebuildEntries()
    {
        _allEntries = SelectedGame is null
            ? Array.Empty<ModLoadEntry>()
            : LoadOrderBuilder.Build(SelectedGame, _catalog);
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        Entries.Clear();
        foreach (var e in ModFilter.Filter(_allEntries, SearchText, FilterMode)) Entries.Add(e);
        SelectedEntry = Entries.Count > 0 ? Entries[0] : null;
        OnChanged(nameof(EntryCountLabel));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
```

- [ ] **Step 2: ビルドして確認**

```powershell
dotnet build reloaded-helper.slnx --configuration Release --no-restore
```

期待: ビルド成功（0 errors）

- [ ] **Step 3: テストを実行**

```powershell
dotnet test --configuration Release --no-build
```

期待: 全テスト PASS

- [ ] **Step 4: コミット**

```powershell
git add src/ReloadedHelper.Core/MainViewModel.cs
git commit -m "feat(core): MainViewModel adds FilterMode, EntryCountLabel, ToggleEnabled"
```

---

## Task 4: App.xaml.cs — ApplySortAllGames グループ化

**Files:**
- Modify: `src/ReloadedHelper.App/App.xaml.cs`

**Interfaces:**
- Consumes: `AppConfigWriter.WriteOrder()` (既存), `LoadOrderSorter.Sort()` (既存)
- Produces: 起動時ソートが「有効グループ→無効グループ」の順になる

- [ ] **Step 1: App.xaml.cs の ApplySortAllGames メソッドを書き換える**

`App.xaml.cs` 内の `ApplySortAllGames` メソッド全体を以下に置き換える:

```csharp
private static void ApplySortAllGames(MainViewModel mainVm, ReloadedInstall install)
{
    var depMap = mainVm.AllMods.ToDictionary(
        kv => kv.Key,
        kv => (IReadOnlyList<string>)kv.Value.Dependencies,
        StringComparer.OrdinalIgnoreCase);

    bool anyChanged = false;
    foreach (var game in mainVm.Games)
    {
        if (game.SortedMods.Count == 0) continue;

        var enabledSet    = new HashSet<string>(game.EnabledMods, StringComparer.OrdinalIgnoreCase);
        var enabledGroup  = game.SortedMods.Where(enabledSet.Contains).ToList();
        var disabledGroup = game.SortedMods.Where(id => !enabledSet.Contains(id)).ToList();

        var sortedEnabled  = LoadOrderSorter.Sort(enabledGroup,  depMap);
        var sortedDisabled = LoadOrderSorter.Sort(disabledGroup, depMap);
        var newSorted = sortedEnabled.Concat(sortedDisabled).ToList();

        bool changed = !newSorted.SequenceEqual(
            game.SortedMods, StringComparer.OrdinalIgnoreCase);
        if (!changed) continue;

        var configPath = Path.Combine(game.FolderPath, "AppConfig.json");
        if (!File.Exists(configPath)) continue;

        AppConfigWriter.WriteOrder(configPath, game.AppId, newSorted);
        anyChanged = true;
    }

    if (anyChanged) mainVm.LoadFrom(install);
}
```

- [ ] **Step 2: ビルドして確認**

```powershell
dotnet build reloaded-helper.slnx --configuration Release --no-restore
```

期待: ビルド成功

- [ ] **Step 3: コミット**

```powershell
git add src/ReloadedHelper.App/App.xaml.cs
git commit -m "feat: auto-sort groups enabled mods first, disabled mods below"
```

---

## Task 5: SettingsView.xaml — % 表示とフォルダパスの修正

**Files:**
- Modify: `src/ReloadedHelper.App/Views/SettingsView.xaml`

**Interfaces:**
- なし（表示修正のみ）

**根拠:** StackPanel が `HorizontalAlignment="Left"` の場合、WPF は子要素の測定時に無限幅を渡す。その結果、内側 Grid の `Width="*"` 列が 0px に解決され、スライダーとフォルダパス TextBox が消える。`HorizontalAlignment="Stretch"` にすると親の利用可能幅（オーバーレイパネルの幅）が渡され `*` 列が正しく解決される。

- [ ] **Step 1: SettingsView.xaml を 1 行変更**

`src/ReloadedHelper.App/Views/SettingsView.xaml` の StackPanel 行:

変更前:
```xml
        <StackPanel Margin="32,24" MaxWidth="520" HorizontalAlignment="Left">
```

変更後:
```xml
        <StackPanel Margin="32,24" MaxWidth="520" HorizontalAlignment="Stretch">
```

- [ ] **Step 2: ビルドして確認**

```powershell
dotnet build reloaded-helper.slnx --configuration Release --no-restore
```

期待: ビルド成功

- [ ] **Step 3: コミット**

```powershell
git add src/ReloadedHelper.App/Views/SettingsView.xaml
git commit -m "fix: settings zoom slider and folder path now visible (HorizontalAlignment Stretch)"
```

---

## Task 6: MainWindow.xaml + .cs — トレイ・オーバーレイ・バックドロップ・ToolTip

**Files:**
- Modify: `src/ReloadedHelper.App/MainWindow.xaml`
- Modify: `src/ReloadedHelper.App/MainWindow.xaml.cs`

**Interfaces:**
- なし（UI 構造変更のみ）

### Step 6-1: MainWindow.xaml の全面改訂

- [ ] **Step 1: MainWindow.xaml を以下の内容で完全置き換える**

`src/ReloadedHelper.App/MainWindow.xaml` を以下に置き換える:

```xml
<Window x:Class="ReloadedHelper.App.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:core="clr-namespace:ReloadedHelper.Core;assembly=ReloadedHelper.Core"
        xmlns:app="clr-namespace:ReloadedHelper.App"
        xmlns:views="clr-namespace:ReloadedHelper.App.Views"
        xmlns:tb="clr-namespace:H.NotifyIcon;assembly=H.NotifyIcon.Wpf"
        Title="Reloaded Helper" Height="700" Width="1100" MinWidth="800" MinHeight="500"
        Background="{DynamicResource BgMainBrush}"
        FontFamily="{DynamicResource MainFont}"
        StateChanged="Window_StateChanged"
        Closed="Window_Closed">

    <Window.Resources>
        <BooleanToVisibilityConverter x:Key="BoolToVisConverter"/>
    </Window.Resources>

    <Grid>
        <Grid.LayoutTransform>
            <ScaleTransform ScaleX="{Binding SettingsVm.Scale}"
                            ScaleY="{Binding SettingsVm.Scale}"/>
        </Grid.LayoutTransform>
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="64"/>
            <ColumnDefinition Width="*"/>
        </Grid.ColumnDefinitions>

        <!-- ── システムトレイアイコン（XAML で宣言してトレイに正しく登録） ── -->
        <tb:TaskbarIcon x:Name="TrayIcon" Grid.ColumnSpan="2"
                        IconSource="/Assets/app.ico"
                        ToolTipText="Reloaded Helper"
                        Visibility="Collapsed"
                        TrayLeftMouseDown="TrayIcon_TrayLeftMouseDown">
            <tb:TaskbarIcon.ContextMenu>
                <ContextMenu>
                    <MenuItem Header="表示" Click="TrayShow_Click"/>
                    <MenuItem Header="終了" Click="TrayExit_Click"/>
                </ContextMenu>
            </tb:TaskbarIcon.ContextMenu>
        </tb:TaskbarIcon>

        <!-- ── サイドナビ ── -->
        <Border Grid.Column="0" Background="{DynamicResource BgBarBrush}">
            <DockPanel LastChildFill="False">

                <!-- ロゴ -->
                <Border DockPanel.Dock="Top" Height="64">
                    <TextBlock Text="rh" HorizontalAlignment="Center" VerticalAlignment="Center"
                               FontSize="18" FontWeight="Bold" Foreground="{DynamicResource AccentBrush}"/>
                </Border>

                <!-- MOD 一覧ボタン（ToolTip なし） -->
                <Button DockPanel.Dock="Top" Click="NavModList_Click">
                    <Button.Style>
                        <Style TargetType="Button" BasedOn="{StaticResource NavButtonStyle}">
                            <Style.Triggers>
                                <DataTrigger Binding="{Binding IsModListActive}" Value="True">
                                    <Setter Property="Background" Value="{DynamicResource BgNavSelectedBrush}"/>
                                </DataTrigger>
                            </Style.Triggers>
                        </Style>
                    </Button.Style>
                    <TextBlock Text="&#x2261;" FontSize="22" Foreground="{DynamicResource TextBodyBrush}"/>
                </Button>

                <!-- 設定ボタン（ToolTip なし） -->
                <Button DockPanel.Dock="Top" Click="NavSettings_Click">
                    <Button.Style>
                        <Style TargetType="Button" BasedOn="{StaticResource NavButtonStyle}">
                            <Style.Triggers>
                                <DataTrigger Binding="{Binding IsSettingsActive}" Value="True">
                                    <Setter Property="Background" Value="{DynamicResource BgNavSelectedBrush}"/>
                                </DataTrigger>
                            </Style.Triggers>
                        </Style>
                    </Button.Style>
                    <TextBlock Text="&#x2699;" FontSize="20" Foreground="{DynamicResource TextBodyBrush}"/>
                </Button>

                <!-- ヘルプボタン（下端、ToolTip なし） -->
                <Button DockPanel.Dock="Bottom" Click="NavHelp_Click">
                    <Button.Style>
                        <Style TargetType="Button" BasedOn="{StaticResource NavButtonStyle}">
                            <Style.Triggers>
                                <DataTrigger Binding="{Binding IsHelpActive}" Value="True">
                                    <Setter Property="Background" Value="{DynamicResource BgNavSelectedBrush}"/>
                                </DataTrigger>
                            </Style.Triggers>
                        </Style>
                    </Button.Style>
                    <TextBlock Text="?" FontSize="18" FontWeight="Bold"
                               Foreground="{DynamicResource TextBodyBrush}"/>
                </Button>

            </DockPanel>
        </Border>

        <!-- ── コンテンツエリア（MOD 一覧のみ、オーバーレイは外に移動） ── -->
        <Grid Grid.Column="1">
            <views:ModListView DataContext="{Binding ModListVm}"/>
        </Grid>

        <!-- ── 設定・ヘルプ オーバーレイ（ColumnSpan="2" でウィンドウ全幅センタリング） ── -->
        <Grid Grid.ColumnSpan="2"
              Visibility="{Binding IsOverlayVisible, Converter={StaticResource BoolToVisConverter}}">

            <!-- 半透明バックドロップ（クリックで閉じる） -->
            <Rectangle Fill="Black" Opacity="0.45"
                       MouseLeftButtonDown="Backdrop_MouseDown"/>

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
</Window>
```

### Step 6-2: MainWindow.xaml.cs の改訂

- [ ] **Step 2: MainWindow.xaml.cs を以下の内容で完全置き換える**

```csharp
// src/ReloadedHelper.App/MainWindow.xaml.cs
using System.Windows;
using ReloadedHelper.Core;

namespace ReloadedHelper.App;

public partial class MainWindow : Window
{
    private readonly ShellViewModel _shell;

    public MainWindow(ShellViewModel shell)
    {
        _shell     = shell;
        DataContext = shell;
        InitializeComponent();
    }

    // ── サイドナビ ──
    private void NavModList_Click(object sender, RoutedEventArgs e)  => _shell.ShowModList();
    private void NavSettings_Click(object sender, RoutedEventArgs e) => _shell.ShowSettings();
    private void NavHelp_Click(object sender, RoutedEventArgs e)     => _shell.ShowHelp();

    // ── オーバーレイを閉じる ──
    private void CloseOverlay_Click(object sender, RoutedEventArgs e)
        => _shell.ShowModList();

    private void Backdrop_MouseDown(object sender,
        System.Windows.Input.MouseButtonEventArgs e)
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

    // ── 最小化 → トレイ ──
    private void Window_StateChanged(object sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized && _shell.SettingsVm.MinimizeToTray)
        {
            ShowInTaskbar       = false;
            TrayIcon.Visibility = Visibility.Visible;
            Hide();
        }
    }

    private void RestoreWindow()
    {
        Show();
        WindowState         = WindowState.Maximized;
        ShowInTaskbar       = true;
        Activate();
        TrayIcon.Visibility = Visibility.Collapsed;
    }

    // ── トレイアイコンイベント ──
    private void TrayIcon_TrayLeftMouseDown(object sender,
        H.NotifyIcon.RoutedEventArgs e) => RestoreWindow();
    private void TrayShow_Click(object sender, RoutedEventArgs e) => RestoreWindow();
    private void TrayExit_Click(object sender, RoutedEventArgs e)
        => Application.Current.Shutdown();

    // ── 終了 ──
    private void Window_Closed(object sender, EventArgs e)
    {
        TrayIcon.Dispose();
    }
}
```

**注意:** `H.NotifyIcon.RoutedEventArgs` の型名は H.NotifyIcon.Wpf 2.4.1 の実際の型名を確認してビルドエラーが出た場合は `object sender, System.Windows.RoutedEventArgs e` に変更する。

- [ ] **Step 3: ビルドして確認**

```powershell
dotnet build reloaded-helper.slnx --configuration Release --no-restore
```

期待: ビルド成功（0 errors）

もし `H.NotifyIcon.RoutedEventArgs` でエラーが出る場合は、`TrayIcon_TrayLeftMouseDown` のシグネチャを以下に変更:
```csharp
private void TrayIcon_TrayLeftMouseDown(object sender, RoutedEventArgs e) => RestoreWindow();
```
または:
```csharp
private void TrayIcon_TrayLeftMouseDown(object sender, EventArgs e) => RestoreWindow();
```
エラーメッセージが示す型名に合わせる。

- [ ] **Step 4: コミット**

```powershell
git add src/ReloadedHelper.App/MainWindow.xaml `
        src/ReloadedHelper.App/MainWindow.xaml.cs
git commit -m "fix: tray icon via XAML, overlay centered on full window, backdrop click closes, remove nav tooltips"
```

---

## Task 7: ModListView.xaml + ModListView.xaml.cs — ホイール・トグル・フィルタボタン

**Files:**
- Modify: `src/ReloadedHelper.App/Views/ModListView.xaml`
- Create: `src/ReloadedHelper.App/Views/ModListView.xaml.cs`

**Interfaces:**
- Consumes:
  - `MainViewModel.FilterMode` (from Task 3)
  - `MainViewModel.EntryCountLabel` (from Task 3)
  - `MainViewModel.ToggleEnabled(ModLoadEntry)` (from Task 3)
  - `FilterMode` enum (from Task 1)

### Step 7-1: ModListView.xaml の更新

- [ ] **Step 1: ModListView.xaml を以下の内容で完全置き換える**

```xml
<!-- src/ReloadedHelper.App/Views/ModListView.xaml -->
<UserControl x:Class="ReloadedHelper.App.Views.ModListView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:app="clr-namespace:ReloadedHelper.App"
             xmlns:core="clr-namespace:ReloadedHelper.Core;assembly=ReloadedHelper.Core"
             Background="{DynamicResource BgMainBrush}">
    <UserControl.Resources>
        <app:PathToImageConverter x:Key="Img"/>
    </UserControl.Resources>

    <!-- DataContext = MainViewModel -->
    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="56"/>
            <RowDefinition Height="*"/>
        </Grid.RowDefinitions>

        <!-- ── 上部バー ── -->
        <Border Grid.Row="0" Background="{DynamicResource BgBarBrush}">
            <Grid>
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="*"/>
                    <ColumnDefinition Width="Auto"/>
                </Grid.ColumnDefinitions>

                <!-- ゲームタブ（マウスホイールで切り替え） -->
                <ListBox Grid.Column="0"
                         ItemsSource="{Binding Games}"
                         SelectedItem="{Binding SelectedGame}"
                         DisplayMemberPath="DisplayName"
                         Background="Transparent" BorderThickness="0"
                         ScrollViewer.VerticalScrollBarVisibility="Disabled"
                         ScrollViewer.HorizontalScrollBarVisibility="Hidden"
                         ItemContainerStyle="{DynamicResource GameTabItemStyle}"
                         PreviewMouseWheel="GameTabs_MouseWheel">
                    <ListBox.ItemsPanel>
                        <ItemsPanelTemplate>
                            <StackPanel Orientation="Horizontal"/>
                        </ItemsPanelTemplate>
                    </ListBox.ItemsPanel>
                </ListBox>

                <!-- 検索ボックス -->
                <Border Grid.Column="1"
                        Background="{DynamicResource BgInputBrush}"
                        BorderBrush="{DynamicResource BorderInputBrush}"
                        BorderThickness="1" CornerRadius="6"
                        Margin="8,10,12,10" Padding="8,0" MinWidth="200">
                    <Grid VerticalAlignment="Center">
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="Auto"/>
                            <ColumnDefinition Width="*"/>
                        </Grid.ColumnDefinitions>
                        <TextBlock Grid.Column="0" Text="&#x1F50D;"
                                   Foreground="{DynamicResource TextLabelBrush}"
                                   VerticalAlignment="Center" Margin="0,0,6,0"/>
                        <Grid Grid.Column="1">
                            <TextBox x:Name="SearchBox"
                                     Text="{Binding SearchText, UpdateSourceTrigger=PropertyChanged}"
                                     Background="Transparent" BorderThickness="0" Padding="0,6"
                                     FontSize="{DynamicResource FontSizeSearch}"
                                     Foreground="{DynamicResource TextBodyBrush}"
                                     CaretBrush="{DynamicResource TextBodyBrush}"
                                     VerticalAlignment="Center">
                                <TextBox.Style>
                                    <Style TargetType="TextBox">
                                        <Setter Property="Background"      Value="Transparent"/>
                                        <Setter Property="BorderThickness" Value="0"/>
                                        <Setter Property="Padding"         Value="0,6"/>
                                        <Setter Property="Template">
                                            <Setter.Value>
                                                <ControlTemplate TargetType="TextBox">
                                                    <ScrollViewer x:Name="PART_ContentHost" Focusable="False"
                                                                  HorizontalScrollBarVisibility="Hidden"
                                                                  VerticalScrollBarVisibility="Hidden"/>
                                                </ControlTemplate>
                                            </Setter.Value>
                                        </Setter>
                                    </Style>
                                </TextBox.Style>
                            </TextBox>
                            <!-- プレースホルダ -->
                            <TextBlock Text="MOD を検索..."
                                       Foreground="{DynamicResource TextMetaBrush}"
                                       FontSize="{DynamicResource FontSizeSearch}"
                                       VerticalAlignment="Center"
                                       IsHitTestVisible="False">
                                <TextBlock.Style>
                                    <Style TargetType="TextBlock">
                                        <Setter Property="Visibility" Value="Collapsed"/>
                                        <Style.Triggers>
                                            <DataTrigger Binding="{Binding Text, ElementName=SearchBox}" Value="">
                                                <Setter Property="Visibility" Value="Visible"/>
                                            </DataTrigger>
                                        </Style.Triggers>
                                    </Style>
                                </TextBlock.Style>
                            </TextBlock>
                        </Grid>
                    </Grid>
                </Border>
            </Grid>
        </Border>

        <!-- ── 本体：MOD リスト + 詳細パネル ── -->
        <Grid Grid.Row="1">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*"/>
                <ColumnDefinition Width="280"/>
            </Grid.ColumnDefinitions>

            <!-- MOD リスト -->
            <DockPanel Grid.Column="0" Margin="12,8,6,8">

                <!-- 件数ヘッダ + フィルタボタン -->
                <Grid DockPanel.Dock="Top" Margin="0,0,0,6">
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="*"/>
                        <ColumnDefinition Width="Auto"/>
                    </Grid.ColumnDefinitions>

                    <!-- 件数ラベル -->
                    <TextBlock Grid.Column="0"
                               Text="{Binding EntryCountLabel, Mode=OneWay}"
                               FontSize="{DynamicResource FontSizeLabel}"
                               Foreground="{DynamicResource TextMetaBrush}"
                               VerticalAlignment="Center"
                               Margin="4,0,0,0"/>

                    <!-- フィルタボタン：全て / 有効 / 無効 -->
                    <StackPanel Grid.Column="1" Orientation="Horizontal">
                        <Button Content="全て" Click="FilterAll_Click"
                                Padding="8,4" Margin="0,0,3,0" Cursor="Hand"
                                Foreground="{DynamicResource TextBodyBrush}"
                                BorderBrush="{DynamicResource BorderInputBrush}" BorderThickness="1"
                                FontSize="{DynamicResource FontSizeLabel}">
                            <Button.Style>
                                <Style TargetType="Button">
                                    <Setter Property="Background" Value="{DynamicResource BgInputBrush}"/>
                                    <Style.Triggers>
                                        <DataTrigger Binding="{Binding FilterMode}"
                                                     Value="{x:Static core:FilterMode.All}">
                                            <Setter Property="Background" Value="{DynamicResource AccentBrush}"/>
                                        </DataTrigger>
                                    </Style.Triggers>
                                </Style>
                            </Button.Style>
                        </Button>
                        <Button Content="有効" Click="FilterEnabled_Click"
                                Padding="8,4" Margin="0,0,3,0" Cursor="Hand"
                                Foreground="{DynamicResource TextBodyBrush}"
                                BorderBrush="{DynamicResource BorderInputBrush}" BorderThickness="1"
                                FontSize="{DynamicResource FontSizeLabel}">
                            <Button.Style>
                                <Style TargetType="Button">
                                    <Setter Property="Background" Value="{DynamicResource BgInputBrush}"/>
                                    <Style.Triggers>
                                        <DataTrigger Binding="{Binding FilterMode}"
                                                     Value="{x:Static core:FilterMode.EnabledOnly}">
                                            <Setter Property="Background" Value="{DynamicResource AccentBrush}"/>
                                        </DataTrigger>
                                    </Style.Triggers>
                                </Style>
                            </Button.Style>
                        </Button>
                        <Button Content="無効" Click="FilterDisabled_Click"
                                Padding="8,4" Cursor="Hand"
                                Foreground="{DynamicResource TextBodyBrush}"
                                BorderBrush="{DynamicResource BorderInputBrush}" BorderThickness="1"
                                FontSize="{DynamicResource FontSizeLabel}">
                            <Button.Style>
                                <Style TargetType="Button">
                                    <Setter Property="Background" Value="{DynamicResource BgInputBrush}"/>
                                    <Style.Triggers>
                                        <DataTrigger Binding="{Binding FilterMode}"
                                                     Value="{x:Static core:FilterMode.DisabledOnly}">
                                            <Setter Property="Background" Value="{DynamicResource AccentBrush}"/>
                                        </DataTrigger>
                                    </Style.Triggers>
                                </Style>
                            </Button.Style>
                        </Button>
                    </StackPanel>
                </Grid>

                <ListBox ItemsSource="{Binding Entries}"
                         SelectedItem="{Binding SelectedEntry}"
                         ItemContainerStyle="{DynamicResource ModCardItemStyle}"
                         VirtualizingPanel.IsVirtualizing="True"
                         VirtualizingPanel.VirtualizationMode="Recycling"
                         ScrollViewer.VerticalScrollBarVisibility="Hidden"
                         ScrollViewer.HorizontalScrollBarVisibility="Disabled"
                         Background="Transparent" BorderThickness="0">
                    <ListBox.ItemTemplate>
                        <DataTemplate>
                            <Grid Height="54">
                                <Grid.ColumnDefinitions>
                                    <ColumnDefinition Width="36"/>
                                    <ColumnDefinition Width="50"/>
                                    <ColumnDefinition Width="*"/>
                                    <ColumnDefinition Width="52"/>
                                </Grid.ColumnDefinitions>

                                <!-- 順番 -->
                                <TextBlock Grid.Column="0"
                                           Text="{Binding Order}"
                                           FontFamily="{DynamicResource MonoFont}"
                                           FontSize="{DynamicResource FontSizeOrder}"
                                           Foreground="{DynamicResource TextMetaBrush}"
                                           HorizontalAlignment="Right"
                                           VerticalAlignment="Center"
                                           Margin="0,0,6,0"/>

                                <!-- サムネ 42×42 角丸8 -->
                                <Border Grid.Column="1" Width="42" Height="42" CornerRadius="8"
                                        Margin="0,0,8,0" VerticalAlignment="Center" ClipToBounds="True">
                                    <Grid>
                                        <Border>
                                            <Border.Background>
                                                <LinearGradientBrush StartPoint="0,0" EndPoint="1,1">
                                                    <GradientStop Color="#2a2c30" Offset="0"/>
                                                    <GradientStop Color="#1a1c1f" Offset="1"/>
                                                </LinearGradientBrush>
                                            </Border.Background>
                                        </Border>
                                        <Image Source="{Binding Info.IconPath, Converter={StaticResource Img}}"
                                               Stretch="UniformToFill"/>
                                    </Grid>
                                </Border>

                                <!-- 名前 + 作者 -->
                                <StackPanel Grid.Column="2" VerticalAlignment="Center">
                                    <TextBlock Text="{Binding DisplayName}"
                                               FontSize="{DynamicResource FontSizeCardTitle}"
                                               FontWeight="SemiBold"
                                               Foreground="{DynamicResource TextBodyBrush}"
                                               TextTrimming="CharacterEllipsis"/>
                                    <TextBlock Text="{Binding Info.ModAuthor}"
                                               FontSize="{DynamicResource FontSizeCardBody}"
                                               Foreground="{DynamicResource TextLabelBrush}"
                                               TextTrimming="CharacterEllipsis"/>
                                </StackPanel>

                                <!-- ON/OFF トグル（クリックで有効/無効を切り替え） -->
                                <ToggleButton Grid.Column="3"
                                             IsChecked="{Binding Enabled, Mode=OneWay}"
                                             Click="ModToggle_Click"
                                             Style="{DynamicResource ToggleSwitchStyle}"
                                             HorizontalAlignment="Center" VerticalAlignment="Center"/>
                            </Grid>
                        </DataTemplate>
                    </ListBox.ItemTemplate>
                </ListBox>
            </DockPanel>

            <!-- 詳細パネル（右 280px） -->
            <Border Grid.Column="1" Background="{DynamicResource BgBarBrush}">
                <ScrollViewer VerticalScrollBarVisibility="Hidden">
                    <StackPanel DataContext="{Binding SelectedEntry}" Margin="12,12,12,12">

                        <!-- ヒーローサムネ 横幅いっぱい × 120 -->
                        <Border CornerRadius="10" Height="120" Margin="0,0,0,12" ClipToBounds="True">
                            <Grid>
                                <Border>
                                    <Border.Background>
                                        <LinearGradientBrush StartPoint="0,0" EndPoint="1,1">
                                            <GradientStop Color="#2a2c30" Offset="0"/>
                                            <GradientStop Color="#141518" Offset="1"/>
                                        </LinearGradientBrush>
                                    </Border.Background>
                                </Border>
                                <Image Source="{Binding Info.IconPath, Converter={StaticResource Img}}"
                                       Stretch="UniformToFill"/>
                            </Grid>
                        </Border>

                        <!-- MOD 名 -->
                        <TextBlock Text="{Binding DisplayName}"
                                   FontSize="{DynamicResource FontSizeDetailTitle}"
                                   FontWeight="SemiBold"
                                   Foreground="{DynamicResource TextPrimaryBrush}"
                                   TextWrapping="Wrap" Margin="0,0,0,4"/>

                        <!-- ID（等幅） -->
                        <TextBlock Text="{Binding ModId}"
                                   FontFamily="{DynamicResource MonoFont}"
                                   FontSize="{DynamicResource FontSizeDetailId}"
                                   Foreground="{DynamicResource TextMetaBrush}"
                                   TextTrimming="CharacterEllipsis" Margin="0,0,0,10"/>

                        <!-- 作者 -->
                        <StackPanel Orientation="Horizontal" Margin="0,0,0,3">
                            <TextBlock Text="作者: " FontSize="{DynamicResource FontSizeDetailBody}"
                                       Foreground="{DynamicResource TextLabelBrush}"/>
                            <TextBlock Text="{Binding Info.ModAuthor}"
                                       FontSize="{DynamicResource FontSizeDetailBody}"
                                       Foreground="{DynamicResource TextDetailBrush}"
                                       TextWrapping="Wrap"/>
                        </StackPanel>

                        <!-- バージョン -->
                        <StackPanel Orientation="Horizontal" Margin="0,0,0,6">
                            <TextBlock Text="バージョン: " FontSize="{DynamicResource FontSizeDetailBody}"
                                       Foreground="{DynamicResource TextLabelBrush}"/>
                            <TextBlock Text="{Binding Info.ModVersion}"
                                       FontSize="{DynamicResource FontSizeDetailBody}"
                                       Foreground="{DynamicResource TextDetailBrush}"/>
                        </StackPanel>

                        <!-- URL（null / 空のとき非表示） -->
                        <TextBlock Margin="0,0,0,10">
                            <TextBlock.Style>
                                <Style TargetType="TextBlock">
                                    <Setter Property="Visibility" Value="Visible"/>
                                    <Style.Triggers>
                                        <DataTrigger Binding="{Binding Info.ProjectUrl}" Value="{x:Null}">
                                            <Setter Property="Visibility" Value="Collapsed"/>
                                        </DataTrigger>
                                        <DataTrigger Binding="{Binding Info.ProjectUrl}" Value="">
                                            <Setter Property="Visibility" Value="Collapsed"/>
                                        </DataTrigger>
                                    </Style.Triggers>
                                </Style>
                            </TextBlock.Style>
                            <Hyperlink NavigateUri="{Binding Info.ProjectUrl}"
                                       Foreground="{DynamicResource AccentBrush}"
                                       TextDecorations="None"
                                       RequestNavigate="Hyperlink_RequestNavigate">
                                <Run Text="{Binding Info.ProjectUrl}"/>
                            </Hyperlink>
                        </TextBlock>

                        <!-- 区切り線 -->
                        <Border Height="1" Background="{DynamicResource BgSeparatorBrush}" Margin="0,0,0,10"/>

                        <!-- 説明文 -->
                        <TextBlock Text="{Binding Info.ModDescription}"
                                   FontSize="{DynamicResource FontSizeDetailBody}"
                                   Foreground="{DynamicResource TextDetailBrush}"
                                   TextWrapping="Wrap" LineHeight="22"/>
                    </StackPanel>
                </ScrollViewer>
            </Border>
        </Grid>
    </Grid>
</UserControl>
```

### Step 7-2: ModListView.xaml.cs を新規作成

- [ ] **Step 2: ModListView.xaml.cs を新規作成**

```csharp
// src/ReloadedHelper.App/Views/ModListView.xaml.cs
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Navigation;
using ReloadedHelper.Core;

namespace ReloadedHelper.App.Views;

public partial class ModListView : UserControl
{
    public ModListView()
    {
        InitializeComponent();
    }

    // ゲームタブをマウスホイールで切り替え
    private void GameTabs_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (DataContext is not MainViewModel vm || vm.Games.Count == 0) return;
        int idx = vm.Games.IndexOf(vm.SelectedGame);
        idx = e.Delta < 0
            ? Math.Min(idx + 1, vm.Games.Count - 1)
            : Math.Max(idx - 1, 0);
        vm.SelectedGame = vm.Games[idx];
        e.Handled = true;
    }

    // MOD トグル（有効/無効切り替え）
    private void ModToggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton btn
            && btn.DataContext is ModLoadEntry entry
            && DataContext is MainViewModel vm)
        {
            vm.ToggleEnabled(entry);
        }
    }

    // フィルタボタン
    private void FilterAll_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm) vm.FilterMode = FilterMode.All;
    }

    private void FilterEnabled_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm) vm.FilterMode = FilterMode.EnabledOnly;
    }

    private void FilterDisabled_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm) vm.FilterMode = FilterMode.DisabledOnly;
    }

    // URL ハイパーリンク（既存の動作を維持）
    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName        = e.Uri.AbsoluteUri,
            UseShellExecute = true
        });
        e.Handled = true;
    }
}
```

**注意:** 元の `ModListView.xaml` に `Hyperlink_RequestNavigate` ハンドラが XAML に記述されているかを確認する。もし `ModListView.xaml.cs` なしで動いていたなら、このハンドラはどこか別のファイルに定義されているか、または削除されていた可能性がある。ビルドエラーになる場合は `RequestNavigate="Hyperlink_RequestNavigate"` を XAML から削除して対応する。

- [ ] **Step 3: ビルドして確認**

```powershell
dotnet build reloaded-helper.slnx --configuration Release --no-restore
```

期待: ビルド成功（0 errors）

- [ ] **Step 4: コミット**

```powershell
git add src/ReloadedHelper.App/Views/ModListView.xaml `
        src/ReloadedHelper.App/Views/ModListView.xaml.cs
git commit -m "feat: MOD list filter buttons, mouse wheel game switch, toggle enables/disables mod"
```

---

## Task 8: 全体確認 + タグ付きリリース

- [ ] **Step 1: 全テストを実行**

```powershell
dotnet test --configuration Release
```

期待: 全テスト PASS

- [ ] **Step 2: アプリを起動して確認チェックリストを実行**

```powershell
dotnet run --project src/ReloadedHelper.App --configuration Release
```

以下を目視確認:
- [ ] 起動時にウィンドウが最大化される
- [ ] 最小化 → 設定で「最小化したらタスクトレイに格納する」をONにして最小化 → トレイアイコンが表示される
- [ ] トレイアイコンをクリックで復帰・最大化される
- [ ] 設定オーバーレイが画面の**ウィンドウ全体の中央**に表示される（右寄りでない）
- [ ] 設定スライダーが見える・操作できる
- [ ] フォルダパスが全文表示されている（バックスラッシュで表示される）
- [ ] 設定オーバーレイの外側（暗い部分）をクリックするとオーバーレイが閉じる
- [ ] ナビボタンにホバーしてもツールチップが出ない
- [ ] MOD リスト上でマウスホイールを回すとゲームタブが切り替わる
- [ ] MOD のトグルをクリックすると有効/無効が切り替わり、リストが更新される
- [ ] [有効]ボタンをクリックすると有効 MOD のみ表示される
- [ ] [無効]ボタンをクリックすると無効 MOD のみ表示される
- [ ] [全て]ボタンで全 MOD が表示される
- [ ] 起動時に有効 MOD が上部・無効 MOD が下部にグループ分けされている

- [ ] **Step 3: v0.4.0 タグを付けてリリース**

```powershell
git tag v0.4.0
git push origin main --tags
```

GitHub Actions が release.yml を起動し、`v0.4.0` のリリースと exe が作成される。

- [ ] **Step 4: GitHub Releases から exe をダウンロードして配置**

GitHub → Releases → v0.4.0 → `ReloadedHelper.App.exe` をダウンロードして `C:\FreeSoft\ReloadedHelper\` に配置する。

---

## セルフレビュー

**スペックカバレッジ確認:**

| 要件 | タスク |
|------|--------|
| 1. トレイアイコン修正 | Task 6 |
| 2. % とフォルダパス表示修正 | Task 5 |
| 3. オーバーレイ中央寄せ | Task 6 |
| 4. バックドロップクリックで閉じる | Task 6 |
| 5. マウスホイールでゲーム切り替え | Task 7 |
| 6. ナビボタンのツールチップ削除 | Task 6 |
| 7. 有効/無効フィルタ | Task 1 + Task 3 + Task 7 |
| 8. 起動時グループ化ソート | Task 4 |
| 9. MOD トグルでリアルタイム更新 | Task 2 + Task 3 + Task 7 |

**プレースホルダーなし:** 全ステップに実際のコードあり ✓

**型の一貫性:**
- `FilterMode` (enum): Task 1 定義 → Task 3・Task 7 で使用 ✓
- `ModFilter.Filter(entries, search, mode)`: Task 1 定義 → Task 3 の `ApplyFilter()` で使用 ✓
- `AppConfigWriter.WriteEnabledAndSorted(configPath, appId, enabled, sorted)`: Task 2 定義 → Task 3 の `ToggleEnabled()` で使用 ✓
- `MainViewModel.FilterMode`, `EntryCountLabel`, `ToggleEnabled()`: Task 3 定義 → Task 7 XAML/cs で使用 ✓
