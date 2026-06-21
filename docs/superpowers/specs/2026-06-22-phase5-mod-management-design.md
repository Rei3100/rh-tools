# Phase 5: MOD管理強化 設計書

## 目的

Phase 4 の残バグを修正し、MOD管理機能を大幅強化する。
「ヘルパー側で全部完結する」をビジョンとし、Reloaded-II 本体を一切触らずに済む状態を目指す。

## 制約

- ランタイム NuGet パッケージ追加禁止（System.Text.Json のみ可）
- Win32 P/Invoke は可（既存コードで実績あり）
- ユーザーデータは `%APPDATA%\ReloadedHelper` 以外に保存禁止

## スコープ（Phase 5）

| # | 機能 | 説明 |
|---|------|------|
| A | バグ修正 | IsLibrary 未読取・カテゴリバッジ未表示を修正 |
| B | フレームワーク強制有効化 | IsLibrary=true の MOD を常時有効・自動並び替え対象に |
| C | 個別編集・削除ダイアログ | 「…」ボタンから開くウィンドウで全フィールド編集＋ゴミ箱削除 |
| D | 個別・複数選択更新 | Ctrl+クリックで複数選択し「選択中を更新」ボタンで一括更新 |
| E | 強制再取得 | バージョン不問で全MODを常に再取得・上書き |

| F | タスクトレイメニュー配色修正 | 右クリックメニューのテキストが読みづらい問題を修正 |

**スコープ外（Phase 6 以降）**: MODコンフィグ設定UI / Reloaded-IIログ解析・クラッシュ対策

---

## アーキテクチャ

### Core 変更（`src/ReloadedHelper.Core/`）

| ファイル | 変更内容 |
|---------|---------|
| `Models.cs` | `ModInfo` に `bool IsLibrary` 追加。`ModLoadEntry` に `bool IsLibrary = false` 追加。`CategoryLabel` がフレームワーク時に "フレームワーク" を返すよう変更 |
| `ModConfigParser.cs` | `IsLibrary` フィールドを読み取る（デフォルト `false`） |
| `LoadOrder.cs` | `LoadOrderBuilder.Build()` で `IsLibrary==true` の MOD を常時 `Enabled=true` にする |
| `MainViewModel.cs` | `ToggleEnabled()` で IsLibrary をブロック。個別・複数更新用 `RefreshAction` の引数拡張 |

### App 変更（`src/ReloadedHelper.App/`）

| ファイル | 変更内容 |
|---------|---------|
| `App.xaml.cs` | `RefreshModMetadataAsync` にターゲット MOD ID リストの引数追加。バージョンチェック削除（全MOD常時処理）。`ApplySortAllGames` で IsLibrary MOD を必ず enabled グループに含める |
| `Views/ModListView.xaml` | ListBox を `SelectionMode="Extended"` に変更。各行に「…」ボタン（6列目）追加。IsLibrary MOD のトグルを非表示に。「選択中を更新 (N件)」ボタン追加 |
| `Views/ModListView.xaml.cs` | `SelectionChanged` ハンドラ、「選択中を更新」ハンドラ追加 |
| `Views/ModEditWindow.xaml` *(新規)* | 個別編集ウィンドウ（編集フォーム＋削除ボタン） |
| `Views/ModEditWindow.xaml.cs` *(新規)* | 保存・個別更新・削除ロジック |

---

## 詳細設計

### A. バグ修正

#### IsLibrary 未読取の修正

`ModConfigParser.Parse()` に以下を追加：

```csharp
var isLibrary = GetBool(root, "IsLibrary");  // デフォルト false

private static bool GetBool(JsonElement element, string propertyName)
{
    if (!element.TryGetProperty(propertyName, out var prop)) return false;
    return prop.ValueKind == JsonValueKind.True;
}
```

`ModInfo` レコードに `bool IsLibrary` を追加（既存フィールドの後ろ）。

#### カテゴリバッジ未表示の修正

根本原因：`RefreshModMetadataAsync` がバージョン一致 MOD をスキップするため、カテゴリデータが保存されない可能性がある。
Feature E（バージョンチェック削除）により自然に解消される。

---

### B. フレームワーク強制有効化

#### `Models.cs` の変更

```csharp
public sealed record ModInfo(
    ...既存フィールド...,
    bool IsLibrary)  // 追加

public sealed record ModLoadEntry(
    int Order, string ModId, ModInfo? Info, bool Enabled,
    string? Category = null,
    bool IsLibrary = false)  // 追加
{
    public string? CategoryLabel =>
        IsLibrary ? "フレームワーク" :   // フレームワーク優先
        Category switch { ... };        // 既存マッピング
}
```

#### `LoadOrderBuilder.Build()` の変更

```csharp
var isLibrary = info?.IsLibrary ?? false;
var enabled   = enabledSet.Contains(id) || isLibrary;  // IsLibrary は常に有効
list.Add(new ModLoadEntry(i + 1, id, info, enabled, category, isLibrary));
```

#### `ApplySortAllGames` の変更（App.xaml.cs）

```csharp
// IsLibrary の MOD を必ず enabled グループに含める
var enabledGroup = game.SortedMods
    .Where(id => enabledSet.Contains(id) ||
                 (catalog.TryGetValue(id, out var m) && m.IsLibrary))
    .ToList();
```

さらに、現在 `ApplySortAllGames` は `AppConfigWriter.WriteOrder()` のみ呼んでいるが、これを `AppConfigWriter.WriteEnabledAndSorted()` に変更し、IsLibrary MOD を `newEnabled` リストに追加して書き込む。これにより AppConfig.json の enabledMods にも自動で追記される。

#### `MainViewModel.ToggleEnabled()` の変更

```csharp
public void ToggleEnabled(ModLoadEntry entry)
{
    if (entry.IsLibrary) return;  // フレームワークはトグル禁止
    ...既存処理...
}
```

#### UI（ModListView.xaml）

フレームワーク MOD のトグルを非表示：
```xml
<ToggleButton ...>
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

カテゴリバッジは `CategoryLabel` にすでにバインドされているため、"フレームワーク" が自動的に表示される。バッジの色は既存の `AccentBrush` を使用（区別しやすくする場合は別色の `FrameworkBrush` を Colors.xaml に追加）。

---

### C. 個別編集・削除ダイアログ

#### 開き方（2通り）

**①「…」ボタン（6列目）**：各 MOD カードの右端に追加

```xml
<ColumnDefinition Width="36"/>  <!-- … ボタン -->
...
<Button Grid.Column="5" Content="…"
        Click="EditButton_Click" Tag="{Binding}"
        Width="28" Height="28" Padding="0"
        Style="{DynamicResource IconButtonStyle}"/>
```

**②右クリック ContextMenu**：DataTemplate に ContextMenu を追加

```xml
<Grid Height="54">
    <Grid.ContextMenu>
        <ContextMenu>
            <MenuItem Header="編集..."   Click="EditMenu_Click"/>
            <MenuItem Header="更新"      Click="RefreshMenu_Click"/>
            <Separator/>
            <MenuItem Header="削除（ゴミ箱へ）" Click="DeleteMenu_Click"/>
        </ContextMenu>
    </Grid.ContextMenu>
    ...
</Grid>
```

右クリックメニューの各項目は「…」ボタンと同じロジックを呼ぶ。`Separator` はゴミ箱削除の誤操作防止のために区切る。

#### `ModEditWindow.xaml` の構成

```
┌─────────────────────────────────────────┐
│ MOD名（日本語）                          │
│ [TextBox - TranslatedName]              │
│                                          │
│ 説明（日本語）                           │
│ [TextBox AcceptsReturn - TranslatedDesc] │
│                                          │
│ GameBanana URL                           │
│ [TextBox - UrlOverride]                  │
│                                          │
│ メモ                                     │
│ [TextBox AcceptsReturn - Notes]          │
│                                          │
│ GameBanana ID（自動取得）                │
│ [TextBox - GameBananaId]                │
│                                          │
│ [個別更新]  [保存]  [キャンセル]         │
│                                          │
│ ─────────────────────────────────────── │
│         [このMODを削除（ゴミ箱へ）]      │
└─────────────────────────────────────────┘
```

- **保存**: `UserDataStore.Load()` → 各フィールドを更新 → `UserDataStore.Save()` → `LoadFrom()` → ウィンドウを閉じる
- **個別更新**: 保存後に `RefreshModMetadataAsync([このMODのみ])` を実行
- **削除**: `MessageBox.Show("「{MOD名}」のフォルダをゴミ箱に移動します。よろしいですか？")` → OK → MOD削除処理 → `LoadFrom()` → ウィンドウを閉じる

#### MOD 削除処理（`RecycleBinHelper.cs` — App プロジェクト）

Win32 P/Invoke を使用（既存の App.xaml.cs パターンに倣う）：

```csharp
[DllImport("shell32.dll", CharSet = CharSet.Unicode)]
private static extern int SHFileOperation(ref SHFILEOPSTRUCT op);

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
private struct SHFILEOPSTRUCT
{
    public IntPtr hwnd;
    public uint   wFunc;
    public string pFrom;
    public string? pTo;
    public ushort fFlags;
    public bool   fAnyOperationsAborted;
    public IntPtr hNameMappings;
    public string? lpszProgressTitle;
}

private const uint   FO_DELETE         = 0x0003;
private const ushort FOF_ALLOWUNDO     = 0x0040;
private const ushort FOF_NOCONFIRMATION = 0x0010;

public static void SendToRecycleBin(string folderPath)
{
    var op = new SHFILEOPSTRUCT
    {
        wFunc  = FO_DELETE,
        pFrom  = folderPath + "\0\0",
        fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION
    };
    SHFileOperation(ref op);
}
```

削除後の後処理（`ModEditWindow.xaml.cs`）：
1. すべてのゲームの AppConfig.json から対象 MOD ID を除去（`AppConfigWriter.RemoveMod()`）
2. `UserDataStore.Load()` → `Mods.Remove(modId)` → `UserDataStore.Save()`
3. `RecycleBinHelper.SendToRecycleBin(modFolderPath)`
4. `mainVm.LoadFrom(install)` でリロード

#### `AppConfigWriter.RemoveMod()` の追加（新規メソッド）

既存の `AppConfigWriter.cs` に追加：
```csharp
public static void RemoveMod(string configPath, string appId, string modId)
```

`enabledMods` と `sortedMods` 配列から `modId` を除外して書き込む。

---

### D. 個別・複数選択更新

#### ListBox の複数選択対応

```xml
<ListBox ...
         SelectionMode="Extended"
         SelectionChanged="ModList_SelectionChanged">
```

#### ツールバーの「選択中を更新」ボタン

```xml
<Button x:Name="RefreshSelectedButton"
        Content="選択中を更新 (0件)"
        Click="RefreshSelected_Click"
        Visibility="Collapsed"
        .../>
```

`SelectionChanged` ハンドラで件数を更新し、1件以上で `Visibility="Visible"` にする。

#### `RefreshModMetadataAsync` の引数拡張

```csharp
private static async Task RefreshModMetadataAsync(
    MainViewModel modListVm,
    ReloadedInstall install,
    IReadOnlyList<string>? targetModIds = null)  // null = 全件
```

`toProcess` の生成：
```csharp
var toProcess = targetModIds is null
    ? catalog.Values.ToList()
    : catalog.Values.Where(m => targetModIds.Contains(m.ModId)).ToList();
```

`modListVm.RefreshAction` は全件更新用に残す。`MainViewModel` に `Func<IReadOnlyList<string>, Task>? RefreshSelectedAction` プロパティを追加し、`ModEditWindow`（個別更新）と `ModListView.xaml.cs`（選択中を更新）の両方からこのデリゲートを呼び出す。`App.xaml.cs` の `OnStartup()` でデリゲートを登録する。

---

### E. 強制再取得（バージョン不問）

`RefreshModMetadataAsync` の `toProcess` 生成を変更：

**変更前：**
```csharp
var toProcess = catalog.Values
    .Where(mod =>
    {
        if (!userData.Mods.TryGetValue(mod.ModId, out var ud)) return true;
        if (string.IsNullOrEmpty(mod.ModVersion)) return ud.FetchedAt is null;
        return ud.FetchedVersion != mod.ModVersion;
    })
    .ToList();
```

**変更後：**
```csharp
// targetModIds が null なら全件、指定があればその MOD のみ
var toProcess = targetModIds is null
    ? catalog.Values.ToList()
    : catalog.Values.Where(m => targetModIds.Contains(m.ModId)).ToList();
```

効果：
- 手動翻訳済みの既存データも GameBanana 公式データで上書き
- バージョン変更なしの MOD も毎回再取得

---

### F. タスクトレイ右クリックメニュー配色修正

#### 問題

`H.NotifyIcon.Wpf` の `TaskbarIcon.ContextMenu` は WPF の `ContextMenu` コントロールを使用している。アプリがダークテーマを定義しているため、Windows ライトモード時に ContextMenu の背景色と文字色が低コントラストになり読みづらい。

#### 修正

`MainWindow.xaml` のトレイアイコン ContextMenu に明示的なスタイルを設定する：

```xml
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

`DynamicResource` を使うことでアプリのカラーテーマ（ダーク固定）と一致させ、Windows のライト/ダークモードに関わらず常にアプリの配色で表示される。`Separator` も追加して「表示」と「終了」を区切る。

---

## データフロー

### 起動時

```
OnStartup()
  ├─ modListVm.LoadFrom(install)        … MOD 一覧読み込み
  ├─ ApplySortAllGames()                … IsLibrary を enabled グループに含めてソート
  └─ Task.Run(RefreshModMetadataAsync)  … 全件強制再取得（バックグラウンド）
       └─ カテゴリ・翻訳・GB情報を保存
       └─ LoadFrom() で UI 更新
```

### 個別更新（「…」→「個別更新」）

```
ModEditWindow.RefreshThis_Click()
  → 保存
  → Task.Run(RefreshModMetadataAsync([このMODのID]))
  → LoadFrom() で UI 更新
  → ウィンドウを閉じる
```

### 複数選択更新（Ctrl+クリック → 「選択中を更新」）

```
ModListView.RefreshSelected_Click()
  → 選択中 MOD の ModId リストを取得
  → Task.Run(RefreshModMetadataAsync([選択中のIDリスト]))
  → LoadFrom() で UI 更新
```

---

## エラーハンドリング

| 状況 | 対処 |
|------|------|
| 削除対象 MOD が IsLibrary | 削除ボタンを非表示（フレームワーク MOD はヘルパーから削除禁止） |
| 個別更新中に別の更新が走る | `IsUpdating` フラグで二重実行を防止（既存パターン） |
| RecycleBin 送りに失敗 | `SHFileOperation` の戻り値を確認し、エラー時は `MessageBox.Show()` でメッセージ表示 |
| 全件更新の途中でエラー | `catch` でスキップし次の MOD に進む（既存パターン） |

---

## テスト方針

- `ModConfigParser` の IsLibrary 読み取りは xUnit テスト追加
- `LoadOrderBuilder.Build()` で IsLibrary MOD が常に Enabled=true になることを xUnit テスト追加
- `AppConfigWriter.RemoveMod()` は xUnit テスト追加
- UI 動作（ダイアログ開閉・削除確認・複数選択）は目視確認
- ゴミ箱送りは実機で目視確認

---

## ファイル変更サマリー

### 変更ファイル

- `src/ReloadedHelper.Core/Models.cs`
- `src/ReloadedHelper.Core/ModConfigParser.cs`
- `src/ReloadedHelper.Core/LoadOrder.cs`
- `src/ReloadedHelper.Core/MainViewModel.cs`
- `src/ReloadedHelper.Core/AppConfigWriter.cs`
- `src/ReloadedHelper.App/App.xaml.cs`
- `src/ReloadedHelper.App/Views/ModListView.xaml`（右クリックContextMenu追加含む）
- `src/ReloadedHelper.App/Views/ModListView.xaml.cs`
- `src/ReloadedHelper.App/MainWindow.xaml`（トレイContextMenuスタイル修正）

### 新規ファイル

- `src/ReloadedHelper.App/Views/ModEditWindow.xaml`
- `src/ReloadedHelper.App/Views/ModEditWindow.xaml.cs`
- `src/ReloadedHelper.App/RecycleBinHelper.cs`
- `tests/ReloadedHelper.Core.Tests/ModConfigParserIsLibraryTests.cs`
- `tests/ReloadedHelper.Core.Tests/LoadOrderBuilderIsLibraryTests.cs`
- `tests/ReloadedHelper.Core.Tests/AppConfigWriterRemoveModTests.cs`
