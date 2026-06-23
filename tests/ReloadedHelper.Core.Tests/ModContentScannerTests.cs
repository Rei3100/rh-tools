using System.IO;
namespace ReloadedHelper.Core.Tests;

public class ModContentScannerTests
{
    private static string MakeMod(params (string rel, string name)[] files)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"mod-{Guid.NewGuid():N}");
        foreach (var (rel, name) in files)
        {
            var full = Path.Combine(dir, rel.Replace('/', Path.DirectorySeparatorChar), name);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, "x");
        }
        return dir;
    }

    [Fact]
    public void Scan_collects_cpk_and_awb_paths_normalized_lowercase()
    {
        var dir = MakeMod(
            ("P5REssentials/CPK/BASE.CPK/Bustup", "B100_000_00.BIN"),
            ("FEmulator/AWB/BGM_42.AWB", "851_x.adx"),
            ("Readme", "notes.txt"));            // redirect 外は無視
        try
        {
            var o = ModContentScanner.Scan(dir, "m1");
            Assert.Equal("m1", o.ModId);
            Assert.Contains("p5ressentials/cpk/base.cpk/bustup/b100_000_00.bin", o.Paths);
            Assert.Contains("femulator/awb/bgm_42.awb/851_x.adx", o.Paths);
            Assert.Equal(2, o.Paths.Count);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Scan_returns_empty_when_no_redirect_roots()
    {
        var dir = MakeMod(("Something", "a.dll"));
        try { Assert.Empty(ModContentScanner.Scan(dir, "m2").Paths); }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Scan_missing_folder_is_empty()
    {
        Assert.Empty(ModContentScanner.Scan(Path.Combine(Path.GetTempPath(), "nope-xyz"), "m3").Paths);
    }

    [Fact]
    public void Scan_collects_bgme_and_costumes_paths()
    {
        var dir = MakeMod(
            ("BGME/Persona", "music.pme"),
            ("Costumes/Joker/01", "model.gmd"));
        try
        {
            var paths = ModContentScanner.Scan(dir, "m4").Paths;
            Assert.Contains("bgme/persona/music.pme", paths);
            Assert.Contains("costumes/joker/01/model.gmd", paths);
        }
        finally { Directory.Delete(dir, true); }
    }
}
