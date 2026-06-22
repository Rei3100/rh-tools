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

    [Fact]
    public void Phase4_fields_roundtrip()
    {
        var dir = Path.Combine(Path.GetTempPath(), "rh_ud4_" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(dir, "userdata.json");
        try
        {
            var file = new UserDataFile();
            var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            file.Mods["mod.a"] = new ModUserData
            {
                GameBananaId = "123456",
                Category = "Sound",
                FetchedAt = now,
                FetchedVersion = "1.2.3"
            };
            UserDataStore.Save(path, file);

            var loaded = UserDataStore.Load(path);
            var m = loaded.Mods["mod.a"];
            Assert.Equal("123456", m.GameBananaId);
            Assert.Equal("Sound", m.Category);
            Assert.Equal(now, m.FetchedAt);
            Assert.Equal("1.2.3", m.FetchedVersion);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void Roundtrip_preserves_new_fields()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ud-{Guid.NewGuid():N}.json");
        try
        {
            var file = new UserDataFile();
            file.Mods["m1"] = new ModUserData
            {
                OriginalName = "Beta Lavenza",
                OriginalDescription = "restores model",
                Author = "lonelycrow"
            };
            UserDataStore.Save(path, file);
            var loaded = UserDataStore.Load(path);

            Assert.Equal("Beta Lavenza", loaded.Mods["m1"].OriginalName);
            Assert.Equal("restores model", loaded.Mods["m1"].OriginalDescription);
            Assert.Equal("lonelycrow", loaded.Mods["m1"].Author);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
