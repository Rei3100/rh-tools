# Task 1: Solution scaffold + CI workflow (local only)

You are implementing Task 1 of the Reloaded-II Helper foundation viewer plan.
This task scaffolds the .NET solution, adds a smoke test, sets up GitHub
Actions, and commits — **without** creating the GitHub remote (the user
will confirm the repo name separately).

## Current state (already done in the working tree)

The controller has already run these commands. Do NOT redo them:

- `dotnet new sln -n reloaded-helper` (the solution file is `reloaded-helper.slnx`)
- `dotnet new classlib -n ReloadedHelper.Core -o src/ReloadedHelper.Core -f net10.0`
- `dotnet new wpf -n ReloadedHelper.App -o src/ReloadedHelper.App` (TFM `net10.0-windows`)
- `dotnet new xunit -n ReloadedHelper.Core.Tests -o tests/ReloadedHelper.Core.Tests -f net10.0`
- Added all three projects to the solution
- `dotnet add src/ReloadedHelper.App reference src/ReloadedHelper.Core`
- `dotnet add tests/ReloadedHelper.Core.Tests reference src/ReloadedHelper.Core`
- Removed `src/ReloadedHelper.Core/Class1.cs` and `tests/ReloadedHelper.Core.Tests/UnitTest1.cs`
- `git init -b main`
- Created empty `.superpowers/sdd/` and `.superpowers/briefs/` directories

`dotnet sln list` should already show all 3 projects.

Note: When `dotnet new xunit` ran, NuGet package restore failed because the
sandbox at that moment was offline. Your shell now has network access; a
plain `dotnet restore` will pull the missing packages.

## Global constraints (from the plan)

- TFM: `net10.0` for Core/Tests; `net10.0-windows` + `UseWPF` for App.
- JSON: System.Text.Json only. No third-party runtime deps. xUnit (test-only) allowed.
- READ-ONLY: this app never writes inside the user's Reloaded-II install.
- No paid services, no required network at runtime.
- Repo will be GitHub Public, no README (controller will create it later).
- Distribution will be a self-contained single-file win-x64 .exe (set up in a later task).

## What you must produce in this task

1. **Restore packages** for the test project so xUnit etc. are usable:
   - `dotnet restore` from the repo root. It must succeed.
2. **Smoke test** at `tests/ReloadedHelper.Core.Tests/SmokeTest.cs`:
   ```csharp
   namespace ReloadedHelper.Core.Tests;

   public class SmokeTest
   {
       [Fact]
       public void Two_plus_two_is_four()
       {
           Assert.Equal(4, 2 + 2);
       }
   }
   ```
3. **Verify it passes:** `dotnet test` from the repo root. Expect 1 test passing.
4. **`.gitignore`** at repo root with at least:
   ```
   bin/
   obj/
   publish/
   *.user
   ```
5. **GitHub Actions workflow** at `.github/workflows/build.yml`:
   ```yaml
   name: build
   on:
     push:
     workflow_dispatch:
   jobs:
     build:
       runs-on: windows-latest
       steps:
         - uses: actions/checkout@v4
         - uses: actions/setup-dotnet@v4
           with:
             dotnet-version: '10.0.x'
         - run: dotnet restore
         - run: dotnet build --configuration Release --no-restore
         - run: dotnet test --configuration Release --no-build
   ```
6. **Move the plan into the committed tree** (it already exists at
   `docs/superpowers/plans/2026-06-20-foundation-viewer.md`).
7. **Commit everything** on the `main` branch:
   ```bash
   git add -A
   git commit -m "chore: scaffold solution, core/app/tests projects, smoke test, CI"
   ```
   Use this exact message. Confirm with `git log --oneline` afterward.

## What you must NOT do

- Do NOT run `gh repo create` or push to any remote. The user has not yet
  confirmed the repo name; that step belongs to the controller.
- Do NOT add any runtime NuGet packages.
- Do NOT modify any file outside this repository.

## Reporting

Write your full report (commands run, file list, test output, any issues)
to `.superpowers/briefs/task-1-report.md`. In the message you return, give
only: status (DONE / DONE_WITH_CONCERNS / NEEDS_CONTEXT / BLOCKED), the
commit hash, a one-line test summary, and any concerns. The controller
will read the report file for the rest.
