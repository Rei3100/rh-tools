# Task 2: ModInfo model + ModConfigParser (TDD)

Implement the first piece of the Core library: a `ModInfo` immutable record
plus a parser that turns a single `ModConfig.json` text into a `ModInfo`.
This is pure logic — no file I/O in this task. Follow strict TDD: write the
failing tests first, see them fail, then implement, see them pass, commit.

## Files

- Create `src/ReloadedHelper.Core/Models.cs` containing **only** `ModInfo` for this task. (GameInfo and ModLoadEntry are added in later tasks; don't add them now.)
- Create `src/ReloadedHelper.Core/ModConfigParser.cs`.
- Create `tests/ReloadedHelper.Core.Tests/ModConfigParserTests.cs`.

## Global constraints (from the plan)

- TFM net10.0 for Core/Tests. No third-party runtime NuGet packages — System.Text.Json only.
- No file I/O in this task (parser takes a `string json`, not a path).
- Namespace: `ReloadedHelper.Core`. Tests in `ReloadedHelper.Core.Tests`.
- Use `using JsonDocument` so we don't hold parsed JSON longer than needed.

## ModInfo record (exact shape)

```csharp
public sealed record ModInfo(
    string ModId,
    string ModName,
    string ModAuthor,
    string ModVersion,
    string ModDescription,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> Dependencies,
    IReadOnlyList<string> OptionalDependencies,
    IReadOnlyList<string> SupportedAppIds,
    string? ProjectUrl,
    string? GitHubUserName,
    string? GitHubRepositoryName,
    string? IconFileName,
    string FolderPath)
{
    public string? IconPath =>
        string.IsNullOrEmpty(IconFileName) ? null : Path.Combine(FolderPath, IconFileName);

    public string DisplayName => string.IsNullOrEmpty(ModName) ? ModId : ModName;
}
```

(Use `using System.IO;` so `Path.Combine` resolves.)

## ModConfigParser (exact behavior)

```csharp
public static class ModConfigParser
{
    public static ModInfo Parse(string json, string folderPath);
}
```

Field mapping from the JSON:

- `ModId`, `ModName`, `ModAuthor`, `ModVersion`, `ModDescription` → same names. Missing or non-string → `""`.
- `Tags` → `Tags` (string array; missing → empty list).
- `ModDependencies` → `Dependencies` (string array; missing → empty list).
- `OptionalDependencies` → `OptionalDependencies` (string array; missing → empty list).
- `SupportedAppId` → `SupportedAppIds` (string array; missing → empty list).
- `ProjectUrl` → `ProjectUrl`. Missing OR whitespace-only → `null`.
- `PluginData.GitHubRelease.UserName` → `GitHubUserName` (string or `null`).
- `PluginData.GitHubRelease.RepositoryName` → `GitHubRepositoryName` (string or `null`).
- `ModIcon` → `IconFileName`. Empty string → `null`.
- `FolderPath` → from the parameter; passed through unchanged.

When iterating string arrays, skip non-string entries defensively.

## Tests (TDD — write these first, watch them fail)

`tests/ReloadedHelper.Core.Tests/ModConfigParserTests.cs`:

```csharp
namespace ReloadedHelper.Core.Tests;

public class ModConfigParserTests
{
    private const string Full = """
    {
      "ModId": "BGME.Framework",
      "ModName": "BGMEフレーム",
      "ModAuthor": "RyoTune",
      "ModVersion": "4.1.2",
      "ModDescription": "BGMを拡張",
      "ModIcon": "Preview.png",
      "Tags": [],
      "ModDependencies": ["Ryo.Reloaded", "crifs.v2.hook"],
      "OptionalDependencies": [],
      "SupportedAppId": ["p4g.exe", "p5r.exe"],
      "PluginData": { "GitHubRelease": { "UserName": "RyoTune", "RepositoryName": "BGME" } },
      "ProjectUrl": "https://gamebanana.com/mods/477399"
    }
    """;

    [Fact]
    public void Parses_core_fields_including_unicode()
    {
        var m = ModConfigParser.Parse(Full, @"C:\Mods\BGME.Framework");
        Assert.Equal("BGME.Framework", m.ModId);
        Assert.Equal("BGMEフレーム", m.ModName);
        Assert.Equal("RyoTune", m.ModAuthor);
        Assert.Equal("4.1.2", m.ModVersion);
        Assert.Equal(new[] { "Ryo.Reloaded", "crifs.v2.hook" }, m.Dependencies);
        Assert.Equal(new[] { "p4g.exe", "p5r.exe" }, m.SupportedAppIds);
        Assert.Equal("https://gamebanana.com/mods/477399", m.ProjectUrl);
        Assert.Equal("RyoTune", m.GitHubUserName);
        Assert.Equal("BGME", m.GitHubRepositoryName);
        Assert.Equal(@"C:\Mods\BGME.Framework\Preview.png", m.IconPath);
    }

    [Fact]
    public void Missing_optional_fields_become_null_or_empty()
    {
        var m = ModConfigParser.Parse("""{ "ModId": "x", "ModName": "X" }""", @"C:\Mods\x");
        Assert.Equal("x", m.ModId);
        Assert.Null(m.ProjectUrl);
        Assert.Null(m.GitHubUserName);
        Assert.Null(m.IconPath);
        Assert.Empty(m.Dependencies);
        Assert.Equal("", m.ModDescription);
    }
}
```

## Step order (strict TDD)

1. Write the tests file exactly as above. Do NOT create `Models.cs` or `ModConfigParser.cs` yet.
2. Run `dotnet test --filter FullyQualifiedName~ModConfigParserTests` — expect compile failure / both tests failing because the types do not exist. Capture the exit code / error.
3. Create `Models.cs` with `ModInfo` exactly as specified above.
4. Create `ModConfigParser.cs` implementing `Parse(string json, string folderPath)`.
5. Run `dotnet test --filter FullyQualifiedName~ModConfigParserTests` — expect both tests passing.
6. Run the full suite once: `dotnet test`. Expect 3 tests (this task's 2 + SmokeTest) all passing.
7. `git add -A && git commit -m "feat(core): ModInfo model and ModConfig.json parser"`.

## What you must NOT do

- Don't add `GameInfo`, `ModLoadEntry`, file-system catalogs, or any other types from later tasks.
- Don't add NuGet packages.
- Don't push to any remote.
- Don't touch files outside this repository.

## Reporting

Write your full report (test fail output, test pass output, commit hash,
any deviations) to `.superpowers/briefs/task-2-report.md`. Return only:
status, commit hash, one-line test summary, and any concerns.
