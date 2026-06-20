namespace ReloadedHelper.Core;

public static class LoadOrderBuilder
{
    public static IReadOnlyList<ModLoadEntry> Build(GameInfo game, IReadOnlyDictionary<string, ModInfo> catalog)
    {
        var enabled = new HashSet<string>(game.EnabledMods, StringComparer.Ordinal);
        var list = new List<ModLoadEntry>(game.SortedMods.Count);
        for (int i = 0; i < game.SortedMods.Count; i++)
        {
            var id = game.SortedMods[i];
            catalog.TryGetValue(id, out var info);
            list.Add(new ModLoadEntry(i + 1, id, info, enabled.Contains(id)));
        }
        return list;
    }
}

public static class ModFilter
{
    public static IReadOnlyList<ModLoadEntry> Filter(IReadOnlyList<ModLoadEntry> entries, string? search)
    {
        if (string.IsNullOrWhiteSpace(search)) return entries;
        var s = search.Trim();
        return entries.Where(e =>
            e.ModId.Contains(s, StringComparison.OrdinalIgnoreCase) ||
            e.DisplayName.Contains(s, StringComparison.OrdinalIgnoreCase)).ToList();
    }
}
