namespace ReloadedHelper.Core;

public enum ModRole { VisualOverride, BaseLayer, Music, Library, Unknown }

public static class ModRoleClassifier
{
    // カテゴリ→役割の知識テーブル（拡張ポイント：値を足すだけで賢くなる）。
    // GameBanana の実カテゴリ（複数形・別名）に合わせる。大小・前後空白は無視。
    public static ModRole Classify(ModInfo mod, string? category)
    {
        if (mod.IsLibrary) return ModRole.Library;
        var key = category?.Trim().ToLowerInvariant();
        return key switch
        {
            // 見た目を上書きする＝競合したら勝たせたい
            "skin" or "skins" or
            "texture" or "textures" or "texture packs" or
            "ui" or "user interface" or
            "portrait" or "portraits" or
            "model" or "models" or "model packs" or
            "persona" or "personas" => ModRole.VisualOverride,
            // 音楽は別枠（曲ID単位で判定）
            "sound" or "music" => ModRole.Music,
            // 土台・大型改修＝競合したら負けてよい
            "gameplay mechanics" or "characters" or "character" or
            "cutscenes / fmv" or "cutscenes" => ModRole.BaseLayer,
            // それ以外（QOL/Fixes/Cheats/Events/Misc 等）は不明＝自動判断せず要確認
            _ => ModRole.Unknown,
        };
    }
}
