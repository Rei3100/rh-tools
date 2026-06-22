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
