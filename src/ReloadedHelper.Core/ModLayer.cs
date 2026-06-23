namespace ReloadedHelper.Core;

// 役割を「弱い土台(前)〜勝たせたい見た目(後)」の層ランクに対応させる。
// ランクが小さいほどリストの前（弱い・負けてよい）。
public static class ModLayer
{
    public static int Rank(ModRole role) => role switch
    {
        ModRole.Library => 0,        // ライブラリ・前提
        ModRole.BaseLayer => 1,      // 大型改修・システム
        ModRole.Music => 2,          // 音楽・サウンド
        ModRole.VisualOverride => 3, // 見た目の上書き
        _ => 4,                      // Unknown：末尾寄せ
    };

    public static string Label(ModRole role) => role switch
    {
        ModRole.Library => "ライブラリ・前提",
        ModRole.BaseLayer => "大型改修・システム",
        ModRole.Music => "音楽・サウンド",
        ModRole.VisualOverride => "見た目の上書き",
        _ => "個別・その他",
    };
}
