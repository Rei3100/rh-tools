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
