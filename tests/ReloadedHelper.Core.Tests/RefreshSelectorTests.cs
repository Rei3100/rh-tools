namespace ReloadedHelper.Core.Tests;

public class RefreshSelectorTests
{
    private static ModInfo Mod(string id, string ver) =>
        new(id, id, "", ver, "", Array.Empty<string>(), Array.Empty<string>(),
            Array.Empty<string>(), new[] { "p5r.exe" }, null, null, null, null, "C:\\x");

    [Fact]
    public void Force_selects_all()
    {
        var cat = new[] { Mod("a", "1.0"), Mod("b", "1.0") };
        var ud = new UserDataFile();
        ud.Mods["a"] = new ModUserData { FetchedVersion = "1.0" };
        Assert.Equal(2, RefreshSelector.Select(cat, ud, force: true).Count);
    }

    [Fact]
    public void Incremental_selects_new_and_version_changed_only()
    {
        var cat = new[] { Mod("a", "1.0"), Mod("b", "2.0"), Mod("c", "1.0") };
        var ud = new UserDataFile();
        ud.Mods["a"] = new ModUserData { FetchedVersion = "1.0" }; // 変化なし → 除外
        ud.Mods["b"] = new ModUserData { FetchedVersion = "1.0" }; // 変化あり → 対象
        // c は未登録 → 対象
        var ids = RefreshSelector.Select(cat, ud, force: false).Select(m => m.ModId).ToList();
        Assert.Equal(new[] { "b", "c" }, ids);
    }
}
