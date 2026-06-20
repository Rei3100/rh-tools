# Task 3 Report: GameInfo model + AppConfigParser

## Status
✅ **COMPLETE** — All tests passing, commit created.

## Commit Hash
`ca6d099` — feat(core): GameInfo model and AppConfig.json parser

## Test Summary
5 tests passing: 1 smoke test + 2 ModConfig tests + 2 AppConfig tests.
- `AppConfigParserTests.Parses_game_with_order_and_enabled_set()` ✅
- `AppConfigParserTests.Missing_arrays_become_empty()` ✅

## Implementation
1. **GameInfo record** appended to `src/ReloadedHelper.Core/Models.cs`:
   - Properties: `AppId`, `AppName`, `AppLocation`, `IconFileName`, `EnabledMods`, `SortedMods`, `FolderPath`
   - Computed properties: `IconPath` (defensive path combine), `DisplayName` (fallback to AppId)

2. **AppConfigParser** created at `src/ReloadedHelper.Core/AppConfigParser.cs`:
   - `Parse(string json, string folderPath) -> GameInfo`
   - Field mapping: AppId/AppName/AppLocation default to "", EnabledMods/SortedMods default to empty lists
   - AppIcon → IconFileName with null conversion on empty string
   - Defensive array parsing: skips non-string entries, preserves order

3. **AppConfigParserTests** created at `tests/ReloadedHelper.Core.Tests/AppConfigParserTests.cs`:
   - Full JSON with all fields (Japanese name, icon, mod lists)
   - Minimal JSON (missing arrays/icon)

## Concerns
None. TDD flow (tests first → fail → implement → pass) followed strictly. Defensive parsing pattern matches ModConfigParser exactly. No modifications to ModInfo.
