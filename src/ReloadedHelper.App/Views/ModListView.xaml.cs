// src/ReloadedHelper.App/Views/ModListView.xaml.cs
using System.Linq;
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

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm && !vm.IsUpdating && vm.RefreshAction is { } action)
            await action();
    }

    // URL ハイパーリンク（既存の動作を維持）
    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = e.Uri.AbsoluteUri,
            UseShellExecute = true
        });
        e.Handled = true;
    }

    // ── 複数選択 ──
    private void ModList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var count = ModListBox.SelectedItems.Count;
        RefreshSelectedButton.Content = $"選択中を更新 ({count}件)";
        RefreshSelectedButton.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void RefreshSelected_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        if (vm.RefreshSelectedAction is null) return;

        var ids = ModListBox.SelectedItems
            .OfType<ModLoadEntry>()
            .Select(entry => entry.ModId)
            .ToList();

        if (ids.Count == 0) return;
        await vm.RefreshSelectedAction(ids);
    }

    // ── 「…」ボタン ──
    private void EditButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is ModLoadEntry entry)
            OpenEditWindow(entry);
    }

    // ── 右クリックメニュー ──
    private void EditMenu_Click(object sender, RoutedEventArgs e)
    {
        if (GetContextMenuEntry(sender) is { } entry) OpenEditWindow(entry);
    }

    private async void RefreshMenu_Click(object sender, RoutedEventArgs e)
    {
        if (GetContextMenuEntry(sender) is not { } entry) return;
        if (DataContext is not MainViewModel vm) return;
        if (vm.RefreshSelectedAction is null) return;
        await vm.RefreshSelectedAction(new[] { entry.ModId });
    }

    private void DeleteMenu_Click(object sender, RoutedEventArgs e)
    {
        if (GetContextMenuEntry(sender) is { } entry)
            OpenEditWindow(entry);
    }

    private void OpenEditWindow(ModLoadEntry entry)
    {
        if (DataContext is not MainViewModel vm) return;
        var win = new ModEditWindow(entry, vm);
        win.Owner = Window.GetWindow(this);
        win.ShowDialog();
    }

    private static ModLoadEntry? GetContextMenuEntry(object sender)
    {
        if (sender is MenuItem item &&
            item.Parent is ContextMenu cm &&
            cm.PlacementTarget is Grid grid &&
            grid.DataContext is ModLoadEntry entry)
            return entry;
        return null;
    }
}
