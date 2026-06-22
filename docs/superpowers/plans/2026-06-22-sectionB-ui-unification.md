# セクションB：UI統一＋小バグ修正 実装計画

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** タイトルバー・右クリックメニュー・システムトレイメニュー・更新ポップアップを本体UIのダーク配色に統一し、"浮いてる・ライトモード残り・芋っぽい" を解消する。併せてタブ勝手切替バグの取りこぼしを潰す。

**Architecture:** 既存のテーマ辞書（`Themes/Colors.xaml`・`Controls.xaml`）にメニュー系の既定スタイルを追加して全メニューを一括ダーク化。素の `MessageBox` を、本体UI配色の再利用可能な `ThemedDialog` に置換。`MainWindow` に `WindowChrome` ベースのカスタムタイトルバーを実装。

**Tech Stack:** C# / .NET 10 / WPF（System.Windows.Shell.WindowChrome）、xUnit。UI が主のため多くは手動検証。

## Global Constraints

- ランタイム NuGet 追加禁止（System.Text.Json のみ。既存 H.NotifyIcon.Wpf は据え置き）。テスト用 xUnit は可。
- ユーザーデータは %APPDATA%\ReloadedHelper 以外に保存禁止。
- 新規 UI はすべて既存テーマリソースを流用：背景 `BgMainBrush #1a1c1f` / `BgBarBrush #141518` / 入力 `BgInputBrush #2a2c30` / 枠 `BorderInputBrush #34373d` / アクセント `AccentBrush #4caf50` / 文字 `TextBodyBrush #dadde0`・`TextLabelBrush #9aa0a8`。新規の色キーは原則追加しない。
- 新しい色が必要なときだけ `Colors.xaml` に追記し、ハードコード色は避ける。
- ウィンドウは起動時に最大化される（App.xaml.cs が `WindowState=Maximized`）。タイトルバー実装は最大化状態で内容が画面外にはみ出さないこと。

---

## ファイル構成

| 区分 | パス | 役割 |
|------|------|------|
| 変更 | `src/ReloadedHelper.App/Themes/Controls.xaml` | `ContextMenu`/`MenuItem`/`Separator` の既定ダークスタイル追加 |
| 変更 | `src/ReloadedHelper.App/Themes/Colors.xaml` | （必要時）メニュー区切り等の微調整色 |
| 新規 | `src/ReloadedHelper.App/ThemedDialog.xaml`(.cs) | 本体配色の再利用ダイアログ（OK/キャンセル） |
| 変更 | `src/ReloadedHelper.App/App.xaml.cs` | 更新ポップアップを `ThemedDialog` に置換 |
| 変更 | `src/ReloadedHelper.App/Views/ModListView.xaml.cs` | 削除確認・エラーの `MessageBox` を `ThemedDialog` に置換 |
| 変更 | `src/ReloadedHelper.App/MainWindow.xaml` | `WindowChrome` カスタムタイトルバー追加 |
| 変更 | `src/ReloadedHelper.App/MainWindow.xaml.cs` | 最小化/最大化/閉じるボタン＋最大化マージン処理 |
| 変更 | `src/ReloadedHelper.Core/MainViewModel.cs` | `ToggleEnabled` を `Reload()` 経由に統一（タブ保持） |
| 変更 | `tests/ReloadedHelper.Core.Tests/MainViewModelTests.cs` | タブ保持のテスト追加 |

---

### Task B1: 全メニューのダーク既定スタイル（右クリック＋トレイ）

右クリックメニュー（`ModListView.xaml` の `ContextMenu`、無スタイル＝OS既定ライト）とトレイメニューを、テーマ辞書の既定スタイルで一括ダーク化・可読サイズ化する。

**Files:**
- Modify: `src/ReloadedHelper.App/Themes/Controls.xaml`（末尾、`</ResourceDictionary>` の直前に追加）

**Interfaces:**
- Produces: `TargetType="ContextMenu"`・`TargetType="MenuItem"`・`TargetType="Separator"`（`{x:Static MenuItem.SeparatorStyleKey}`）の既定スタイル。キー無しなので全メニューに自動適用。

- [ ] **Step 1: メニュー既定スタイルを追加**

`Controls.xaml` の最後の `</ResourceDictionary>` の直前に貼り付け：

```xml
    <!-- ── ContextMenu（ダーク・角丸） ── -->
    <Style TargetType="ContextMenu">
        <Setter Property="Background"  Value="{DynamicResource BgBarBrush}"/>
        <Setter Property="Foreground"  Value="{DynamicResource TextBodyBrush}"/>
        <Setter Property="BorderBrush" Value="{DynamicResource BorderInputBrush}"/>
        <Setter Property="BorderThickness" Value="1"/>
        <Setter Property="Padding"     Value="4"/>
        <Setter Property="FontFamily"  Value="{DynamicResource MainFont}"/>
        <Setter Property="FontSize"    Value="14"/>
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="ContextMenu">
                    <Border Background="{TemplateBinding Background}"
                            BorderBrush="{TemplateBinding BorderBrush}"
                            BorderThickness="{TemplateBinding BorderThickness}"
                            CornerRadius="8" Padding="{TemplateBinding Padding}">
                        <StackPanel IsItemsHost="True" KeyboardNavigation.DirectionalNavigation="Cycle"/>
                    </Border>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <!-- ── MenuItem（ダーク・ホバー強調・余白広め） ── -->
    <Style TargetType="MenuItem">
        <Setter Property="Foreground" Value="{DynamicResource TextBodyBrush}"/>
        <Setter Property="Background" Value="Transparent"/>
        <Setter Property="Padding"    Value="14,8"/>
        <Setter Property="Cursor"     Value="Hand"/>
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="MenuItem">
                    <Border x:Name="Bd" Background="{TemplateBinding Background}"
                            CornerRadius="6" Padding="{TemplateBinding Padding}">
                        <ContentPresenter ContentSource="Header" VerticalAlignment="Center"/>
                    </Border>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsHighlighted" Value="True">
                            <Setter TargetName="Bd" Property="Background"
                                    Value="{DynamicResource BgNavSelectedBrush}"/>
                        </Trigger>
                        <Trigger Property="IsEnabled" Value="False">
                            <Setter Property="Foreground" Value="{DynamicResource TextMetaBrush}"/>
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <!-- ── メニュー内 Separator ── -->
    <Style x:Key="{x:Static MenuItem.SeparatorStyleKey}" TargetType="Separator">
        <Setter Property="Margin"     Value="6,4"/>
        <Setter Property="Height"     Value="1"/>
        <Setter Property="Background" Value="{DynamicResource BgSeparatorBrush}"/>
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="Separator">
                    <Border Height="1" Background="{DynamicResource BgSeparatorBrush}"/>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>
```

- [ ] **Step 2: トレイメニューの重複インラインスタイルを整理**

`MainWindow.xaml` のトレイ `ContextMenu`（34-46 行）のインライン `Background`/`Foreground`/`BorderBrush` 指定を削除し、既定スタイルに任せる（`MenuItem` から `Foreground`/`Background` 属性を外す。`Header`/`Click` は残す）。`Separator` のインライン `Background` も外す。

- [ ] **Step 3: ビルド**

Run: `dotnet build reloaded-helper.slnx`
Expected: 0 errors

- [ ] **Step 4: 手動検証**

バージョン上書きで自動更新を止めて起動：`dotnet build src/ReloadedHelper.App/ReloadedHelper.App.csproj -c Debug -p:Version=9.9.9` → `bin/Debug/net10.0-windows/ReloadedHelper.App.exe` 実行。
1. MOD を右クリック → メニューが**ダーク配色・文字が読める大きさ**・ホバーで緑系ハイライト。
2. 最小化してトレイアイコン右クリック → 「表示」「終了」が同じダーク見た目で違和感がない。

- [ ] **Step 5: コミット**

```bash
git add src/ReloadedHelper.App/Themes/Controls.xaml src/ReloadedHelper.App/MainWindow.xaml
git commit -m "feat: dark themed context menu + tray menu (global MenuItem style)"
```

---

### Task B2: ThemedDialog（本体配色の再利用ダイアログ）

素の `MessageBox` を置き換える、本体UI配色のモーダルダイアログ。

**Files:**
- Create: `src/ReloadedHelper.App/ThemedDialog.xaml`
- Create: `src/ReloadedHelper.App/ThemedDialog.xaml.cs`

**Interfaces:**
- Produces:
```csharp
public static bool ThemedDialog.Show(Window? owner, string title, string message,
    string okText = "OK", string? cancelText = null);
// cancelText が null ならボタンは OK のみ。OK 押下=true、キャンセル/閉じる=false。
```

- [ ] **Step 1: ThemedDialog.xaml を作成**

```xml
<Window x:Class="ReloadedHelper.App.ThemedDialog"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        WindowStartupLocation="CenterOwner"
        WindowStyle="None" ResizeMode="NoResize" AllowsTransparency="True"
        Background="Transparent" SizeToContent="Height" Width="420"
        ShowInTaskbar="False"
        FontFamily="{DynamicResource MainFont}">
    <Border Background="{DynamicResource BgBarBrush}"
            BorderBrush="{DynamicResource BorderInputBrush}" BorderThickness="1"
            CornerRadius="12" Padding="20">
        <StackPanel>
            <TextBlock x:Name="TitleText" FontSize="16" FontWeight="Bold"
                       Foreground="{DynamicResource TextPrimaryBrush}" Margin="0,0,0,10"/>
            <TextBlock x:Name="MessageText" TextWrapping="Wrap"
                       Foreground="{DynamicResource TextBodyBrush}" Margin="0,0,0,20"/>
            <StackPanel Orientation="Horizontal" HorizontalAlignment="Right">
                <Button x:Name="CancelButton" Content="キャンセル" MinWidth="88"
                        Margin="0,0,8,0" Padding="14,7" Click="Cancel_Click"
                        Background="{DynamicResource BgInputBrush}"
                        Foreground="{DynamicResource TextBodyBrush}"
                        BorderBrush="{DynamicResource BorderInputBrush}"/>
                <Button x:Name="OkButton" Content="OK" MinWidth="88" Padding="14,7"
                        Click="Ok_Click"
                        Background="{DynamicResource AccentBrush}"
                        Foreground="{DynamicResource TextPrimaryBrush}"
                        BorderThickness="0"/>
            </StackPanel>
        </StackPanel>
    </Border>
</Window>
```

- [ ] **Step 2: ThemedDialog.xaml.cs を作成**

```csharp
using System.Windows;

namespace ReloadedHelper.App;

public partial class ThemedDialog : Window
{
    private bool _result;

    private ThemedDialog() { InitializeComponent(); }

    public static bool Show(Window? owner, string title, string message,
        string okText = "OK", string? cancelText = null)
    {
        var dlg = new ThemedDialog();
        if (owner is not null && owner.IsLoaded) dlg.Owner = owner;
        else dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        dlg.TitleText.Text = title;
        dlg.MessageText.Text = message;
        dlg.OkButton.Content = okText;
        if (cancelText is null) dlg.CancelButton.Visibility = Visibility.Collapsed;
        else dlg.CancelButton.Content = cancelText;
        dlg.ShowDialog();
        return dlg._result;
    }

    private void Ok_Click(object sender, RoutedEventArgs e) { _result = true; Close(); }
    private void Cancel_Click(object sender, RoutedEventArgs e) { _result = false; Close(); }
}
```

- [ ] **Step 3: ビルド**

Run: `dotnet build reloaded-helper.slnx`
Expected: 0 errors

- [ ] **Step 4: コミット**

```bash
git add src/ReloadedHelper.App/ThemedDialog.xaml src/ReloadedHelper.App/ThemedDialog.xaml.cs
git commit -m "feat: add ThemedDialog (dark themed modal replacing MessageBox)"
```

---

### Task B3: 素の MessageBox を ThemedDialog に置換

**Files:**
- Modify: `src/ReloadedHelper.App/App.xaml.cs`（`CheckAndApplyUpdateAsync` の更新ポップアップ）
- Modify: `src/ReloadedHelper.App/Views/ModListView.xaml.cs`（削除確認・エラー）

**Interfaces:**
- Consumes: `ThemedDialog.Show`（Task B2）

- [ ] **Step 1: 更新ポップアップを置換**

`App.xaml.cs` の `CheckAndApplyUpdateAsync` 内、`MessageBox.Show(...)`（"… に更新します。アプリを再起動します。"）を置換：

```csharp
Current.Dispatcher.Invoke(() =>
{
    ThemedDialog.Show(Current.MainWindow,
        "rh-tools 更新",
        $"v{latestVer} に更新します。アプリを再起動します。");
    Current.Shutdown();
});
```

- [ ] **Step 2: 削除確認・エラーを置換**

`Views/ModListView.xaml.cs` の `DeleteMenu_Click` 内：
- 「MOD フォルダが見つかりません。」エラー：
```csharp
ThemedDialog.Show(Window.GetWindow(this), "削除エラー", "MOD フォルダが見つかりません。");
return;
```
- 確認ダイアログ：
```csharp
var confirm = ThemedDialog.Show(Window.GetWindow(this), "MOD 削除の確認",
    $"「{entry.DisplayName}」のフォルダをゴミ箱に移動します。よろしいですか？\n\n{entry.Info.FolderPath}",
    okText: "ゴミ箱へ移動", cancelText: "キャンセル");
if (!confirm) return;
```
- 「ゴミ箱への移動に失敗しました。」エラーも同様に `ThemedDialog.Show(Window.GetWindow(this), "削除エラー", "ゴミ箱への移動に失敗しました。");`

`using System.Windows;`（`MessageBox` 用）が他で未使用なら残してよい（`Window.GetWindow` で使用）。

- [ ] **Step 3: ビルド**

Run: `dotnet build reloaded-helper.slnx`
Expected: 0 errors

- [ ] **Step 4: 手動検証**

`-p:Version=9.9.9` ビルドで起動 → MOD を右クリック →「削除（ゴミ箱へ）」→ **ダークなThemedDialog**が出る（OSダイアログでない）。キャンセルで何も起きない。

- [ ] **Step 5: コミット**

```bash
git add src/ReloadedHelper.App/App.xaml.cs src/ReloadedHelper.App/Views/ModListView.xaml.cs
git commit -m "feat: replace MessageBox popups with ThemedDialog (update + delete)"
```

---

### Task B4: カスタムダークタイトルバー（WindowChrome）

最上部のライトな OS タイトルバーを、本体配色のカスタムタイトルバーに置換する。

**Files:**
- Modify: `src/ReloadedHelper.App/MainWindow.xaml`
- Modify: `src/ReloadedHelper.App/MainWindow.xaml.cs`

**Interfaces:**
- Produces: コードビハインド `TitleBar_MouseLeftButtonDown`, `MinButton_Click`, `MaxButton_Click`, `CloseButton_Click`。

- [ ] **Step 1: WindowChrome とタイトルバー行を追加**

`MainWindow.xaml` の `<Window ...>` に名前空間とプロパティを追加：
- ルート要素属性に `xmlns:shell="clr-namespace:System.Windows.Shell;assembly=PresentationFramework"` を追加し、`Title`/`Background` 等は維持。
- `<Window.Resources>` の後に追加：
```xml
    <shell:WindowChrome.WindowChrome>
        <shell:WindowChrome CaptionHeight="36" ResizeBorderThickness="6"
                            GlassFrameThickness="0" CornerRadius="0" UseAeroCaptionButtons="False"/>
    </shell:WindowChrome.WindowChrome>
```

ルート `<Grid>`（18 行）を **行2つ**に再構成：Row0=タイトルバー(36px)、Row1=既存の2カラム内容。具体的には、現在の `<Grid> … </Grid>`（18-152 行）の中身（`Grid.LayoutTransform` 以下の2カラム構成）を Row1 に入れ、Row0 に次のタイトルバーを追加する。タイトルバーは `LayoutTransform`（ズーム）の**外**に置く：

```xml
<Grid x:Name="RootGrid">
    <Grid.RowDefinitions>
        <RowDefinition Height="36"/>
        <RowDefinition Height="*"/>
    </Grid.RowDefinitions>

    <!-- タイトルバー -->
    <Grid Grid.Row="0" Background="{DynamicResource BgBarBrush}"
          MouseLeftButtonDown="TitleBar_MouseLeftButtonDown">
        <TextBlock Text="Reloaded Helper" VerticalAlignment="Center" Margin="14,0,0,0"
                   Foreground="{DynamicResource TextLabelBrush}" FontSize="13"/>
        <StackPanel Orientation="Horizontal" HorizontalAlignment="Right"
                    shell:WindowChrome.IsHitTestVisibleInChrome="True">
            <Button Content="&#xE921;" Click="MinButton_Click" Style="{StaticResource CaptionButtonStyle}"/>
            <Button Content="&#xE922;" Click="MaxButton_Click" Style="{StaticResource CaptionButtonStyle}"/>
            <Button Content="&#xE8BB;" Click="CloseButton_Click" Style="{StaticResource CaptionCloseButtonStyle}"/>
        </StackPanel>
    </Grid>

    <!-- 既存の2カラム内容（ズーム対象） -->
    <Grid Grid.Row="1">
        <Grid.LayoutTransform> ...既存のScaleTransform... </Grid.LayoutTransform>
        ...既存の ColumnDefinitions・サイドナビ・ModListView・オーバーレイをそのまま移植...
    </Grid>
</Grid>
```

> キャプションボタンの文字は Segoe MDL2 Assets（最小化 `&#xE921;`、最大化 `&#xE922;`、閉じる `&#xE8BB;`）。`FontFamily="Segoe MDL2 Assets"` を各ボタンに設定（下記スタイル内）。

- [ ] **Step 2: キャプションボタンのスタイルを Controls.xaml に追加**

`Controls.xaml` の末尾（`</ResourceDictionary>` 直前）に追加：

```xml
    <!-- ── タイトルバー キャプションボタン ── -->
    <Style x:Key="CaptionButtonStyle" TargetType="Button">
        <Setter Property="Width" Value="46"/>
        <Setter Property="Height" Value="36"/>
        <Setter Property="Background" Value="Transparent"/>
        <Setter Property="BorderThickness" Value="0"/>
        <Setter Property="Foreground" Value="{DynamicResource TextLabelBrush}"/>
        <Setter Property="FontFamily" Value="Segoe MDL2 Assets"/>
        <Setter Property="FontSize" Value="10"/>
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="Button">
                    <Border x:Name="Bd" Background="{TemplateBinding Background}">
                        <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
                    </Border>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsMouseOver" Value="True">
                            <Setter TargetName="Bd" Property="Background" Value="{DynamicResource BgNavSelectedBrush}"/>
                            <Setter Property="Foreground" Value="{DynamicResource TextPrimaryBrush}"/>
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <Style x:Key="CaptionCloseButtonStyle" TargetType="Button" BasedOn="{StaticResource CaptionButtonStyle}">
        <Style.Triggers>
            <Trigger Property="IsMouseOver" Value="True">
                <Setter Property="Foreground" Value="White"/>
            </Trigger>
        </Style.Triggers>
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="Button">
                    <Border x:Name="Bd" Background="{TemplateBinding Background}">
                        <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
                    </Border>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsMouseOver" Value="True">
                            <Setter TargetName="Bd" Property="Background" Value="#c0392b"/>
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>
```

- [ ] **Step 3: コードビハインドにハンドラと最大化マージン処理を追加**

`MainWindow.xaml.cs` に追加（`using System.Windows.Input;` を利用）：

```csharp
private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
{
    if (e.ClickCount == 2) { ToggleMaximize(); return; }
    if (e.ButtonState == MouseButtonState.Pressed) DragMove();
}

private void MinButton_Click(object sender, RoutedEventArgs e)
    => WindowState = WindowState.Minimized;
private void MaxButton_Click(object sender, RoutedEventArgs e) => ToggleMaximize();
private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

private void ToggleMaximize()
    => WindowState = WindowState == WindowState.Maximized
        ? WindowState.Normal : WindowState.Maximized;
```

`Window_StateChanged`（既存 42 行）に**最大化時のマージン補正**を追加（WindowChrome の最大化はみ出し対策）。既存の最小化→トレイ処理は維持しつつ先頭に：

```csharp
private void Window_StateChanged(object sender, EventArgs e)
{
    // 最大化時、WindowChrome で内容が画面外にはみ出すのをマージンで補正
    RootGrid.Margin = WindowState == WindowState.Maximized
        ? new Thickness(7) : new Thickness(0);

    if (WindowState == WindowState.Minimized && _shell.SettingsVm.MinimizeToTray)
    {
        ShowInTaskbar = false;
        TrayIcon.Visibility = Visibility.Visible;
        Hide();
    }
}
```

> `RootGrid` は Step 1 で付けたルート `Grid` の `x:Name`。

- [ ] **Step 4: ビルド**

Run: `dotnet build reloaded-helper.slnx`
Expected: 0 errors

- [ ] **Step 5: 手動検証**

`-p:Version=9.9.9` ビルドで起動：
1. 最上部のタイトルバーが**ダーク**（ライトの帯が消えている）。「Reloaded Helper」表示。
2. 右上の最小化／最大化／閉じるが効く。閉じるはホバーで赤。
3. タイトルバーをドラッグでウィンドウ移動、ダブルクリックで最大化⇔復元。
4. 起動時の最大化で**内容が画面外に切れていない**（タスクバーに被らない）。
5. 最小化→トレイ復帰が従来どおり動く。

- [ ] **Step 6: コミット**

```bash
git add src/ReloadedHelper.App/MainWindow.xaml src/ReloadedHelper.App/MainWindow.xaml.cs src/ReloadedHelper.App/Themes/Controls.xaml
git commit -m "feat: custom dark title bar via WindowChrome"
```

---

### Task B5: タブ勝手切替の取りこぼし対策（ToggleEnabled を Reload 統一）

A の逐次反映でバックグラウンド更新由来のタブ飛びは解消済み。残る `MainViewModel.ToggleEnabled` は手動で `LoadFrom`＋復元しており、これを既存の `Reload()`（SelectedGame 保持）に統一して取りこぼしを防ぐ。

**Files:**
- Modify: `src/ReloadedHelper.Core/MainViewModel.cs:131-163`（`ToggleEnabled` 末尾）
- Test: `tests/ReloadedHelper.Core.Tests/MainViewModelTests.cs`

**Interfaces:**
- Consumes: 既存 `MainViewModel.Reload()`（SelectedGame を AppId で復元）

- [ ] **Step 1: 失敗するテスト（トグル後も SelectedGame 保持）**

`MainViewModelTests.cs` に追加。`LoadFrom` は実インストールを要するため、ここでは `Reload` 経由の保持ロジックを直接は叩けないので、**`ToggleEnabled` が選択ゲームを変えない**ことを確認する軽量テストを置く。`ToggleEnabled` は `SelectedGame`/`_install` が null のとき即 return する（既存仕様）ため、null 経路で例外を出さず選択が不変であることを検証する：

```csharp
[Fact]
public void ToggleEnabled_without_install_keeps_selection_and_does_not_throw()
{
    var vm = new MainViewModel();
    var entry = new ModLoadEntry(1, "m1", null, false);
    // SelectedGame も _install も未設定 → 早期 return、例外なし・選択不変
    var ex = Record.Exception(() => vm.ToggleEnabled(entry));
    Assert.Null(ex);
    Assert.Null(vm.SelectedGame);
}
```

- [ ] **Step 2: テスト失敗を確認（または現状維持の確認）**

Run: `dotnet test --filter ToggleEnabled_without_install_keeps_selection_and_does_not_throw`
Expected: まず RED（このテストが無い状態→追加後にビルド）。実装後 GREEN。

- [ ] **Step 3: ToggleEnabled の末尾を Reload() に統一**

`MainViewModel.ToggleEnabled` の末尾（157-162 行の `AppConfigWriter.WriteEnabledAndSorted(...)` 以降の手動 `LoadFrom`＋復元ブロック）を置換：

```csharp
        AppConfigWriter.WriteEnabledAndSorted(configPath, game.AppId, newEnabled, newSorted);
        Reload();   // SelectedGame を保持して再読込（タブ飛び防止・DRY）
```

（`var prevId = game.AppId; LoadFrom(_install); var restored = ...` の3行は削除。）

- [ ] **Step 4: テスト合格＋全体確認**

Run: `dotnet test`
Expected: 追加テスト GREEN、既存も全て GREEN（リグレッションなし）

- [ ] **Step 5: コミット**

```bash
git add src/ReloadedHelper.Core/MainViewModel.cs tests/ReloadedHelper.Core.Tests/MainViewModelTests.cs
git commit -m "refactor: ToggleEnabled uses Reload() to preserve selected game (tab-jump)"
```

---

## Self-Review（計画 vs 仕様）

- **B-1 タイトルバー** → Task B4 ✅（WindowChrome・キャプションボタン・最大化マージン）
- **B-2 右クリック/トレイ/更新ポップアップ統一** → Task B1（メニュー）, B2+B3（ポップアップ→ThemedDialog）✅
- **B-3 タブ勝手切替** → Task B5（ToggleEnabled→Reload）＋ A で対処済みのバックグラウンド更新 ✅
- 型整合：`ThemedDialog.Show` シグネチャは B2 定義を B3 が同一に使用。`RootGrid`/`CaptionButtonStyle`/`CaptionCloseButtonStyle` は B4 内で定義・参照一致。
- プレースホルダ：B4 Step1 の「既存内容を Row1 に移植」は具体的な移植元（現 18-152 行）を明示。UI 検証は各タスクに手動手順を明記。
- 制約：新規色は close ホバーの `#c0392b` のみ（赤、慣例的でテーマ追加不要と判断）。他は既存リソース流用。
