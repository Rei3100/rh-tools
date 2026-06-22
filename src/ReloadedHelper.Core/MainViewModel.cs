using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ReloadedHelper.Core;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private IReadOnlyDictionary<string, ModInfo> _catalog = new Dictionary<string, ModInfo>();
    private IReadOnlyList<ModLoadEntry> _allEntries = Array.Empty<ModLoadEntry>();
    private ReloadedInstall? _install;
    private UserDataFile _userData = new();

    public IReadOnlyDictionary<string, ModInfo> AllMods => _catalog;

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
    public Func<IReadOnlyList<string>, Task>? RefreshSelectedAction { get; set; }

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
                RebuildEntries();
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
        SelectedGame = Games.Count > 0 ? Games[0] : null;
        if (SelectedGame is null) RebuildEntries();
    }

    public void Reload()
    {
        if (_install is null) return;
        var prevId = SelectedGame?.AppId;
        LoadFrom(_install);
        if (prevId is not null)
        {
            var restored = Games.FirstOrDefault(g => g.AppId == prevId);
            if (restored is not null) SelectedGame = restored;
        }
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

        var prevId = game.AppId;
        LoadFrom(_install);
        var restored = Games.FirstOrDefault(g => g.AppId == prevId);
        if (restored is not null) SelectedGame = restored;
    }

    private void RebuildEntries()
    {
        _allEntries = SelectedGame is null
            ? Array.Empty<ModLoadEntry>()
            : LoadOrderBuilder.Build(SelectedGame, _catalog, _userData);
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
