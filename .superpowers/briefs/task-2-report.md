# Task 2 Report: ModInfo + ModConfigParser (TDD)

## TDD Process

### Step 1: Write Tests First
Created `tests/ReloadedHelper.Core.Tests/ModConfigParserTests.cs` with two test cases:
1. `Parses_core_fields_including_unicode()` - parses full ModConfig.json with all fields
2. `Missing_optional_fields_become_null_or_empty()` - verifies defaults for missing fields

### Step 2: Verify Tests Fail (Before Implementation)
```
C:\Users\rainb\src\reloaded-helper\tests\ReloadedHelper.Core.Tests\ModConfigParserTests.cs(25,17): error CS0103: 現在のコンテキストに 'ModConfigParser' という名前は存在しません
C:\Users\rainb\src\reloaded-helper\tests\ReloadedHelper.Core.Tests\ModConfigParserTests.cs(41,17): error CS0103: 現在のコンテキストに 'ModConfigParser' という名前は存在しません
```

Tests failed as expected because `ModConfigParser` did not exist.

### Step 3: Implement ModInfo Record
Created `src/ReloadedHelper.Core/Models.cs` with:
- `ModInfo` sealed record with 14 properties (exact signature from spec)
- `IconPath` computed property: combines `FolderPath` + `IconFileName` with `Path.Combine()`
- `DisplayName` computed property: returns `ModName` if non-empty, else `ModId`

### Step 4: Implement ModConfigParser
Created `src/ReloadedHelper.Core/ModConfigParser.cs` with:
- Static `Parse(string json, string folderPath)` method
- JSON parsing using `System.Text.Json` with `using JsonDocument` scope
- Correct field mapping including Unicode support (confirmed by test with Japanese text "BGMEフレーム")
- Defensive array iteration (skips non-string entries)
- Proper null handling:
  - Missing/non-string fields → `""` for core strings, `null` for optional
  - Empty/whitespace URLs → `null`
  - Empty icon filenames → `null`
  - Nested GitHub metadata extraction from `PluginData.GitHubRelease`

### Step 5: Verify Tests Pass (After Implementation)
```
成功!   -失敗:     0、合格:     2、スキップ:     0、合計:     2、期間: 8 ms
```

Both ModConfigParserTests passed.

### Step 6: Verify Full Test Suite
```
成功!   -失敗:     0、合格:     3、スキップ:     0、合計:     3、期間: 5 ms
```

All 3 tests pass:
- 2 new ModConfigParser tests (from this task)
- 1 SmokeTest (from Task 1 - untouched)

### Step 7: Commit
```
git commit -m "feat(core): ModInfo model and ModConfig.json parser"
commit b291efa4c2c7b32569a55026a7a8780556d9c756
```

## Implementation Details

### Files Created
1. `src/ReloadedHelper.Core/Models.cs` - ModInfo record with computed properties
2. `src/ReloadedHelper.Core/ModConfigParser.cs` - Parse() static method with JSON handling
3. `tests/ReloadedHelper.Core.Tests/ModConfigParserTests.cs` - Two comprehensive test cases

### Key Design Decisions
- Used `using JsonDocument` to avoid holding parsed JSON longer than needed (as per spec)
- System.Text.Json only (no third-party packages)
- Defensive array iteration with non-string skip
- String.IsNullOrWhiteSpace() for URL validation
- Path.Combine() for IconPath computation
- net10.0 TFM as specified

## Deviations
None. All requirements followed exactly as specified in task-2-brief.md.

## Test Output Summary
- **Before implementation**: 2 compile errors (types do not exist)
- **After implementation**: 3/3 tests passing (2 new + 1 smoke test)
- **Full suite**: 100% pass rate

## Concerns
None. Implementation is clean, tests pass, SmokeTest untouched, all requirements met.
