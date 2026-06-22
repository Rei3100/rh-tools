# セクションC：MOD個別コンフィグ設定 実装計画

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 本体側に設定項目があるMOD（`User/Mods/<ModId>/Config.json`）を、ヘルパー側からダークなフォームで読み書きできるようにする。項目名は機械翻訳のベストエフォートで日本語化（キャッシュ）。

**Architecture:** Core 層に Config.json の型安全な読み書き（`ModConfigStore`）とキー名整形（`ConfigLabel`）を実装。App 層は型に応じた入力欄を動的生成する `ModConfigWindow` を追加し、右クリック／MOD編集画面から開く。ラベルは既存 `TranslationService`（無料Google翻訳）で best-effort 翻訳し、`ModUserData` にキャッシュ。

**Tech Stack:** C# / .NET 10 / WPF、System.Text.Json、xUnit。フォーム生成はコードビハインドで動的構築。

## Global Constraints

- ランタイム NuGet 追加禁止（System.Text.Json のみ）。テスト用 xUnit は可。
- ユーザーデータは %APPDATA%\ReloadedHelper 以外に保存禁止（Config.json は Reloaded 配下＝MOD設定の実体なので対象外。ラベルキャッシュは userdata.json に保存）。
- 設定の読み書きで **JSON の型と他フィールドを必ず保持**（bool/整数/小数/文字列の別、未対応のネスト要素やキー順を壊さない）。
- 実行時に外部 AI を呼ばない（翻訳は Google 無料エンドポイントのみ、失敗時は英語整形ラベルにフォールバック）。
- 新規 UI は既存テーマ流用：`BgMainBrush`/`BgInputBrush`/`BorderInputBrush`/`AccentBrush`/`TextBodyBrush`/`TextLabelBrush`/`MainFont`、フォントサイズ `FontSizeLabel`/`FontSizeCardBody`。
- 設定の意味（各項目の説明）は本体DLL内のため取得不可。**項目名の機械翻訳はベストエフォート**で、外れても許容（README/保証なし）。

---

## ファイル構成

| 区分 | パス | 役割 |
|------|------|------|
| 変更 | `src/ReloadedHelper.Core/ReloadedInstall.cs` | `UserModsDir` 追加 |
| 新規 | `src/ReloadedHelper.Core/ModConfigStore.cs` | Config.json の型安全 読み/書き＋パス解決 |
| 新規 | `src/ReloadedHelper.Core/ConfigLabel.cs` | キー名を人間可読に整形（camelCase/snake 分割） |
| 変更 | `src/ReloadedHelper.Core/UserData.cs` | `ModUserData.ConfigLabels`（翻訳ラベルのキャッシュ） |
| 新規 | `src/ReloadedHelper.App/Views/ModConfigWindow.xaml`(.cs) | 型別フォームの動的生成・保存・ラベル翻訳 |
| 変更 | `src/ReloadedHelper.App/Themes/Controls.xaml` | クリック可能トグル `ToggleSwitchEditableStyle` |
| 変更 | `src/ReloadedHelper.App/Views/ModListView.xaml`(.cs) | 右クリック「設定...」追加 |
| 変更 | `src/ReloadedHelper.App/Views/ModEditWindow.xaml`(.cs) | 「設定」ボタン追加（Config.json がある時のみ） |
| 新規 | `tests/ReloadedHelper.Core.Tests/ModConfigStoreTests.cs` ほか | 各 Core クラスのテスト |

---

### Task C1: ReloadedInstall.UserModsDir ＋ ModConfigStore.Read

**Files:**
- Modify: `src/ReloadedHelper.Core/ReloadedInstall.cs`
- Create: `src/ReloadedHelper.Core/ModConfigStore.cs`
- Test: `tests/ReloadedHelper.Core.Tests/ModConfigStoreTests.cs`

**Interfaces:**
- Produces:
```csharp
public enum ConfigFieldKind { Bool, Number, Text }
public sealed record ModConfigField(string Name, ConfigFieldKind Kind, string Value);
public static class ModConfigStore
{
    public static string PathFor(ReloadedInstall install, string modId); // <UserModsDir>/<modId>/Config.json
    public static bool Exists(ReloadedInstall install, string modId);
    public static IReadOnlyList<ModConfigField> Read(string configPath); // 無い/不正なら空。トップレベルの scalar のみ。Value: bool→"true"/"false"、Number→生トークン、Text→文字列
}
```
- `ReloadedInstall.UserModsDir => Path.Combine(RootPath, "User", "Mods")`

- [ ] **Step 1: 失敗するテスト**

```csharp
using System.IO;
namespace ReloadedHelper.Core.Tests;

public class ModConfigStoreTests
{
    private static string WriteTemp(string json)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"cfg-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "Config.json");
        File.WriteAllText(path, json);
        return path;
    }

    [Fact]
    public void Read_parses_scalar_fields_with_kinds()
    {
        var path = WriteTemp("""
        {"HotReload":false,"LogLevel":"Information","BaseBgmId_P5R":12000,"Volume":0.5}
        """);
        var fields = ModConfigStore.Read(path);

        Assert.Equal(4, fields.Count);
        Assert.Equal(new ModConfigField("HotReload", ConfigFieldKind.Bool, "false"), fields[0]);
        Assert.Equal(new ModConfigField("LogLevel", ConfigFieldKind.Text, "Information"), fields[1]);
        Assert.Equal(new ModConfigField("BaseBgmId_P5R", ConfigFieldKind.Number, "12000"), fields[2]);
        Assert.Equal(new ModConfigField("Volume", ConfigFieldKind.Number, "0.5"), fields[3]);
    }

    [Fact]
    public void Read_returns_empty_for_missing_or_invalid()
    {
        Assert.Empty(ModConfigStore.Read(Path.Combine(Path.GetTempPath(), "nope-Config.json")));
        var bad = WriteTemp("not json");
        Assert.Empty(ModConfigStore.Read(bad));
    }

    [Fact]
    public void Read_skips_nested_objects_and_arrays()
    {
        var path = WriteTemp("""{"A":true,"Nested":{"x":1},"Arr":[1,2],"B":"t"}""");
        var fields = ModConfigStore.Read(path);
        Assert.Equal(new[] { "A", "B" }, fields.Select(f => f.Name).ToArray());
    }
}
```

- [ ] **Step 2: テスト失敗を確認**

Run: `dotnet test --filter ModConfigStore`
Expected: FAIL（型なし）

- [ ] **Step 3: 実装**

`ReloadedInstall.cs` に追加：
```csharp
    public string UserModsDir => Path.Combine(RootPath, "User", "Mods");
```

`ModConfigStore.cs`：
```csharp
using System.IO;
using System.Text.Json;

namespace ReloadedHelper.Core;

public enum ConfigFieldKind { Bool, Number, Text }

public sealed record ModConfigField(string Name, ConfigFieldKind Kind, string Value);

public static class ModConfigStore
{
    public static string PathFor(ReloadedInstall install, string modId) =>
        Path.Combine(install.UserModsDir, modId, "Config.json");

    public static bool Exists(ReloadedInstall install, string modId) =>
        File.Exists(PathFor(install, modId));

    public static IReadOnlyList<ModConfigField> Read(string configPath)
    {
        if (!File.Exists(configPath)) return Array.Empty<ModConfigField>();
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(configPath));
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return Array.Empty<ModConfigField>();

            var result = new List<ModConfigField>();
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                switch (prop.Value.ValueKind)
                {
                    case JsonValueKind.True:
                        result.Add(new ModConfigField(prop.Name, ConfigFieldKind.Bool, "true")); break;
                    case JsonValueKind.False:
                        result.Add(new ModConfigField(prop.Name, ConfigFieldKind.Bool, "false")); break;
                    case JsonValueKind.Number:
                        result.Add(new ModConfigField(prop.Name, ConfigFieldKind.Number, prop.Value.GetRawText())); break;
                    case JsonValueKind.String:
                        result.Add(new ModConfigField(prop.Name, ConfigFieldKind.Text, prop.Value.GetString() ?? "")); break;
                    // Object/Array/Null は対象外（Write で原本保持）
                }
            }
            return result;
        }
        catch (JsonException) { return Array.Empty<ModConfigField>(); }
        catch (IOException) { return Array.Empty<ModConfigField>(); }
    }
}
```

- [ ] **Step 4: テスト合格を確認**

Run: `dotnet test --filter ModConfigStore`
Expected: PASS

- [ ] **Step 5: コミット**

```bash
git add src/ReloadedHelper.Core/ReloadedInstall.cs src/ReloadedHelper.Core/ModConfigStore.cs tests/ReloadedHelper.Core.Tests/ModConfigStoreTests.cs
git commit -m "feat: ModConfigStore.Read + ReloadedInstall.UserModsDir"
```

---

### Task C2: ModConfigStore.Write（型・順序・未対応キー保持）

**Files:**
- Modify: `src/ReloadedHelper.Core/ModConfigStore.cs`
- Test: `tests/ReloadedHelper.Core.Tests/ModConfigStoreTests.cs`

**Interfaces:**
- Produces: `static void ModConfigStore.Write(string configPath, IReadOnlyList<ModConfigField> fields)`
  - 原本のキー順・未対応要素（ネスト/配列）を保持。fields に一致するトップレベル scalar のみ値を上書き。Number は整数なら整数、小数なら小数で書く。原本が無ければ何もしない。

- [ ] **Step 1: 失敗するテスト**

```csharp
[Fact]
public void Write_updates_values_preserving_types_order_and_unmanaged_keys()
{
    var path = WriteTemp("""
    {"HotReload":false,"LogLevel":"Information","BaseBgmId_P5R":12000,"Nested":{"x":1}}
    """);
    ModConfigStore.Write(path, new[]
    {
        new ModConfigField("HotReload", ConfigFieldKind.Bool, "true"),
        new ModConfigField("LogLevel", ConfigFieldKind.Text, "Debug"),
        new ModConfigField("BaseBgmId_P5R", ConfigFieldKind.Number, "13000"),
    });

    using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
    var root = doc.RootElement;
    Assert.True(root.GetProperty("HotReload").GetBoolean());                 // bool 維持
    Assert.Equal("Debug", root.GetProperty("LogLevel").GetString());          // 文字列 維持
    Assert.Equal(13000, root.GetProperty("BaseBgmId_P5R").GetInt32());        // 整数 維持
    Assert.Equal(1, root.GetProperty("Nested").GetProperty("x").GetInt32());  // 未対応キー 保持
    // キー順保持
    Assert.Equal(new[] { "HotReload", "LogLevel", "BaseBgmId_P5R", "Nested" },
        root.EnumerateObject().Select(p => p.Name).ToArray());
}

[Fact]
public void Write_keeps_decimal_numbers_as_decimal()
{
    var path = WriteTemp("""{"Volume":0.5}""");
    ModConfigStore.Write(path, new[] { new ModConfigField("Volume", ConfigFieldKind.Number, "0.8") });
    using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
    Assert.Equal(0.8, doc.RootElement.GetProperty("Volume").GetDouble(), 3);
}

[Fact]
public void Write_does_nothing_when_file_missing()
{
    var path = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.json");
    ModConfigStore.Write(path, new[] { new ModConfigField("A", ConfigFieldKind.Bool, "true") });
    Assert.False(File.Exists(path));
}
```

- [ ] **Step 2: テスト失敗を確認**

Run: `dotnet test --filter Write_`
Expected: FAIL

- [ ] **Step 3: 実装**

`ModConfigStore` に追加：
```csharp
public static void Write(string configPath, IReadOnlyList<ModConfigField> fields)
{
    if (!File.Exists(configPath)) return;
    var byName = fields.ToDictionary(f => f.Name, StringComparer.Ordinal);
    try
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(configPath));
        if (doc.RootElement.ValueKind != JsonValueKind.Object) return;

        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions
        {
            Indented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }))
        {
            w.WriteStartObject();
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (byName.TryGetValue(prop.Name, out var f) && IsScalar(prop.Value.ValueKind))
                {
                    w.WritePropertyName(prop.Name);
                    WriteValue(w, f);
                }
                else
                {
                    prop.WriteTo(w); // 未対応 or fields 未指定 → 原本保持
                }
            }
            w.WriteEndObject();
        }
        File.WriteAllBytes(configPath, ms.ToArray());
    }
    catch (JsonException) { }
    catch (IOException) { }
}

private static bool IsScalar(JsonValueKind k) =>
    k is JsonValueKind.True or JsonValueKind.False or JsonValueKind.Number or JsonValueKind.String;

private static void WriteValue(Utf8JsonWriter w, ModConfigField f)
{
    switch (f.Kind)
    {
        case ConfigFieldKind.Bool:
            w.WriteBooleanValue(bool.TryParse(f.Value, out var b) && b);
            break;
        case ConfigFieldKind.Number:
            if (long.TryParse(f.Value, out var l)) w.WriteNumberValue(l);
            else if (double.TryParse(f.Value, System.Globalization.CultureInfo.InvariantCulture, out var d)) w.WriteNumberValue(d);
            else w.WriteNumberValue(0);
            break;
        default:
            w.WriteStringValue(f.Value);
            break;
    }
}
```

- [ ] **Step 4: テスト合格を確認**

Run: `dotnet test --filter ModConfigStore`
Expected: PASS（C1 のテストも維持）

- [ ] **Step 5: コミット**

```bash
git add src/ReloadedHelper.Core/ModConfigStore.cs tests/ReloadedHelper.Core.Tests/ModConfigStoreTests.cs
git commit -m "feat: ModConfigStore.Write preserves types/order/unmanaged keys"
```

---

### Task C3: ConfigLabel.Humanize（キー名整形）

**Files:**
- Create: `src/ReloadedHelper.Core/ConfigLabel.cs`
- Test: `tests/ReloadedHelper.Core.Tests/ConfigLabelTests.cs`

**Interfaces:**
- Produces: `static string ConfigLabel.Humanize(string key)` — camelCase/PascalCase/snake_case を空白区切りの語に。連続大文字や数字の境界も分割。

- [ ] **Step 1: 失敗するテスト**

```csharp
namespace ReloadedHelper.Core.Tests;

public class ConfigLabelTests
{
    [Theory]
    [InlineData("DisableVictoryBgm", "Disable Victory Bgm")]
    [InlineData("HotReload", "Hot Reload")]
    [InlineData("BaseBgmId_P5R", "Base Bgm Id P5R")]
    [InlineData("log_level", "Log Level")]
    [InlineData("Volume", "Volume")]
    [InlineData("", "")]
    public void Humanize_splits_words(string key, string expected)
    {
        Assert.Equal(expected, ConfigLabel.Humanize(key));
    }
}
```

- [ ] **Step 2: テスト失敗を確認**

Run: `dotnet test --filter ConfigLabel`
Expected: FAIL

- [ ] **Step 3: 実装**

```csharp
using System.Text;
using System.Text.RegularExpressions;

namespace ReloadedHelper.Core;

public static class ConfigLabel
{
    public static string Humanize(string key)
    {
        if (string.IsNullOrEmpty(key)) return "";
        // アンダースコアを空白に
        var s = key.Replace('_', ' ');
        // camelCase / PascalCase / 数字境界で空白挿入
        s = Regex.Replace(s, "([a-z0-9])([A-Z])", "$1 $2");
        s = Regex.Replace(s, "([a-z])([0-9])", "$1 $2"); // 小文字→数字のみ分割（P5R 等の型番は割らない）
        // 連続空白を1つに、各語を先頭大文字化
        var words = s.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var sb = new StringBuilder();
        for (int i = 0; i < words.Length; i++)
        {
            if (i > 0) sb.Append(' ');
            var w = words[i];
            sb.Append(char.ToUpperInvariant(w[0]));
            sb.Append(w.Length > 1 ? w[1..] : "");
        }
        return sb.ToString();
    }
}
```

> 注: 数字分割を `([a-z])([0-9])`（小文字の直後の数字のみ）に限定しているため、`P5R` のような型番は割らず保持される。`BaseBgmId_P5R` → `Base Bgm Id P5R`。

- [ ] **Step 4: テスト合格を確認**

Run: `dotnet test --filter ConfigLabel`
Expected: PASS（必要なら Step3 注記どおり数字境界の正規表現を `([a-z])([0-9])` に調整）

- [ ] **Step 5: コミット**

```bash
git add src/ReloadedHelper.Core/ConfigLabel.cs tests/ReloadedHelper.Core.Tests/ConfigLabelTests.cs
git commit -m "feat: ConfigLabel.Humanize for readable config key labels"
```

---

### Task C4: ModUserData.ConfigLabels（翻訳ラベルのキャッシュ）

**Files:**
- Modify: `src/ReloadedHelper.Core/UserData.cs`
- Test: `tests/ReloadedHelper.Core.Tests/UserDataTests.cs`

**Interfaces:**
- Produces: `ModUserData.ConfigLabels`（`Dictionary<string,string>?`、キー=Config の項目名、値=日本語ラベル）

- [ ] **Step 1: 失敗するテスト**

`UserDataTests.cs` に追加：
```csharp
[Fact]
public void Roundtrip_preserves_config_labels()
{
    var path = Path.Combine(Path.GetTempPath(), $"ud-{Guid.NewGuid():N}.json");
    try
    {
        var file = new UserDataFile();
        file.Mods["m1"] = new ModUserData
        {
            ConfigLabels = new Dictionary<string, string> { ["DisableVictoryBgm"] = "勝利BGMを無効化" }
        };
        UserDataStore.Save(path, file);
        var loaded = UserDataStore.Load(path);
        Assert.Equal("勝利BGMを無効化", loaded.Mods["m1"].ConfigLabels!["DisableVictoryBgm"]);
    }
    finally { if (File.Exists(path)) File.Delete(path); }
}
```

- [ ] **Step 2: テスト失敗を確認**

Run: `dotnet test --filter Roundtrip_preserves_config_labels`
Expected: FAIL

- [ ] **Step 3: フィールド追加**

`ModUserData` に追加：
```csharp
    // セクションC 追加
    public Dictionary<string, string>? ConfigLabels { get; set; }
```

- [ ] **Step 4: テスト合格を確認**

Run: `dotnet test --filter Roundtrip_preserves_config_labels`
Expected: PASS

- [ ] **Step 5: コミット**

```bash
git add src/ReloadedHelper.Core/UserData.cs tests/ReloadedHelper.Core.Tests/UserDataTests.cs
git commit -m "feat: add ModUserData.ConfigLabels cache"
```

---

### Task C5: ModConfigWindow（型別フォームの動的生成・翻訳ラベル・保存）

**Files:**
- Create: `src/ReloadedHelper.App/Views/ModConfigWindow.xaml`
- Create: `src/ReloadedHelper.App/Views/ModConfigWindow.xaml.cs`
- Modify: `src/ReloadedHelper.App/Themes/Controls.xaml`（クリック可能トグル追加）

**Interfaces:**
- Consumes: `ModConfigStore`(C1/C2), `ConfigLabel.Humanize`(C3), `ModUserData.ConfigLabels`(C4), `TranslationService`（既存）, `ReloadedInstall`, `ModLoadEntry`
- Produces: `public ModConfigWindow(string modId, string displayName, ReloadedInstall install)`

> UI のため手動検証。

- [ ] **Step 1: クリック可能トグルスタイルを追加**

`Controls.xaml` 末尾（`</ResourceDictionary>` 直前）に追加：
```xml
    <!-- ── 編集用トグル（クリック可能） ── -->
    <Style x:Key="ToggleSwitchEditableStyle" TargetType="ToggleButton"
           BasedOn="{StaticResource ToggleSwitchStyle}">
        <Setter Property="IsHitTestVisible" Value="True"/>
        <Setter Property="Focusable" Value="True"/>
        <Setter Property="Cursor" Value="Hand"/>
    </Style>
```

- [ ] **Step 2: ModConfigWindow.xaml を作成**

```xml
<Window x:Class="ReloadedHelper.App.Views.ModConfigWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="MOD 設定" Width="460" Height="560"
        WindowStartupLocation="CenterOwner"
        Background="{DynamicResource BgMainBrush}"
        FontFamily="{DynamicResource MainFont}">
    <Grid Margin="20">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>

        <TextBlock x:Name="HeaderText" Grid.Row="0" FontSize="16" FontWeight="Bold"
                   Foreground="{DynamicResource TextPrimaryBrush}" Margin="0,0,0,12"
                   TextWrapping="Wrap"/>

        <ScrollViewer Grid.Row="1" VerticalScrollBarVisibility="Auto">
            <StackPanel x:Name="FieldsPanel"/>
        </ScrollViewer>

        <StackPanel Grid.Row="2" Orientation="Horizontal" HorizontalAlignment="Right" Margin="0,16,0,0">
            <Button x:Name="SaveButton" Content="保存" Click="Save_Click"
                    Padding="14,7" Margin="0,0,8,0"
                    Background="{DynamicResource AccentBrush}" Foreground="White"
                    BorderThickness="0" Cursor="Hand"/>
            <Button Content="キャンセル" Click="Cancel_Click" Padding="14,7"
                    Background="{DynamicResource BgInputBrush}"
                    Foreground="{DynamicResource TextBodyBrush}"
                    BorderBrush="{DynamicResource BorderInputBrush}" BorderThickness="1" Cursor="Hand"/>
        </StackPanel>
    </Grid>
</Window>
```

- [ ] **Step 3: ModConfigWindow.xaml.cs を作成**

```csharp
using System.Collections.Generic;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using ReloadedHelper.Core;

namespace ReloadedHelper.App.Views;

public partial class ModConfigWindow : Window
{
    private readonly string _modId;
    private readonly string _configPath;
    private readonly List<(ModConfigField Field, FrameworkElement Editor)> _editors = new();

    public ModConfigWindow(string modId, string displayName, ReloadedInstall install)
    {
        _modId = modId;
        _configPath = ModConfigStore.PathFor(install, modId);
        InitializeComponent();
        HeaderText.Text = $"{displayName} の設定";
        BuildForm();
    }

    private void BuildForm()
    {
        var fields = ModConfigStore.Read(_configPath);
        var ud = UserDataStore.Load(UserDataStore.DefaultPath);
        ud.Mods.TryGetValue(_modId, out var data);
        var labels = data?.ConfigLabels;

        if (fields.Count == 0)
        {
            FieldsPanel.Children.Add(new TextBlock
            {
                Text = "このMODには編集できる設定がありません。",
                Foreground = (System.Windows.Media.Brush)FindResource("TextLabelBrush"),
                Margin = new Thickness(0, 8, 0, 0),
            });
            SaveButton.IsEnabled = false;
            return;
        }

        foreach (var f in fields)
        {
            var label = labels is not null && labels.TryGetValue(f.Name, out var cached)
                ? cached : ConfigLabel.Humanize(f.Name);

            var lbl = new TextBlock
            {
                Text = label,
                Foreground = (System.Windows.Media.Brush)FindResource("TextLabelBrush"),
                Margin = new Thickness(0, 10, 0, 4),
            };
            lbl.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeLabel");
            FieldsPanel.Children.Add(lbl);

            FrameworkElement editor = f.Kind switch
            {
                ConfigFieldKind.Bool => MakeToggle(f.Value == "true"),
                _ => MakeTextBox(f.Value),
            };
            FieldsPanel.Children.Add(editor);
            _editors.Add((f, editor));
        }

        // ラベルをベストエフォート翻訳（キャッシュが無い項目のみ）
        _ = TranslateLabelsAsync(fields, ud, data);
    }

    private ToggleButton MakeToggle(bool isOn)
    {
        var t = new ToggleButton { IsChecked = isOn, HorizontalAlignment = HorizontalAlignment.Left };
        t.SetResourceReference(StyleProperty, "ToggleSwitchEditableStyle");
        return t;
    }

    private TextBox MakeTextBox(string value)
    {
        var tb = new TextBox { Text = value, Padding = new Thickness(8, 6, 8, 6) };
        return tb; // 既定の TextBox スタイル（ダーク）が適用される
    }

    private async System.Threading.Tasks.Task TranslateLabelsAsync(
        IReadOnlyList<ModConfigField> fields, UserDataFile ud, ModUserData? data)
    {
        var missing = new List<ModConfigField>();
        var existing = data?.ConfigLabels;
        foreach (var f in fields)
            if (existing is null || !existing.ContainsKey(f.Name)) missing.Add(f);
        if (missing.Count == 0) return;

        try
        {
            using var http = new HttpClient();
            var tr = new TranslationService(http);
            data ??= new ModUserData();
            data.ConfigLabels ??= new Dictionary<string, string>();

            for (int i = 0; i < missing.Count; i++)
            {
                var f = missing[i];
                var en = ConfigLabel.Humanize(f.Name);
                var ja = await tr.TranslateAsync(en, "ja");
                data.ConfigLabels[f.Name] = ja;
                // 対応する画面ラベルを更新（FieldsPanel 内の TextBlock は editor の直前）
                int idx = fields.ToList().FindIndex(x => x.Name == f.Name);
                if (idx >= 0)
                {
                    var labelElement = FieldsPanel.Children[idx * 2] as TextBlock;
                    if (labelElement is not null)
                        Dispatcher.Invoke(() => labelElement.Text = ja);
                }
            }
            ud.Mods[_modId] = data;
            UserDataStore.Save(UserDataStore.DefaultPath, ud);
        }
        catch { /* 翻訳失敗は無視（英語ラベルのまま） */ }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var updated = new List<ModConfigField>();
        foreach (var (field, editor) in _editors)
        {
            string value = editor switch
            {
                ToggleButton t => (t.IsChecked == true) ? "true" : "false",
                TextBox tb => tb.Text,
                _ => field.Value,
            };
            updated.Add(field with { Value = value });
        }
        ModConfigStore.Write(_configPath, updated);
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
}
```

> 補足: ラベルとエディタは `FieldsPanel` に「ラベル, エディタ, ラベル, エディタ…」の順で追加されるため、`fields[idx]` のラベルは `Children[idx*2]`。空設定時は Save 無効。

- [ ] **Step 4: ビルド**

Run: `dotnet build reloaded-helper.slnx`
Expected: 0 errors

- [ ] **Step 5: コミット**

```bash
git add src/ReloadedHelper.App/Views/ModConfigWindow.xaml src/ReloadedHelper.App/Views/ModConfigWindow.xaml.cs src/ReloadedHelper.App/Themes/Controls.xaml
git commit -m "feat: ModConfigWindow — dynamic typed form for mod Config.json"
```

---

### Task C6: 開く導線（右クリック「設定...」＋MOD編集の「設定」ボタン）

**Files:**
- Modify: `src/ReloadedHelper.App/Views/ModListView.xaml`（ContextMenu に項目追加）
- Modify: `src/ReloadedHelper.App/Views/ModListView.xaml.cs`（ハンドラ＋install 参照）
- Modify: `src/ReloadedHelper.App/Views/ModEditWindow.xaml`（「設定」ボタン）
- Modify: `src/ReloadedHelper.App/Views/ModEditWindow.xaml.cs`（ハンドラ）
- Modify: `src/ReloadedHelper.Core/MainViewModel.cs`（`Install` 公開 read-only プロパティ）

**Interfaces:**
- Consumes: `ModConfigWindow`(C5), `ModConfigStore.Exists`(C1)
- Produces: `MainViewModel.Install`（`ReloadedInstall?` read-only。`_install` を公開）

- [ ] **Step 1: MainViewModel に Install を公開**

`MainViewModel` に追加：
```csharp
public ReloadedInstall? Install => _install;
```

- [ ] **Step 2: 右クリックメニューに「設定...」を追加**

`ModListView.xaml` の ContextMenu（298-303 行）に項目を追加（「編集...」の次あたり）：
```xml
<MenuItem Header="設定..." Click="ConfigMenu_Click"/>
```

`ModListView.xaml.cs` にハンドラを追加（既存 `GetContextMenuEntry` を利用）：
```csharp
private void ConfigMenu_Click(object sender, RoutedEventArgs e)
{
    if (GetContextMenuEntry(sender) is not { } entry) return;
    if (DataContext is not MainViewModel vm || vm.Install is not { } install) return;
    if (!ModConfigStore.Exists(install, entry.ModId))
    {
        ThemedDialog.Show(Window.GetWindow(this), "MOD 設定",
            "このMODには編集できる設定（Config.json）がありません。");
        return;
    }
    var win = new ModConfigWindow(entry.ModId, entry.DisplayName, install) { Owner = Window.GetWindow(this) };
    win.ShowDialog();
}
```
（`using ReloadedHelper.Core;` は既存。`ThemedDialog` は `ReloadedHelper.App` 名前空間＝Section B で追加済み。必要なら `using ReloadedHelper.App;`。）

- [ ] **Step 3: MOD編集ウィンドウに「設定」ボタンを追加**

`ModEditWindow.xaml` のボタン行（60-77 行の `StackPanel`）の先頭に追加：
```xml
<Button x:Name="BtnConfig" Content="設定" Click="BtnConfig_Click"
        Padding="12,6" Margin="0,0,8,0"
        Background="{DynamicResource BgInputBrush}"
        Foreground="{DynamicResource TextBodyBrush}"
        BorderBrush="{DynamicResource BorderInputBrush}" BorderThickness="1" Cursor="Hand"/>
```

`ModEditWindow.xaml.cs`：`LoadFields()` 末尾で Config.json の有無に応じてボタン表示を切替、ハンドラ追加：
```csharp
// LoadFields() 末尾に追加
BtnConfig.Visibility =
    (_vm.Install is { } inst && ModConfigStore.Exists(inst, _entry.ModId))
        ? Visibility.Visible : Visibility.Collapsed;
```
```csharp
private void BtnConfig_Click(object sender, RoutedEventArgs e)
{
    if (_vm.Install is not { } install) return;
    var win = new ModConfigWindow(_entry.ModId, _entry.DisplayName, install) { Owner = this };
    win.ShowDialog();
}
```

- [ ] **Step 4: ビルド**

Run: `dotnet build reloaded-helper.slnx`
Expected: 0 errors

- [ ] **Step 5: 手動検証**

`-p:Version=9.9.9` ビルドで起動：
1. 設定を持つMOD（例 `BGME.BattleThemes`・`BGME.Framework`・`Black Hair Futaba`）を右クリック →「設定...」→ ダークなフォームが開く。
2. bool 項目はトグル、数値項目は数値欄、文字列は入力欄。ラベルが（少し待つと）日本語に変わる。
3. 値を変えて「保存」→ 閉じる。`User/Mods/<MOD>/Config.json` を開くと**型を保ったまま**値が更新されている。MOD編集画面の「設定」ボタンからも同様に開ける。
4. 設定が無いMODを右クリック →「設定...」→「設定がありません」のThemedDialog（または編集画面に設定ボタンが出ない）。

- [ ] **Step 6: コミット**

```bash
git add src/ReloadedHelper.App/Views/ModListView.xaml src/ReloadedHelper.App/Views/ModListView.xaml.cs src/ReloadedHelper.App/Views/ModEditWindow.xaml src/ReloadedHelper.App/Views/ModEditWindow.xaml.cs src/ReloadedHelper.Core/MainViewModel.cs
git commit -m "feat: open ModConfigWindow from right-click and edit window"
```

---

## Self-Review（計画 vs 仕様）

- **C-1 自動フォーム化** → Task C1（Read）, C2（Write）, C5（型別コントロール生成）✅
- **C-2 開き方（編集画面／右クリック）** → Task C6 ✅
- **C-3 日本語ラベルのベストエフォート＋限界** → Task C3（整形）, C4（キャッシュ）, C5（翻訳）✅。失敗時は英語整形ラベルにフォールバック。
- 型整合：`ModConfigField`/`ConfigFieldKind`/`ModConfigStore.Read/Write/Exists/PathFor`/`MainViewModel.Install`/`ModConfigWindow` ctor は全タスクで一貫。
- 型保持：C2 が bool/整数/小数/文字列と未対応キー・順序を保持（テストで担保）。
- プレースホルダ：C3 の数字境界（`P5R`）について実装調整の指針を明記。C5/C6 の UI は手動検証手順を明記。
- 制約：新規色なし。翻訳は既存 `TranslationService`（無料・実行時AI非依存）。設定意味の限界を明記。
