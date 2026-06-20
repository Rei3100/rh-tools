// src/ReloadedHelper.App/MainWindow.xaml.cs
using System.Windows;
using H.NotifyIcon;
using ReloadedHelper.Core;

namespace ReloadedHelper.App;

public partial class MainWindow : Window
{
    private readonly ShellViewModel _shell;
    private readonly TaskbarIcon    _tray;

    public MainWindow(ShellViewModel shell)
    {
        _shell     = shell;
        DataContext = shell;
        InitializeComponent();

        _tray = BuildTrayIcon();
    }

    // ── サイドナビ ──
    private void NavModList_Click(object sender, RoutedEventArgs e)  => _shell.ShowModList();
    private void NavSettings_Click(object sender, RoutedEventArgs e) => _shell.ShowSettings();
    private void NavHelp_Click(object sender, RoutedEventArgs e)     => _shell.ShowHelp();

    // ── 最小化 → トレイ ──
    private void Window_StateChanged(object sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized && _shell.SettingsVm.MinimizeToTray)
        {
            ShowInTaskbar    = false;
            _tray.Visibility = Visibility.Visible;
            Hide();
        }
    }

    private void RestoreWindow()
    {
        Show();
        WindowState   = WindowState.Normal;
        ShowInTaskbar = true;
        Activate();
    }

    // ── 終了 ──
    private void Window_Closed(object sender, EventArgs e)
    {
        _tray.Dispose();
    }

    // ── トレイアイコン構築 ──
    private TaskbarIcon BuildTrayIcon()
    {
        var icon = new TaskbarIcon
        {
            IconSource   = new System.Windows.Media.Imaging.BitmapImage(
                               new Uri("pack://application:,,,/Assets/app.ico")),
            ToolTipText  = "Reloaded Helper",
            Visibility   = Visibility.Collapsed,
        };

        var menu       = new System.Windows.Controls.ContextMenu();
        var itemShow   = new System.Windows.Controls.MenuItem { Header = "表示" };
        var itemExit   = new System.Windows.Controls.MenuItem { Header = "終了" };
        itemShow.Click += (_, _) => RestoreWindow();
        itemExit.Click += (_, _) => Application.Current.Shutdown();
        menu.Items.Add(itemShow);
        menu.Items.Add(itemExit);
        icon.ContextMenu = menu;

        icon.TrayLeftMouseDown += (_, _) => RestoreWindow();

        return icon;
    }
}
