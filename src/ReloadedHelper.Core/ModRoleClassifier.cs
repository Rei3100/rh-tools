namespace ReloadedHelper.Core;

public enum ModRole { VisualOverride, BaseLayer, Music, Library, Unknown }

public static class ModRoleClassifier
{
    // カテゴリ→役割の知識テーブル（拡張ポイント：値を足すだけで賢くなる）。
    public static ModRole Classify(ModInfo mod, string? category)
    {
        if (mod.IsLibrary) return ModRole.Library;
        return category switch
        {
            "Skin" or "Texture" or "UI" => ModRole.VisualOverride,
            "Sound" => ModRole.Music,
            "Gameplay Mechanics" => ModRole.BaseLayer,
            _ => ModRole.Unknown,
        };
    }
}
