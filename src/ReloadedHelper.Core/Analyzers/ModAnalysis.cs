namespace ReloadedHelper.Core.Analyzers;

public sealed class ModAnalysis
{
    private readonly IReadOnlyList<IModAnalyzer> _analyzers;
    private readonly List<string> _failures = new();

    public ModAnalysis(IReadOnlyList<IModAnalyzer> analyzers) => _analyzers = analyzers;

    public IReadOnlyList<string> Failures => _failures;

    public ModResources Analyze(ModInfo mod)
    {
        var all = new List<ResourceKey>();
        foreach (var analyzer in _analyzers)
        {
            try { all.AddRange(analyzer.Analyze(mod)); }
            catch (Exception ex)
            {
                _failures.Add($"{mod.ModId}: {analyzer.GetType().Name} 失敗: {ex.Message}");
            }
        }
        return new ModResources(mod.ModId, all);
    }
}
