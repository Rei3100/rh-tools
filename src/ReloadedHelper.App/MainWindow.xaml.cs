// src/ReloadedHelper.App/MainWindow.xaml.cs
using System.Windows;
using ReloadedHelper.Core;

namespace ReloadedHelper.App;

public partial class MainWindow : Window
{
    private readonly ShellViewModel _shell;

    public MainWindow(ShellViewModel shell)
    {
        _shell = shell;
        DataContext = shell;
        InitializeComponent();
    }

    // ── サイドナビ ──
    private void NavModList_Click(object sender, RoutedEventArgs e) => _shell.ShowModList();
    private void NavSettings_Click(object sender, RoutedEventArgs e) => _shell.ShowSettings();
    private void NavHelp_Click(object sender, RoutedEventArgs e) => _shell.ShowHelp();

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

    // ── タイトルバー操作 ──
    private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2) { ToggleMaximize(); return; }
        if (e.ButtonState == System.Windows.Input.MouseButtonState.Pressed) DragMove();
    }

    private void MinButton_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState.Minimized;
    private void MaxButton_Click(object sender, RoutedEventArgs e) => ToggleMaximize();
    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void ToggleMaximize()
        => WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal : WindowState.Maximized;

    // ── 最小化 → トレイ ──
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

    private void RestoreWindow()
    {
        Show();
        WindowState = WindowState.Maximized;
        ShowInTaskbar = true;
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
