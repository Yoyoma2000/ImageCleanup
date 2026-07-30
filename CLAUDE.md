# ImageCleanup

Windows desktop app for deduplicating/organizing image and video folders.
C#/.NET 9, WinUI 3 for UI.

## Architecture
- src/ImageCleanup.Core — pure logic, no UI/IO framework deps. Hashing,
  quality scoring, EXIF parsing, screenshot heuristics, video sampling.
  Must stay unit-testable without a UI or filesystem mock.
- src/ImageCleanup.Data — SQLite cache (Microsoft.Data.Sqlite) for file
  hashes/metadata, and per-feature staging models. Each feature that stages
  file actions gets its own table + repository (OrganizationStaging /
  OrganizationStagingRepository for Duplicates, QualityStaging /
  QualityStagingRepository for Quality) — deliberately not a shared table
  with a discriminator column, so one feature's review/commit/ClearStaged
  can never touch another's rows. Both repositories implement
  IStagingRepository so CommitService's execution logic (Delete/Move,
  per-entry error handling) runs against either without duplication.
- src/ImageCleanup.App — WinUI 3, MVVM, NavigationView + Frame page shell:
  - MainWindow.xaml is the shell only — shared folder-selection toolbar +
    NavigationView/Frame. It hosts no feature logic itself.
  - Services/ScanSessionService — singleton (registered in App.xaml.cs via
    Microsoft.Extensions.DependencyInjection, resolved through the static
    `App.Services` provider). Owns the current folder + scanned FileRecords
    (ObservableCollection<FileRecord>) and the shared SQLite connection
    string; exposes ScanFolderAsync/RefreshAsync and a ScanCompleted event
    for pages to rebuild derived state from. This is the single source of
    truth every feature page reads from — no page runs its own scan.
  - Views/ — one Page per feature (DuplicatesPage and QualityPage
    implemented; OrganizationPage is still a stub "coming soon"
    placeholder), plus GroupDetailDialog (a ContentDialog, not a nav page).
  - ViewModels/ — one ViewModel per feature page (DuplicatesViewModel,
    QualityViewModel) plus shared per-row view models (FileActionViewModel,
    StagingEntryViewModel, DuplicateGroupViewModel, ThumbnailLoader) usable
    by any future feature. FileActionViewModel has two constructors: the
    original `(fileRecordId, filePath, isSuggested)` bool overload used by
    Duplicates (defaults Keep/Delete), and a newer
    `(fileRecordId, filePath, initialAction, blurScore)` overload used by
    Quality (defaults explicitly, e.g. "None", and optionally carries a
    BlurScore for display — unused/null for Duplicates rows).
- tests/ImageCleanup.Core.Tests — xUnit.

## Conventions
- Core never references Data or App.
- File moves/deletes always go through a staged/dry-run step before
  touching disk — no direct File.Delete calls from ViewModels.
- New hashing/scoring logic goes in Core with a matching xUnit test.
- Claude Code cannot launch or interact with the WinUI app (no GUI
  access) — after a green build, describe what should happen when run
  and defer actual UI verification (build success in Visual Studio,
  visual rendering, click-through of features) to Alan.

## Commands
- Build: dotnet build
- Test: dotnet test
- Run: dotnet run --project src/ImageCleanup.App  (CLI-only for Core/Data;
  App must be run via Visual Studio F5 — see Notes)

## Notes
- App cannot be built via `dotnet build` CLI (MSB4062 — missing PRI/MRT DLL from plain SDK).
  Build the App project via Visual Studio; Core/Data/tests build fine from CLI.
- ulong stored as signed long in SQLite; cast on read with (ulong)GetInt64().
- Always parse DateTime from SQLite with DateTimeStyles.RoundtripKind.

## Status
Sessions 1–11 complete. 118 tests passing (76 Core, 42 Data), 0 failures.

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
  wired via delegate in DuplicatesViewModel (formerly MainViewModel — see
  session 10 restructure below)
- Bug fix: near-blank/solid-colour images collapsed to near-zero DHash values
  and formed false near-dup groups. LowDetail flag (pixel variance < 50)
  excludes them from the perceptual-hash phase; exact-hash grouping is
  unaffected.
- SchemaVersion on FileRecords: NeedsRescan returns true when the cached row
  was written by an older schema (currently v0 → v1 for LowDetail), so new
  computed fields are never silently left null on previously-cached files.
- Thumbnail previews: Core.Thumbnails.ThumbnailGenerator (Image.Load<Rgba32> +
  Resize with ResizeMode.Max, PNG-encoded, null on corrupt/unreadable files —
  same pattern as ExifReader). Data.Services.ThumbnailCache get-or-generates
  and caches thumbnail PNGs on disk under %LOCALAPPDATA%\ImageCleanup\thumbnails,
  keyed by SHA256(filePath + LastModified.Ticks + maxDimension) so a changed
  source file regenerates instead of serving a stale thumbnail; BLOBs were
  deliberately kept out of FileRecords/SQLite. App: FileActionViewModel and
  StagingEntryViewModel expose a `Thumbnail` ImageSource populated
  asynchronously via a shared ThumbnailLoader helper (generates bytes on a
  background thread, decodes BitmapImage back on the DispatcherQueue) so the
  text list renders immediately and thumbnails fill in as they're ready.
  DuplicatesPage.xaml shows a 64px thumbnail per row in the duplicate group
  list and a 40px thumbnail in the staging review panel.
- Group detail view: a "View Group" button per group in the list opens
  GroupDetailDialog (ContentDialog, chosen over a Frame/Page swap so the scan
  results stay underneath — see file). It binds directly to the same
  DuplicateGroupViewModel/FileActionViewModel instances as the main list (no
  copy), so Delete/Move/None changes made in the dialog flow through the same
  ActionChanged → DuplicatesViewModel.OnFileActionChanged path and show up in
  the staging panel immediately. Shows a 320px thumbnail per file in a wrapping
  grid inside a ScrollViewer (tested layout-wise up to the 8-file group), with
  the suggested/keep file highlighted via a border (FileActionViewModel.
  KeepBorderThickness) plus the same "★ Keep" badge used in the list. The
  320px thumbnails are a separate ThumbnailCache entry (FileActionViewModel.
  DetailThumbnail / RequestDetailThumbnail) from the list's 64px ones — same
  cache, different maxDimension key — and are only requested when the dialog
  is opened (DuplicatesViewModel.RequestDetailThumbnails), not eagerly for
  every file at scan time.
- Bug fix: "Keep" is now a real, always-selectable action (Core.Grouping.
  KeepSelector.KeepAction = "Keep") in FileActionViewModel.AvailableActions
  alongside None/Delete/Move, not just an automatic label tied to the
  original SuggestionEngine pick. FileActionViewModel.IsSuggested is now
  computed from SelectedAction == Keep (was a fixed init-only bool), so the
  ★ badge and detail-view border follow whichever file the user currently
  has selected as Keep. DuplicatesViewModel.OnFileActionChanged enforces
  "only one Keep per group" via the new pure Core.Grouping.KeepSelector.
  ResolveKeepConflicts (unit-tested) — selecting Keep on a file resets any
  other file in the same group that was Keep back to Delete. Keep/None both
  mean "no staged action" (only Delete/Move create OrganizationStaging
  rows). DuplicateGroupViewModel.Header is now computed live (not cached at
  construction) so the "Keep: filename" text stays accurate after
  reassignment. Default behavior is unchanged: SuggestionEngine's pick still
  starts as Keep, all other files still default to Delete.
- Bug fix: GroupDetailDialog's file grid intermittently lost its ComboBox
  (and other controls) for some files in larger groups. Root cause: the
  dialog hosted a virtualizing ItemsWrapGrid panel inside a plain
  ItemsControl — WinUI only guarantees correct container recycling for a
  virtualizing panel when it's hosted by a ListViewBase (ListView/GridView),
  not a bare ItemsControl, so recycled containers weren't always fully
  rebound. Fixed by switching the host from ItemsControl to GridView (which
  uses ItemsWrapGrid as its native panel and handles recycling correctly),
  with SelectionMode="None" to keep it non-interactive like the app's other
  lists. Not unit-testable (WinUI container virtualization); verify manually
  per the checklist below.
- Restructured the App from a single MainWindow into a NavigationView + Frame
  page shell, in preparation for the Quality and Organization features:
  - Added Services/ScanSessionService (singleton, resolved via a new minimal
    DI container — Microsoft.Extensions.DependencyInjection, wired up in
    App.xaml.cs as a static `App.Services` provider, the first DI container
    in this codebase). It owns CurrentFolder, the shared SQLite connection
    string, and Records (ObservableCollection<FileRecord>) — the scanning
    logic (file enumeration, hashing, EXIF, blur/screenshot/low-detail) moved
    here verbatim from the old MainViewModel.ScanFiles, unchanged. Fires
    ScanCompleted once Records is fully repopulated so pages can rebuild
    derived state; RefreshAsync() re-scans the current folder (used after a
    commit changes what's on disk).
  - Renamed MainViewModel → ViewModels/DuplicatesViewModel: mechanical move,
    no change to duplicate-detection, staging, or Keep/Delete/Move/None
    logic. It now reads FileRecords from ScanSessionService.Records (via the
    ScanCompleted event, plus an immediate rebuild on construction if a scan
    already happened) instead of scanning itself. CommitStagedChangesAsync
    now calls ScanSessionService.RefreshAsync() after a successful commit so
    the shared record set — and every page reading it — reflects the new
    disk state, instead of just locally clearing its own collections.
  - MainWindow.xaml is now a shell: shared "Select Folder" button + status
    text (bound to ScanSessionService) above a NavigationView with
    Duplicates/Quality/Organization nav items and a Frame. Selecting a
    folder calls ScanSessionService.ScanFolderAsync once; every page reacts
    via ScanCompleted rather than re-scanning independently.
  - Old MainWindow.xaml's duplicate-list/staging-panel content moved
    mechanically into Views/DuplicatesPage.xaml (+ DuplicatesViewModel),
    minus the folder-selection toolbar row (now shared, in the shell).
    DuplicatesPage uses NavigationCacheMode.Enabled so Frame.Navigate reuses
    one Page/ViewModel instance instead of creating (and re-subscribing to
    ScanCompleted) a new one on every nav visit.
  - GroupDetailDialog moved from the App project root into Views/ (namespace
    ImageCleanup.App.Views) for consistency — no behavior change.
  - QualityPage and OrganizationPage are stub Pages ("Coming soon" text only,
    no ViewModel, no logic) wired into the NavigationView's Quality/
    Organization nav items.
  - OrganizationStagingRepository/CommitService are untouched and still used
    exactly as before, only now via DuplicatesViewModel instead of
    MainViewModel.
- Bug fix: NavigationView's collapsed/compact display showed clipped label
  text instead of switching to icon-only. Root cause: `PaneDisplayMode` was
  set to `"Left"`, which forces the pane permanently expanded — the
  hamburger toggle doesn't shrink it to a compact icon-only pane under
  `"Left"`, it just clips whatever doesn't fit. Fixed by changing to
  `"LeftCompact"` (gives the hamburger toggle its normal two-state
  collapse/expand behavior) and adding a `SymbolIcon` to each
  NavigationViewItem (Copy for Duplicates, Filter for Quality, Folder for
  Organization) — required under LeftCompact, since a collapsed item with
  no icon renders as an empty slot. `CompactModeThresholdWidth` was left
  alone; that property only affects `PaneDisplayMode="Auto"`; the
  toggle-driven behavior described is `LeftCompact`'s.
- Quality feature staging: added QualityStaging (Data/DbInitializer) +
  QualityStagingRepository, a full mirror of OrganizationStaging /
  OrganizationStagingRepository but a genuinely separate table/class rather
  than a shared table with a discriminator column — chosen specifically so
  neither feature's StageAction/GetPendingActions/ClearStaged/commit can
  ever touch the other's rows (a shared-table-with-filter approach would
  have required threading a source filter through all of those, including
  CommitService and DuplicatesViewModel's existing ClearStaged-on-rescan
  call — real regression risk to the working Duplicates flow). Added
  IStagingRepository (Data/Repositories) — the common shape both
  repositories implement — and gave CommitService a second constructor
  overload taking an IStagingRepository directly (the original
  single-connection-string constructor is untouched and still defaults to
  OrganizationStagingRepository, so all existing CommitServiceTests needed
  zero changes).
- Quality feature UI: QualityPage/QualityViewModel replace the stub. Reads
  every scanned file from ScanSessionService.Records, sorts blurriest-first
  via the new pure Core.Quality.QualityReviewOrder.SortBlurriestFirst
  (ascending BlurScore, excludes null/missing BlurScore, no threshold or
  auto-flagging — unit-tested), and shows a flat list — thumbnail (reused
  ThumbnailCache, same 64px default as Duplicates), file path, BlurScore
  (FileActionViewModel.BlurScoreDisplay), and a Delete/Keep/Move/None action
  ComboBox per file, defaulting to None (nothing pre-staged). Actions are
  staged through QualityStagingRepository — never OrganizationStaging — via
  the same ActionChanged callback pattern Duplicates uses, but with no
  per-group Keep-conflict resolution (that logic is Duplicates-specific and
  lives in DuplicatesViewModel, not FileActionViewModel; Quality's "Keep"
  just means "reviewed, keep as-is", independently per file). Quality has
  its own "Review Staged Changes" panel/Commit button, fully independent
  from Duplicates' (QualityPage.xaml mirrors DuplicatesPage.xaml's staging
  panel layout). QualityViewModel.CommitStagedChangesAsync reuses
  CommitService (via the new IStagingRepository constructor, passing its
  own QualityStagingRepository) for the confirm/commit/summary flow —
  Recycle Bin delete via the same delegate pattern, per-file error handling
  that doesn't abort the batch — then calls ScanSessionService.RefreshAsync()
  so Duplicates (and Quality itself) reflect the post-commit disk state, same
  as Duplicates' own commit already does. QualityPage uses
  NavigationCacheMode.Enabled like DuplicatesPage, for the same reason
  (avoid re-subscribing to ScanCompleted on every nav visit).

### Known constraints
- App runs via Visual Studio F5 only — `dotnet build`/`dotnet run` fail with
  MSB4062 (PRI/MRT packaging task missing from plain .NET SDK).
- Framework-dependent: requires Windows App Runtime 1.6.x installed on the
  target machine.
- WinUI 3 → WPF migration is under consideration given packaging friction;
  Core and Data have no UI framework dependency and would be unaffected.

### Not yet started
- Organization feature: nav item + stub page exist (OrganizationPage), but
  no ViewModel or staging logic — virtual folder organization is not
  implemented. OrganizationStagingRepository/CommitService are currently
  only used by Duplicates; Quality now has its own QualityStagingRepository
  (see above) — Organization will need to decide whether it reuses
  OrganizationStaging (it's already named for this feature) or gets its own
  table following the same separate-table precedent as Quality.
- IsScreenshot / LowDetail signals shown in UI (BlurScore now shown, in
  Quality)
- Recursive folder scanning (currently top-level only)
- Installer / distribution
- Thumbnail cache eviction/cleanup (cache directory grows unbounded today)
- Quality review list has no thumbnail-size detail view equivalent to
  Duplicates' GroupDetailDialog (not requested yet — Quality's flat list
  already shows a 64px thumbnail per row)

### Next planned
- Verify the NavigationView shell, DuplicatesPage restructure, nav icons,
  and the new Quality feature via Visual Studio F5 (see "Manual
  verification needed" below) — none of this is CLI-testable at the App
  layer.
- Organization feature (staging model decision above, then ViewModel/Page).

### Manual verification needed (Alan, via Visual Studio F5)
Thumbnails and the group detail view were built and unit-tested where
possible (Core/Data), but the App project cannot be built or run from the
CLI (see Known constraints), so the WinUI rendering itself is unverified.
When run:
- Select a folder with duplicate/near-duplicate images — each row in the
  duplicate group list should show a small preview to the left of the file
  path, appearing shortly after the text (async load), not blocking the list
  from showing immediately.
- The staging review panel at the bottom should show a smaller thumbnail per
  staged entry.
- Corrupt or non-image files (if any slip through the extension filter)
  should just show a blank/empty Image control rather than crashing.
- Re-scanning the same folder should feel faster for thumbnails already
  cached under %LOCALAPPDATA%\ImageCleanup\thumbnails (cache hit, no re-decode).
- Click "View Group" on a small (2-3 file) group — a dialog should open
  showing each file at ~320px with the scan results still visible/paused
  underneath; the suggested/keep file should have a visible border plus the
  "★ Keep" badge.
- Click "View Group" on the largest available group (8-file group from the
  near-dup grouping test scenario, if reproducible) — images should wrap
  2-3 per row and the dialog should scroll rather than overflow the screen
  or window bounds.
- In the detail dialog, change a file's action (e.g. None → Delete, or pick
  Move and type a target path) — confirm the change appears in the main
  window's staging review panel without closing the dialog, and that
  closing the dialog and reopening "View Group" reflects the same state.
- Confirm the larger (320px) thumbnails in the detail dialog load in
  progressively (not blocking dialog open) and that reopening the same
  group's dialog a second time shows thumbnails immediately (already cached,
  no re-generation).
- Keep reassignment (list view and detail view): on a group, change a
  non-suggested file's action to "Keep" — confirm the ★ badge/border moves
  to that file, and the file that used to be Keep (the original
  SuggestionEngine pick) automatically flips to "Delete" (and gets staged/
  appears in the staging panel). Confirm the group header's "Keep: ..."
  filename updates to match. Confirm only ever one file per group shows the
  ★ badge, however many times you reassign it, and that reassigning from the
  detail dialog updates the main list (and vice versa) since both bind the
  same FileActionViewModel instances.
- GroupDetailDialog ComboBox rendering: open "View Group" on several
  different-sized groups (2-3 files, a 4+ file group, and the 8-file group)
  and confirm every single file card shows its Delete/Keep/Move/None
  ComboBox — none should render without it. Scroll the dialog up/down if the
  group is large enough to scroll, and re-check that no card loses its
  ComboBox after scrolling (this is the scenario that previously reproduced
  the bug via container reuse).
- NavigationView shell (session 10 restructure — highest-priority checks,
  since none of this is CLI-testable): app launches showing the "Select
  Folder" button + status text above a NavigationView with Duplicates/
  Quality/Organization items, Duplicates selected by default and its page
  content visible.
- Click Quality and Organization nav items — each shows its "Coming soon"
  placeholder text with no errors; click back to Duplicates — the
  previously-scanned groups/staging state should still be there exactly as
  you left it (NavigationCacheMode.Enabled keeping the page/ViewModel alive
  across nav, not resetting per visit).
- Select a folder from the shell's "Select Folder" button (not from inside
  a page) — confirm scanning, grouping, thumbnails, and staging all appear
  in the Duplicates page exactly as before the restructure.
- Stage some Delete/Move actions, then switch to Quality/Organization and
  back to Duplicates before committing — confirm the staged state survived
  the nav round-trip.
- Run a full commit (with actual staged Delete/Move actions) and confirm:
  the confirmation + summary dialogs still appear, the commit succeeds, and
  afterward the Duplicates page's groups/staging reflect the post-commit
  disk state (via the new ScanSessionService.RefreshAsync() call) rather
  than just going blank.
- Select a new folder after being on Quality/Organization — confirm
  switching back to Duplicates shows the new folder's results, not stale
  data from the previous folder.
- NavigationView collapse/expand: toggle the hamburger button — the pane
  should collapse to icon-only (Copy/Filter/Folder icons, no clipped text)
  and expand back to icon+label, both readable at the small collapsed size.
- Quality — blurriest-first order: scan a folder with a mix of sharp and
  blurry images and open the Quality tab; confirm the list is ordered
  blurriest-first (lowest BlurScore at top) by comparing the shown
  BlurScore numbers against a visual sense of which images actually look
  blurriest. Confirm files with no BlurScore (if any slipped through, e.g.
  a corrupt/unsupported image) are simply absent from the list rather than
  appearing at either end.
- Quality — default state: right after a scan, every file's action
  ComboBox should read "None" — nothing should appear in Quality's staging
  panel until you actually change an action.
- Quality/Duplicates staging isolation: stage a Delete or Move on a couple
  of files in Quality, then switch to Duplicates — its staging panel should
  be completely unaffected (no Quality-staged files appear there), and vice
  versa when staging something in Duplicates first. This is the core
  guarantee behind the separate QualityStaging table — worth confirming
  directly rather than assuming.
- Quality commit: stage a Delete/Move in Quality, click Commit — confirm
  the same confirmation → commit → summary dialog flow as Duplicates,
  that the file is actually deleted/moved, and that switching to
  Duplicates afterward reflects the file's removal (e.g. it no longer
  appears in any duplicate group) without needing to manually rescan —
  this depends on Quality's commit calling ScanSessionService.RefreshAsync()
  the same way Duplicates' does.
- Quality — nav round-trip: stage some actions in Quality, switch to
  Duplicates or Organization and back — confirm the staged state and scroll
  position survived (NavigationCacheMode.Enabled keeping QualityViewModel
  alive across nav, same as DuplicatesPage).
