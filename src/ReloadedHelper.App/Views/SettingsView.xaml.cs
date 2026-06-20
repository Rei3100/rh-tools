// src/ReloadedHelper.App/Views/SettingsView.xaml.cs
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using ReloadedHelper.Core;

namespace ReloadedHelper.App.Views;

public partial class SettingsView : UserControl
{
    public SettingsView() => InitializeComponent();

    private SettingsViewModel Vm => (SettingsViewModel)DataContext;

    private void ResetZoom_Click(object sender, RoutedEventArgs e)
        => Vm.UiZoomPercent = 100;

    private void ChangeFolder_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Reloaded-II のフォルダを選んでください" };
        if (dlg.ShowDialog() != true) return;
        var install = new ReloadedInstall(dlg.FolderName);
        if (!install.IsValid)
        {
            MessageBox.Show("そのフォルダに Mods と Apps が見つかりません。Reloaded-II 本体のフォルダを選んでください。");
            return;
        }
        Vm.ReloadedInstallPath = install.RootPath;
    }
}
