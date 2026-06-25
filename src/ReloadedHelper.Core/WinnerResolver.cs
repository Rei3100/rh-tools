namespace ReloadedHelper.Core;

// 競合する全MODの中から「後ろ＝勝ち（反映される側）」にすべき1つを、複数の証拠で必ず決め切る。
// 証拠は強い順にタプル比較し、最後は現在順インデックスで必ず一意化する（＝"決められない"が無い）。
public sealed record WinnerEvidence(
    IReadOnlyList<string> ConflictMods,
    Func<string, PlacementHint> HintOf,           // 作者の配置指示
    Func<string, string, bool> DependsOn,         // a が b に依存
    Func<string, int> ResourceCount,              // 触る資源の総数（少ない＝狙った上書き）
    Func<string, ModType> TypeOf,
    Func<string, int> CurrentIndex);              // 現在順での位置（後ろほど大）

public static class WinnerResolver
{
    public static string Resolve(WinnerEvidence ev)
    {
        string? best = null;
        (int hint, int ext, int spec, int rank, int idx) bestKey = default;
        foreach (var m in ev.ConflictMods)
        {
            var key = KeyFor(m, ev);
            if (best is null || Compare(key, bestKey) > 0) { best = m; bestKey = key; }
        }
        return best!; // ConflictMods は2件以上（呼び出し側が保証）
    }

    private static (int hint, int ext, int spec, int rank, int idx) KeyFor(string m, WinnerEvidence ev)
    {
        int hint = ev.HintOf(m) switch { PlacementHint.Late => 1, PlacementHint.Early => -1, _ => 0 };
        int ext = ev.ConflictMods.Any(o =>
            !string.Equals(o, m, StringComparison.OrdinalIgnoreCase) && ev.DependsOn(m, o)) ? 1 : 0;
        int spec = -ev.ResourceCount(m);            // 資源が少ない＝後ろ
        int rank = ModTypeInfo.Rank(ev.TypeOf(m));  // 弱い tie-break
        int idx = ev.CurrentIndex(m);               // 現在順で後ろほど勝ち＋一意化
        return (hint, ext, spec, rank, idx);
    }

    private static int Compare(
        (int hint, int ext, int spec, int rank, int idx) a,
        (int hint, int ext, int spec, int rank, int idx) b)
    {
        int c;
        if ((c = a.hint.CompareTo(b.hint)) != 0) return c;
        if ((c = a.ext.CompareTo(b.ext)) != 0) return c;
        if ((c = a.spec.CompareTo(b.spec)) != 0) return c;
        if ((c = a.rank.CompareTo(b.rank)) != 0) return c;
        return a.idx.CompareTo(b.idx);
    }
}
