namespace ReloadedHelper.Core;

public enum FilterMode { All, EnabledOnly, DisabledOnly }

public static class LoadOrderBuilder
{
    public static IReadOnlyList<ModLoadEntry> Build(
        GameInfo game,
        IReadOnlyDictionary<string, ModInfo> catalog,
        UserDataFile? userData = null)
    {
        var enabled = new HashSet<string>(game.EnabledMods, StringComparer.Ordinal);
        var list = new List<ModLoadEntry>(game.SortedMods.Count);
        for (int i = 0; i < game.SortedMods.Count; i++)
        {
            var id = game.SortedMods[i];
            catalog.TryGetValue(id, out var info);
            var category = userData?.Mods.GetValueOrDefault(id)?.Category;
            list.Add(new ModLoadEntry(i + 1, id, info, enabled.Contains(id), category));
        }
        return list;
    }
}

public static class ModFilter
{
    public static IReadOnlyList<ModLoadEntry> Filter(
        IReadOnlyList<ModLoadEntry> entries, string? search,
        FilterMode mode = FilterMode.All)
    {
        IEnumerable<ModLoadEntry> result = entries;

        result = mode switch
        {
            FilterMode.EnabledOnly  => result.Where(e => e.Enabled),
            FilterMode.DisabledOnly => result.Where(e => !e.Enabled),
            _                       => result
        };

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            result = result.Where(e =>
                e.ModId.Contains(s, StringComparison.OrdinalIgnoreCase) ||
                e.DisplayName.Contains(s, StringComparison.OrdinalIgnoreCase));
        }

        return result.ToList();
    }
}
