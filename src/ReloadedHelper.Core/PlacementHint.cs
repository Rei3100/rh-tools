using System.Text.RegularExpressions;

namespace ReloadedHelper.Core;

// 作者がMODの説明文に書いた「読み込み順の指示」を、限定的・高精度なパターンで拾う。
// 確信が持てる表現だけを拾い、曖昧／相反する文は None（証拠なし）にする。
public enum PlacementHint { None, Late, Early }

public static class PlacementHintParser
{
    // 「後ろ/下/最後＝このMODを後勝ちにしたい」
    private static readonly Regex Late = new(
        @"load\s+(this\s+)?(mod\s+)?(below|after|last)\b|\bbut\s+below\b|\bat\s+the\s+bottom|highest\s+priority|一番下に|最後に(読み込|ロード|配置|入れ)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // 「前/上/最初＝このMODを土台側にしたい」
    private static readonly Regex Early = new(
        @"load\s+(this\s+)?(mod\s+)?(above|before|first)\b|at\s+the\s+top|lowest\s+priority|一番上に|最初に(読み込|ロード|配置|入れ)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static PlacementHint Parse(ModInfo mod)
    {
        var text = mod.ModDescription;
        if (string.IsNullOrWhiteSpace(text)) return PlacementHint.None;
        bool late = Late.IsMatch(text);
        bool early = Early.IsMatch(text);
        if (late && !early) return PlacementHint.Late;
        if (early && !late) return PlacementHint.Early;
        return PlacementHint.None; // 相反／どちらも無し → 確信なし
    }
}
