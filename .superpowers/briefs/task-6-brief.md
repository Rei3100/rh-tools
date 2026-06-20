# Task 6: AppSettings + Phase-2 UserData box (TDD)

Two tiny JSON-backed stores under `%APPDATA%\ReloadedHelper`:

- `AppSettings` — remembers the user's Reloaded-II install path between launches.
- `ModUserData` / `UserDataFile` / `UserDataStore` — the empty "box" for Phase-2 features (translations, URL overrides, notes). Defined and round-trips on disk, but NOT yet wired to the UI.

Strict TDD with temp directories.

## Files

- Create `src/ReloadedHelper.Core/Settings.cs`.
- Create `src/ReloadedHelper.Core/UserData.cs`.
- Create `tests/ReloadedHelper.Core.Tests/SettingsTests.cs`.
- Create `tests/ReloadedHelper.Core.Tests/UserDataTests.cs`.

## Global constraints

- TFM net10.0; System.Text.Json only; xUnit (test-only) OK.
- WRITE policy: writes go ONLY into our own `%APPDATA%\ReloadedHelper`
  folder. Never write into the Reloaded-II install.
- Tests must use temp directories — never touch the real `%APPDATA%`.
- Don't add UserData wiring to the (yet to be built) ViewModel; this task
  defines and persists the structure only.

## Settings.cs

```csharp
using System.IO;
using System.Text.Json;

namespace ReloadedHelper.Core;

public sealed class AppSettings
{
    public string? ReloadedInstallPath { get; set; }
}

public static class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                     "ReloadedHelper", "settings.json");

    public static AppSettings Load(string path)
    {
        if (!File.Exists(path)) return new AppSettings();
        try { return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? new AppSettings(); }
        catch (JsonException) { return new AppSettings(); }
    }

    public static void Save(string path, AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(settings, Options));
    }
}
```

## UserData.cs

```csharp
using System.IO;
using System.Text.Json;

namespace ReloadedHelper.Core;

public sealed class ModUserData
{
    public string? TranslatedName { get; set; }
    public string? TranslatedDescription { get; set; }
    public string? UrlOverride { get; set; }
    public string? Notes { get; set; }
}

public sealed class UserDataFile
{
    public Dictionary<string, ModUserData> Mods { get; set; } = new();
}

public static class UserDataStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                     "ReloadedHelper", "userdata.json");

    public static UserDataFile Load(string path)
    {
        if (!File.Exists(path)) return new UserDataFile();
        try { return JsonSerializer.Deserialize<UserDataFile>(File.ReadAllText(path)) ?? new UserDataFile(); }
        catch (JsonException) { return new UserDataFile(); }
    }

    public static void Save(string path, UserDataFile file)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(file, Options));
    }
}
```

## Tests (write first, see them fail)

`tests/ReloadedHelper.Core.Tests/SettingsTests.cs`:

```csharp
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
```

`tests/ReloadedHelper.Core.Tests/UserDataTests.cs`:

```csharp
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
```

## Step order (strict TDD)

1. Write both test files. Don't create production files yet.
2. `dotnet test --filter "FullyQualifiedName~SettingsTests|FullyQualifiedName~UserDataTests"` → expect FAIL.
3. Create `Settings.cs` and `UserData.cs`.
4. Same test command → expect PASS (3 tests).
5. Full suite: `dotnet test` → expect 13 passing (10 existing + 3 new).
6. Commit: `git add -A && git commit -m "feat(core): settings persistence and phase-2 userdata box"`.

## What you must NOT do

- Don't wire UserData into the (not-yet-written) MainViewModel.
- Don't add NuGet packages.
- Don't write into the real `%APPDATA%` from tests — use temp dirs.

## Reporting

Full report → `.superpowers/briefs/task-6-report.md`. Return: status,
commit hash, one-line test summary, concerns.
