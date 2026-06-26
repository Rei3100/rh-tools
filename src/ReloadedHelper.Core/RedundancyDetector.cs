using ReloadedHelper.Core.Analyzers;

namespace ReloadedHelper.Core;

// 依存関係に無い2MODが同じ資源を大きく重ねて触る＝「片方だけ想定の重複」候補。
public sealed record RedundantPair(string ModA, string ModB, int SharedCount, int SmallerCount);

public static class RedundancyDetector
{
    public static IReadOnlyList<RedundantPair> Detect(
        IReadOnlyList<ModResources> orderedEnabled,
        IReadOnlyDictionary<string, ModInfo> catalog,
        double threshold = 0.8)
    {
        // 資源キー集合を MOD ごとに作る
        var sets = new List<(string Id, HashSet<string> Keys)>();
        foreach (var mr in orderedEnabled)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var rk in mr.Resources) keys.Add(rk.ToString());
            if (keys.Count > 0) sets.Add((mr.ModId, keys));
        }

        var result = new List<RedundantPair>();
        for (int i = 0; i < sets.Count; i++)
            for (int j = i + 1; j < sets.Count; j++)
            {
                var (aId, aKeys) = sets[i];
                var (bId, bKeys) = sets[j];
                if (DependsOn(catalog, aId, bId) || DependsOn(catalog, bId, aId)) continue;

                int shared = aKeys.Count(bKeys.Contains);
                if (shared == 0) continue;
                int smaller = Math.Min(aKeys.Count, bKeys.Count);
                if ((double)shared / smaller >= threshold)
                    result.Add(new RedundantPair(aId, bId, shared, smaller));
            }
        return result;
    }

    private static bool DependsOn(IReadOnlyDictionary<string, ModInfo> catalog, string mod, string maybeDep)
    {
        return catalog.TryGetValue(mod, out var info) &&
               info.Dependencies.Any(d => string.Equals(d, maybeDep, StringComparison.OrdinalIgnoreCase));
    }
}
