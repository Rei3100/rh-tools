// src/ReloadedHelper.Core/ContentRoleClassifier.cs
using System.IO;

namespace ReloadedHelper.Core;

public sealed record RoleDecision(ModRole Role, string Reason);

// MODの中身（フォルダ構成）を一次情報に役割を決める。
// カテゴリは GameBanana 照合できたMODにしか付かないため、中身を優先して判定する。
public static class ContentRoleClassifier
{
    public static RoleDecision Classify(ModInfo mod, string? category)
    {
        if (mod.IsLibrary)
            return new(ModRole.Library, "ライブラリ指定のため最前に配置");

        var folder = mod.FolderPath;
        if (HasSubdir(folder, "BGME"))
            return new(ModRole.Music, "BGMEフォルダがあるため音楽として配置");
        if (HasSubdir(folder, "Costumes"))
            return new(ModRole.VisualOverride, "衣装フォルダがあるため見た目として後方に配置");

        var byCategory = ModRoleClassifier.Classify(mod, category);
        if (byCategory != ModRole.Unknown)
            return new(byCategory, $"カテゴリ「{category}」のため{ModLayer.Label(byCategory)}に配置");

        if (ModContentScanner.Scan(folder, mod.ModId).Paths.Count > 0)
            return new(ModRole.VisualOverride, "ゲームファイルを上書きするMODのため見た目として後方に配置");

        return new(ModRole.Unknown, "役割を判定できないため末尾に配置");
    }

    private static bool HasSubdir(string folder, string name)
    {
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return false;
        return Directory.GetDirectories(folder)
            .Any(d => string.Equals(Path.GetFileName(d), name, StringComparison.OrdinalIgnoreCase));
    }
}
