using ReloadedHelper.Core.Analyzers;

namespace ReloadedHelper.Core;

public sealed record FileConflict(string PathKey, IReadOnlyList<string> ModIds, string WinnerModId);

public static class ConflictDetector
{
    public static IReadOnlyList<FileConflict> Detect(IReadOnlyList<ModOverrides> orderedEnabled)
    {
        var byPath = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var mo in orderedEnabled)
            foreach (var p in mo.Paths)
            {
                if (!byPath.TryGetValue(p, out var list)) byPath[p] = list = new List<string>();
                if (!list.Contains(mo.ModId)) list.Add(mo.ModId);
            }

        var result = new List<FileConflict>();
        foreach (var (path, mods) in byPath)
            if (mods.Count > 1)
                result.Add(new FileConflict(path, mods, mods[^1])); // 読み込み順で最後＝優先＝勝者
        return result;
    }

    public static IReadOnlyList<FileConflict> Detect(IReadOnlyList<ModResources> orderedEnabled)
    {
        var byKey = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var mr in orderedEnabled)
            foreach (var rk in mr.Resources)
            {
                var k = rk.ToString();
                if (!byKey.TryGetValue(k, out var list)) byKey[k] = list = new List<string>();
                if (!list.Contains(mr.ModId)) list.Add(mr.ModId);
            }

        var result = new List<FileConflict>();
        foreach (var (key, mods) in byKey)
            if (mods.Count > 1)
                result.Add(new FileConflict(key, mods, mods[^1]));
        return result;
    }
}
