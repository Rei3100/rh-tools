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
        var settings = SettingsStore.Load(settingsPath);

        var install = ResolveInstall(settings);
        if (install is null) { Shutdown(); return; }

        settings.ReloadedInstallPath = install.RootPath;
        SettingsStore.Save(settingsPath, settings);

        var vm = new MainViewModel();
        vm.LoadFrom(install);
        new MainWindow(vm).Show();
    }

    private static ReloadedInstall? ResolveInstall(AppSettings settings)
    {
        if (!string.IsNullOrEmpty(settings.ReloadedInstallPath))
        {
            var saved = new ReloadedInstall(settings.ReloadedInstallPath);
            if (saved.IsValid) return saved;
        }
        while (true)
        {
            var dlg = new OpenFolderDialog
            {
                Title = "Reloaded-II のフォルダを選んでください (Reloaded-II.exe がある場所)"
            };
            if (dlg.ShowDialog() != true) return null;
            var picked = new ReloadedInstall(dlg.FolderName);
            if (picked.IsValid) return picked;
            MessageBox.Show("そのフォルダに Mods と Apps が見つかりません。Reloaded-II 本体のフォルダを選んでください。");
        }
    }
}
