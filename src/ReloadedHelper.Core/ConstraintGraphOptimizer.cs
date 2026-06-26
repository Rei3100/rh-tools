namespace ReloadedHelper.Core;

// 統一制約グラフ：依存辺(ハード)＋重なり辺(ハード・依存と循環するものは捨てる)を集め、
// (グループrank昇順 → 現在順) 優先度で安定トポロジカルソート。Unresolved は出さない。
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

        // 1. 依存辺（dep → mod）。currentOrder にあるもののみ。
        var depEdges = new List<(string Before, string After)>();
        var depAdj = currentOrder.ToDictionary(m => m, _ => new List<string>(), StringComparer.OrdinalIgnoreCase);
        foreach (var mod in currentOrder)
            if (dependenciesOf.TryGetValue(mod, out var deps))
                foreach (var dep in deps)
                    if (present.Contains(dep) && !string.Equals(dep, mod, StringComparison.OrdinalIgnoreCase))
                    {
                        depEdges.Add((dep, mod));
                        depAdj[dep].Add(mod);
                    }

        // 2. 依存到達可能性: DepReaches(x, y) = 依存辺だけで x から y へ行けるか。
        bool DepReaches(string from, string to)
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
                if (depAdj.TryGetValue(cur, out var nexts))
                    foreach (var n in nexts) stack.Push(n);
            }
            return false;
        }

        // 3. 重なり辺（more→fewer）。依存が逆（fewer→more）を強制しているものは捨てる。
        var overlap = OverlapEdges.Build(conflicts, resourceCountByMod);
        var edges = new List<(string Before, string After)>(depEdges);
        foreach (var (before, after) in overlap)
        {
            if (!present.Contains(before) || !present.Contains(after)) continue;
            if (DepReaches(after, before)) continue; // 依存が after→before を強制＝矛盾 → 捨てる
            edges.Add((before, after));
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
