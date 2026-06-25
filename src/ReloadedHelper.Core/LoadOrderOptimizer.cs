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
        IReadOnlyDictionary<string, string>? typeReasons = null,
        IReadOnlyDictionary<string, int>? resourceCountByMod = null,
        IReadOnlyDictionary<string, PlacementHint>? hintsByMod = null)
    {
        // 1. 依存を満たす基準順。大域的な種類整列は行わない（競合に無関係なMODは動かさない）。
        var order = LoadOrderSorter.Sort(currentOrder, dependenciesOf).ToList();
        var reasons = new List<PlacementReason>();

        // 証拠の引き当て（依存ソート後の位置を安定 tiebreak に使う）
        var indexSnapshot = order
            .Select((id, i) => (id, i))
            .ToDictionary(x => x.id, x => x.i, StringComparer.OrdinalIgnoreCase);
        int CurrentIndex(string id) => indexSnapshot.TryGetValue(id, out var i) ? i : int.MaxValue;
        int ResourceCount(string id) =>
            resourceCountByMod is not null && resourceCountByMod.TryGetValue(id, out var n) ? n : int.MaxValue;
        PlacementHint HintOf(string id) =>
            hintsByMod is not null && hintsByMod.TryGetValue(id, out var h) ? h : PlacementHint.None;
        ModType TypeOf(string id) => typesByMod.GetValueOrDefault(id, ModType.Unknown);
        bool DependsOn(string mod, string maybeDep) =>
            dependenciesOf.TryGetValue(mod, out var d) && d.Contains(maybeDep, StringComparer.OrdinalIgnoreCase);

        foreach (var c in conflicts)
        {
            if (c.ModIds.Count < 2) continue;

            // 勝者：2件はユーザーの好み（あれば）を最優先、無ければ証拠で必ず決め切る。
            string winner =
                (c.ModIds.Count == 2 ? prefs.GetWinner(appId, c.ModIds[0], c.ModIds[1]) : null)
                ?? WinnerResolver.Resolve(new WinnerEvidence(
                    c.ModIds, HintOf, DependsOn, ResourceCount, TypeOf, CurrentIndex));

            var losers = c.ModIds
                .Where(m => !string.Equals(m, winner, StringComparison.OrdinalIgnoreCase))
                .ToList();

            int wi = IndexOf(order, winner);
            if (wi < 0) continue;

            // 依存を壊す移動はしない（依存＝最強の制約）。勝たせられない事情を正直に記録。
            var blocking = losers.Where(l => DependsOn(l, winner)).ToList();
            if (blocking.Count > 0)
            {
                reasons.Add(new PlacementReason(winner, blocking[^1],
                    $"「{winner}」を後ろにしたいところですが、「{blocking[^1]}」が「{winner}」に依存するため前に保ちます（結果「{blocking[^1]}」側が反映されます）。"));
                continue;
            }

            int maxLoser = losers.Select(l => IndexOf(order, l)).DefaultIfEmpty(-1).Max();
            if (maxLoser < 0 || wi > maxLoser) continue; // すでに全敗者より後ろ＝OK

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

        // Unresolved は廃止（必ず決め切る）。互換のため空で返す。
        return new OptimizeResult(order, reasons,
            System.Array.Empty<(string A, string B)>(), placements);
    }

    private static int IndexOf(List<string> list, string id)
        => list.FindIndex(x => string.Equals(x, id, StringComparison.OrdinalIgnoreCase));
}
