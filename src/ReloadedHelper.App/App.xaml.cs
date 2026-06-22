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
        var settingsVm = new SettingsViewModel(settingsPath);

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
        modListVm.RefreshAction = () => RefreshModMetadataAsync(modListVm, install, force: false);
        modListVm.ForceRefreshAction = () => RefreshModMetadataAsync(modListVm, install, force: true);
        modListVm.RefreshSelectedAction = ids =>
            Task.Run(() => RefreshModMetadataAsync(modListVm, install, force: true, ids));
        _ = Task.Run(() => RefreshModMetadataAsync(modListVm, install, force: false));
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
                ThemedDialog.Show(Current.MainWindow,
                    "rh-tools 更新",
                    $"v{latestVer} に更新します。アプリを再起動します。");
                Current.Shutdown();
            });
        }
        catch { /* 更新失敗は無視して起動を続ける */ }
    }

    private static async Task RefreshModMetadataAsync(
        MainViewModel modListVm, ReloadedInstall install,
        bool force, IReadOnlyList<string>? targetModIds = null)
    {
        bool started = false;
        Current.Dispatcher.Invoke(() =>
        {
            if (modListVm.IsUpdating) return;
            modListVm.IsUpdating = true; started = true;
            modListVm.UpdateReportLines.Clear();
        });
        if (!started) return;

        var results = new List<MetadataRefreshResult>();
        try
        {
            using var http = new System.Net.Http.HttpClient();
            http.DefaultRequestHeaders.UserAgent.ParseAdd("rh-tools/1.0");
            var refresher = new ModMetadataRefresher(new GameBananaClient(http), new TranslationService(http));

            var userData = UserDataStore.Load(UserDataStore.DefaultPath);
            var catalog = Current.Dispatcher.Invoke(() => modListVm.AllMods);

            var pool = targetModIds is null
                ? RefreshSelector.Select(catalog.Values, userData, force)
                : catalog.Values.Where(m => targetModIds.Contains(m.ModId, StringComparer.OrdinalIgnoreCase)).ToList();

            int total = pool.Count, processed = 0;
            if (total == 0) return;

            foreach (var mod in pool)
            {
                processed++;
                Current.Dispatcher.Invoke(() => modListVm.UpdateProgress = $"更新中 {processed}/{total} 件...");

                userData.Mods.TryGetValue(mod.ModId, out var modData);
                modData ??= new ModUserData();

                var r = await refresher.RefreshAsync(mod, modData);
                results.Add(r);

                ModConfigUpdater.Write(mod.FolderPath, r.JaName, r.JaDesc, r.Author);

                modData.GameBananaId = r.GbId;
                modData.Category = r.Category;
                modData.Author = r.Author;
                modData.TranslatedName = r.JaName;
                modData.TranslatedDescription = r.JaDesc;
                modData.FetchedAt = DateTime.UtcNow;
                modData.FetchedVersion = mod.ModVersion;
                userData.Mods[mod.ModId] = modData;

                // 1件ごとに即 UI 反映（タブは切り替わらない）
                Current.Dispatcher.Invoke(() =>
                {
                    modListVm.ApplyMetadataToRow(mod.ModId, r.JaName, r.JaDesc, r.Category, r.Author);
                    modListVm.UpdateReportLines.Add(UpdateReportLog.Format(r));
                });
            }

            UserDataStore.Save(UserDataStore.DefaultPath, userData);
            UpdateReportLog.Append(UpdateReportLog.DefaultPath, results);

            // 最後に整合のため SelectedGame を保持して再読込
            Current.Dispatcher.Invoke(() => modListVm.Reload());
        }
        catch (Exception ex)
        {
            Current.Dispatcher.Invoke(() =>
                modListVm.UpdateReportLines.Add($"[エラー] 更新中に問題が発生: {ex.Message}"));
        }
        finally
        {
            Current.Dispatcher.Invoke(() =>
            {
                modListVm.IsUpdating = false;
                modListVm.UpdateProgress = "";
            });
        }
    }

    private static void ApplySortAllGames(MainViewModel mainVm, ReloadedInstall install)
    {
        var catalog = mainVm.AllMods;
        var depMap = catalog.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlyList<string>)kv.Value.Dependencies,
            StringComparer.OrdinalIgnoreCase);

        bool anyChanged = false;
        foreach (var game in mainVm.Games)
        {
            if (game.SortedMods.Count == 0) continue;

            var enabledSet = new HashSet<string>(game.EnabledMods, StringComparer.OrdinalIgnoreCase);

            // IsLibrary MOD は常に enabled グループへ
            var enabledGroup = game.SortedMods
                .Where(id => enabledSet.Contains(id) ||
                             (catalog.TryGetValue(id, out var m) && m.IsLibrary))
                .ToList();
            var disabledGroup = game.SortedMods
                .Where(id => !enabledGroup.Contains(id, StringComparer.OrdinalIgnoreCase))
                .ToList();

            var sortedEnabled = LoadOrderSorter.Sort(enabledGroup, depMap);
            var sortedDisabled = LoadOrderSorter.Sort(disabledGroup, depMap);
            var newSorted = sortedEnabled.Concat(sortedDisabled).ToList();
            var newEnabled = sortedEnabled.ToList();

            var configPath = Path.Combine(game.FolderPath, "AppConfig.json");
            if (!File.Exists(configPath)) continue;

            bool sortChanged = !newSorted.SequenceEqual(game.SortedMods, StringComparer.OrdinalIgnoreCase);
            bool enabledChanged = !newEnabled.SequenceEqual(game.EnabledMods, StringComparer.OrdinalIgnoreCase);
            if (!sortChanged && !enabledChanged) continue;

            AppConfigWriter.WriteEnabledAndSorted(configPath, game.AppId, newEnabled, newSorted);
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
