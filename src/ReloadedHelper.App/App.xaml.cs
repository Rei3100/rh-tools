// src/ReloadedHelper.App/App.xaml.cs
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
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
        _ = Task.Run(CheckAndApplyUpdateAsync); // バックグラウンドで更新確認
    }

    private static async Task CheckAndApplyUpdateAsync()
    {
        try
        {
            using var http = new HttpClient();
            var checker = new UpdateChecker(http);
            var (latestVer, downloadUrl) = await checker.CheckAsync();
            if (latestVer is null || downloadUrl is null) return;

            var currentVer = Assembly.GetExecutingAssembly()
                                     .GetName().Version?.ToString(3) ?? "0.0.0";
            if (!Version.TryParse(latestVer, out var latest)) return;
            if (!Version.TryParse(currentVer, out var current)) return;
            if (latest <= current) return;

            // Download new exe
            var tempPath = Path.Combine(Path.GetTempPath(), "rh-tools-update.exe");
            var data = await http.GetByteArrayAsync(downloadUrl);
            await File.WriteAllBytesAsync(tempPath, data);

            // Apply update: PowerShell replaces exe after this process exits
            var currentExe = Environment.ProcessPath
                             ?? Path.Combine(
                                 AppContext.BaseDirectory, "ReloadedHelper.App.exe");
            var script =
                $"Start-Sleep -Milliseconds 1500; " +
                $"Copy-Item -Path '{tempPath}' -Destination '{currentExe}' -Force; " +
                $"Start-Process -FilePath '{currentExe}'";

            Process.Start(new ProcessStartInfo
            {
                FileName = "powershell",
                Arguments = $"-WindowStyle Hidden -Command \"{script}\"",
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden
            });

            Current.Dispatcher.Invoke(() =>
            {
                MessageBox.Show(
                    $"v{latestVer} に更新します。アプリを再起動します。",
                    "rh-tools 更新",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                Current.Shutdown();
            });
        }
        catch { /* 更新失敗は無視して起動を続ける */ }
    }

    private static void ApplySortAllGames(MainViewModel mainVm, ReloadedInstall install)
    {
        var depMap = mainVm.AllMods.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlyList<string>)kv.Value.Dependencies,
            StringComparer.OrdinalIgnoreCase);

        bool anyChanged = false;
        foreach (var game in mainVm.Games)
        {
            if (game.SortedMods.Count == 0) continue;

            var enabledSet    = new HashSet<string>(game.EnabledMods, StringComparer.OrdinalIgnoreCase);
            var enabledGroup  = game.SortedMods.Where(enabledSet.Contains).ToList();
            var disabledGroup = game.SortedMods.Where(id => !enabledSet.Contains(id)).ToList();

            var sortedEnabled  = LoadOrderSorter.Sort(enabledGroup,  depMap);
            var sortedDisabled = LoadOrderSorter.Sort(disabledGroup, depMap);
            var newSorted = sortedEnabled.Concat(sortedDisabled).ToList();

            bool changed = !newSorted.SequenceEqual(
                game.SortedMods, StringComparer.OrdinalIgnoreCase);
            if (!changed) continue;

            var configPath = Path.Combine(game.FolderPath, "AppConfig.json");
            if (!File.Exists(configPath)) continue;

            AppConfigWriter.WriteOrder(configPath, game.AppId, newSorted);
            anyChanged = true;
        }

        if (anyChanged) mainVm.LoadFrom(install);
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
