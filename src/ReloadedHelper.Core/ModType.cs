namespace ReloadedHelper.Core;

// MODの種類。ランクが小さいほどリスト前方（土台）・大きいほど後方（見た目の上書き）。
public enum ModType
{
    Library, Gameplay, Battle, Event, Music, Model, Costume, SkinTexture, Portrait, Ui, Unknown
}

public static class ModTypeInfo
{
    public static int Rank(ModType t) => t switch
    {
        ModType.Library => 0,
        ModType.Gameplay => 1,
        ModType.Battle => 2,
        ModType.Event => 3,
        ModType.Music => 4,
        ModType.Model => 5,
        ModType.Costume => 6,
        ModType.SkinTexture => 7,
        ModType.Portrait => 8,
        ModType.Ui => 9,
        _ => 10, // Unknown
    };

    public static string Label(ModType t) => t switch
    {
        ModType.Library => "ライブラリ/前提",
        ModType.Gameplay => "ゲーム機能・修正",
        ModType.Battle => "戦闘・ボス",
        ModType.Event => "イベント・ストーリー",
        ModType.Music => "音楽・BGM",
        ModType.Model => "モデル",
        ModType.Costume => "衣装",
        ModType.SkinTexture => "スキン・テクスチャ",
        ModType.Portrait => "立ち絵",
        ModType.Ui => "UI",
        _ => "判定不能",
    };
}
