# Task 3: GameInfo model + AppConfigParser (TDD)

Add the second data model and its parser. Same pattern as Task 2 — pure
logic, no file I/O, strict TDD.

## Files

- Modify `src/ReloadedHelper.Core/Models.cs`: **append** `GameInfo` to it (do not touch the existing `ModInfo`).
- Create `src/ReloadedHelper.Core/AppConfigParser.cs`.
- Create `tests/ReloadedHelper.Core.Tests/AppConfigParserTests.cs`.

## Global constraints

- TFM net10.0. System.Text.Json only. xUnit allowed for tests.
- Namespace: `ReloadedHelper.Core` / `ReloadedHelper.Core.Tests`.
- No file I/O in this task (`AppConfigParser.Parse(string json, string folderPath)`).
- Do NOT add `ModLoadEntry`, `Catalogs`, or anything else from later tasks.

## GameInfo record (append to Models.cs)

```csharp
public sealed record GameInfo(
    string AppId,
    string AppName,
    string AppLocation,
    string? IconFileName,
    IReadOnlyList<string> EnabledMods,
    IReadOnlyList<string> SortedMods,
    string FolderPath)
{
    public string? IconPath =>
        string.IsNullOrEmpty(IconFileName) ? null : Path.Combine(FolderPath, IconFileName);

    public string DisplayName => string.IsNullOrEmpty(AppName) ? AppId : AppName;
}
```

## AppConfigParser (exact behavior)

```csharp
public static class AppConfigParser
{
    public static GameInfo Parse(string json, string folderPath);
}
```

Field mapping:

- `AppId`, `AppName`, `AppLocation` → strings. Missing/non-string → `""`.
- `AppIcon` → `IconFileName`; empty string → `null`.
- `EnabledMods` → `EnabledMods` (string array; missing → empty list).
- `SortedMods` → `SortedMods` (string array; missing → empty list, **order preserved**).
- `FolderPath` ← parameter, unchanged.

Skip non-string array entries defensively, same approach as `ModConfigParser`.

## Tests (write first, see them fail)

`tests/ReloadedHelper.Core.Tests/AppConfigParserTests.cs`:

```csharp
namespace ReloadedHelper.Core.Tests;

public class AppConfigParserTests
{
    private const string Json = """
    {
      "AppId": "p5r.exe",
      "AppName": "ペルソナ5",
      "AppLocation": "C:\\Steam\\P5R.exe",
      "AppIcon": "Icon.png",
      "EnabledMods": ["a", "b"],
      "SortedMods": ["lib", "a", "b", "c"]
    }
    """;

    [Fact]
    public void Parses_game_with_order_and_enabled_set()
    {
        var g = AppConfigParser.Parse(Json, @"C:\Apps\p5r.exe");
        Assert.Equal("p5r.exe", g.AppId);
        Assert.Equal("ペルソナ5", g.AppName);
        Assert.Equal(@"C:\Steam\P5R.exe", g.AppLocation);
        Assert.Equal(new[] { "lib", "a", "b", "c" }, g.SortedMods);
        Assert.Equal(new[] { "a", "b" }, g.EnabledMods);
        Assert.Equal(@"C:\Apps\p5r.exe\Icon.png", g.IconPath);
    }

    [Fact]
    public void Missing_arrays_become_empty()
    {
        var g = AppConfigParser.Parse("""{ "AppId": "x", "AppName": "X" }""", @"C:\Apps\x");
        Assert.Empty(g.SortedMods);
        Assert.Empty(g.EnabledMods);
        Assert.Null(g.IconPath);
    }
}
```

## Step order (strict TDD)

1. Add the test file as above. Do NOT add `GameInfo` or the parser yet.
2. `dotnet test --filter FullyQualifiedName~AppConfigParserTests` → expect FAIL (does not compile).
3. Append `GameInfo` to `Models.cs`. (`using System.IO;` is already there from Task 2.)
4. Create `AppConfigParser.cs`.
5. `dotnet test --filter FullyQualifiedName~AppConfigParserTests` → expect PASS (2 tests).
6. Run full suite: `dotnet test` → expect 5 tests passing (1 smoke + 2 ModConfig + 2 AppConfig).
7. Commit: `git add -A && git commit -m "feat(core): GameInfo model and AppConfig.json parser"`.

## Reporting

Write your full report (fail output, pass output, commit hash, anything
notable) to `.superpowers/briefs/task-3-report.md`. Return only status,
commit hash, one-line test summary, concerns.
