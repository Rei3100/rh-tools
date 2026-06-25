namespace ReloadedHelper.Core;

public sealed record PlacementReason(string MovedModId, string AgainstModId, string Message);

public sealed record ModPlacement(string ModId, int LayerRank, string LayerLabel, string Reason);

public sealed record OptimizeResult(
    IReadOnlyList<string> Order,
    IReadOnlyList<PlacementReason> Reasons,
    IReadOnlyList<(string A, string B)> Unresolved,
    IReadOnlyList<ModPlacement> Placements);

public static class LoadOrderOptimizer
{
    public static OptimizeResult Optimize(
        string appId,
        IReadOnlyList<string> currentOrder,
        IReadOnlyDictionary<string, IReadOnlyList<string>> dependenciesOf,
        IReadOnlyList<FileConflict> conflicts,
        IReadOnlyDictionary<string, ModType> typesByMod,
        PreferenceStore prefs,
        IReadOnlyDictionary<string, string>? typeReasons = null)
    {
        // 0. 種類ランクで全MODを安定整列（弱い土台が前、見た目が後ろ）。同種は元順を維持。
        var layered = currentOrder
            .Select((id, i) => (id, i, rank: ModTypeInfo.Rank(typesByMod.GetValueOrDefault(id, ModType.Unknown))))
            .OrderBy(x => x.rank).ThenBy(x => x.i)
            .Select(x => x.id)
            .ToList();

        // 1. 依存を満たす基準順（層整列を初期順として依存ソート）
        var order = LoadOrderSorter.Sort(layered, dependenciesOf).ToList();
        var reasons = new List<PlacementReason>();
        var unresolved = new List<(string A, string B)>();
        // 同じMODペアが複数ファイルで競合しても「要確認」は1件だけにする（順不同で同一視）。
        var seenUnresolved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void AddUnresolved(string a, string b)
        {
            var (lo, hi) = string.CompareOrdinal(a, b) <= 0 ? (a, b) : (b, a);
            if (seenUnresolved.Add($"{lo.ToLowerInvariant()}|{hi.ToLowerInvariant()}"))
                unresolved.Add((a, b));
        }

        // 依存制約: dep は dependent より前。入れ替えがこれを壊さないか確認用。
        bool DependsOn(string mod, string maybeDep)
            => dependenciesOf.TryGetValue(mod, out var d) &&
               d.Contains(maybeDep, StringComparer.OrdinalIgnoreCase);

        foreach (var c in conflicts)
        {
            if (c.ModIds.Count < 2) continue;
            // 代表ペア（履歴で「要確認」を可視化するため。多重競合でも握りつぶさない）
            var repA = c.ModIds[0];
            var repB = c.ModIds[^1];

            // 勝者決定：2件はユーザーの好みを優先、それ以外（多重）は種類で一意なら解決。
            string? winner = c.ModIds.Count == 2
                ? prefs.GetWinner(appId, c.ModIds[0], c.ModIds[1]) ?? DecideByType(c.ModIds, typesByMod)
                : DecideByType(c.ModIds, typesByMod);

            if (winner is null) { AddUnresolved(repA, repB); continue; }

            var losers = c.ModIds
                .Where(m => !string.Equals(m, winner, StringComparison.OrdinalIgnoreCase))
                .ToList();

            int wi = IndexOf(order, winner);
            if (wi < 0) continue;

            // 勝者が壊す依存（敗者が勝者に依存）があるなら自動では動かさない
            if (losers.Any(l => DependsOn(l, winner))) { AddUnresolved(repA, repB); continue; }

            int maxLoser = losers.Select(l => IndexOf(order, l)).DefaultIfEmpty(-1).Max();
            if (maxLoser < 0 || wi > maxLoser) continue; // すでに全敗者より後ろ＝OK

            // 勝者を、全敗者の後ろ（最後の敗者の直後）へ移動
            order.RemoveAt(wi);
            maxLoser = losers.Select(l => IndexOf(order, l)).Max();
            order.Insert(maxLoser + 1, winner);
            var loserLabel = losers.Count == 1 ? losers[0] : $"{losers.Count}個のMOD";
            reasons.Add(new PlacementReason(winner, losers[^1],
                $"「{winner}」を{loserLabel}より後ろに配置しました（{winner} の上書きを反映）。"));
        }

        var placements = order.Select(id =>
        {
            var type = typesByMod.GetValueOrDefault(id, ModType.Unknown);
            var reason = typeReasons != null && typeReasons.TryGetValue(id, out var r) && !string.IsNullOrWhiteSpace(r)
                ? r
                : $"{ModTypeInfo.Label(type)}として配置";
            return new ModPlacement(id, ModTypeInfo.Rank(type), ModTypeInfo.Label(type), reason);
        }).ToList();

        return new OptimizeResult(order, reasons, unresolved, placements);
    }

    // 競合する全MODのうち、種類ランクが最大で一意なものを勝者に（後ろ＝勝ち）。
    // 同点・最大が Unknown なら自動判断しない（＝要確認）。
    private static string? DecideByType(IReadOnlyList<string> mods, IReadOnlyDictionary<string, ModType> types)
    {
        var ranked = mods
            .Select(m => (mod: m, type: types.GetValueOrDefault(m, ModType.Unknown)))
            .Select(x => (x.mod, x.type, rank: ModTypeInfo.Rank(x.type)))
            .OrderByDescending(x => x.rank)
            .ToList();

        var top = ranked[0];
        if (top.type == ModType.Unknown) return null;
        if (ranked.Count(x => x.rank == top.rank) > 1) return null;
        return top.mod;
    }

    private static int IndexOf(List<string> list, string id)
        => list.FindIndex(x => string.Equals(x, id, StringComparison.OrdinalIgnoreCase));
}
