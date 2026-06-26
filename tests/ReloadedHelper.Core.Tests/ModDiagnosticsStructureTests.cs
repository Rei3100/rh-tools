using ReloadedHelper.Core;
using ReloadedHelper.Core.Analyzers;
using Xunit;

namespace ReloadedHelper.Core.Tests;

public class ModDiagnosticsStructureTests
{
    private static GameInfo Game() => new("p5r", "P5R", "", null,
        new[] { "m" }, new[] { "m" }, "");
    private static Dictionary<string, ModInfo> Catalog() => new()
    {
        ["m"] = new("m", "M", "", "1", "", Array.Empty<string>(), Array.Empty<string>(),
            Array.Empty<string>(), new[] { "p5r" }, null, null, null, null, ""),
    };

    [Fact]
    public void Analyze_IncludesStructureWarnings()
    {
        var warnings = new[] { new StructureWarning("m", "置き場所が変です") };
        var result = ModDiagnostics.Analyze(Game(), Catalog(),
            Array.Empty<FileConflict>(), warnings);

        Assert.Contains(result, d => d.ModId == "m"
            && d.Severity == DiagnosticSeverity.Warning
            && d.Message.Contains("置き場所"));
    }
}
