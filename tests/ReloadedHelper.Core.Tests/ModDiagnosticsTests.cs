namespace ReloadedHelper.Core.Tests;

public class ModDiagnosticsTests
{
    private static ModInfo Mod(string id, string[] appIds, string[]? deps = null) =>
        new(id, id, "", "1.0", "", Array.Empty<string>(), deps ?? Array.Empty<string>(),
            Array.Empty<string>(), appIds, null, null, null, null, "C:\\x");

    private static ModInfo LibMod(string id, string[] appIds) =>
        new(id, id, "", "1.0", "", Array.Empty<string>(), Array.Empty<string>(),
            Array.Empty<string>(), appIds, null, null, null, null, "C:\\x", IsLibrary: true);

    private static GameInfo Game(string appId, string[] enabled) =>
        new(appId, appId, "", null, enabled, enabled, "C:\\g");

    [Fact]
    public void Warns_on_appid_mismatch()
    {
        var cat = new Dictionary<string, ModInfo> { ["A"] = Mod("A", new[] { "p4g.exe" }) };
        var diags = ModDiagnostics.Analyze(Game("p5r.exe", new[] { "A" }), cat, Array.Empty<FileConflict>());
        Assert.Contains(diags, d => d.ModId == "A" && d.Severity == DiagnosticSeverity.Warning && d.Message.Contains("向け"));
    }

    [Fact]
    public void Warns_on_disabled_dependency()
    {
        var cat = new Dictionary<string, ModInfo>
        {
            ["A"] = Mod("A", new[] { "p5r.exe" }, new[] { "Lib" }),
            ["Lib"] = Mod("Lib", new[] { "p5r.exe" }),
        };
        // Lib は有効リストに無い
        var diags = ModDiagnostics.Analyze(Game("p5r.exe", new[] { "A" }), cat, Array.Empty<FileConflict>());
        Assert.Contains(diags, d => d.ModId == "A" && d.Severity == DiagnosticSeverity.Warning && d.Message.Contains("無効"));
    }

    [Fact]
    public void No_warning_for_disabled_library_dependency()
    {
        var cat = new Dictionary<string, ModInfo>
        {
            ["A"] = Mod("A", new[] { "p5r.exe" }, new[] { "Lib" }),
            ["Lib"] = LibMod("Lib", new[] { "p5r.exe" }),
        };
        // Lib はライブラリMODで有効リストに無い（正常）
        var diags = ModDiagnostics.Analyze(Game("p5r.exe", new[] { "A" }), cat, Array.Empty<FileConflict>());
        Assert.DoesNotContain(diags, d => d.ModId == "A" && d.Message.Contains("無効"));
    }

    [Fact]
    public void Warns_on_missing_dependency()
    {
        var cat = new Dictionary<string, ModInfo> { ["A"] = Mod("A", new[] { "p5r.exe" }, new[] { "Gone" }) };
        var diags = ModDiagnostics.Analyze(Game("p5r.exe", new[] { "A" }), cat, Array.Empty<FileConflict>());
        Assert.Contains(diags, d => d.ModId == "A" && d.Message.Contains("見つかりません"));
    }

    [Fact]
    public void ConflictDiagnostic_DoesNotTellUserToReorder()
    {
        // ユーザーは並び替えしない・できない。手動並べ替えを促す文言を出さない。
        var cat = new Dictionary<string, ModInfo>
        {
            ["loser"] = Mod("loser", new[] { "p5r.exe" }),
            ["winner"] = Mod("winner", new[] { "p5r.exe" }),
        };
        var conflicts = new[] { new FileConflict("file:a", new[] { "loser", "winner" }, "winner") };
        var diags = ModDiagnostics.Analyze(Game("p5r.exe", new[] { "loser", "winner" }), cat, conflicts);
        Assert.DoesNotContain(diags, d => d.Message.Contains("入れ替え"));
    }

    [Fact]
    public void Info_on_overwritten_loser_aggregated()
    {
        var cat = new Dictionary<string, ModInfo>
        {
            ["A"] = Mod("A", new[] { "p5r.exe" }),
            ["B"] = Mod("B", new[] { "p5r.exe" }),
        };
        var conflicts = new[]
        {
            new FileConflict("p/x.bin", new[] { "A", "B" }, "B"),
            new FileConflict("p/y.bin", new[] { "A", "B" }, "B"),
        };
        var diags = ModDiagnostics.Analyze(Game("p5r.exe", new[] { "A", "B" }), cat, conflicts);
        var loserDiag = Assert.Single(diags, d => d.ModId == "A" && d.Severity == DiagnosticSeverity.Info);
        Assert.Contains("2", loserDiag.Message);     // 2 ファイル集約
        Assert.Contains("B", loserDiag.Message);
    }
}
