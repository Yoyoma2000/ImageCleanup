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
Sessions 1–6 complete. 87 tests passing (59 Core, 28 Data), 0 failures.

### Completed
- Core: DHash perceptual hash + Hamming distance, BlurDetector (Laplacian
  variance), ExifReader (MetadataExtractor), ScreenshotHeuristic (aspect-ratio),
  LowDetailDetector (pixel-variance), SuggestionEngine (exact + near-dup
  grouping via union-find with LowDetail exclusion)
- Data: FileCacheRepository (with SchemaVersion-aware NeedsRescan),
  OrganizationStagingRepository, CommitService (delete via caller-supplied
  delegate so Microsoft.VisualBasic stays out of the net9.0 Data layer),
  DbInitializer with idempotent ALTER TABLE column migrations
- App: FolderPicker scan pipeline, duplicate review UI with per-file
  Delete/Move/None ComboBoxes, staging review panel with Remove per entry,
  commit flow with confirmation + summary ContentDialogs, RecycleBin delete
  wired via delegate in MainViewModel
- Bug fix: near-blank/solid-colour images collapsed to near-zero DHash values
  and formed false near-dup groups. LowDetail flag (pixel variance < 50)
  excludes them from the perceptual-hash phase; exact-hash grouping is
  unaffected.
- SchemaVersion on FileRecords: NeedsRescan returns true when the cached row
  was written by an older schema (currently v0 → v1 for LowDetail), so new
  computed fields are never silently left null on previously-cached files.

### Known constraints
- App runs via Visual Studio F5 only — `dotnet build`/`dotnet run` fail with
  MSB4062 (PRI/MRT packaging task missing from plain .NET SDK).
- Framework-dependent: requires Windows App Runtime 1.6.x installed on the
  target machine.
- WinUI 3 → WPF migration is under consideration given packaging friction;
  Core and Data have no UI framework dependency and would be unaffected.

### Not yet started
- Thumbnail/image previews in the duplicate list (currently text-only)
- IsScreenshot / BlurScore / LowDetail signals shown in UI
- Recursive folder scanning (currently top-level only)
- Installer / distribution

### Next planned
- Thumbnail previews in the duplicate group list so review doesn't rely on
  filenames alone.
