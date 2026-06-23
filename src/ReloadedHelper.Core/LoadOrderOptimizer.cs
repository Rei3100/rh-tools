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

        // 依存制約: dep は dependent より前。入れ替えがこれを壊さないか確認用。
        bool DependsOn(string mod, string maybeDep)
            => dependenciesOf.TryGetValue(mod, out var d) &&
               d.Contains(maybeDep, StringComparer.OrdinalIgnoreCase);

        foreach (var c in conflicts)
        {
            if (c.ModIds.Count != 2) continue; // 多重競合は当面ペア単位のみ自動化
            var a = c.ModIds[0];
            var b = c.ModIds[1];

            string? winner = prefs.GetWinner(appId, a, b) ?? DecideByRole(a, b, rolesByMod);
            if (winner is null) { unresolved.Add((a, b)); continue; }

            var loser = string.Equals(winner, a, StringComparison.OrdinalIgnoreCase) ? b : a;

            int wi = IndexOf(order, winner), li = IndexOf(order, loser);
            if (wi < 0 || li < 0) continue;
            if (wi > li) continue; // すでに勝者が後ろ＝OK

            // winner を loser の後ろへ移動したいが、依存を壊すなら諦める
            if (DependsOn(loser, winner)) { unresolved.Add((a, b)); continue; }

            order.RemoveAt(wi);
            li = IndexOf(order, loser);
            order.Insert(li + 1, winner);
            reasons.Add(new PlacementReason(winner, loser,
                $"「{winner}」を「{loser}」より後ろに配置しました（{winner} の上書きを反映）。"));
        }

        return new OptimizeResult(order, reasons, unresolved);
    }

    private static string? DecideByRole(string a, string b, IReadOnlyDictionary<string, ModRole> roles)
    {
        var ra = roles.GetValueOrDefault(a, ModRole.Unknown);
        var rb = roles.GetValueOrDefault(b, ModRole.Unknown);
        int Rank(ModRole r) => r switch
        {
            ModRole.VisualOverride => 2, // 勝たせたい
            ModRole.BaseLayer => 0,      // 負けてよい
            _ => 1,
        };
        if (ra == rb) return null;
        if (ra == ModRole.Unknown || rb == ModRole.Unknown) return null;
        return Rank(ra) > Rank(rb) ? a : Rank(rb) > Rank(ra) ? b : null;
    }

    private static int IndexOf(List<string> list, string id)
        => list.FindIndex(x => string.Equals(x, id, StringComparison.OrdinalIgnoreCase));
}
