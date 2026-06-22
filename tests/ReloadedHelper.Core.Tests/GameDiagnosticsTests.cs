using System.IO;
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
}
