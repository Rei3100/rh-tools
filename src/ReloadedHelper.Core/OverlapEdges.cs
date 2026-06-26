namespace ReloadedHelper.Core;

// 同じ資源を触るMOD対から「触る資源が多い方 → 少ない方」の辺を作る。
// 少ない方が後＝勝ち（狙い撃ちの小さいMODが、広く触る大きいMODに勝つ＝LOOTの重なり則）。
public static class OverlapEdges
{
    public static IReadOnlyList<(string Before, string After)> Build(
        IReadOnlyList<FileConflict> conflicts,
        IReadOnlyDictionary<string, int> resourceCountByMod)
    {
        int Count(string id) => resourceCountByMod.TryGetValue(id, out var n) ? n : 0;

        var result = new List<(string Before, string After)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var c in conflicts)
        {
            var mods = c.ModIds;
            for (int i = 0; i < mods.Count; i++)
                for (int j = i + 1; j < mods.Count; j++)
                {
                    var a = mods[i];
                    var b = mods[j];
                    if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) continue;

                    int ca = Count(a), cb = Count(b);
                    if (ca == cb) continue; // 同数 → 辺なし（タイブレークに委ねる）

                    var (before, after) = ca > cb ? (a, b) : (b, a); // 多い→少ない
                    var key = $"{before.ToLowerInvariant()}{after.ToLowerInvariant()}";
                    if (seen.Add(key)) result.Add((before, after));
                }
        }
        return result;
    }
}
