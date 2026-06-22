namespace ReloadedHelper.Core;

public static class RefreshSelector
{
    public static IReadOnlyList<ModInfo> Select(
        IEnumerable<ModInfo> catalog, UserDataFile userData, bool force)
    {
        var result = new List<ModInfo>();
        foreach (var mod in catalog)
        {
            if (force) { result.Add(mod); continue; }
            if (!userData.Mods.TryGetValue(mod.ModId, out var ud)) { result.Add(mod); continue; }
            if (ud.FetchedVersion != mod.ModVersion) result.Add(mod);
        }
        return result;
    }
}
