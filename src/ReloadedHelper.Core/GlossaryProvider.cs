using System.Text.RegularExpressions;

namespace ReloadedHelper.Core;

public static class GlossaryProvider
{
    private static readonly IReadOnlyDictionary<string, (string En, string Ja)[]> Terms =
        new Dictionary<string, (string, string)[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["p5r.exe"] =
            [
                // 複合名は必ず単独名より前に配置（長いものから置換して衝突を防ぐ）
                ("Ryuji Sakamoto",  "坂本竜司"),
                ("Ann Takamaki",    "高巻杏"),
                ("Yusuke Kitagawa", "喜多川祐介"),
                ("Makoto Niijima",  "新島真"),
                ("Futaba Sakura",   "佐倉双葉"),
                ("Haru Okumura",    "奥村春"),
                ("Goro Akechi",     "明智吾郎"),
                ("Phantom Thieves", "怪盗団"),
                ("Velvet Room",     "ベルベットルーム"),
                ("Metaverse",       "異世界"),
                ("Mementos",        "メメントス"),
                ("Confidant",       "コープ"),
                ("Cognitive",       "認知"),
                ("Palace",          "パレス"),
                ("Shadow",          "シャドウ"),
                ("Persona",         "ペルソナ"),
                ("Joker",           "ジョーカー"),
                ("Ryuji",           "竜司"),
                ("Ann",             "杏"),
                ("Yusuke",          "祐介"),
                ("Makoto",          "真"),
                ("Futaba",          "双葉"),
                ("Haru",            "春"),
                ("Morgana",         "モルガナ"),
                ("Akechi",          "明智"),
                ("Lavenza",         "ラヴェンツァ"),
                ("Igor",            "イゴール"),
                ("Sojiro",          "惣治郎"),
                ("Kasumi",          "かすみ"),
            ],
            ["p4g.exe"] =
            [
                ("Investigation Team", "自称特別捜査隊"),
                ("Yu Narukami",        "鳴上悠"),
                ("Midnight Channel",   "マヨナカテレビ"),
                ("Yosuke",             "陽介"),
                ("Chie",               "千枝"),
                ("Yukiko",             "雪子"),
                ("Kanji",              "完二"),
                ("Rise",               "りせ"),
                ("Teddie",             "クマ"),
                ("Naoto",              "直斗"),
            ],
            ["p5s.exe"] =
            [
                ("EMMA",     "エマ"),
                ("Jail",     "監獄"),
                ("Monarch",  "モナーク"),
                ("Sophia",   "ソフィア"),
                ("Zenkichi", "善吉"),
            ],
        };

    public static string Apply(string text, string appId)
    {
        if (!Terms.TryGetValue(appId, out var terms)) return text;
        foreach (var (en, ja) in terms)
            text = Regex.Replace(text, $@"\b{Regex.Escape(en)}\b", ja, RegexOptions.IgnoreCase);
        return text;
    }
}
