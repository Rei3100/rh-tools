using System.IO;

namespace ReloadedHelper.Core.Tests;

public class CatalogsTests
{
    private static string NewTempDir()
    {
        var p = Path.Combine(Path.GetTempPath(), "rh_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(p);
        return p;
    }

    [Fact]
    public void ModCatalog_loads_each_folder_keyed_by_modid_and_skips_bad()
    {
        var mods = NewTempDir();
        try
        {
            Directory.CreateDirectory(Path.Combine(mods, "A"));
            File.WriteAllText(Path.Combine(mods, "A", "ModConfig.json"),
                """{ "ModId": "mod.a", "ModName": "A" }""");
            Directory.CreateDirectory(Path.Combine(mods, "B"));
            File.WriteAllText(Path.Combine(mods, "B", "ModConfig.json"),
                "{ this is not json");
            Directory.CreateDirectory(Path.Combine(mods, "C")); // no ModConfig.json

            var catalog = ModCatalog.LoadAll(mods);

            Assert.True(catalog.ContainsKey("mod.a"));
            Assert.Single(catalog);
        }
        finally { Directory.Delete(mods, true); }
    }

    [Fact]
    public void GameCatalog_loads_appconfigs()
    {
        var apps = NewTempDir();
        try
        {
            Directory.CreateDirectory(Path.Combine(apps, "p5r.exe"));
            File.WriteAllText(Path.Combine(apps, "p5r.exe", "AppConfig.json"),
                """{ "AppId": "p5r.exe", "AppName": "P5R", "SortedMods": ["x"] }""");

            var games = GameCatalog.LoadAll(apps);

            Assert.Single(games);
            Assert.Equal("p5r.exe", games[0].AppId);
        }
        finally { Directory.Delete(apps, true); }
    }

    [Fact]
    public void Install_derives_dirs_and_validity()
    {
        var root = NewTempDir();
        try
        {
            var install = new ReloadedInstall(root);
            Assert.False(install.IsValid);
            Directory.CreateDirectory(install.ModsDir);
            Directory.CreateDirectory(install.AppsDir);
            Assert.True(install.IsValid);
        }
        finally { Directory.Delete(root, true); }
    }
}
