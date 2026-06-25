using System.IO;
using ReloadedHelper.Core.Analyzers;
namespace ReloadedHelper.Core.Tests;

public class GameDiagnosticsTests
{
    private static string MakeModWithFile(string overrideRel)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"gmod-{Guid.NewGuid():N}");
        var full = Path.Combine(dir, overrideRel.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, "x");
        return dir;
    }

    private static ModInfo Mod(string id, string folder) =>
        new(id, id, "", "1.0", "", Array.Empty<string>(), Array.Empty<string>(),
            Array.Empty<string>(), new[] { "p5r.exe" }, null, null, null, null, folder);

    // 最小限のゲーム＋カタログを返すヘルパ（資源テスト用）
    private static (GameInfo, Dictionary<string, ModInfo>) MinimalGame()
    {
        var game = new GameInfo("p5r", "P5R", "", null, new[] { "m" }, new[] { "m" }, "");
        var catalog = new Dictionary<string, ModInfo>(StringComparer.OrdinalIgnoreCase)
        {
            ["m"] = new("m", "M", "", "1", "", Array.Empty<string>(), Array.Empty<string>(),
                Array.Empty<string>(), new[] { "p5r" }, null, null, null, null, ""),
        };
        return (game, catalog);
    }

    [Fact]
    public void Run_detects_conflict_between_two_enabled_mods()
    {
        var dirA = MakeModWithFile("P5REssentials/CPK/Base.cpk/x.bin");
        var dirB = MakeModWithFile("P5REssentials/CPK/BASE.CPK/X.BIN"); // 大文字違い＝同一パス扱い
        try
        {
            var cat = new Dictionary<string, ModInfo> { ["A"] = Mod("A", dirA), ["B"] = Mod("B", dirB) };
            var game = new GameInfo("p5r.exe", "P5R", "", null,
                new[] { "A", "B" }, new[] { "A", "B" }, "C:\\g"); // SortedMods=[A,B] → B 後勝ち
            var res = GameDiagnostics.Run(game, cat);

            Assert.NotEmpty(res.Conflicts);
            Assert.Equal("B", res.Conflicts[0].WinnerModId);
            Assert.Contains(res.Diagnostics, d => d.ModId == "A" && d.Severity == DiagnosticSeverity.Info);
        }
        finally { Directory.Delete(dirA, true); Directory.Delete(dirB, true); }
    }

    [Fact]
    public void Run_ExposesAnalyzedResources()
    {
        // 有効MODごとに解析済み資源が返ること（件数は0以上、ModIdが揃う）。
        // ※ 既存テストの Game()/Catalog() ヘルパを流用。詳細な資源数は各 Analyzer テストで担保。
        var (game, catalog) = MinimalGame();
        var result = GameDiagnostics.Run(game, catalog);
        Assert.NotNull(result.Resources);
        Assert.All(result.Resources, r => Assert.False(string.IsNullOrEmpty(r.ModId)));
    }
}
