# ImageCleanup

Windows desktop app for deduplicating/organizing image and video folders.
C#/.NET 9, WinUI 3 for UI.

## Architecture
- src/ImageCleanup.Core — pure logic, no UI/IO framework deps. Hashing,
  quality scoring, EXIF parsing, screenshot heuristics, video sampling.
  Must stay unit-testable without a UI or filesystem mock.
- src/ImageCleanup.Data — SQLite cache (Microsoft.Data.Sqlite) for file
  hashes/metadata, and the virtual-organization staging model.
- src/ImageCleanup.App — WinUI 3, MVVM. Views/ and ViewModels/.
- tests/ImageCleanup.Core.Tests — xUnit.

## Conventions
- Core never references Data or App.
- File moves/deletes always go through a staged/dry-run step before
  touching disk — no direct File.Delete calls from ViewModels.
- New hashing/scoring logic goes in Core with a matching xUnit test.

## Commands
- Build: dotnet build
- Test: dotnet test
- Run: dotnet run --project src/ImageCleanup.App

## Notes
- App cannot be built via `dotnet build` CLI (MSB4062 — missing PRI/MRT DLL from plain SDK).
  Build the App project via Visual Studio; Core/Data/tests build fine from CLI.
- ulong stored as signed long in SQLite; cast on read with (ulong)GetInt64().
- Always parse DateTime from SQLite with DateTimeStyles.RoundtripKind.

## Status
Sessions 1–5 complete.
- Core: DHash, BlurDetector, ExifReader, ScreenshotHeuristic, SuggestionEngine
- Data: FileCacheRepository, OrganizationStagingRepository, CommitService
- App: FolderPicker scan pipeline, duplicate review UI, per-file action staging,
  staging panel with commit flow, confirmation + summary dialogs
- Tests: Core (hash, quality, heuristics, grouping), Data (cache, staging, commit)
