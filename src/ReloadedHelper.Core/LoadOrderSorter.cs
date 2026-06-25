// src/ReloadedHelper.Core/LoadOrderSorter.cs
namespace ReloadedHelper.Core;

public static class LoadOrderSorter
{
    /// <summary>
    /// Returns modIds in load order where dependencies come before dependents.
    /// Preserves relative order among mods with no dependency relationship.
    /// Circular dependencies are appended at the end in original order.
    /// </summary>
    public static IReadOnlyList<string> Sort(
        IReadOnlyList<string> currentOrder,
        IReadOnlyDictionary<string, IReadOnlyList<string>> dependenciesOf)
    {
        var allMods = new HashSet<string>(currentOrder, StringComparer.OrdinalIgnoreCase);
        // Map each modId to its original index (for stable ordering)
        var indexMap = currentOrder
            .Select((id, i) => (id, i))
            .ToDictionary(x => x.id, x => x.i, StringComparer.OrdinalIgnoreCase);

        // Build: dep → list of mods that depend on dep
        var dependents = allMods.ToDictionary(
            m => m, _ => new List<string>(), StringComparer.OrdinalIgnoreCase);
        var inDegree = allMods.ToDictionary(
            m => m, _ => 0, StringComparer.OrdinalIgnoreCase);

        foreach (var mod in allMods)
        {
            if (!dependenciesOf.TryGetValue(mod, out var deps)) continue;
            foreach (var dep in deps)
            {
                if (!allMods.Contains(dep)) continue; // not installed → ignore
                dependents[dep].Add(mod);
                inDegree[mod]++;
            }
        }

        // Kahn's algorithm with original-index priority for stable ordering
        var comparer = Comparer<(int idx, string id)>.Create((a, b) =>
        {
            int c = a.idx.CompareTo(b.idx);
            return c != 0 ? c : StringComparer.OrdinalIgnoreCase.Compare(a.id, b.id);
        });
        var ready = new SortedSet<(int idx, string id)>(comparer);
        foreach (var mod in allMods.Where(m => inDegree[m] == 0))
            ready.Add((indexMap[mod], mod));

        var result = new List<string>(allMods.Count);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        while (ready.Count > 0)
        {
            var item = ready.Min;
            ready.Remove(item);
            result.Add(item.id);
            visited.Add(item.id);

            foreach (var dependent in dependents[item.id])
            {
                inDegree[dependent]--;
                if (inDegree[dependent] == 0)
                    ready.Add((indexMap.GetValueOrDefault(dependent, int.MaxValue), dependent));
            }
        }

        // Append circular-dep remainder in original order
        foreach (var mod in currentOrder)
            if (!visited.Contains(mod))
                result.Add(mod);

        return result.AsReadOnly();
    }

    /// <summary>
    /// 「Before は After より前」を全て満たし、自由な部分は (groupRank昇順 → 現在順index昇順) で並べる。
    /// currentOrder に無いノードを含む辺は無視。循環に巻き込まれたノードは末尾へ現在順で残す。
    /// </summary>
    public static IReadOnlyList<string> SortByEdges(
        IReadOnlyList<string> currentOrder,
        IReadOnlyCollection<(string Before, string After)> edges,
        IReadOnlyDictionary<string, int> groupRankOf)
    {
        var present = new HashSet<string>(currentOrder, StringComparer.OrdinalIgnoreCase);
        var index = currentOrder
            .Select((id, i) => (id, i))
            .ToDictionary(x => x.id, x => x.i, StringComparer.OrdinalIgnoreCase);

        var afters = currentOrder.ToDictionary(
            m => m, _ => new List<string>(), StringComparer.OrdinalIgnoreCase);
        var inDegree = currentOrder.ToDictionary(
            m => m, _ => 0, StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (before, after) in edges)
        {
            if (!present.Contains(before) || !present.Contains(after)) continue;
            if (string.Equals(before, after, StringComparison.OrdinalIgnoreCase)) continue;
            var key = $"{before.ToLowerInvariant()} {after.ToLowerInvariant()}";
            if (!seen.Add(key)) continue;
            afters[before].Add(after);
            inDegree[after]++;
        }

        int Rank(string id) => groupRankOf.TryGetValue(id, out var r) ? r : int.MaxValue;
        var comparer = Comparer<string>.Create((x, y) =>
        {
            int c = Rank(x).CompareTo(Rank(y));
            if (c != 0) return c;
            int ci = index.GetValueOrDefault(x).CompareTo(index.GetValueOrDefault(y));
            return ci != 0 ? ci : StringComparer.OrdinalIgnoreCase.Compare(x, y);
        });

        var ready = new SortedSet<string>(comparer);
        foreach (var m in currentOrder)
            if (inDegree[m] == 0) ready.Add(m);

        var result = new List<string>(currentOrder.Count);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (ready.Count > 0)
        {
            var m = ready.Min!;
            ready.Remove(m);
            result.Add(m);
            visited.Add(m);
            foreach (var a in afters[m])
                if (--inDegree[a] == 0) ready.Add(a);
        }

        // 循環に巻き込まれて出られなかったノードは現在順で末尾へ。
        foreach (var m in currentOrder)
            if (!visited.Contains(m)) result.Add(m);

        return result;
    }
}
