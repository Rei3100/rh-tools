using System.IO;
using System.Text.Json;

namespace ReloadedHelper.Core.Tests;

public class ModConfigUpdaterTests
{
    private static string NewTempDir()
    {
        var p = Path.Combine(Path.GetTempPath(), "rh_mcu_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(p);
        return p;
    }

    [Fact]
    public void Write_updates_name_and_description_preserves_other_fields()
    {
        var dir = NewTempDir();
        try
        {
            var json = """
                {
                  "ModId": "mod.test",
                  "ModName": "Test Mod",
                  "ModAuthor": "Author",
                  "ModVersion": "1.0.0",
                  "ModDescription": "English description",
                  "SupportedAppId": ["p5r.exe"]
                }
                """;
            File.WriteAllText(Path.Combine(dir, "ModConfig.json"), json);

            ModConfigUpdater.Write(dir, "テストMOD", "日本語説明");

            var written = File.ReadAllText(Path.Combine(dir, "ModConfig.json"));
            using var doc = JsonDocument.Parse(written);
            var root = doc.RootElement;
            Assert.Equal("テストMOD",   root.GetProperty("ModName").GetString());
            Assert.Equal("日本語説明",   root.GetProperty("ModDescription").GetString());
            Assert.Equal("mod.test",    root.GetProperty("ModId").GetString());
            Assert.Equal("Author",      root.GetProperty("ModAuthor").GetString());
            Assert.Equal("1.0.0",       root.GetProperty("ModVersion").GetString());
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Write_does_nothing_when_no_config_file()
    {
        var dir = NewTempDir();
        try
        {
            // No exception, no file created
            ModConfigUpdater.Write(dir, "名前", "説明");
            Assert.False(File.Exists(Path.Combine(dir, "ModConfig.json")));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Write_preserves_japanese_unicode_without_escaping()
    {
        var dir = NewTempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "ModConfig.json"),
                """{"ModId":"x","ModName":"X","ModDescription":"D"}""");

            ModConfigUpdater.Write(dir, "日本語名前", "日本語の説明文");

            var written = File.ReadAllText(Path.Combine(dir, "ModConfig.json"));
            // Unicode エスケープ（日 など）でなく生の日本語で保存される
            Assert.Contains("日本語名前", written);
            Assert.Contains("日本語の説明文", written);
        }
        finally { Directory.Delete(dir, true); }
    }
}
