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
    string FolderPath)
{
    public string? IconPath =>
        string.IsNullOrEmpty(IconFileName) ? null : Path.Combine(FolderPath, IconFileName);

    public string DisplayName => string.IsNullOrEmpty(ModName) ? ModId : ModName;
}
