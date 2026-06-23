using System.IO;

namespace ReloadedHelper.Core;

public sealed record ModInfo(
    string ModId,
    string ModName,
    string ModAuthor,
    string ModVersion,
    string ModDescription,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> Dependencies,
    IReadOnlyList<string> OptionalDependencies,
    IReadOnlyList<string> SupportedAppIds,
    string? ProjectUrl,
    string? GitHubUserName,
    string? GitHubRepositoryName,
    string? IconFileName,
    string FolderPath,
    bool IsLibrary = false)
{
    public string? IconPath =>
        string.IsNullOrEmpty(IconFileName) ? null : Path.Combine(FolderPath, IconFileName);

    public string DisplayName => string.IsNullOrEmpty(ModName) ? ModId : ModName;
}

public sealed record GameInfo(
    string AppId,
    string AppName,
    string AppLocation,
    string? IconFileName,
    IReadOnlyList<string> EnabledMods,
    IReadOnlyList<string> SortedMods,
    string FolderPath)
{
    public string? IconPath =>
        string.IsNullOrEmpty(IconFileName) ? null : Path.Combine(FolderPath, IconFileName);

    public string DisplayName => string.IsNullOrEmpty(AppName) ? AppId : AppName;
}

public sealed record ModLoadEntry(
    int Order,
    string ModId,
    ModInfo? Info,
    bool Enabled,
    string? Category = null,
    bool IsLibrary = false,
    string? PlacementReason = null)
{
    public string DisplayName =>
        Info is { ModName.Length: > 0 } ? Info.ModName : ModId;

    public string? CategoryLabel =>
        IsLibrary ? "フレームワーク" :
        Category switch
        {
            "Sound" => "サウンド",
            "Skin" => "スキン",
            "Texture" => "テクスチャ",
            "UI" => "UI",
            "Gameplay Mechanics" => "ゲームプレイ",
            "Misc" => "その他",
            "Quality Of Life" => "QOL",
            null => null,
            _ => Category,
        };
}

public sealed record GameBananaModInfo(string Name, string Text, string? Category, string GameId, string? Author);
