using System.IO;

namespace ReloadedHelper.Core.Tests;

public class MainViewModelTests
{
    private static ReloadedInstall BuildInstall()
    {
        var root = Path.Combine(Path.GetTempPath(), "rh_vm_" + Guid.NewGuid().ToString("N"));
        var mods = Path.Combine(root, "Mods");
        var apps = Path.Combine(root, "Apps");
        Directory.CreateDirectory(Path.Combine(mods, "Alpha"));
        File.WriteAllText(Path.Combine(mods, "Alpha", "ModConfig.json"),
            """{ "ModId": "a", "ModName": "Alpha" }""");
        Directory.CreateDirectory(Path.Combine(apps, "p5r.exe"));
        File.WriteAllText(Path.Combine(apps, "p5r.exe", "AppConfig.json"),
            """{ "AppId": "p5r.exe", "AppName": "P5R", "EnabledMods": ["a"], "SortedMods": ["a", "b"] }""");
        return new ReloadedInstall(root);
    }

    [Fact]
    public void Loading_selects_first_game_and_builds_entries()
    {
        var install = BuildInstall();
        try
        {
            var vm = new MainViewModel();
            vm.LoadFrom(install);
            Assert.Single(vm.Games);
            Assert.NotNull(vm.SelectedGame);
            Assert.Equal(2, vm.Entries.Count);
            Assert.Equal("Alpha", vm.Entries[0].DisplayName);
        }
        finally { Directory.Delete(install.RootPath, true); }
    }

    [Fact]
    public void SearchText_filters_entries()
    {
        var install = BuildInstall();
        try
        {
            var vm = new MainViewModel();
            vm.LoadFrom(install);
            vm.SearchText = "alph";
            Assert.Single(vm.Entries);
            vm.SearchText = "";
            Assert.Equal(2, vm.Entries.Count);
        }
        finally { Directory.Delete(install.RootPath, true); }
    }

    [Fact]
    public void IsUpdating_fires_PropertyChanged()
    {
        var vm = new MainViewModel();
        var changed = new List<string>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName ?? "");

        vm.IsUpdating = true;

        Assert.Contains("IsUpdating", changed);
    }

    [Fact]
    public void UpdateProgress_fires_PropertyChanged()
    {
        var vm = new MainViewModel();
        var changed = new List<string>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName ?? "");

        vm.UpdateProgress = "更新中 1/10 件...";

        Assert.Contains("UpdateProgress", changed);
        Assert.Equal("更新中 1/10 件...", vm.UpdateProgress);
    }

    private static ModInfo Info(string id, string name) =>
        new(id, name, "old", "1.0", "olddesc", Array.Empty<string>(), Array.Empty<string>(),
            Array.Empty<string>(), new[] { "p5r.exe" }, null, null, null, null, "C:\\x");

    [Fact]
    public void ApplyMetadataToRow_replaces_matching_entry()
    {
        var vm = new MainViewModel();
        vm.Entries.Add(new ModLoadEntry(1, "m1", Info("m1", "Old"), true));
        vm.Entries.Add(new ModLoadEntry(2, "m2", Info("m2", "Keep"), true));

        vm.ApplyMetadataToRow("m1", "新名", "新説明", "Skin", "author");

        var e = vm.Entries.First(x => x.ModId == "m1");
        Assert.Equal("新名", e.Info!.ModName);
        Assert.Equal("新説明", e.Info.ModDescription);
        Assert.Equal("author", e.Info.ModAuthor);
        Assert.Equal("Skin", e.Category);
        Assert.Equal("Keep", vm.Entries.First(x => x.ModId == "m2").Info!.ModName); // 無関係は不変
    }

    [Fact]
    public void ApplyMetadataToRow_ignores_unknown_id()
    {
        var vm = new MainViewModel();
        vm.Entries.Add(new ModLoadEntry(1, "m1", Info("m1", "Old"), true));
        vm.ApplyMetadataToRow("nope", "x", "y", null, null); // 例外を投げない
        Assert.Single(vm.Entries);
    }
}
