using System.IO;

namespace ReloadedHelper.Core;

public sealed class ReloadedInstall(string rootPath)
{
    public string RootPath { get; } = rootPath;
    public string ModsDir => Path.Combine(RootPath, "Mods");
    public string AppsDir => Path.Combine(RootPath, "Apps");
    public bool IsValid => Directory.Exists(ModsDir) && Directory.Exists(AppsDir);
}
