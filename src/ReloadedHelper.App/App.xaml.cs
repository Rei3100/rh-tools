// src/ReloadedHelper.App/App.xaml.cs
using System.Linq;
using System.Windows;
using Microsoft.Win32;
using ReloadedHelper.Core;

namespace ReloadedHelper.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var settingsPath = SettingsStore.DefaultPath;
        var settingsVm   = new SettingsViewModel(settingsPath);

        var install = ResolveInstall(settingsVm);
        if (install is null) { Shutdown(); return; }

        settingsVm.ReloadedInstallPath = install.RootPath;

        var modListVm = new MainViewModel();
        modListVm.LoadFrom(install);

        // RememberLastGame: 前回のゲームを復元
        if (settingsVm.RememberLastGame && settingsVm.LastGameId is { } lastId)
        {
            var last = modListVm.Games.FirstOrDefault(g => g.AppId == lastId);
            if (last is not null) modListVm.SelectedGame = last;
        }

        // SelectedGame が変わったら LastGameId を保存
        modListVm.PropertyChanged += (_, ev) =>
        {
            if (ev.PropertyName == nameof(MainViewModel.SelectedGame) && settingsVm.RememberLastGame)
                settingsVm.LastGameId = modListVm.SelectedGame?.AppId;
        };

        var shell = new ShellViewModel(modListVm, settingsVm);
        new MainWindow(shell).Show();
    }

    private static ReloadedInstall? ResolveInstall(SettingsViewModel sv)
    {
        if (!string.IsNullOrEmpty(sv.ReloadedInstallPath))
        {
            var saved = new ReloadedInstall(sv.ReloadedInstallPath);
            if (saved.IsValid) return saved;
        }
        while (true)
        {
            var dlg = new OpenFolderDialog
            {
                Title = "Reloaded-II のフォルダを選んでください"
            };
            if (dlg.ShowDialog() != true) return null;
            var picked = new ReloadedInstall(dlg.FolderName);
            if (picked.IsValid) return picked;
            MessageBox.Show("そのフォルダに Mods と Apps が見つかりません。Reloaded-II 本体のフォルダを選んでください。");
        }
    }
}
