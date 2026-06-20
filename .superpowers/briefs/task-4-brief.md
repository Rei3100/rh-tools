# Task 4: ReloadedInstall + catalog loaders (TDD with temp dirs)

This task adds the file-system layer: a small `ReloadedInstall` value
object that knows where Mods/Apps live under the chosen install root, and
two catalog loaders that walk the filesystem and produce dictionaries /
lists of the records from Tasks 2-3.

Strict TDD with **temporary directories** — never read the user's real
Reloaded-II install in tests.

## Files

- Create `src/ReloadedHelper.Core/ReloadedInstall.cs`.
- Create `src/ReloadedHelper.Core/Catalogs.cs` (contains both `ModCatalog` and `GameCatalog`).
- Create `tests/ReloadedHelper.Core.Tests/CatalogsTests.cs`.

## Global constraints

- TFM net10.0; System.Text.Json only; xUnit test-only.
- READ-ONLY: loaders ONLY read; never write or move files.
- No third-party runtime deps.
- Skip non-existent / unparseable mods or apps silently (catch `JsonException`); do not crash on bad data.
- Skip mods with empty `ModId` (would corrupt the dictionary).

## ReloadedInstall

```csharp
using System.IO;

namespace ReloadedHelper.Core;

public sealed class ReloadedInstall(string rootPath)
{
    public string RootPath { get; } = rootPath;
    public string ModsDir => Path.Combine(RootPath, "Mods");
    public string AppsDir => Path.Combine(RootPath, "Apps");
    public bool IsValid => Directory.Exists(ModsDir) && Directory.Exists(AppsDir);
}
```

## Catalogs.cs

```csharp
using System.IO;
using System.Text.Json;

namespace ReloadedHelper.Core;

public static class ModCatalog
{
    public static IReadOnlyDictionary<string, ModInfo> LoadAll(string modsDir)
    {
        var result = new Dictionary<string, ModInfo>(StringComparer.Ordinal);
        if (!Directory.Exists(modsDir)) return result;
        foreach (var folder in Directory.EnumerateDirectories(modsDir))
        {
            var cfg = Path.Combine(folder, "ModConfig.json");
            if (!File.Exists(cfg)) continue;
            try
            {
                var info = ModConfigParser.Parse(File.ReadAllText(cfg), folder);
                if (!string.IsNullOrEmpty(info.ModId)) result[info.ModId] = info;
            }
            catch (JsonException) { /* skip malformed mod */ }
        }
        return result;
    }
}

public static class GameCatalog
{
    public static IReadOnlyList<GameInfo> LoadAll(string appsDir)
    {
        var result = new List<GameInfo>();
        if (!Directory.Exists(appsDir)) return result;
        foreach (var folder in Directory.EnumerateDirectories(appsDir))
        {
            var cfg = Path.Combine(folder, "AppConfig.json");
            if (!File.Exists(cfg)) continue;
            try { result.Add(AppConfigParser.Parse(File.ReadAllText(cfg), folder)); }
            catch (JsonException) { /* skip malformed app */ }
        }
        return result;
    }
}
```

## Tests (write first, see them fail)

`tests/ReloadedHelper.Core.Tests/CatalogsTests.cs`:

```csharp
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
```

## Step order (strict TDD)

1. Write the test file as above. Don't create the production files yet.
2. `dotnet test --filter FullyQualifiedName~CatalogsTests` → expect FAIL (doesn't compile).
3. Create `ReloadedInstall.cs`.
4. Create `Catalogs.cs`.
5. `dotnet test --filter FullyQualifiedName~CatalogsTests` → expect PASS (3 tests).
6. Run full suite: `dotnet test` → expect 8 passing (1 smoke + 2 ModConfig + 2 AppConfig + 3 catalog).
7. Commit: `git add -A && git commit -m "feat(core): ReloadedInstall paths and mod/game catalog loaders"`.

## What you must NOT do

- Do NOT touch `ModInfo`, `GameInfo`, or the parsers from Tasks 2-3.
- Do NOT add `ModLoadEntry`, `LoadOrderBuilder`, `Settings`, or anything from later tasks.
- Do NOT read any file under `C:\FreeSoft\Reloaded-II`.
- Do NOT add NuGet packages.

## Reporting

Full report to `.superpowers/briefs/task-4-report.md`. Return: status,
commit hash, one-line test summary, concerns.
