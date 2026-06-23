using System.IO;

namespace ReloadedHelper.Core.Analyzers;

public sealed record StructureWarning(string ModId, string Message);

public static class StructureAnalyzer
{
    private static readonly string[] KnownRoots =
        { "P5REssentials", "FEmulator", "BGME", "Costumes" };

    public static IReadOnlyList<StructureWarning> Check(ModInfo mod)
    {
        var warnings = new List<StructureWarning>();
        if (!Directory.Exists(mod.FolderPath)) return warnings;

        var topNames = Directory.GetDirectories(mod.FolderPath)
            .Select(d => Path.GetFileName(d))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        bool hasRootAtTop = KnownRoots.Any(topNames.Contains);
        if (hasRootAtTop) return warnings; // 正常

        // トップに無いが、1階層下に既知ルートが埋もれていないか
        foreach (var sub in Directory.GetDirectories(mod.FolderPath))
            foreach (var root in KnownRoots)
                if (Directory.Exists(Path.Combine(sub, root)))
                {
                    warnings.Add(new StructureWarning(mod.ModId,
                        $"MODの中身が1階層深い場所（{Path.GetFileName(sub)}）に入っています。" +
                        $"このままでは認識されない可能性があります。"));
                    return warnings;
                }
        return warnings;
    }
}
