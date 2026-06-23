using ReloadedHelper.Core;
using ReloadedHelper.Core.Analyzers;
using Xunit;

namespace ReloadedHelper.Core.Tests.Analyzers;

public class ModAnalysisTests
{
    private sealed class FakeAnalyzer(ResourceKey[] keys) : IModAnalyzer
    {
        public IReadOnlyList<ResourceKey> Analyze(ModInfo mod) => keys;
    }
    private sealed class ThrowingAnalyzer : IModAnalyzer
    {
        public IReadOnlyList<ResourceKey> Analyze(ModInfo mod) => throw new InvalidOperationException("boom");
    }
    private static ModInfo Mod() => new("m", "M", "", "1", "",
        Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(),
        null, null, null, null, "");

    [Fact]
    public void Analyze_CombinesAllAnalyzerResources()
    {
        var a = new ModAnalysis(new IModAnalyzer[]
        {
            new FakeAnalyzer(new[]{ new ResourceKey(ResourceKind.File,"x") }),
            new FakeAnalyzer(new[]{ new ResourceKey(ResourceKind.Song,"1") }),
        });
        var res = a.Analyze(Mod());
        Assert.Equal(2, res.Resources.Count);
    }

    [Fact]
    public void Analyze_RecordsFailure_ButContinues()
    {
        var a = new ModAnalysis(new IModAnalyzer[]
        {
            new ThrowingAnalyzer(),
            new FakeAnalyzer(new[]{ new ResourceKey(ResourceKind.File,"x") }),
        });
        var res = a.Analyze(Mod());
        Assert.Single(res.Resources);
        Assert.Single(a.Failures);
    }
}
