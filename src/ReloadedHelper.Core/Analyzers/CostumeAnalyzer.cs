using System.IO;

namespace ReloadedHelper.Core.Analyzers;

public sealed class CostumeAnalyzer : IModAnalyzer
{
    public IReadOnlyList<ResourceKey> Analyze(ModInfo mod)
    {
        var root = Path.Combine(mod.FolderPath, "Costumes");
        if (!Directory.Exists(root)) return Array.Empty<ResourceKey>();

        var result = new List<ResourceKey>();
        foreach (var charDir in Directory.GetDirectories(root))
        {
            var character = Path.GetFileName(charDir).ToLowerInvariant();
            foreach (var slotDir in Directory.GetDirectories(charDir))
            {
                var slot = Path.GetFileName(slotDir).ToLowerInvariant();
                result.Add(new ResourceKey(ResourceKind.Costume, $"{character}/{slot}"));
            }
        }
        return result;
    }
}
