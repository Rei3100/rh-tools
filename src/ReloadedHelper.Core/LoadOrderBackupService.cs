// src/ReloadedHelper.Core/LoadOrderBackupService.cs
namespace ReloadedHelper.Core;

public static class LoadOrderBackupService
{
    private static string BackupDir(string appId) => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ReloadedHelper", "backups", appId);

    public static void Backup(string configPath, string appId)
    {
        var dir = BackupDir(appId);
        Directory.CreateDirectory(dir);
        var stamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss-fff");
        File.Copy(configPath, Path.Combine(dir, $"{stamp}.json"), overwrite: true);
        Prune(dir);
    }

    private static void Prune(string dir)
    {
        var old = Directory.GetFiles(dir, "*.json")
            .OrderByDescending(f => f)
            .Skip(3);
        foreach (var f in old) File.Delete(f);
    }

    public static IReadOnlyList<string> ListBackups(string appId) =>
        Directory.Exists(BackupDir(appId))
            ? Directory.GetFiles(BackupDir(appId), "*.json")
                       .OrderByDescending(f => f)
                       .ToArray()
            : Array.Empty<string>();

    public static void Restore(string backupPath, string configPath) =>
        File.Copy(backupPath, configPath, overwrite: true);
}
