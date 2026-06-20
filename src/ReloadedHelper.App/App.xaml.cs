// src/ReloadedHelper.App/App.xaml.cs
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using Microsoft.Win32;
using ReloadedHelper.Core;

namespace ReloadedHelper.App;

public partial class App : Application
{
    private static Mutex? _appMutex;

    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    private const int SW_RESTORE = 9;

    private static void ActivateExistingInstance()
    {
        var currentId = Environment.ProcessId;
        var name = Path.GetFileNameWithoutExtension(
            Environment.ProcessPath ?? "ReloadedHelper.App");
        foreach (var proc in Process.GetProcessesByName(name))
        {
            if (proc.Id == currentId) continue;
            var hwnd = proc.MainWindowHandle;
            if (hwnd == IntPtr.Zero) continue;
            ShowWindow(hwnd, SW_RESTORE);
            SetForegroundWindow(hwnd);
            return;
        }
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        _appMutex = new Mutex(true, "Global\\ReloadedHelper_v1", out bool createdNew);
        if (!createdNew)
        {
            ActivateExistingInstance();
            Shutdown();
            return;
        }

        base.OnStartup(e);

        var settingsPath = SettingsStore.DefaultPath;
        var settingsVm   = new SettingsViewModel(settingsPath);

        var install = ResolveInstall(settingsVm);
        if (install is null) { Shutdown(); return; }

        settingsVm.ReloadedInstallPath = install.RootPath;

        var modListVm = new MainViewModel();
        modListVm.LoadFrom(install);
        ApplySortAllGames(modListVm, install);   // 依存関係トポロジーで自動並び替え

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
        var window = new MainWindow(shell);
        window.WindowState = WindowState.Maximized;
        window.Show();
    }

    private static void ApplySortAllGames(MainViewModel mainVm, ReloadedInstall install)
    {
        // Build dependency map: modId → list of modIds it depends on
        var depMap = mainVm.AllMods.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlyList<string>)kv.Value.Dependencies,
            StringComparer.OrdinalIgnoreCase);

        bool anyChanged = false;
        foreach (var game in mainVm.Games)
        {
            if (game.EnabledMods.Count == 0) continue;

            var sorted = LoadOrderSorter.Sort(game.EnabledMods, depMap);

            // Check if order actually changed
            bool changed = !sorted.SequenceEqual(
                game.EnabledMods, StringComparer.OrdinalIgnoreCase);
            if (!changed) continue;

            var configPath = Path.Combine(game.FolderPath, "AppConfig.json");
            if (!File.Exists(configPath)) continue;

            AppConfigWriter.WriteOrder(configPath, game.AppId, sorted);
            anyChanged = true;
        }

        if (anyChanged)
        {
            // Reload from disk so UI shows the sorted order
            mainVm.LoadFrom(install);
        }
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
