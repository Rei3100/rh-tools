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
            FileName        = e.Uri.AbsoluteUri,
            UseShellExecute = true
        });
        e.Handled = true;
    }
}
