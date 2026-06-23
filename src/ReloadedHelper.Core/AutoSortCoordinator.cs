using System.IO;
using System.Text.Json;

namespace ReloadedHelper.Core;

public enum AutoSortTrigger { Startup, ToggleEnable, Delete, ForcedRefresh }

public sealed record AutoSortHistoryEntry(
    DateTime At,
    AutoSortTrigger Trigger,
    IReadOnlyList<PlacementReason> Reasons,
    IReadOnlyList<UnresolvedPair> Unresolved);

public sealed record UnresolvedPair(string A, string B);

public sealed class AutoSortCoordinator
{
    private readonly string _path;
    private readonly List<AutoSortHistoryEntry> _history;
    private const int MaxEntries = 50;

    public AutoSortCoordinator(string historyDir)
    {
        Directory.CreateDirectory(historyDir);
        _path = Path.Combine(historyDir, "autosort-history.json");
        try
        {
            _history = File.Exists(_path)
                ? JsonSerializer.Deserialize<List<AutoSortHistoryEntry>>(File.ReadAllText(_path)) ?? new()
                : new();
        }
        catch
        {
            _history = new();
        }
    }

    public IReadOnlyList<AutoSortHistoryEntry> History => _history;

    public AutoSortHistoryEntry Apply(
        AutoSortTrigger trigger,
        OptimizeResult result,
        Action<IReadOnlyList<string>> applyOrder,
        Action backup)
    {
        backup();
        applyOrder(result.Order);

        var entry = new AutoSortHistoryEntry(
            DateTime.Now, trigger, result.Reasons,
            result.Unresolved.Select(u => new UnresolvedPair(u.A, u.B)).ToList());

        _history.Insert(0, entry);
        if (_history.Count > MaxEntries) _history.RemoveRange(MaxEntries, _history.Count - MaxEntries);
        File.WriteAllText(_path, JsonSerializer.Serialize(_history,
            new JsonSerializerOptions { WriteIndented = true }));
        return entry;
    }
}
