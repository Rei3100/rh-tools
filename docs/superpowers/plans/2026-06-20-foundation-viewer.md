# Reloaded-II Helper — Foundation Viewer Implementation Plan

Goal: read-only Windows desktop app that reads a Reloaded-II install and shows every mod per game (load order, on/off, details).

Architecture: two-layer .NET solution. `ReloadedHelper.Core` (class library, no WPF) holds models + parsing + logic, fully unit-tested (TDD). `ReloadedHelper.App` (WPF) holds only the 3-pane UI + wiring. `ReloadedHelper.Core.Tests` (xUnit) tests Core. GitHub Actions builds/tests/publishes a single-file .exe.

## Global Constraints
- TFM: `net10.0` (Core/Tests), `net10.0-windows` + UseWPF (App). SDK 10.0.301 confirmed.
- JSON: System.Text.Json only. No third-party runtime deps. xUnit (test-only) allowed.
- READ-ONLY: never write/move/modify anything inside the Reloaded-II install. Our own files live under `%APPDATA%\ReloadedHelper`.
- No paid services, no required network. Offline-capable.
- Repo: GitHub Public, no README. Default name `reloaded-helper` (confirm before public push).
- Distribution: Actions publishes self-contained single-file win-x64 .exe.
- Reloaded layout (verified): `<root>\Mods\<folder>\ModConfig.json`, `<root>\Apps\<appid>\AppConfig.json`. `ReloadedII.json` paths can be STALE → derive Mods/Apps from user-chosen root.

### Verified formats
- AppConfig.json: AppId, AppName (may be \uXXXX), AppLocation, AppIcon; EnabledMods (ON set); SortedMods (load order, top=first, includes disabled).
- ModConfig.json: ModId, ModName, ModAuthor, ModVersion, ModDescription; ModIcon (filename); Tags/ModDependencies/OptionalDependencies/SupportedAppId (arrays); ProjectUrl (sometimes absent); PluginData.GitHubRelease.UserName/.RepositoryName (sometimes present).

## Tasks
### Task 1: Solution scaffold + green CI + GitHub repo
Files: `reloaded-helper.sln`, 3 csproj, `tests/.../SmokeTest.cs`, `.github/workflows/build.yml`, `.gitignore`.
Steps:
- `dotnet new sln`; classlib Core (net10.0); wpf App (net10.0-windows); xunit Tests (net10.0); add to sln; App→Core ref; Tests→Core ref; delete Class1.cs.
- SmokeTest: one `[Fact]` asserting `2+2==4`. Run `dotnet test` → PASS.
- `.gitignore` (bin/ obj/ publish/ *.user). build.yml: on push + workflow_dispatch; windows-latest; setup-dotnet 10.0.x; restore/build Release/test.
- `git init -b main`, commit. **Confirm repo name with user**, then `gh repo create reloaded-helper --public --source=. --push`. Verify `gh run watch` green.
Deliverable: green CI on a trivial passing test.

### Task 2: ModInfo + ModConfigParser (TDD)
Files: Core/Models.cs (ModInfo), Core/ModConfigParser.cs; Tests/ModConfigParserTests.cs.
- `ModInfo` record: ModId, ModName, ModAuthor, ModVersion, ModDescription, Tags, Dependencies, OptionalDependencies, SupportedAppIds, ProjectUrl?, GitHubUserName?, GitHubRepositoryName?, IconFileName?, FolderPath. Computed: `IconPath` = Path.Combine(FolderPath, IconFileName) or null; `DisplayName` = ModName||ModId.
- `ModConfigParser.Parse(string json, string folderPath) -> ModInfo`. Map ModDependencies→Dependencies, SupportedAppId→SupportedAppIds, PluginData.GitHubRelease.UserName/RepositoryName→GitHub*. Empty/blank ProjectUrl→null. Missing fields→""/empty/null.
- Tests: (a) full JSON incl `\uXXXX` name → fields incl IconPath + GitHub + deps; (b) minimal `{ModId,ModName}` → ProjectUrl/GitHub/IconPath null, deps empty.
Verify: `dotnet test --filter ~ModConfigParserTests` fails then passes. Commit.

### Task 3: GameInfo + AppConfigParser (TDD)
Files: Core/Models.cs (add GameInfo), Core/AppConfigParser.cs; Tests/AppConfigParserTests.cs.
- `GameInfo` record: AppId, AppName, AppLocation, IconFileName?, EnabledMods, SortedMods, FolderPath; IconPath; DisplayName.
- `AppConfigParser.Parse(json, folderPath) -> GameInfo`. Missing arrays→empty.
- Tests: order+enabled+unicode AppName+IconPath; missing arrays→empty.
Verify filter ~AppConfigParserTests. Commit.

### Task 4: ReloadedInstall + catalogs (TDD, temp dirs)
Files: Core/ReloadedInstall.cs, Core/Catalogs.cs; Tests/CatalogsTests.cs.
- `ReloadedInstall(string rootPath)`: ModsDir=root\Mods, AppsDir=root\Apps, IsValid = both dirs exist.
- `ModCatalog.LoadAll(modsDir) -> IReadOnlyDictionary<string,ModInfo>` keyed by ModId; skip folders lacking ModConfig.json; catch JsonException (skip malformed); skip empty ModId.
- `GameCatalog.LoadAll(appsDir) -> IReadOnlyList<GameInfo>`; same robustness.
- Tests (temp dirs): mod catalog keys by ModId + skips malformed/missing; game catalog loads; install validity toggles when Mods+Apps created.
Verify ~CatalogsTests. Commit.

### Task 5: LoadOrderBuilder + ModFilter (TDD)
Files: Core/Models.cs (add ModLoadEntry), Core/LoadOrder.cs; Tests/LoadOrderTests.cs.
- `ModLoadEntry(int Order, string ModId, ModInfo? Info, bool Enabled)`; DisplayName = Info.ModName||ModId.
- `LoadOrderBuilder.Build(GameInfo, catalog) -> IReadOnlyList<ModLoadEntry>`: iterate SortedMods in order, Order=i+1, Info from catalog (null ok), Enabled = EnabledMods.Contains(id).
- `ModFilter.Filter(entries, string?) -> IReadOnlyList<ModLoadEntry>`: blank→all; else case-insensitive contains on ModId or DisplayName.
- Tests: order preserved + enabled flags + missing-info falls back to id; filter matches id/name + blank returns all.
Verify ~LoadOrderTests. Commit.

### Task 6: Settings + UserData box (TDD)
Files: Core/Settings.cs, Core/UserData.cs; Tests/SettingsTests.cs, Tests/UserDataTests.cs.
- `AppSettings { string? ReloadedInstallPath }`; `SettingsStore.DefaultPath` = `%APPDATA%\ReloadedHelper\settings.json`; Load (missing→empty, JsonException→empty), Save (creates dir, WriteIndented).
- Phase-2 box: `ModUserData { TranslatedName, TranslatedDescription, UrlOverride, Notes }`; `UserDataFile { Dictionary<string,ModUserData> Mods }`; `UserDataStore` DefaultPath `userdata.json`, Load/Save (UnsafeRelaxedJsonEscaping so Japanese stays readable). Defined + round-trips; NOT wired to UI yet.
- Tests: settings roundtrip + missing-file→null path; userdata roundtrip of a Japanese TranslatedDescription.
Verify ~SettingsTests / ~UserDataTests. Commit.

### Task 7: MainViewModel + WPF 3-pane UI
Files: Core/MainViewModel.cs (no WPF types — INotifyPropertyChanged only); App/PathToImageConverter.cs; App MainWindow.xaml(.cs), App.xaml(.cs); Tests/MainViewModelTests.cs.
- `MainViewModel`: ObservableCollection<GameInfo> Games; ObservableCollection<ModLoadEntry> Entries; SelectedGame (set→rebuild entries); SelectedEntry; SearchText (set→refilter). `LoadFrom(ReloadedInstall)`: load catalog+games, select first game. Internal `_allEntries` from LoadOrderBuilder; ApplyFilter via ModFilter.
- ViewModel tests (temp install): LoadFrom selects first game + builds entries (DisplayName resolved); SearchText filters then clears.
- PathToImageConverter (App): string path→BitmapImage with CacheOption=OnLoad (no file lock); missing→null.
- MainWindow.xaml: 3 columns — left ListBox Games (DisplayName); middle search TextBox (UpdateSourceTrigger=PropertyChanged) + ListView Entries (cols #, ON, MOD); right details panel bound to SelectedEntry (icon via converter, DisplayName, ModId, author, version, ProjectUrl, description, dependencies ItemsControl).
- App.xaml.cs OnStartup: load settings; if saved path valid use it, else loop OpenFolderDialog until IsValid (or cancel→Shutdown); save path; new MainViewModel; LoadFrom; show MainWindow. Remove StartupUri from App.xaml.
- Manual verify: `dotnet run --project src/ReloadedHelper.App` → pick `C:\FreeSoft\Reloaded-II` → 3 games; p5r.exe ~180 mods in correct order + on/off; details/thumbnail show; search filters; Reloaded files unchanged. Commit.

### Task 8: CI publishes single-file .exe + end-to-end
Files: .github/workflows/build.yml.
- Append publish step: `dotnet publish src/ReloadedHelper.App/ReloadedHelper.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish`; then upload-artifact `publish/ReloadedHelper.App.exe`.
- Commit + push; `gh run watch` green; download artifact; .exe launches as in Task 7.

## Verification (Phase 1 done)
1. `dotnet test` all green locally.
2. Actions green + produces ReloadedHelper.App.exe artifact.
3. Downloaded .exe opens; first run asks for folder, remembers it next launch.
4. Left pane = 3 games; p5r.exe shows ~180 mods in Reloaded's order with matching on/off.
5. Mod details show author/version/description/deps/thumbnail; missing-URL mods just show no URL.
6. Search filters by name/id.
7. Reloaded-II files unchanged after use (read-only confirmed).

## Self-review
- Coverage: viewer (T2–7), per-game order+on/off (T3,5,7), read-only (no write APIs on install; only %APPDATA% writes), user-picks-folder for stale ReloadedII.json (T7), phase-2 userdata box (T6), CI/.exe (T1,8).
- Type names consistent across tasks: ModInfo, GameInfo, ModLoadEntry, ReloadedInstall, MainViewModel.LoadFrom, ModFilter.Filter, LoadOrderBuilder.Build.
- No placeholders.
