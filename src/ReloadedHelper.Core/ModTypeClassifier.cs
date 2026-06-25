// src/ReloadedHelper.Core/ModTypeClassifier.cs
using System.IO;

namespace ReloadedHelper.Core;

public sealed record TypeDecision(ModType Type, string Reason);

// MODの種類を、フォルダ・カテゴリ・名前/説明のキーワードから判定する。
// GameBananaカテゴリは欠落しがちなので、名前・IDの語からも種類を拾う。
public static class ModTypeClassifier
{
    // 名前・説明から種類を拾うキーワード。優先順（最初に当たったものを採用）。
    private static readonly (ModType Type, string[] Keywords)[] KeywordRules =
    {
        (ModType.Music, new[] { "music", "bgm", "sound", "ost", "soundtrack", "song", "サウンド", "音楽" }),
        (ModType.Portrait, new[] { "portrait", "bustup", "立ち絵", "ポートレート", "バストアップ" }),
        (ModType.Costume, new[] { "costume", "outfit", "衣装", "コスチューム", "swimsuit", "水着" }),
        (ModType.SkinTexture, new[] { "skin", "texture", "スキン", "テクスチャ", "retexture", "recolor", "リテクスチャ" }),
        (ModType.Model, new[] { "model", "モデル", "mesh" }),
        (ModType.Ui, new[] { "interface", "hud", "カットイン", "cutin", "cut-in", "dualsense" }),
        (ModType.Battle, new[] { "battle", "boss", "bossfight", "戦闘", "ボス", "encounter" }),
        (ModType.Event, new[] { "cutscene", "fmv", "story", "event", "イベント", "ストーリー", "カットシーン" }),
        (ModType.Gameplay, new[] { "cheat", "fix", "patch", "qol", "tweak", "disable", "difficulty", "修正", "パッチ", "チート" }),
    };

    // 信頼できる特定カテゴリ → 種類。
    private static readonly Dictionary<string, ModType> CategoryMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["skins"] = ModType.SkinTexture,
        ["skin"] = ModType.SkinTexture,
        ["textures"] = ModType.SkinTexture,
        ["texture"] = ModType.SkinTexture,
        ["texture packs"] = ModType.SkinTexture,
        ["portraits"] = ModType.Portrait,
        ["portrait"] = ModType.Portrait,
        ["models"] = ModType.Model,
        ["model packs"] = ModType.Model,
        ["personas"] = ModType.Model,
        ["persona"] = ModType.Model,
        ["user interface"] = ModType.Ui,
        ["ui"] = ModType.Ui,
        ["battles"] = ModType.Battle,
        ["cutscenes / fmv"] = ModType.Event,
        ["cutscenes"] = ModType.Event,
        ["sound"] = ModType.Music,
        ["music"] = ModType.Music,
    };

    // 役割がぼやけたカテゴリ → ゲーム機能（土台寄り）。
    private static readonly HashSet<string> VagueGameplay = new(StringComparer.OrdinalIgnoreCase)
    {
        "other/misc", "qol", "fixes", "cheats", "events", "bomb/defuse",
    };

    public static TypeDecision Classify(ModInfo mod, string? category)
    {
        if (mod.IsLibrary)
            return new(ModType.Library, "ライブラリ指定のため前方に配置");

        var folder = mod.FolderPath;
        if (HasSubdir(folder, "BGME"))
            return new(ModType.Music, "BGMEフォルダがあるため音楽として配置");
        if (HasSubdir(folder, "Costumes"))
            return new(ModType.Costume, "衣装フォルダがあるため衣装として配置");

        var cat = category?.Trim();
        if (!string.IsNullOrEmpty(cat) && CategoryMap.TryGetValue(cat, out var byCat))
            return new(byCat, $"カテゴリ「{cat}」のため{ModTypeInfo.Label(byCat)}に配置");

        var blob = $"{mod.ModId} {mod.ModName} {mod.ModDescription}".ToLowerInvariant();
        foreach (var (type, kws) in KeywordRules)
            if (kws.Any(k => blob.Contains(k)))
                return new(type, $"名前・説明から{ModTypeInfo.Label(type)}と判定");

        if (!string.IsNullOrEmpty(cat) &&
            (cat.Equals("Characters", StringComparison.OrdinalIgnoreCase) ||
             cat.Equals("Character", StringComparison.OrdinalIgnoreCase)))
            return new(ModType.Model, "カテゴリ「Characters」のためモデルに配置");

        if (!string.IsNullOrEmpty(cat) && VagueGameplay.Contains(cat))
            return new(ModType.Gameplay, $"カテゴリ「{cat}」のためゲーム機能に配置");

        if (ModContentScanner.Scan(folder, mod.ModId).Paths.Count > 0)
            return new(ModType.SkinTexture, "ゲームファイルを上書きするため見た目として配置");

        return new(ModType.Unknown, "手がかりがないため末尾に配置");
    }

    private static bool HasSubdir(string folder, string name)
    {
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return false;
        return Directory.GetDirectories(folder)
            .Any(d => string.Equals(Path.GetFileName(d), name, StringComparison.OrdinalIgnoreCase));
    }
}
