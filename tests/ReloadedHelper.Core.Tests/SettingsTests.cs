using System.IO;

namespace ReloadedHelper.Core.Tests;

public class SettingsTests
{
    [Fact]
    public void Save_then_load_roundtrips()
    {
        var dir = Path.Combine(Path.GetTempPath(), "rh_set_" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(dir, "settings.json");
        try
        {
            SettingsStore.Save(path, new AppSettings { ReloadedInstallPath = @"C:\FreeSoft\Reloaded-II" });
            var loaded = SettingsStore.Load(path);
            Assert.Equal(@"C:\FreeSoft\Reloaded-II", loaded.ReloadedInstallPath);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void Load_missing_file_returns_empty_settings()
    {
        var loaded = SettingsStore.Load(Path.Combine(Path.GetTempPath(), "rh_nope_" + Guid.NewGuid().ToString("N") + ".json"));
        Assert.Null(loaded.ReloadedInstallPath);
    }
}
