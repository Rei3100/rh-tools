// tests/ReloadedHelper.Core.Tests/LoadOrderBackupServiceTests.cs
using ReloadedHelper.Core;

namespace ReloadedHelper.Core.Tests;

public class LoadOrderBackupServiceTests : IDisposable
{
    private readonly string _tmpConfig = Path.GetTempFileName();

    public LoadOrderBackupServiceTests()
        => File.WriteAllText(_tmpConfig, """{"EnabledMods":["A","B"]}""");

    public void Dispose()
    {
        File.Delete(_tmpConfig);
        // Clean up backup dirs created by tests so tests are idempotent
        foreach (var appId in new[] { "test-app-backup1", "test-app-prune", "test-app-restore" })
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ReloadedHelper", "backups", appId);
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Backup_CreatesFileInBackupDirectory()
    {
        LoadOrderBackupService.Backup(_tmpConfig, "test-app-backup1");
        var backups = LoadOrderBackupService.ListBackups("test-app-backup1");
        Assert.Single(backups);
        Assert.EndsWith(".json", backups[0]);
    }

    [Fact]
    public void Backup_KeepsOnlyLatestThree()
    {
        for (int i = 0; i < 5; i++)
        {
            Thread.Sleep(10); // ensure different timestamps
            LoadOrderBackupService.Backup(_tmpConfig, "test-app-prune");
        }
        var backups = LoadOrderBackupService.ListBackups("test-app-prune");
        Assert.Equal(3, backups.Count);
    }

    [Fact]
    public void Restore_CopiesBackupToConfigPath()
    {
        LoadOrderBackupService.Backup(_tmpConfig, "test-app-restore");
        var backup = LoadOrderBackupService.ListBackups("test-app-restore")[0];

        var dest = Path.GetTempFileName();
        File.WriteAllText(dest, "{}");
        LoadOrderBackupService.Restore(backup, dest);

        Assert.Equal("""{"EnabledMods":["A","B"]}""", File.ReadAllText(dest));
        File.Delete(dest);
    }
}
