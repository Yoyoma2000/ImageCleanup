# ImageCleanup

Windows desktop app for deduplicating/organizing image and video folders.
C#/.NET 9, WinUI 3 for UI.

## Architecture
- src/ImageCleanup.Core — pure logic, no UI/IO framework deps. Hashing,
  quality scoring, EXIF parsing, screenshot heuristics, HasExif-based
  metadata classification (MetadataClassifier — the current, more reliable
  approach for Organization; see Status below for why over
  ScreenshotHeuristic), recursive directory walking (IO.ImageFileEnumerator),
  video sampling. Must stay unit-testable without a UI or filesystem mock —
  ImageFileEnumerator does touch the filesystem (like ExifReader/DHasher
  already do) but has no UI framework dependency, so it's still testable
  here with real temp directories.
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
- DbInitializer's column-existence checks use PRAGMA table_info(<table>)
  rather than attempt-ALTER-TABLE-and-catch — deliberate, don't revert:
  the old pattern threw (and Visual Studio surfaced as first-chance
  exceptions) on every normal startup once a column already existed,
  since SQLite has no ADD COLUMN IF NOT EXISTS. Avoid using exceptions for
  expected control flow here.
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
Sessions 1–19 complete. 155 tests passing (101 Core, 54 Data), 0 failures.

**All three core features are feature-complete and manually verified
end-to-end on real data:**
- **Duplicates** — recursive scan → exact/near-dup detection
  (SuggestionEngine) → independent staging (OrganizationStagingRepository)
  → Recycle Bin commit (CommitService).
- **Quality** — recursive scan → blurriest-first review (QualityReviewOrder)
  → independent staging (QualityStagingRepository) → Recycle Bin commit
  (CommitService).
- **Organization** — recursive scan → Year/Month/Category planning
  (OrganizationPlanner) → TreeView preview → real move execution with a
  pre-execution move log (OrganizationExecutor). v1 is all-or-nothing
  (whole plan or nothing) with no staging table of its own — see Known
  gaps below.

All three share the same recursive, hidden/system/reparse-point-aware
scan (ScanSessionService + Core.IO.ImageFileEnumerator) and the same
ThumbnailCache-backed preview thumbnails.

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
- Finding: a temporary diagnostic (Debug.WriteLine dump of IsScreenshot vs.
  HasExif vs. actual dimensions, added and removed within this session —
  no longer in the codebase) run against real mixed folders (screenshots +
  actual camera/phone photos) showed ScreenshotHeuristic's aspect-ratio
  matching added little over HasExif alone — HasExif by itself was the far
  more reliable signal for telling real photos apart from everything else.
  Decision: for Organization purposes, classify files by HasExif directly
  rather than gating on ScreenshotHeuristic. Added Core.Metadata.
  MetadataCategory (Photo / NoMetadata) and Core.Metadata.MetadataClassifier.
  ClassifyMetadata(ExifMetadata) -> MetadataCategory, a pure HasExif ->
  enum mapping (unit-tested). Named NoMetadata rather than "Screenshot"
  deliberately: it also catches downloads, memes, and edited/resaved images
  that lost their EXIF on save, not just screenshots. ScreenshotHeuristic
  itself is untouched and still available — it's just not used to gate this
  categorization anymore.
- Recursive folder scanning: ScanSessionService now scans a selected folder's
  entire subtree (no depth limit) instead of just the top level. Added
  Core.IO.ImageFileEnumerator — manual stack-based recursion rather than
  `Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)`,
  deliberately: that single-call form has no way to skip a specific
  subdirectory, and one inaccessible folder anywhere in the tree throws and
  aborts the entire enumeration. Walking directory-by-directory means each
  directory's failure is independent (permission-denied, broken
  junction/symlink, a directory that disappears mid-scan — all just get
  skipped, recorded, and the walk continues) and files stream out via
  `yield return` as each directory is visited, so per-file processing
  (hashing, EXIF, thumbnailing) can start well before the whole tree has
  been walked. Hidden and system subdirectories (FileAttributes.Hidden /
  .System) are skipped during recursion — not applied to the user-selected
  root itself, only to subdirectories discovered while walking. Skipped
  directories are collected (ScanSessionService.LastSkippedDirectories) and
  surface as a "(N folder(s) skipped...)" suffix on StatusText rather than
  a separate log. DuplicatesViewModel and QualityViewModel needed zero
  changes — both already read unconditionally from
  ScanSessionService.Records, so nested files flow through to both features
  automatically (confirmed by inspection, no top-level-only assumptions
  found elsewhere in the App layer). StatusText now says "including
  subfolders" to make the recursive behavior visible to the user.
- Bug fix: ThumbnailCache crashed with an unhandled IOException
  ("...being used by another process") once recursive scanning started
  requesting far more thumbnails concurrently. Root cause: two concurrent
  callers requesting the same cache key (same file path + LastModified +
  maxDimension — e.g. a FileActionViewModel row and its corresponding
  StagingEntryViewModel row both request a thumbnail for the same staged
  file, independently and concurrently via ThumbnailLoader's
  Task.Run-per-request pattern) raced on the same cache PNG file with no
  synchronization at all. Fixed with a static
  ConcurrentDictionary<string, SemaphoreSlim> keyed by the absolute cache
  file path (static and path-keyed rather than per-instance: Duplicates and
  Quality each own a separate ThumbnailCache instance, but both point at
  the same physical cache directory by default, so the lock has to work
  across instances, not just within one) — the second concurrent caller
  waits for the first to finish and then reads its result via a
  double-checked cache read, rather than racing to write. Cache writes now
  go through a temp-file-then-File.Move(overwrite:true) pattern (defense in
  depth alongside the lock, shrinking the window where a reader could hit a
  partially-written file to a single rename). Cache reads/generates/writes
  each now catch IOException specifically and fail gracefully (return
  null/skip that one thumbnail) instead of propagating and crashing the
  scan. Added a concurrency test (32 parallel calls across multiple
  ThumbnailCache instances for the same key) confirming no exception and
  consistent results — stable across repeated runs.
- Investigated (per the same fix): whether recursive scanning could
  discover the same physical file more than once, e.g. via a directory
  reparse point (junction/symlink) resolving back to an already-visited
  location. Confirmed ImageFileEnumerator had no defense against this.
  Added two guards: subdirectories with FileAttributes.ReparsePoint are now
  skipped during recursion (same as hidden/system — this is the actual fix
  for reparse-point cycles, since a lightweight visited-directory set can't
  detect "same physical directory via a different logical path" without
  resolving the reparse target); and a per-walk yielded-files set dedupes
  by resolved full path as defense in depth for the same physical file
  being reachable under two literal paths. Not directly attributable as
  *the* cause of the observed crash — analysis suggests the thumbnail race
  above is fully explained by ordinary concurrent requests for the same
  staged file's thumbnail, independent of any duplicate-discovery bug — but
  the reparse-point gap was real and worth closing regardless. Not
  portably unit-testable: confirmed experimentally that Windows silently
  ignores attempts to set FileAttributes.ReparsePoint directly via
  File.SetAttributes on a plain directory (it only takes effect via actual
  junction/symlink creation APIs), so this can't be simulated in a
  deterministic cross-environment test the way the hidden/system skip
  tests are.
- Organization hierarchy engine (Core.Organization) — **Core-only planning
  logic, no UI or file-moving yet.** OrganizationPlanner.BuildHierarchy
  (IEnumerable<ImageRecord>) -> OrganizationPlan computes a proposed
  Year/Month/MetadataCategory folder tree (e.g. "2024/03/Photo") without
  touching the filesystem — nothing is moved, this only computes what
  *would* happen. Year/Month comes from ImageRecord.DateTaken, falling back
  to LastModified when DateTaken is null (always true for NoMetadata-
  category files, by definition); MetadataCategory reuses the existing
  MetadataClassifier (Photo/NoMetadata) rather than duplicating that logic.
  ImageRecord gained two new fields to support this (DateTaken, HasExif) —
  additive/non-breaking, existing SuggestionEngine/App-layer mapping code
  untouched, both just default (null/false) where not populated. The plan
  is a tree of YearGroup -> MonthGroup -> CategoryGroup -> PlannedFile,
  each level carrying a Label and FileCount for a future TreeView to bind
  to directly. Naming conflicts (two files resolving to the same filename
  within the same destination folder) are resolved by appending the
  source file's parent folder name (e.g. "IMG_0001 (from Test).jpg"); if
  that's *still* not unique (same filename AND same parent folder name,
  e.g. two different drives both having a "Test" folder), falls back to
  numbering ("IMG_0001 (from Test) (2).jpg"). Conflict resolution is scoped
  per destination folder (a HashSet reset per CategoryGroup) — same
  filename landing in two different Year/Month buckets is not a conflict.
  10 new tests: year/month grouping and ordering, DateTaken vs.
  LastModified fallback (both directions), Photo/NoMetadata split within a
  month, target folder/path computation, both conflict-resolution tiers,
  conflict scoping across destination folders, and empty input.
- OrganizationPlanner can now run against real scanned data. Added
  Data.Models.ImageRecordMapper (FileRecord -> ImageRecord extension method,
  `ToImageRecord()`) as the single centralized conversion — previously this
  mapping only existed as a private method inside DuplicatesViewModel; it's
  now shared so Quality/Organization don't grow their own copies (Quality
  doesn't currently need it; Organization will). DateTaken copies straight
  from FileRecord.DateTaken (already cached from ExifReader during scan —
  confirmed, no schema change needed). HasExif has no persisted FileRecord
  column, so it's derived as `DateTaken.HasValue || CameraModel is not
  null` — both of those are only ever populated from the same EXIF read
  that would have set HasExif, so either one present already implies it.
  4 new tests confirm the round-trip (EXIF present -> DateTaken + HasExif
  true, no EXIF -> both null/false, CameraModel-only -> still HasExif true,
  and the other fields copy through unchanged). Confirmed SuggestionEngine
  doesn't read DateTaken/HasExif at all, so Duplicates' grouping behavior
  is provably unchanged; Quality doesn't use ImageRecord/this mapper at
  all — neither feature's existing tests needed changes.
- Organization preview UI: OrganizationPage/OrganizationViewModel replace
  the stub — a TreeView over OrganizationPlanner's proposed Year/Month/
  Category hierarchy. **Preview only — no commit/move controls, nothing
  moves a file yet** (that's the next step). Split the mapping in two
  layers, same "extract the pure part to Core" pattern as KeepSelector/
  QualityReviewOrder: Core.Organization.OrganizationTreeNode +
  OrganizationTreeBuilder.BuildTree(OrganizationPlan) is a pure,
  UI-framework-free flattening of the plan into label/count/rename-aware
  nodes (unit-tested, 6 new tests — Year/Month/Category node shape, month
  NAME not number, file node source/target names, both renamed and
  not-renamed cases); App-layer OrganizationNodeViewModel is a thin
  wrapper adding only Thumbnail/DispatcherQueue/Visibility properties on
  top, kept deliberately dumb so the actual logic stays testable. Renamed
  files (conflict resolution changed the name) show their target filename
  plus a small "renamed" badge, both driven by OrganizationTreeNode.
  WasRenamed. OrganizationViewModel.RebuildAsync runs
  BuildHierarchy+BuildTree inside Task.Run (mandatory per the perf
  requirement — a real library can be thousands of files) and rebuilds on
  ScanSessionService.ScanCompleted, same trigger Duplicates/Quality use,
  though unlike them this one is genuinely async instead of synchronous,
  specifically because of that perf requirement. Thumbnails are NOT
  requested for every file when the plan builds; TreeView.Expanding (wired
  in OrganizationPage's code-behind) calls
  OrganizationViewModel.RequestThumbnailsFor(node) which only generates
  thumbnails for a Category node's files the first time that node is
  actually expanded — the one piece of "lazy loading" implemented, since
  it directly targets the expensive part (thumbnail generation) without
  the added complexity of also lazily materializing the tree-node objects
  themselves (those are cheap, no I/O, so built eagerly off the snapshot
  once BuildHierarchy/BuildTree return — see file for the explicit
  reasoning if this needs revisiting on very large libraries, since
  DispatcherQueue.GetForCurrentThread() is still called once per node
  including every file node, which is a real if likely-minor per-node
  cost at large scale).
- Organization move execution (v1: all files, no per-file selection yet —
  a future enhancement). **This is the first feature in the app that moves
  files outside Recycle Bin safety** — Delete actions elsewhere
  (Duplicates/Quality) go through the Recycle Bin via a delegate;
  Organization's Move is a real, non-reversible-through-Recycle-Bin
  File.Move, called out explicitly in the confirmation dialog.
  - Data.Services.OrganizationExecutor (same layer/style as CommitService —
    plain File.Move, no delegate injection needed since there's no
    OS-specific "safe move" the way Recycle-Bin-delete needed one).
    Execute(OrganizationPlan, destinationRoot): computes each file's full
    target path (destinationRoot + the plan's Year/Month/Category +
    already-conflict-resolved filename), **writes a durable JSON move log
    before attempting any move** (so a record survives even if the app
    crashes mid-batch), then moves each file with a per-file try/catch —
    one locked/missing/permission-denied file is recorded as a failure and
    does not abort the rest, same pattern as CommitService. Returns
    succeeded/failed counts, per-file failure reasons, and the log path.
  - **Move log**: `%LOCALAPPDATA%\ImageCleanup\move-logs\move-log_
    yyyyMMdd_HHmmss.json`, one per execution — a JSON object with
    Timestamp, DestinationRoot, and a Moves array of every planned
    {SourcePath, DestinationPath} pair (regardless of whether that
    particular move went on to succeed or fail — the log reflects the
    plan, not the outcome). **This log is the only safety net for now —
    there is no automated undo.** A human would need to read the log and
    manually move files back; that's a known, explicit gap, not an
    oversight (see Not yet started).
  - 6 new tests (temp-directory-based, no SQLite needed —
    OrganizationExecutor doesn't touch the DB at all): successful moves to
    the computed nested path, destination directories created as needed,
    one missing-source failure among a batch that still completes the
    rest, and — the most direct proof of "log written before execution,
    not after" — a case where *every* move fails yet the log still
    contains all the planned entries (if the log were only populated as
    moves succeeded, an all-failures run would produce an empty log; it
    doesn't).
  - App layer: OrganizationPage gained a destination folder picker
    (defaults to the currently-scanned source folder via
    ScanSessionService.CurrentFolder, but a user's explicit pick via
    FolderPicker is never clobbered by a later rescan) and an "Organize
    Files…" button gated on IsIdle + a non-empty plan + a chosen
    destination. Confirmation dialog states the file count, destination,
    and explicitly warns this is a real move, not reversible via Recycle
    Bin, before anything executes; summary dialog after shows succeeded/
    failed counts and the move log's path. After a successful execution,
    calls ScanSessionService.RefreshAsync() — same as Duplicates/Quality's
    commit flows — so every page reflects the new disk state.
  - Added App.ShellWindow (a static Window reference set in OnLaunched) —
    OrganizationPage needed its own FolderPicker (for the destination),
    and a Page has no HWND of its own in an unpackaged app the way
    MainWindow does; WindowNative.GetWindowHandle needs an actual Window.
    Named ShellWindow, not MainWindow, since the latter collides with the
    MainWindow class itself.
  - Flagged, not fixed (out of scope for v1): files moved by Organization
    aren't explicitly removed from FileCacheRepository the way
    CommitService's Move already does for Duplicates/Quality-committed
    files. RefreshAsync's rescan naturally stops surfacing files moved
    outside CurrentFolder (correct/expected), but their old FileRecords
    rows become orphaned in the SQLite cache rather than being cleaned up
    — harmless today, but worth revisiting alongside the existing
    thumbnail-cache-eviction gap if the cache noticeably bloats over time.
- Bug fix: Organization move-execution month folders now use a hybrid
  "01 - January" naming format (two-digit zero-padded number + " - " +
  full month name) instead of a plain zero-padded number, so File Explorer
  sorts them chronologically (pure word names would sort alphabetically)
  while staying human-readable (pure numbers aren't). This only changes
  OrganizationPlanner's PlannedFile.TargetFolder computation — the only
  consumer of that value is OrganizationExecutor, so real on-disk folders
  are the only thing affected. The TreeView preview's month labels
  (OrganizationTreeBuilder/OrganizationTreeNode) are a separate, independent
  computation and were deliberately left as word-only names ("March") —
  an in-app list has no filesystem chronological-sort concern, so there
  was no reason to change it. 1 new test confirms the hybrid format
  directly on real created directories; 5 existing tests (2 in
  OrganizationPlannerTests, 3 in OrganizationExecutorTests) that asserted
  the old plain-number folder strings were updated to match, computing the
  expected month name via CultureInfo.CurrentCulture rather than
  hardcoding an English name, so they stay correct on non-English machines.
- Bug fix: QualityPage's action ComboBox occasionally showed blank after
  deleting a row (removing a file from Quality's list) instead of "None".
  Root cause: the ComboBox bound via `SelectedItem="{x:Bind SelectedAction,
  Mode=TwoWay}"`, and QualityPage's ListView virtualizes — when a
  container is reused for a different FileActionViewModel after the list
  shrinks, WinUI has to re-match the new item's SelectedAction string
  against the ComboBox's ItemsSource by value, and that re-match doesn't
  always reliably re-run on reuse, leaving SelectedIndex at -1 (blank)
  even though the underlying ViewModel's SelectedAction is correctly set.
  Fixed by adding FileActionViewModel.SelectedActionIndex (an int mirror of
  SelectedAction, mapped against AvailableActions) and switching
  QualityPage's ComboBox to bind `SelectedIndex` instead of `SelectedItem`
  — a plain int has nothing to re-match against an ItemsSource on
  container reuse, so it isn't subject to this class of bug.
  SelectedAction itself (the string every other feature's logic reads) is
  unchanged; SelectedActionIndex is purely additive. Not fixed elsewhere
  (DuplicatesPage/GroupDetailDialog still bind via SelectedItem) since
  only QualityPage was reported affected and those two have different
  virtualization exposure — SelectedActionIndex is available on the shared
  FileActionViewModel if the same symptom ever surfaces there. This is
  WinUI container-recycling behavior, not meaningfully unit-testable (no
  App-layer test project exists, and the bug is specifically about
  virtualized-container reuse timing) — verify manually per the checklist
  below.

### Known gaps / not yet started
- **Video duplicate/near-duplicate detection — not started at all.** The
  app only scans image files today (see ScanSessionService's
  ImageExtensions list); no video sampling/hashing exists yet despite Core
  being scoped for it in the Architecture section below.
- **Organization**: no automated undo — the move log
  (%LOCALAPPDATA%\ImageCleanup\move-logs\*.json) is a human-readable manual
  safety net, not an undo button. Also no per-file selective move yet — v1
  is all-or-nothing (the whole plan executes, or none of it); no way to
  select/deselect individual files or categories, or edit a
  conflict-resolved target filename, before executing. Relatedly, there is
  still no staging table for Organization (OrganizationStagingRepository
  remains Duplicates-only) — v1 executes directly from the plan after a
  confirm dialog rather than through a staging/review cycle the way
  Duplicates/Quality work; whether Organization ever needs staging is an
  open question, not a settled gap.
- **App still cannot build via CLI** — `dotnet build`/`dotnet run` fail
  with MSB4062 (PRI/MRT packaging task missing outside Visual Studio).
  Core/Data build and test fine via CLI; the App project requires Visual
  Studio F5. This has been true since early in the project and hasn't
  changed.
- **WinUI 3 vs. WPF**: raised early in the project as a possible migration
  given packaging friction, never revisited or decided. Core and Data have
  no UI framework dependency either way, so this is purely an App-layer
  question whenever it gets picked back up.
- **No installer or distribution path** — the app currently only runs from
  source via Visual Studio; framework-dependent, requires Windows App
  Runtime 1.6.x installed on the target machine.
- **Recursive scanning** is verified correct on nested folders (hidden/
  system/reparse-point skipping, graceful failure handling on
  inaccessible/missing directories — see Core.IO.ImageFileEnumerator) but
  not stress-tested at large scale (tens of thousands of files);
  performance at that scale is unverified.
- **No settings/preferences persistence** — nothing is remembered between
  launches (e.g. the last-scanned folder), so every session starts from
  "Select Folder."
- Orphaned FileCacheRepository rows after an Organization move — no cache
  cleanup for moved files' old paths (harmless today; worth revisiting
  alongside the thumbnail-cache-eviction gap below if it bloats over time).
- Thumbnail cache eviction/cleanup — the disk cache under
  %LOCALAPPDATA%\ImageCleanup\thumbnails grows unbounded today.
- IsScreenshot / LowDetail signals aren't shown anywhere in the UI
  (BlurScore is, in Quality) — ScreenshotHeuristic itself is unused in
  favor of MetadataClassifier's HasExif-based approach (see Completed,
  session 12) but remains in Core in case it's useful elsewhere later.
- Quality's review list has no thumbnail-size detail view equivalent to
  Duplicates' GroupDetailDialog (not requested — Quality's flat list
  already shows a 64px thumbnail per row).

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
- Recursive scanning: select a real folder with actual nested subfolders
  (not just top-level files) and confirm files at every level — top folder,
  one level deep, two+ levels deep — show up in both the Duplicates and
  Quality tabs, not just top-level files. Confirm StatusText says
  "...including subfolders" and the file count matches the true tree-wide
  total (count files yourself via Explorer if unsure).
- Recursive scanning — hidden/system folders: if the test folder has (or
  you create) a hidden or system-attribute subfolder inside it, confirm its
  files do NOT show up in either tab, while sibling non-hidden folders'
  files still do.
- Recursive scanning — performance: try a folder with a deep/wide real
  nested structure (e.g. a full Pictures library with many
  year/month/event subfolders) and confirm the scan completes in
  reasonable time and the UI doesn't appear frozen while it runs (status
  text should still update to "Scanning…" immediately, per the existing
  async pattern).
- Recursive scanning — inaccessible folders: if feasible, point the scan at
  a folder containing a subfolder you don't have permission to read (or a
  broken shortcut/junction) and confirm the scan completes normally rather
  than showing a "Scan failed" error, with the StatusText's
  "(N folder(s) skipped...)" suffix reflecting it.
- ThumbnailCache crash fix: this is the main thing to re-verify from this
  session — re-run the exact scenario that crashed before (a real, sizeable
  nested folder structure with recursive scanning) and confirm the app no
  longer crashes with an IOException while thumbnails are loading. Watch
  both the Duplicates and Quality tabs populate with thumbnails
  simultaneously (they each request thumbnails independently and now share
  the same lock over the same cache folder) and confirm none show up
  broken/blank in a way that looks different from an ordinary "couldn't
  generate this one" case. If you have Developer Mode enabled and can
  create a real symlinked/junctioned subfolder inside a scan folder,
  confirm the scan no longer hangs or loops on it (skipped, same as a
  hidden folder) — this specific case wasn't unit-testable.
- Organization preview: select a real folder and switch to the Organization
  tab — confirm the summary line shows a sensible file/month count, and
  the tree shows Year -> Month (real month names, e.g. "March", not "03")
  -> Photo/NoMetadata -> individual files. Confirm a Photo-category file
  you know has EXIF (a real camera/phone photo) actually lands under
  Photo, and a screenshot/download lands under NoMetadata.
- Organization — thumbnails load lazily: expand a Category node and
  confirm thumbnails populate progressively (not all at once instantly,
  and not before you expand) — this confirms the Expanding-triggered lazy
  load is actually working rather than the tree eagerly loading everything
  upfront.
- Organization — rename detection: if your test folder has two files with
  the same filename in different subfolders that would land in the same
  Year/Month/Category bucket, confirm the second one shows both its
  original name AND a "renamed" badge with the new "(from FolderName)"
  target name, while the first keeps its plain name with no badge.
- **Organization move execution — USE A DISPOSABLE TEST FOLDER, NOT A REAL
  PHOTO LIBRARY.** This is the first feature in the app that moves files
  outside Recycle Bin safety; verify carefully on throwaway data (copies of
  a few test images, not anything you care about) before ever pointing it
  at real photos.
  - Scan the disposable test folder, switch to Organization, click "Choose
    Destination…" and confirm the picker opens and defaults to (or lets
    you browse from) the scanned folder; confirm the chosen path displays
    clearly next to the button.
  - Click "Organize Files…" and confirm the confirmation dialog shows the
    destination path and an explicit, clear warning that this is a real
    move, not reversible through the Recycle Bin — read it carefully
    rather than clicking through.
  - After confirming, check %LOCALAPPDATA%\ImageCleanup\move-logs\ for a
    new move-log_*.json file, and open it — confirm it lists every file
    that was in the plan with correct source/destination paths, in a
    format you could act on manually if you ever needed to reverse a move.
  - Confirm the files actually moved to
    <destination>\<Year>\<Month>\<Photo|NoMetadata>\<filename>, and that a
    conflict-renamed file landed at its "(from FolderName)" name, not the
    original.
  - Confirm the summary dialog shows correct succeeded/failed counts and
    the move log path; if you can arrange a locked/in-use file to force one
    failure, confirm the rest of the batch still completes and the failure
    is reported rather than the whole thing aborting.
  - Confirm Duplicates and Quality both reflect the moved files' new
    state after execution (gone from their old location, without needing
    a manual rescan) — this depends on ExecutePlanAsync calling
    ScanSessionService.RefreshAsync().
  - Only after all of the above look right on disposable data would it be
    reasonable to try this against a real folder — and even then, treat it
    as safe to abandon a scan on, not as tested-to-perfection.
- Organization — hybrid month folder naming: on the same disposable test
  folder, after running Organize Files, confirm the created month folders
  are named like "01 - January", "03 - March" (zero-padded number, " - ",
  full month name) — not plain "01" and not just "January". Confirm the
  TreeView preview (before executing) still shows the Month node's label
  as just the word name ("January", not "01 - January") — the preview
  intentionally did not change.
- Quality ComboBox blanking fix: scan a folder with several files in
  Quality, stage a Delete on one, commit it so the list shrinks by one
  row, then check every remaining row's action ComboBox — confirm none of
  them show blank; each should show its actual current action ("None"
  unless you'd changed it). Repeat with staging/removing a few different
  rows (not just the first or last) to exercise different container-reuse
  positions, since the bug was intermittent/position-dependent rather
  than affecting every row every time.
