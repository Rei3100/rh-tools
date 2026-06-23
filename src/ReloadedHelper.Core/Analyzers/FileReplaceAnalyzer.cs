namespace ReloadedHelper.Core.Analyzers;

public sealed class FileReplaceAnalyzer : IModAnalyzer
{
    public IReadOnlyList<ResourceKey> Analyze(ModInfo mod)
    {
        var overrides = ModContentScanner.Scan(mod.FolderPath, mod.ModId);
        var result = new List<ResourceKey>(overrides.Paths.Count);
        foreach (var p in overrides.Paths)
            result.Add(new ResourceKey(ResourceKind.File, p));
        return result;
    }
}
