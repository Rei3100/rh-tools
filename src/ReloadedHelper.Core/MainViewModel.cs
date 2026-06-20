using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ReloadedHelper.Core;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private IReadOnlyDictionary<string, ModInfo> _catalog = new Dictionary<string, ModInfo>();
    private IReadOnlyList<ModLoadEntry> _allEntries = Array.Empty<ModLoadEntry>();

    public IReadOnlyDictionary<string, ModInfo> AllMods => _catalog;

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

    public void LoadFrom(ReloadedInstall install)
    {
        _catalog = ModCatalog.LoadAll(install.ModsDir);
        Games.Clear();
        foreach (var g in GameCatalog.LoadAll(install.AppsDir)) Games.Add(g);
        SelectedGame = Games.Count > 0 ? Games[0] : null;
        if (SelectedGame is null) RebuildEntries();
    }

    private void RebuildEntries()
    {
        _allEntries = SelectedGame is null
            ? Array.Empty<ModLoadEntry>()
            : LoadOrderBuilder.Build(SelectedGame, _catalog);
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        Entries.Clear();
        foreach (var e in ModFilter.Filter(_allEntries, SearchText)) Entries.Add(e);
        SelectedEntry = Entries.Count > 0 ? Entries[0] : null;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
