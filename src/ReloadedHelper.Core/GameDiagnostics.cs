namespace ReloadedHelper.Core;

public sealed record GameDiagnosticsResult(
    IReadOnlyList<FileConflict> Conflicts, IReadOnlyList<Diagnostic> Diagnostics);

public static class GameDiagnostics
{
    public static GameDiagnosticsResult Run(GameInfo game, IReadOnlyDictionary<string, ModInfo> catalog)
    {
        var enabled = new HashSet<string>(game.EnabledMods, StringComparer.OrdinalIgnoreCase);
        var ordered = new List<ModOverrides>();
        foreach (var modId in game.SortedMods)               // 読み込み順
        {
            if (!enabled.Contains(modId)) continue;
            if (!catalog.TryGetValue(modId, out var info)) continue;
            ordered.Add(ModContentScanner.Scan(info.FolderPath, modId));
        }
        var conflicts = ConflictDetector.Detect(ordered);
        var diagnostics = ModDiagnostics.Analyze(game, catalog, conflicts);
        return new GameDiagnosticsResult(conflicts, diagnostics);
    }
}
