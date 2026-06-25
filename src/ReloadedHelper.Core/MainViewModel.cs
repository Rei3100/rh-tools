using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ReloadedHelper.Core;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private static readonly string _appDataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ReloadedHelper");

    private IReadOnlyDictionary<string, ModInfo> _catalog = new Dictionary<string, ModInfo>();
    private IReadOnlyList<ModLoadEntry> _allEntries = Array.Empty<ModLoadEntry>();
    private ReloadedInstall? _install;
    private UserDataFile _userData = new();

    private readonly AutoSortCoordinator _coordinator;
    private readonly PreferenceStore _prefs;
    private bool _inReload;
    private IReadOnlyDictionary<string, string> _lastPlacementReasons =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public AutoSortCoordinator Coordinator => _coordinator;

    public MainViewModel()
    {
        _coordinator = new AutoSortCoordinator(_appDataDir);
        _prefs = new PreferenceStore(_appDataDir);
    }

    public IReadOnlyDictionary<string, ModInfo> AllMods => _catalog;
    public ReloadedInstall? Install => _install;

    private bool _isUpdating;
    public bool IsUpdating
    {
        get => _isUpdating;
        set { if (_isUpdating != value) { _isUpdating = value; OnChanged(); } }
    }

    private string _updateProgress = "";
    public string UpdateProgress
    {
        get => _updateProgress;
        set { if (_updateProgress != value) { _updateProgress = value; OnChanged(); } }
    }

    public Func<Task>? RefreshAction { get; set; }
    public Func<Task>? ForceRefreshAction { get; set; }
    public Func<IReadOnlyList<string>, Task>? RefreshSelectedAction { get; set; }

    public ObservableCollection<string> UpdateReportLines { get; } = new();

    public ObservableCollection<GameInfo> Games { get; } = new();
    public ObservableCollection<ModLoadEntry> Entries { get; } = new();

    private GameInfo? _selectedGame;
    public GameInfo? SelectedGame
    {
        get => _selectedGame;
        set
        {
            if (!ReferenceEquals(_selectedGame, value))
            {
                _selectedGame = value;
                OnChanged();
                // ゲームが切り替わる際、前ゲームの配置理由をクリアする。
                // RunAutoSort が途中でリターンした場合でも旧データが残らないようにする。
                _lastPlacementReasons = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                RebuildEntries();
                // ユーザーがタブでゲームを切り替えた瞬間に、そのゲームを自動並び替え。
                // LoadFrom/Reload 中の内部選択では走らせない（_inReload で抑止し、
                // 起動時ソートは LoadFrom 末尾で明示的に1回だけ行う）。
                if (!_inReload && _install is not null && value is not null)
                    RunAutoSort(value.AppId, AutoSortTrigger.Startup);
            }
        }
    }

    private ModLoadEntry? _selectedEntry;
    public ModLoadEntry? SelectedEntry
    {
        get => _selectedEntry;
        set
        {
            if (!ReferenceEquals(_selectedEntry, value))
            {
                _selectedEntry = value;
                OnChanged();
            }
        }
    }

    private string _searchText = "";
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (_searchText != value)
            {
                _searchText = value ?? "";
                OnChanged();
                ApplyFilter();
            }
        }
    }

    private FilterMode _filterMode = FilterMode.All;
    public FilterMode FilterMode
    {
        get => _filterMode;
        set
        {
            if (_filterMode == value) return;
            _filterMode = value;
            OnChanged();
            ApplyFilter();
        }
    }

    public string EntryCountLabel
    {
        get
        {
            var total = _allEntries.Count;
            var shown = Entries.Count;
            return _filterMode switch
            {
                FilterMode.EnabledOnly => $"読み込み順 ・ 有効 {shown} / 全 {total} 件",
                FilterMode.DisabledOnly => $"読み込み順 ・ 無効 {shown} / 全 {total} 件",
                _ => $"読み込み順 ・ 全 {total} 件"
            };
        }
    }

    public void LoadFrom(ReloadedInstall install)
    {
        _install = install;
        _catalog = ModCatalog.LoadAll(install.ModsDir);
        _userData = UserDataStore.Load(UserDataStore.DefaultPath);
        Games.Clear();
        foreach (var g in GameCatalog.LoadAll(install.AppsDir)) Games.Add(g);
        // 読み込み中の内部選択では setter 由来の自動並び替えを抑止（_inReload）。
        var prevInReload = _inReload;
        _inReload = true;
        SelectedGame = Games.Count > 0 ? Games[0] : null;
        _inReload = prevInReload;
        if (SelectedGame is null) RebuildEntries();
        // 起動時（Reload にネストしていない直接ロード）：選択中ゲームを1回だけ自動並び替え。
        else if (!_inReload) RunAutoSort(SelectedGame.AppId, AutoSortTrigger.Startup);
    }

    public void Reload()
    {
        if (_install is null) return;
        var prevId = SelectedGame?.AppId;
        // Reload 中は自動並び替えを抑止し、選択タブを復元するだけに留める。
        // 並び替えが必要な操作（トグル/削除）は呼び出し側が明示的に RunAutoSort する。
        _inReload = true;
        LoadFrom(_install);
        if (prevId is not null)
        {
            var restored = Games.FirstOrDefault(g => g.AppId == prevId);
            if (restored is not null) SelectedGame = restored;
        }
        _inReload = false;
    }

    public void ApplyMetadataToRow(string modId, string jaName, string jaDesc, string? category, string? author)
    {
        for (int i = 0; i < Entries.Count; i++)
        {
            var e = Entries[i];
            if (!string.Equals(e.ModId, modId, StringComparison.OrdinalIgnoreCase)) continue;
            var newInfo = e.Info is null
                ? null
                : e.Info with { ModName = jaName, ModDescription = jaDesc, ModAuthor = author ?? e.Info.ModAuthor };
            Entries[i] = e with { Info = newInfo, Category = category ?? e.Category };
            break;
        }
    }

    public void ToggleEnabled(ModLoadEntry entry)
    {
        if (entry.IsLibrary) return;  // フレームワーク MOD はトグル不可
        if (SelectedGame is null || _install is null) return;

        var game = SelectedGame;
        var enabledSet = game.EnabledMods.ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (enabledSet.Contains(entry.ModId)) enabledSet.Remove(entry.ModId);
        else enabledSet.Add(entry.ModId);

        var depMap = AllMods.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlyList<string>)kv.Value.Dependencies,
            StringComparer.OrdinalIgnoreCase);

        var enabledGroup = game.SortedMods.Where(enabledSet.Contains).ToList();
        var disabledGroup = game.SortedMods.Where(id => !enabledSet.Contains(id)).ToList();
        var sortedEnabled = LoadOrderSorter.Sort(enabledGroup, depMap);
        var sortedDisabled = LoadOrderSorter.Sort(disabledGroup, depMap);
        var newSorted = sortedEnabled.Concat(sortedDisabled).ToList();
        var newEnabled = sortedEnabled.ToList();

        var configPath = Path.Combine(game.FolderPath, "AppConfig.json");
        if (!File.Exists(configPath)) return;

        AppConfigWriter.WriteEnabledAndSorted(configPath, game.AppId, newEnabled, newSorted);
        Reload();   // SelectedGame を保持して再読込（タブ飛び防止・DRY）
        RunAutoSort(game.AppId, AutoSortTrigger.ToggleEnable);
    }

    public void RunAutoSort(string appId, AutoSortTrigger trigger)
    {
        if (SelectedGame is null || _install is null) return;
        var game = SelectedGame;
        if (!string.Equals(game.AppId, appId, StringComparison.OrdinalIgnoreCase)) return;

        var configPath = Path.Combine(game.FolderPath, "AppConfig.json");
        if (!File.Exists(configPath)) return;

        var diagResult = GameDiagnostics.Run(game, _catalog);

        var resourcesByMod = diagResult.Resources.ToDictionary(
            r => r.ModId, r => r.Resources, StringComparer.OrdinalIgnoreCase);
        var resourceCount = diagResult.Resources.ToDictionary(
            r => r.ModId, r => r.Resources.Count, StringComparer.OrdinalIgnoreCase);

        var decisions = BuildTypeDecisions(_allEntries, _catalog, resourcesByMod);
        var types = decisions.ToDictionary(kv => kv.Key, kv => kv.Value.Type, StringComparer.OrdinalIgnoreCase);
        var typeReasons = decisions.ToDictionary(kv => kv.Key, kv => kv.Value.Reason, StringComparer.OrdinalIgnoreCase);
        var hints = BuildHints(_allEntries, _catalog);
        var depMap = _catalog.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlyList<string>)kv.Value.Dependencies,
            StringComparer.OrdinalIgnoreCase);

        var result = LoadOrderOptimizer.Optimize(appId, game.SortedMods, depMap, diagResult.Conflicts,
            types, _prefs, typeReasons, resourceCount, hints);

        // 順序変化の有無に関わらず配置理由は常に保存し UI に反映する。
        _lastPlacementReasons = result.Placements.ToDictionary(
            p => p.ModId, p => p.Reason, StringComparer.OrdinalIgnoreCase);

        // 実際に順序が変わった時だけ書き込み・バックアップ・履歴を残す。
        // （変わらないのに毎回「変更なし」履歴を量産しない）
        if (result.Order.SequenceEqual(game.SortedMods, StringComparer.OrdinalIgnoreCase))
        {
            RebuildEntries();
            return;
        }

        _coordinator.Apply(
            trigger,
            result,
            applyOrder: order => AppConfigWriter.WriteEnabledAndSorted(configPath, appId, game.EnabledMods.ToList(), order.ToList()),
            backup: () => LoadOrderBackupService.Backup(configPath, appId));

        // ファイルを書き直したので UI に反映（Reload() は再帰ループになるため使わない）
        RebuildEntries();
    }

    internal static IReadOnlyList<(string Winner, string Loser)> LearnFromManualOrder(
        IReadOnlyList<string> newOrder,
        IReadOnlyList<FileConflict> conflicts)
    {
        int Idx(string id) => new List<string>(newOrder)
            .FindIndex(x => string.Equals(x, id, StringComparison.OrdinalIgnoreCase));

        var learned = new List<(string, string)>();
        foreach (var c in conflicts)
        {
            if (c.ModIds.Count != 2) continue;
            var a = c.ModIds[0];
            var b = c.ModIds[1];
            int ia = Idx(a), ib = Idx(b);
            if (ia < 0 || ib < 0) continue;
            // 後ろ（index大）が勝者
            if (ia > ib) learned.Add((a, b));
            else if (ib > ia) learned.Add((b, a));
        }
        return learned;
    }

    internal static IReadOnlyDictionary<string, TypeDecision> BuildTypeDecisions(
        IReadOnlyList<ModLoadEntry> entries,
        IReadOnlyDictionary<string, ModInfo> catalog,
        IReadOnlyDictionary<string, IReadOnlyList<Analyzers.ResourceKey>> resourcesByMod)
    {
        var map = new Dictionary<string, TypeDecision>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in entries)
        {
            var info = e.Info ?? (catalog.TryGetValue(e.ModId, out var ci) ? ci : null);
            var res = resourcesByMod.TryGetValue(e.ModId, out var r)
                ? r : System.Array.Empty<Analyzers.ResourceKey>();
            map[e.ModId] = info is null
                ? new TypeDecision(ModType.Unknown, "情報が無いため末尾に配置")
                : ModTypeClassifier.Classify(info, e.Category, res);
        }
        return map;
    }

    private static IReadOnlyDictionary<string, PlacementHint> BuildHints(
        IReadOnlyList<ModLoadEntry> entries,
        IReadOnlyDictionary<string, ModInfo> catalog)
    {
        var map = new Dictionary<string, PlacementHint>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in entries)
        {
            var info = e.Info ?? (catalog.TryGetValue(e.ModId, out var ci) ? ci : null);
            if (info is not null) map[e.ModId] = PlacementHintParser.Parse(info);
        }
        return map;
    }

    private void RebuildEntries()
    {
        var built = SelectedGame is null
            ? Array.Empty<ModLoadEntry>()
            : LoadOrderBuilder.Build(SelectedGame, _catalog, _userData);
        _allEntries = built
            .Select(e => _lastPlacementReasons.TryGetValue(e.ModId, out var r)
                ? e with { PlacementReason = r }
                : e)
            .ToList();
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        Entries.Clear();
        foreach (var e in ModFilter.Filter(_allEntries, SearchText, FilterMode)) Entries.Add(e);
        SelectedEntry = Entries.Count > 0 ? Entries[0] : null;
        OnChanged(nameof(EntryCountLabel));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
