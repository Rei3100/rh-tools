# Task 5: LoadOrderBuilder + ModFilter (TDD)

Combine a `GameInfo` and the mod catalog into an ordered, enriched list
of `ModLoadEntry` rows the UI can show — and a tiny search filter on top.

## Files

- Modify `src/ReloadedHelper.Core/Models.cs`: **append** `ModLoadEntry`. Do not touch `ModInfo` or `GameInfo`.
- Create `src/ReloadedHelper.Core/LoadOrder.cs` (contains `LoadOrderBuilder` and `ModFilter`).
- Create `tests/ReloadedHelper.Core.Tests/LoadOrderTests.cs`.

## Global constraints

- TFM net10.0; System.Text.Json only at runtime; xUnit test-only.
- Namespaces: `ReloadedHelper.Core` / `.Tests`.
- No file I/O in this task.
- Pure logic — no mutation of inputs.

## ModLoadEntry (append to Models.cs)

```csharp
public sealed record ModLoadEntry(int Order, string ModId, ModInfo? Info, bool Enabled)
{
    public string DisplayName =>
        Info is { ModName.Length: > 0 } ? Info.ModName : ModId;
}
```

## LoadOrder.cs

```csharp
namespace ReloadedHelper.Core;

public static class LoadOrderBuilder
{
    public static IReadOnlyList<ModLoadEntry> Build(GameInfo game, IReadOnlyDictionary<string, ModInfo> catalog)
    {
        var enabled = new HashSet<string>(game.EnabledMods, StringComparer.Ordinal);
        var list = new List<ModLoadEntry>(game.SortedMods.Count);
        for (int i = 0; i < game.SortedMods.Count; i++)
        {
            var id = game.SortedMods[i];
            catalog.TryGetValue(id, out var info);
            list.Add(new ModLoadEntry(i + 1, id, info, enabled.Contains(id)));
        }
        return list;
    }
}

public static class ModFilter
{
    public static IReadOnlyList<ModLoadEntry> Filter(IReadOnlyList<ModLoadEntry> entries, string? search)
    {
        if (string.IsNullOrWhiteSpace(search)) return entries;
        var s = search.Trim();
        return entries.Where(e =>
            e.ModId.Contains(s, StringComparison.OrdinalIgnoreCase) ||
            e.DisplayName.Contains(s, StringComparison.OrdinalIgnoreCase)).ToList();
    }
}
```

## Tests (write first, see them fail)

`tests/ReloadedHelper.Core.Tests/LoadOrderTests.cs`:

```csharp
namespace ReloadedHelper.Core.Tests;

public class LoadOrderTests
{
    private static ModInfo Mod(string id, string name) =>
        new(id, name, "", "", "", Array.Empty<string>(), Array.Empty<string>(),
            Array.Empty<string>(), Array.Empty<string>(), null, null, null, null, @"C:\x");

    private static GameInfo Game() =>
        new("p5r.exe", "P5R", "", null,
            EnabledMods: new[] { "a" },
            SortedMods: new[] { "lib", "a", "missing" },
            FolderPath: @"C:\Apps\p5r.exe");

    [Fact]
    public void Build_preserves_order_marks_enabled_and_tolerates_missing_info()
    {
        var catalog = new Dictionary<string, ModInfo>
        {
            ["lib"] = Mod("lib", "Library"),
            ["a"] = Mod("a", "Alpha"),
        };
        var entries = LoadOrderBuilder.Build(Game(), catalog);

        Assert.Equal(3, entries.Count);
        Assert.Equal(1, entries[0].Order);
        Assert.Equal("Library", entries[0].DisplayName);
        Assert.False(entries[0].Enabled);
        Assert.True(entries[1].Enabled);
        Assert.Null(entries[2].Info);
        Assert.Equal("missing", entries[2].DisplayName);
    }

    [Fact]
    public void Filter_matches_id_or_name_case_insensitive()
    {
        var catalog = new Dictionary<string, ModInfo>
        {
            ["lib"] = Mod("lib", "Library"),
            ["a"] = Mod("a", "Alpha"),
        };
        var entries = LoadOrderBuilder.Build(Game(), catalog);

        Assert.Equal(2, ModFilter.Filter(entries, "a").Count);
        Assert.Single(ModFilter.Filter(entries, "alph"));
        Assert.Equal(3, ModFilter.Filter(entries, "  ").Count);
    }
}
```

## Step order (strict TDD)

1. Add the test file. Don't create production code yet.
2. `dotnet test --filter FullyQualifiedName~LoadOrderTests` → expect FAIL.
3. Append `ModLoadEntry` to `Models.cs`.
4. Create `LoadOrder.cs`.
5. `dotnet test --filter FullyQualifiedName~LoadOrderTests` → expect PASS (2 tests).
6. Full suite: `dotnet test` → expect 10 passing (8 existing + 2 new).
7. Commit: `git add -A && git commit -m "feat(core): load-order builder and search filter"`.

## What you must NOT do

- Don't add Settings/UserData/MainViewModel (later tasks).
- Don't add NuGet packages.
- Don't push remotes.

## Reporting

Full report → `.superpowers/briefs/task-5-report.md`. Return: status,
commit hash, one-line test summary, concerns.
