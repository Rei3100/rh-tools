// tests/ReloadedHelper.Core.Tests/AppConfigWriterTests.cs
using ReloadedHelper.Core;

namespace ReloadedHelper.Core.Tests;

public class AppConfigWriterTests : IDisposable
{
    private readonly string _tmp = Path.GetTempFileName();

    public void Dispose()
    {
        File.Delete(_tmp);
        // Clean up backup dirs created by tests so tests are idempotent
        foreach (var appId in new[] { "p5r.exe", "writer-backup-test" })
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ReloadedHelper", "backups", appId);
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void WriteOrder_UpdatesSortedModsPreservingOtherFields()
    {
        File.WriteAllText(_tmp, """
            {
              "AppId": "p5r.exe",
              "AppName": "P5R",
              "EnabledMods": ["ModA", "ModB", "ModC"],
              "SortedMods": ["ModA", "ModB", "ModC"]
            }
            """);

        AppConfigWriter.WriteOrder(_tmp, "p5r.exe", new[] { "ModC", "ModA", "ModB" });

        var result = AppConfigParser.Parse(File.ReadAllText(_tmp), Path.GetTempPath());
        Assert.Equal(new[] { "ModC", "ModA", "ModB" }, result.SortedMods);
        Assert.Equal("P5R", result.AppName); // other fields preserved
    }

    [Fact]
    public void WriteOrder_CreatesBackupBeforeWriting()
    {
        File.WriteAllText(_tmp, """{"AppId":"test","SortedMods":["A","B"]}""");
        var appId = "writer-backup-test";

        AppConfigWriter.WriteOrder(_tmp, appId, new[] { "B", "A" });

        var backups = LoadOrderBackupService.ListBackups(appId);
        Assert.Single(backups);
        // Backup contains original order
        var original = AppConfigParser.Parse(File.ReadAllText(backups[0]), Path.GetTempPath());
        Assert.Equal(new[] { "A", "B" }, original.SortedMods);
    }
}
