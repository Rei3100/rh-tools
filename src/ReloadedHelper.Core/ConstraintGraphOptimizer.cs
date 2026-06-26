namespace ReloadedHelper.Core;

public sealed record PlacementReason(string MovedModId, string AgainstModId, string Message);

public sealed record ModPlacement(string ModId, int LayerRank, string LayerLabel, string Reason);

public sealed record OptimizeResult(
    IReadOnlyList<string> Order,
    IReadOnlyList<PlacementReason> Reasons,
    IReadOnlyList<(string A, string B)> Unresolved,
    IReadOnlyList<ModPlacement> Placements);

public static class ConstraintGraphOptimizer
{
    public static OptimizeResult Optimize(
        IReadOnlyList<string> currentOrder,
        IReadOnlyDictionary<string, IReadOnlyList<string>> dependenciesOf,
        IReadOnlyList<FileConflict> conflicts,
        IReadOnlyDictionary<string, ModType> typesByMod,
        IReadOnlyDictionary<string, int> resourceCountByMod,
        IReadOnlyDictionary<string, string>? typeReasons = null)
    {
        var present = new HashSet<string>(currentOrder, StringComparer.OrdinalIgnoreCase);

        // 1. 依存辺（dep → mod）。currentOrder にあるもののみ。依存は絶対に守る＝最初に全て入れる。
        var depEdges = new List<(string Before, string After)>();
        var adj = currentOrder.ToDictionary(m => m, _ => new List<string>(), StringComparer.OrdinalIgnoreCase);
        foreach (var mod in currentOrder)
            if (dependenciesOf.TryGetValue(mod, out var deps))
                foreach (var dep in deps)
                    if (present.Contains(dep) && !string.Equals(dep, mod, StringComparison.OrdinalIgnoreCase))
                    {
                        depEdges.Add((dep, mod));
                        adj[dep].Add(mod);
                    }

        // 2. 組み合わせグラフ(依存＋採用済み重なり)での到達可能性。これで循環を未然に防ぐ。
        bool Reaches(string from, string to)
        {
            if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase)) return true;
            var stack = new Stack<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            stack.Push(from);
            while (stack.Count > 0)
            {
                var cur = stack.Pop();
                if (!seen.Add(cur)) continue;
                if (string.Equals(cur, to, StringComparison.OrdinalIgnoreCase)) return true;
                if (adj.TryGetValue(cur, out var nexts))
                    foreach (var n in nexts) stack.Push(n);
            }
            return false;
        }

        // 3. 重なり辺（more→fewer）。組み合わせグラフで after→before に既に到達できる＝追加すると循環
        //    （依存を壊しうる）ので捨てる。依存辺は全て先に入っているので依存は決して壊れない。
        var overlap = OverlapEdges.Build(conflicts, resourceCountByMod);
        var edges = new List<(string Before, string After)>(depEdges);
        foreach (var (before, after) in overlap)
        {
            if (!present.Contains(before) || !present.Contains(after)) continue;
            if (Reaches(after, before)) continue;
            edges.Add((before, after));
            adj[before].Add(after);
        }

        // 4. グループ優先度（rank）でソート。
        var ranks = currentOrder.ToDictionary(
            id => id,
            id => ModTypeInfo.Rank(typesByMod.GetValueOrDefault(id, ModType.Unknown)),
            StringComparer.OrdinalIgnoreCase);
        var order = LoadOrderSorter.SortByEdges(currentOrder, edges, ranks).ToList();

        // 5. 配置（種類ラベル＋理由）。
        var placements = order.Select(id =>
        {
            var type = typesByMod.GetValueOrDefault(id, ModType.Unknown);
            var reason = typeReasons != null && typeReasons.TryGetValue(id, out var rs) && !string.IsNullOrWhiteSpace(rs)
                ? rs
                : $"{ModTypeInfo.Label(type)}として配置";
            return new ModPlacement(id, ModTypeInfo.Rank(type), ModTypeInfo.Label(type), reason);
        }).ToList();

        return new OptimizeResult(order, System.Array.Empty<PlacementReason>(),
            System.Array.Empty<(string A, string B)>(), placements);
    }
}
