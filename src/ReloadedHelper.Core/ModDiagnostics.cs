using ReloadedHelper.Core.Analyzers;

namespace ReloadedHelper.Core;

public enum DiagnosticSeverity { Info, Warning }

public sealed record Diagnostic(string ModId, DiagnosticSeverity Severity, string Message);

public static class ModDiagnostics
{
    public static IReadOnlyList<Diagnostic> Analyze(
        GameInfo game,
        IReadOnlyDictionary<string, ModInfo> catalog,
        IReadOnlyList<FileConflict> conflicts,
        IReadOnlyList<StructureWarning>? structureWarnings = null)
    {
        var enabled = new HashSet<string>(game.EnabledMods, StringComparer.OrdinalIgnoreCase);
        var result = new List<Diagnostic>();

        foreach (var modId in game.EnabledMods)
        {
            if (!catalog.TryGetValue(modId, out var info)) continue;

            if (info.SupportedAppIds.Count > 0 &&
                !info.SupportedAppIds.Contains(game.AppId, StringComparer.OrdinalIgnoreCase))
            {
                result.Add(new Diagnostic(modId, DiagnosticSeverity.Warning,
                    $"このMODは「{game.DisplayName}」向けではない可能性があります（対象: {string.Join(", ", info.SupportedAppIds)}）。"));
            }

            foreach (var dep in info.Dependencies)
            {
                if (!catalog.ContainsKey(dep))
                    result.Add(new Diagnostic(modId, DiagnosticSeverity.Warning,
                        $"必要な依存MOD「{dep}」が見つかりません。動作しない可能性があります。"));
                else if (!enabled.Contains(dep) && !catalog[dep].IsLibrary)
                    result.Add(new Diagnostic(modId, DiagnosticSeverity.Warning,
                        $"必要な依存MOD「{DisplayName(catalog, dep)}」が無効です。有効にしてください。"));
            }
        }

        // 競合の集約：(敗者, 勝者) ごとにファイル数を数える
        var pairCount = new Dictionary<(string Loser, string Winner), int>();
        foreach (var c in conflicts)
            foreach (var m in c.ModIds)
                if (!string.Equals(m, c.WinnerModId, StringComparison.OrdinalIgnoreCase))
                {
                    var key = (m, c.WinnerModId);
                    pairCount[key] = pairCount.TryGetValue(key, out var n) ? n + 1 : 1;
                }

        foreach (var (key, count) in pairCount)
            result.Add(new Diagnostic(key.Loser, DiagnosticSeverity.Info,
                $"このMODの {count} 個の項目が「{DisplayName(catalog, key.Winner)}」に上書きされています（読み込み順で後のMODが優先）。意図しない場合は順序を入れ替えてください。"));

        if (structureWarnings is not null)
            foreach (var w in structureWarnings)
                result.Add(new Diagnostic(w.ModId, DiagnosticSeverity.Warning, w.Message));

        return result;
    }

    private static string DisplayName(IReadOnlyDictionary<string, ModInfo> catalog, string modId) =>
        catalog.TryGetValue(modId, out var info) && info.ModName.Length > 0 ? info.ModName : modId;
}
