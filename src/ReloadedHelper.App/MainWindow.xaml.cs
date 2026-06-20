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
        RoutedEventArgs e) => RestoreWindow();
    private void TrayShow_Click(object sender, RoutedEventArgs e) => RestoreWindow();
    private void TrayExit_Click(object sender, RoutedEventArgs e)
        => Application.Current.Shutdown();

    // ── 終了 ──
    private void Window_Closed(object sender, EventArgs e)
    {
        TrayIcon.Dispose();
    }
}
