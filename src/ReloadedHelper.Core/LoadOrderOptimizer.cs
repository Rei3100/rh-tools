namespace ReloadedHelper.Core;

public sealed record PlacementReason(string MovedModId, string AgainstModId, string Message);

public sealed record OptimizeResult(
    IReadOnlyList<string> Order,
    IReadOnlyList<PlacementReason> Reasons,
    IReadOnlyList<(string A, string B)> Unresolved);

public static class LoadOrderOptimizer
{
    public static OptimizeResult Optimize(
        string appId,
        IReadOnlyList<string> currentOrder,
        IReadOnlyDictionary<string, IReadOnlyList<string>> dependenciesOf,
        IReadOnlyList<FileConflict> conflicts,
        IReadOnlyDictionary<string, ModRole> rolesByMod,
        PreferenceStore prefs)
    {
        // 1. 依存を満たす基準順
        var order = LoadOrderSorter.Sort(currentOrder, dependenciesOf).ToList();
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

            // 勝者決定：2件はユーザーの好みを優先、それ以外（多重）は役割で一意なら解決。
            string? winner = c.ModIds.Count == 2
                ? prefs.GetWinner(appId, c.ModIds[0], c.ModIds[1]) ?? DecideByRole(c.ModIds, rolesByMod)
                : DecideByRole(c.ModIds, rolesByMod);

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

        return new OptimizeResult(order, reasons, unresolved);
    }

    // 競合する全MODのうち、役割ランクが最大で一意なものを勝者に。
    // 同点・最大が Unknown のみ等で決められなければ null（＝要確認）。
    private static string? DecideByRole(IReadOnlyList<string> mods, IReadOnlyDictionary<string, ModRole> roles)
    {
        int Rank(ModRole r) => r switch
        {
            ModRole.VisualOverride => 2, // 勝たせたい
            ModRole.BaseLayer => 0,      // 負けてよい
            _ => 1,                      // Unknown/Music 等
        };
        var ranked = mods
            .Select(m => (mod: m, role: roles.GetValueOrDefault(m, ModRole.Unknown)))
            .Select(x => (x.mod, x.role, rank: Rank(x.role)))
            .OrderByDescending(x => x.rank)
            .ToList();

        var top = ranked[0];
        // 最大ランクが Unknown 由来（決め手なし）なら自動判断しない
        if (top.role is ModRole.Unknown or ModRole.Music) return null;
        // 最大ランクが複数いる＝勝者一意でない
        if (ranked.Count(x => x.rank == top.rank) > 1) return null;
        return top.mod;
    }

    private static int IndexOf(List<string> list, string id)
        => list.FindIndex(x => string.Equals(x, id, StringComparison.OrdinalIgnoreCase));
}
