// THROWAWAY diagnostic — dumps the real install's classification to inspect accuracy. Delete after use.
using System.IO;
using System.Text;
using ReloadedHelper.Core;
using Xunit;

namespace ReloadedHelper.Core.Tests;

public class _RealInstallDump
{
    private const string Root = @"C:\FreeSoft\Reloaded-II";
    private const string Out = @"C:\Users\rainb\src\reloaded-helper\classification-dump.txt";

    [Fact]
    public void Dump()
    {
        var install = new ReloadedInstall(Root);
        if (!install.IsValid) { File.WriteAllText(Out, "install not valid: " + install.ModsDir); return; }

        var catalog = ModCatalog.LoadAll(install.ModsDir);
        var games = GameCatalog.LoadAll(install.AppsDir);
        var game = games.FirstOrDefault(g => string.Equals(g.AppId, "p5r.exe", System.StringComparison.OrdinalIgnoreCase));
        if (game is null) { File.WriteAllText(Out, "no p5r game. games=" + string.Join(",", games.Select(g => g.AppId))); return; }

        var userData = UserDataStore.Load(UserDataStore.DefaultPath);
        var entries = LoadOrderBuilder.Build(game, catalog, userData);

        var diag = GameDiagnostics.Run(game, catalog);
        var resByMod = diag.Resources.ToDictionary(r => r.ModId, r => r.Resources, System.StringComparer.OrdinalIgnoreCase);

        // dependents count: how many catalog mods depend on this mod
        var dependents = new Dictionary<string, int>(System.StringComparer.OrdinalIgnoreCase);
        foreach (var kv in catalog)
            foreach (var dep in kv.Value.Dependencies)
                dependents[dep] = dependents.TryGetValue(dep, out var n) ? n + 1 : 1;

        var enabled = new HashSet<string>(game.EnabledMods, System.StringComparer.OrdinalIgnoreCase);

        var rows = new List<(ModType type, string id, int deps, bool lib, int rescount, string reason, string cat)>();
        foreach (var e in entries)
        {
            if (!enabled.Contains(e.ModId)) continue; // 有効MODのみ（自動配置の対象）
            var info = e.Info ?? (catalog.TryGetValue(e.ModId, out var ci) ? ci : null);
            if (info is null) continue;
            var res = resByMod.TryGetValue(e.ModId, out var r) ? r : System.Array.Empty<ReloadedHelper.Core.Analyzers.ResourceKey>();
            var dcount = dependents.TryGetValue(e.ModId, out var dc0) ? dc0 : 0;
            var d = ModTypeClassifier.Classify(info, e.Category, res, dcount);
            rows.Add((d.Type, e.ModId, dependents.TryGetValue(e.ModId, out var dc) ? dc : 0,
                info.IsLibrary, res.Count, d.Reason, e.Category ?? ""));
        }

        var sb = new StringBuilder();
        sb.AppendLine($"=== REAL INSTALL CLASSIFICATION DUMP (p5r, enabled={rows.Count}) ===");
        // group by type, show counts
        foreach (var g in rows.GroupBy(x => x.type).OrderBy(g => ModTypeInfo.Rank(g.Key)))
            sb.AppendLine($"  {ModTypeInfo.Label(g.Key),-16} : {g.Count()}");
        sb.AppendLine();
        sb.AppendLine("--- mods with dependents>=3 (likely frameworks/base) and their assigned type ---");
        foreach (var x in rows.Where(x => x.deps >= 3).OrderByDescending(x => x.deps))
            sb.AppendLine($"  deps={x.deps,-3} lib={x.lib,-5} type={ModTypeInfo.Label(x.type),-16} {x.id}   [{x.reason}]");
        sb.AppendLine();
        sb.AppendLine("--- full list (type | deps | lib | res | id | reason | gbCategory) ---");
        foreach (var x in rows.OrderBy(x => ModTypeInfo.Rank(x.type)).ThenByDescending(x => x.deps).ThenBy(x => x.id))
            sb.AppendLine($"{ModTypeInfo.Label(x.type),-16} | d{x.deps,-2} | {(x.lib ? "LIB" : "   ")} | r{x.rescount,-3} | {x.id,-48} | {x.reason} | {x.cat}");

        // --- 最終順序の検証（新エンジン） ---
        var depMap = catalog.ToDictionary(
            kv => kv.Key, kv => (IReadOnlyList<string>)kv.Value.Dependencies, System.StringComparer.OrdinalIgnoreCase);
        var typeMap = rows.ToDictionary(x => x.id, x => x.type, System.StringComparer.OrdinalIgnoreCase);
        var resCount = diag.Resources.ToDictionary(r => r.ModId, r => r.Resources.Count, System.StringComparer.OrdinalIgnoreCase);
        var opt = ConstraintGraphOptimizer.Optimize(game.SortedMods, depMap, diag.Conflicts, typeMap, resCount);
        sb.AppendLine();
        sb.AppendLine("--- 新エンジン最終順序 先頭25件 ---");
        foreach (var id in opt.Order.Where(enabled.Contains).Take(25))
            sb.AppendLine($"  {(typeMap.TryGetValue(id, out var t) ? ModTypeInfo.Label(t) : "?"),-16} {id}");

        File.WriteAllText(Out, sb.ToString());
        Assert.True(rows.Count > 0, "no rows");
    }
}
