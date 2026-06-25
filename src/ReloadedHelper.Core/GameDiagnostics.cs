using ReloadedHelper.Core.Analyzers;

namespace ReloadedHelper.Core;

public sealed record GameDiagnosticsResult(
    IReadOnlyList<FileConflict> Conflicts,
    IReadOnlyList<Diagnostic> Diagnostics,
    IReadOnlyList<ModResources> Resources);

public static class GameDiagnostics
{
    private static IReadOnlyList<IModAnalyzer> Analyzers() => new IModAnalyzer[]
    {
        new FileReplaceAnalyzer(),
        new BgmeAnalyzer(),
        new CostumeAnalyzer(),
    };

    public static GameDiagnosticsResult Run(GameInfo game, IReadOnlyDictionary<string, ModInfo> catalog)
    {
        var enabled = new HashSet<string>(game.EnabledMods, StringComparer.OrdinalIgnoreCase);
        var analysis = new ModAnalysis(Analyzers());

        var ordered = new List<ModResources>();
        var structureWarnings = new List<StructureWarning>();
        foreach (var modId in game.SortedMods)               // 読み込み順
        {
            if (!enabled.Contains(modId)) continue;
            if (!catalog.TryGetValue(modId, out var info)) continue;
            ordered.Add(analysis.Analyze(info));
            structureWarnings.AddRange(StructureAnalyzer.Check(info));
        }

        var conflicts = ConflictDetector.Detect(ordered);
        var diagnostics = ModDiagnostics.Analyze(game, catalog, conflicts, structureWarnings);
        return new GameDiagnosticsResult(conflicts, diagnostics, ordered);
    }
}
