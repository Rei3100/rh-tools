using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using ReloadedHelper.Core;

namespace ReloadedHelper.App.Views;

public partial class DiagnosticsWindow : Window
{
    public sealed record Row(string Title, string Message, Brush TitleBrush);

    public sealed record HistoryRow(string Label, string Detail);

    private readonly ObservableCollection<Row> _rows = new();
    private readonly ObservableCollection<HistoryRow> _historyRows = new();
    private readonly GameInfo _game;
    private readonly IReadOnlyDictionary<string, ModInfo> _catalog;
    private readonly AutoSortCoordinator _coordinator;

    public DiagnosticsWindow(
        GameInfo game,
        IReadOnlyDictionary<string, ModInfo> catalog,
        AutoSortCoordinator coordinator)
    {
        _game = game;
        _catalog = catalog;
        _coordinator = coordinator;
        InitializeComponent();
        HeaderText.Text = $"{game.DisplayName} の診断";
        FindingsList.ItemsSource = _rows;
        HistoryList.ItemsSource = _historyRows;
        LoadHistory();
        _ = RunAsync();
    }

    private void LoadHistory()
    {
        _historyRows.Clear();
        foreach (var h in _coordinator.History)
        {
            var triggerLabel = h.Trigger switch
            {
                AutoSortTrigger.Startup => "起動時",
                AutoSortTrigger.ToggleEnable => "有効/無効切替",
                AutoSortTrigger.Delete => "MOD削除",
                AutoSortTrigger.ForcedRefresh => "手動更新",
                _ => h.Trigger.ToString(),
            };
            var label = $"{h.At:MM/dd HH:mm:ss}　{triggerLabel}";
            var detail = h.Reasons.Count > 0
                ? string.Join("、", h.Reasons.Select(r => r.MovedModId))
                : "変更なし";
            _historyRows.Add(new HistoryRow(label, detail));
        }
        if (_historyRows.Count == 0)
            _historyRows.Add(new HistoryRow("履歴なし", "まだ自動並び替えは実行されていません。"));
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

    private void RestoreButton_Click(object sender, RoutedEventArgs e)
    {
        var backups = LoadOrderBackupService.ListBackups(_game.AppId);
        if (backups.Count == 0)
        {
            MessageBox.Show("バックアップが見つかりませんでした。", "復元", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var configPath = Path.Combine(_game.FolderPath, "AppConfig.json");
        if (!File.Exists(configPath))
        {
            MessageBox.Show("設定ファイルが見つかりません。", "復元", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var latest = backups[0]; // ListBackups は降順
        var stamp = Path.GetFileNameWithoutExtension(latest);
        var msg = $"最新バックアップ（{stamp}）に復元しますか？";
        if (MessageBox.Show(msg, "元に戻す", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            LoadOrderBackupService.Restore(latest, configPath);
            MessageBox.Show("復元しました。アプリを再起動するか、再読込を行ってください。", "復元完了", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
