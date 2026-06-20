using System.IO;

namespace ReloadedHelper.Core.Tests;

public class UserDataTests
{
    [Fact]
    public void Save_then_load_roundtrips_translation()
    {
        var dir = Path.Combine(Path.GetTempPath(), "rh_ud_" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(dir, "userdata.json");
        try
        {
            var file = new UserDataFile();
            file.Mods["mod.a"] = new ModUserData { TranslatedDescription = "翻訳済み" };
            UserDataStore.Save(path, file);

            var loaded = UserDataStore.Load(path);
            Assert.Equal("翻訳済み", loaded.Mods["mod.a"].TranslatedDescription);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}
