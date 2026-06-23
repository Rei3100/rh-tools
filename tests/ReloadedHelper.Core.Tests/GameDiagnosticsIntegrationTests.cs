using System.IO;
using ReloadedHelper.Core;
using Xunit;

namespace ReloadedHelper.Core.Tests;

public class GameDiagnosticsIntegrationTests
{
    [Fact]
    public void Run_DetectsFileConflict_AcrossTwoMods()
    {
        var root = Directory.CreateTempSubdirectory().FullName;
        string MakeMod(string id)
        {
            var f = Path.Combine(root, id, "P5REssentials", "CPK");
            Directory.CreateDirectory(f);
            File.WriteAllText(Path.Combine(f, "hair.bin"), "x");
            return Path.Combine(root, id);
        }
        var aFolder = MakeMod("a");
        var bFolder = MakeMod("b");

        ModInfo Info(string id, string folder) => new(id, id, "", "1", "",
            Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(),
            new[] { "p5r" }, null, null, null, null, folder);

        var catalog = new Dictionary<string, ModInfo>
        {
            ["a"] = Info("a", aFolder),
            ["b"] = Info("b", bFolder),
        };
        var game = new GameInfo("p5r", "P5R", "", null,
            new[] { "a", "b" }, new[] { "a", "b" }, "");

        var result = GameDiagnostics.Run(game, catalog);

        Assert.Contains(result.Conflicts, c => c.PathKey.Contains("hair.bin"));
    }
}
