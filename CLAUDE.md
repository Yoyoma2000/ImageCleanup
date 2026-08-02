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
  - MainWindow.xaml is the shell — shared folder-selection toolbar +
    NavigationView/Frame, `PaneDisplayMode="Left"` + `IsPaneOpen="False"`
    (pushes content when expanded rather than overlaying it — see Status
    for why `LeftCompact` was rejected) with `Background` set explicitly
    on both `NavigationView` and its `Frame` to avoid a theme-brush
    mismatch during the pane animation. It hosts no feature logic itself.
  - App.xaml — merges `XamlControlsResources` plus a **Dark-only**
    `ResourceDictionary.ThemeDictionaries` override of the
    `NavigationViewDefaultPaneBackground` theme resource (aliased to
    `LayerFillColorDefaultBrush`), giving the pane a brighter elevation
    in Dark theme. Deliberately not done via the `NavigationView.
    PaneBackground` property — that reproducibly crashes the WinAppSDK
    1.6.250205002 XAML compiler with no diagnostics (see Status).
  - Services/ScanSessionService — singleton (registered in App.xaml.cs via
    Microsoft.Extensions.DependencyInjection, resolved through the static
    `App.Services` provider). Owns the current folder + scanned FileRecords
    (ObservableCollection<FileRecord>) and the shared SQLite connection
    string; exposes ScanFolderAsync/RefreshAsync and a ScanCompleted event
    for pages to rebuild derived state from. This is the single source of
    truth every feature page reads from — no page runs its own scan.
  - Localization/LocExtension — a custom WinUI `MarkupExtension`
    (`{loc:Loc Key=...}`) resolving static XAML text through
    `Data.Services.LocalizationService.Current` at parse time; see
    Conventions below for the rule on adding new user-facing strings, and
    Status for the full localization architecture (dictionary location,
    key naming, fallback behavior).
  - Views/ — one Page per feature (Duplicates/Quality/Organization/
    Settings, all implemented), plus two dialogs: GroupDetailDialog
    (Duplicates' multi-file comparison grid) and SinglePhotoDialog (a
    shared "view one photo bigger" dialog reused by Quality and
    Organization — takes a plain `(filePath, Func<byte[]?>)` rather than
    binding to either caller's own ViewModel type).
  - ViewModels/ — one ViewModel per feature page (DuplicatesViewModel,
    QualityViewModel, OrganizationViewModel, SettingsViewModel) plus
    shared per-row view models (FileActionViewModel, StagingEntryViewModel,
    DuplicateGroupViewModel, OrganizationNodeViewModel, ThumbnailLoader,
    ActionDisplay) usable by any future feature. FileActionViewModel has
    two constructors: the original `(fileRecordId, filePath, isSuggested)`
    bool overload used by Duplicates (defaults Keep/Delete), and a newer
    `(fileRecordId, filePath, initialAction, blurScore)` overload used by
    Quality (defaults explicitly, e.g. `ActionType.None`, and optionally
    carries a BlurScore for display — unused/null for Duplicates rows).
    `SelectedActionType` (a `Core.Grouping.ActionType` enum) is the stable
    value business logic reads/compares; `AvailableActions`/
    `SelectedActionIndex` are the localized-display-text/index pair the
    ComboBox actually binds — see Status for why the two are split.
- tests/ImageCleanup.Core.Tests, tests/ImageCleanup.Data.Tests — xUnit.
  No test project exists for ImageCleanup.App (WinUI/XAML layer) — see
  Notes for why, and Status for how App-layer changes get verified
  instead (build-reaching-the-known-wall + Core/Data-level proxy tests
  where a WinUI feature has a testable non-UI counterpart).

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
- **Every new user-facing string must go through LocalizationService**
  (`{loc:Loc Key=...}` in XAML, `LocalizationService.Current.GetString(...)`
  in code-behind/ViewModels) with a matching key added to `dev.json`
  (verbatim/technical wording), `en.json` (plain-language), and
  `zh.json` (Simplified Chinese) — never a hardcoded literal, even a
  short one. Verify key parity across all three files before considering
  a change done (a Node one-liner comparing `Object.keys()` across the
  three JSON files is the pattern used throughout this project's
  history — no persisted test does this, since Data.Tests has no reason
  to reference the App project's bundled Strings/ content). See Status
  for the full architecture and fallback behavior.

## Commands
- Build: dotnet build
- Test: dotnet test
- Run: dotnet run --project src/ImageCleanup.App  (CLI-only for Core/Data;
  App must be run via Visual Studio F5 — see Notes. **Exception**: `dotnet
  build src/ImageCleanup.App` now actually works from the CLI as of the
  MSIX packaging pivot below — see Notes for the full explanation of why
  this constraint, true for the entire rest of this project's history,
  no longer holds.)

## Publishing (MSIX packaging)
**This supersedes the earlier self-contained/unpackaged approach —
unpackaged deployment is confirmed unreliable for this app and should
not be used.** The original unpackaged publish (see "Superseded" below
for the full history) reliably crashed on launch outside the dev
environment with `0xC000027B` in `Microsoft.UI.Xaml.dll` / `combase.dll`
`E_FAIL` — a WinRT/COM activation failure specific to unpackaged apps'
reg-free activation path. MSIX packaging sidesteps this entirely: a
packaged app gets its WinRT classes registered for real at install time
(the mechanism unpackaged apps have to fake via reg-free WinRT, which is
what was failing), so this class of crash structurally can't happen for
a packaged app the way it did here.

**Goal**: an installable `.msix` (+ a small `Dependencies\` folder
carrying the Windows App SDK runtime packages, + a signing certificate
end users trust once) that installs and runs via Windows' normal app
install mechanism — no more "copy a folder and hope reg-free WinRT
works," no more Recycle-Bin-style unpackaged fragility.

### What changed in the project
This app never had a separate **Windows Application Packaging
Project** — it's been unpackaged since day one. Rather than add a
second project, it's now set up for **single-project MSIX** (a Windows
App SDK feature specifically for this: one project, no separate
packaging project, `Package.appxmanifest` lives directly in
`ImageCleanup.App`). Changes made:
- `ImageCleanup.App.csproj`: removed `<WindowsPackageType>None</
  WindowsPackageType>` (that property is what opted the project OUT of
  packaging in the first place) and added `<EnableMsixTooling>true</
  EnableMsixTooling>` plus `<PublishProfile>Properties\PublishProfiles\
  win10-$(Platform).pubxml</PublishProfile>` — the two properties
  Microsoft's official single-project-MSIX conversion steps specify for
  turning an existing non-packaged WinUI 3 project into a packaged one.
  Also added a `Content` item for the new `Images\*.png` package assets
  (below) — `CopyToOutputDirectory=PreserveNewest`, matching the existing
  `Strings\*.json` item's pattern.
- **`Package.appxmanifest`** (new file, project root) — the package
  manifest single-project MSIX needs. `Identity Name="ImageCleanup"`,
  `Publisher="CN=ImageCleanup"` (this exact string is what the signing
  certificate's Subject must match — see Certificate below),
  `DisplayName`/`PublisherDisplayName` both `"ImageCleanup"`,
  `TargetDeviceFamily Name="Windows.Desktop"` matching the app's existing
  `TargetPlatformMinVersion` (10.0.17763.0), and a single `runFullTrust`
  capability (no `internetClient` or anything else — this app does
  real file I/O across arbitrary user-selected folders but never talks
  to the network, so `runFullTrust` alone covers what it actually needs).
  `Executable="$targetnametoken$.exe"` / `EntryPoint="$targetentrypoint$"`
  are the standard MSBuild-filled tokens every WinUI 3 packaged
  template uses — confirmed these resolve correctly (to
  `ImageCleanup.App.exe` / `Windows.FullTrustApplication`) in the
  generated `AppxManifest.xml`, not hand-verified from documentation
  alone.
- **`Images\` folder** (new, project root) — the five package-asset
  images `Package.appxmanifest` references: `Square44x44Logo.png` (44×44),
  `Square150x150Logo.png` (150×150), `Wide310x150Logo.png` (310×150),
  `StoreLogo.png` (50×50), `SplashScreen.png` (620×300). **These are
  placeholder art** — a solid blue fill with a plain "IC" glyph,
  generated programmatically, not real branding. The package builds and
  installs correctly with these; swapping in real icons whenever that's
  prioritized is a pure asset-replacement (same filenames/dimensions),
  no manifest or project changes needed. Flagged under Known gaps below
  so it isn't silently forgotten.
- **`Properties\PublishProfiles\win10-x64.pubxml`, `win10-x86.pubxml`,
  `win10-arm64.pubxml`** (new files) — one per architecture, each just
  `Configuration=Release`, the matching `Platform`/`RuntimeIdentifier`,
  and `SelfContained=false` (see "Framework-dependent, deliberately"
  below). These are what `$(Platform)` resolves against in the
  `<PublishProfile>` property above, and what Visual Studio's **Create
  App Packages** wizard uses as its per-architecture presets.
- **The old `FolderProfile.pubxml`** (from the unpackaged/self-contained
  attempt) is untouched but no longer the recommended path — left in
  place since deleting a file Alan might still want for comparison isn't
  this session's call to make; see "Superseded" below.

### F5 debugging needs its own local deploy — this bit Alan directly
**Packaged (MSIX) apps activate through package identity — a raw
`.exe` launch bypasses that entirely, and F5 defaulted to exactly that
raw launch after the MSIX pivot, producing an immediate crash:
`System.Runtime.InteropServices.COMException: Class not registered
(0x80040154 (REGDB_E_CLASSNOTREG))`.** This is a real, hit-in-practice
regression, not a hypothetical — it worked fine before the pivot (when
the project launched as a plain unpackaged `.exe`, no package identity
involved at all) and broke immediately after (Visual Studio kept
launching the raw `.exe` the same old way, but the app's own code now
expects to be activated *as* a package). Two separate things were
missing, not one — both required, confirmed against Microsoft's
official single-project-MSIX conversion steps rather than guessed at:
- **Configuration Manager's Deploy checkbox wasn't enabled for
  ImageCleanup.App.** In a `.sln` file this shows up as missing
  `.Deploy.0` entries in `GlobalSection(ProjectConfigurationPlatforms)`
  — confirmed directly by inspecting `ImageCleanup.sln`, which had
  `ActiveCfg`/`Build.0` lines for every Debug/Release × Any CPU/x64/x86
  combination but no `Deploy.0` lines at all. Without Deploy enabled,
  a build never registers the package locally, so even a correctly
  packaged build has no local package identity for anything to
  activate against. **Fixed**: added a `Deploy.0` line (matching the
  existing `Build.0` value) for every one of ImageCleanup.App's six
  Debug/Release × Any CPU/x64/x86 configuration/platform combinations.
  Core/Data/the two test projects were left untouched — Deploy only
  makes sense for the one project that's actually an app package.
- **No `Properties\launchSettings.json` existed.** Per Microsoft's own
  single-project-MSIX conversion doc (the same one this whole pivot
  followed originally), Visual Studio 2026+ (confirmed this is what
  Alan's on — `ImageCleanup.sln` reports `VisualStudioVersion = 18.x`,
  and VS's major version 18 *is* the 2026 release line) needs an
  explicit `commandName: "MsixPackage"` launch profile for F5 to
  actually launch the app *as* an activated package rather than
  falling back to a direct executable launch. This file didn't exist at
  all (the project never needed one before — an unpackaged app just
  launches its `.exe` directly, no profile needed). **Fixed**: added
  `src/ImageCleanup.App/Properties/launchSettings.json` with a single
  `"ImageCleanup.App"` profile, `commandName: "MsixPackage"`, matching
  the exact schema from Microsoft's conversion doc verbatim (not
  invented from scratch) — `alwaysReinstallApp: false` so routine F5
  runs stay fast (only re-registers when something actually changed),
  everything else at the documented defaults.
- **Both were needed together, not either alone** — Deploy without the
  right launch profile would still try to launch the raw `.exe` (same
  crash); the right launch profile without Deploy would try to activate
  a package that was never actually registered locally (a different,
  probably even more confusing failure). This is why the task asked to
  "confirm what's actually needed... rather than guessing at a single
  property" — there wasn't a single property here, there were two
  independent gaps, from two different layers of the toolchain (`.sln`
  IDE metadata vs. a per-project launch-profile file).
- **Not expected to affect the already-working Release/Create-App-Packages
  flow at all** — neither change is read by `dotnet build`/`dotnet
  publish`/the packaging MSBuild targets; `.sln` Deploy flags are pure
  Visual-Studio-IDE metadata (confirmed: re-ran `dotnet build -c Debug
  -p:Platform=x64` after both fixes and it succeeds identically to
  before), and `launchSettings.json` is only ever consulted for F5/
  `dotnet run`-style launches, never for a build or a **Create App
  Packages** publish. The Release packaging pipeline verified working
  end-to-end in the previous session (build → sign → trust → install →
  launch, with a throwaway certificate) is untouched by either of these
  files.

### Framework-dependent, deliberately
The three new `.pubxml` profiles set `SelfContained=false` — this
package depends on the `Microsoft.WindowsAppRuntime.1.6` framework
package being present (confirmed in the generated `AppxManifest.xml`:
`<PackageDependency Name="Microsoft.WindowsAppRuntime.1.6" MinVersion=
"6000.519.329.0" .../>`) rather than bundling a private copy the way
the old self-contained/unpackaged attempt did. This is a deliberate
change of direction, not an oversight:
- The entire reason `WindowsAppSDKSelfContained=true` was used before
  was to avoid installing a separate runtime for an *unpackaged* app
  — but self-contained bundling is also exactly what forced the app
  onto the reg-free WinRT activation path that turned out to be the
  crash's root cause. MSIX packaging doesn't need that workaround at
  all: a real package install registers everything properly regardless
  of whether the runtime is bundled or referenced.
- Framework-dependent keeps the `.msix` itself small; the
  `Microsoft.WindowsAppRuntime.1.6` framework package is a normal MSIX
  dependency, installed automatically alongside the app the same way
  any MSIX framework dependency is — Visual Studio's **Create App
  Packages** output includes a `Dependencies\` folder with that
  framework package for every architecture specifically so this
  install can happen offline/sideloaded, not just from the Store.
- If self-contained MSIX is ever wanted instead (e.g. to avoid the
  dependency install step entirely), it's a one-line change
  (`SelfContained=true` in the relevant `.pubxml`) — not attempted here
  since framework-dependent is the standard, lower-friction default for
  MSIX and there's no reason yet to deviate from it.

### Certificate / code signing
MSIX packages must be signed — even for pure sideloading, with no
Microsoft Store involved. This isn't optional the way it effectively
was for the old unpackaged .exe.
- **What it is**: a normal code-signing certificate, self-signed
  (not issued by a public CA) since this app isn't going through the
  Store. Self-signed is completely standard for sideloaded/internal MSIX
  distribution — this isn't a workaround or a lesser option, it's the
  documented, supported approach for exactly this scenario.
- **The certificate's Subject must exactly match `Package.appxmanifest`'s
  `Publisher` value** (`CN=ImageCleanup`) — MSIX validates this at
  install time and rejects a mismatch. As long as Alan generates the
  certificate through Visual Studio's own wizard (below) rather than
  hand-typing a Subject, this can't drift out of sync — the wizard reads
  the manifest's `Publisher` value and pre-fills it.
- **Recommended: generate it via Visual Studio's Publish wizard** (the
  **Create App Packages** flow, not the old **Folder** flow) — it has a
  built-in "create a test certificate" option that handles subject
  matching, key generation, and `.pfx` export in one step. This is the
  path documented in the step-by-step below; don't hand-roll a
  certificate with `New-SelfSignedCertificate`/`makecert` unless the
  wizard's option is unavailable for some reason.
- **Where it's stored**: Visual Studio saves the generated `.pfx`
  alongside the project (e.g.
  `src\ImageCleanup.App\ImageCleanup.App_TemporaryKey.pfx`) and wires
  `<PackageCertificateKeyFile>`/`<PackageCertificateThumbprint>` into
  `ImageCleanup.App.csproj` automatically so every subsequent build/
  publish signs with the *same* certificate — don't remove those
  properties once they appear, and don't regenerate the certificate
  casually: a new certificate means every machine that already trusted
  the old one needs to trust the new one too. **`.pfx` files are now
  git-ignored** (`.gitignore` gained a `*.pfx` rule this session,
  specifically for this — a private key must never be committed).
  **Back this file up somewhere durable once it exists** (it's the
  only copy) — losing it means every future rebuild gets a different
  certificate, breaking trust on every machine that installed a
  previous build.
- **End users must trust this certificate once, per machine, before
  installing** — this is the one-time setup step covered in "Installing
  ImageCleanup" below. It is *not* a per-app-update step; once a
  machine trusts the certificate, every future `.msix` signed with the
  same certificate installs without re-trusting.
- **Verified working, this session**: to confirm the whole pipeline
  (manifest → build → sign → trust → install → launch) actually works
  end-to-end and isn't just theoretically correct from documentation,
  a throwaway self-signed test certificate was generated directly
  (`New-SelfSignedCertificate`, Subject `CN=ImageCleanup` matching the
  manifest), used to sign a real `.msix` built via `dotnet build
  -p:GenerateAppxPackageOnBuild=true`, trusted via `Import-Certificate`
  into both `Cert:\CurrentUser\TrustedPeople` *and*
  `Cert:\CurrentUser\Root`, and installed via `Add-AppxPackage`. **This
  succeeded** — the exact class of crash this whole pivot exists to fix
  did not reproduce. The throwaway certificate and its `.pfx`/`.cer`
  were deleted afterward (from both the certificate store and disk) —
  Alan's real certificate should come from Visual Studio's wizard, not
  reuse this session's throwaway one.
  - **One finding worth knowing about in advance**: importing into
    `Cert:\CurrentUser\Root` (Trusted Root Certification Authorities)
    specifically requires *interactive* confirmation — Windows blocks a
    fully silent/scripted import into that store as a deliberate
    anti-malware protection (confirmed directly: `Import-Certificate`
    into `Cert:\CurrentUser\Root` failed with "UI is not allowed in this
    operation"; only `Cert:\LocalMachine\Root`, which needs admin, can be
    done silently). **This means the double-click-the-.cer-file method
    in the end-user instructions below is the right approach, not
    something to try to script away** — a real end user is expected to
    see and click through that confirmation dialog once.

### Step-by-step (Alan, in Visual Studio — same reason as always: Claude
Code can configure the project but can't drive the Visual Studio UI)
1. Open `ImageCleanup.sln` in Visual Studio.
2. Confirm `Package.appxmanifest` shows up in Solution Explorer (it
   should appear as a normal project item with a manifest icon —
   double-clicking it opens Visual Studio's visual manifest designer).
   Optionally review/adjust `DisplayName`/`Publisher Display Name`/
   `Version` there — everything currently set is a reasonable default,
   not a placeholder that must be changed before this works.
3. Right-click the **ImageCleanup.App** project → **Publish...** →
   choose **Create App Packages** this time (not **Folder** — that was
   the old unpackaged flow).
4. Choose **Sideloading** (not "Microsoft Store under a new app name" —
   this app was never intended for Store distribution).
5. On the signing step: choose **Yes, select a certificate** → **Create...**
   to generate a new test certificate. Visual Studio pre-fills the
   Subject from `Package.appxmanifest`'s `Publisher`
   (`CN=ImageCleanup`) — accept that rather than typing a different
   one. Set a password when prompted (remember it — needed again only
   if re-signing manually later, not for normal rebuilds).
6. Select architecture(s) — **x64** alone is enough unless Alan
   specifically needs x86/arm64 machines covered too.
7. **Generate an app bundle**: choose **Never** — single-project MSIX
   only supports producing a single `.msix` per architecture anyway (not
   an `.msixbundle`), so bundling isn't applicable here.
8. Click **Create**. This builds, packages, and signs — output location
   is shown at the end (see "Output & distribution" below for exactly
   what's in it).
9. **First build only**: if this is the very first time the certificate
   was generated, Visual Studio may also prompt to install it locally
   so *this* machine (the dev machine) can test-install the package too
   — accept that prompt for local testing; it's separate from what end
   users need to do (below).

### Output & distribution
The **Create App Packages** wizard's output folder (path shown at the
end of the wizard, typically under
`src\ImageCleanup.App\AppPackages\ImageCleanup.App_1.0.0.0_Test\` or
similar) contains everything needed to distribute — **give end users
this whole folder, not just the `.msix` file**:
- `ImageCleanup.App_1.0.0.0_x64.msix` — the actual app package.
- `ImageCleanup.App_1.0.0.0_x64.cer` (or similarly named) — the public
  half of the signing certificate. **This is what end users trust before
  installing** — safe to share freely (it contains no private key).
- `Dependencies\<architecture>\Microsoft.WindowsAppRuntime.1.6.msix` —
  the Windows App SDK runtime framework package, one per architecture.
  Needed on any machine that doesn't already have it installed (most
  won't, unless they've installed other Windows App SDK apps before).
- `Install.ps1` — a straightforward PowerShell installer script.
- `Add-AppDevPackage.ps1` (+ `Add-AppDevPackage.resources\`) — an
  older, more defensive install script (checks for a developer license/
  sideloading, prompts to install the certificate, then installs the
  package + dependencies) — this is the one referenced in the end-user
  instructions below, since it's the most forgiving of a machine that
  hasn't sideloaded anything before.
- Confirmed directly this session (via the manual build+sign+install
  described above, not just from documentation): a `dotnet build
  -p:Platform=x64 -c Release -p:GenerateAppxPackageOnBuild=true` from
  the CLI also produces an equivalent (unsigned) `.msix` — **this is
  the surprising finding that overturns this project's long-standing
  "App can't be built via CLI" constraint** (see Notes). This isn't a
  recommended replacement for the VS wizard (no certificate, no
  `Dependencies\`/`Install.ps1` convenience folder, single-architecture
  only) — it's noted here because it's genuinely new information worth
  having on record, and because it's how this session verified the
  packaging config compiles correctly without needing Alan's VS session
  to do it first.

### Installing ImageCleanup (for a non-technical end user)
**Read this section as if explaining it to someone who has never used a
terminal or installed unsigned software before — no assumed knowledge.**
You'll receive a folder (probably a `.zip` someone sent you, or a folder
you copied from a USB drive) — unzip it first if needed, so you have a
plain folder you can open in File Explorer.

**Part 1 — trust the certificate (only needed the first time)**

1. Open the folder you were given. Find the file that ends in `.cer`
   (for example `ImageCleanup.App_1.0.0.0_x64.cer`).
2. Double-click it. A window titled **Certificate** opens.
3. Click the **Install Certificate...** button.
4. A wizard opens. Choose **Local Machine** (not "Current User"), then
   click **Next**. (If Windows asks "Do you want to allow this app to
   make changes to your device?", click **Yes** — installing a
   certificate always asks this, it's normal.)
5. Choose **Place all certificates in the following store**, click
   **Browse...**, select **Trusted Root Certification Authorities**,
   click **OK**, then **Next**, then **Finish**.
6. You'll see a **Security Warning** popup asking to confirm you want
   to install this certificate — this is Windows double-checking, since
   installing a "root" certificate is normally something only IT
   departments do. Since you got this file directly from someone you
   trust (not a random download), click **Yes**.
7. A popup says "The import was successful." Click **OK**.

**Part 2 — allow apps from outside the Microsoft Store (only needed if
this is the first non-Store app you've installed)**

Windows may already allow this — try Part 3 first, and only come back
here if installing fails with a message about "this app can't install"
or "sideloading" or "developer mode."

1. Click the **Start** button, type **Settings**, and open it.
2. Depending on your Windows version, you'll either see a page called
   **For developers** directly, or you'll need to go to **System** →
   **Advanced** first, then look for a **For developers** section.
3. Turn on **Developer Mode**.
4. A warning appears explaining this unlocks some extra features — click
   **Yes** to confirm.

**Part 3 — install the app**

1. Go back to the folder you were given.
2. Right-click the file named **Add-AppDevPackage.ps1** and choose
   **Run with PowerShell**.
3. A blue PowerShell window opens and walks you through installation
   automatically — it checks everything it needs (including the
   certificate from Part 1) and installs the app plus anything else it
   needs along the way. Just follow any prompts it shows (press Enter or
   type **Y** if asked to confirm something).
4. When it finishes, close the PowerShell window. **ImageCleanup** now
   appears in your Start Menu like any other app — search for it or
   pin it, same as anything else you've installed.

**If double-clicking/"Run with PowerShell" gives an error about
scripts being "disabled on this system" or "not digitally signed"**
— some computers block running scripts by default (PowerShell's
execution policy). This is normal, not a sign of a problem, and easy
to work around for just this one script:

1. In the folder you were given, click once in the empty area of the
   File Explorer address bar (at the top of the window).
2. Type `powershell` and press **Enter** — a PowerShell window opens,
   already in the right folder.
3. Type (or copy/paste) this exact line and press **Enter**:
   `powershell -ExecutionPolicy Bypass -File .\Add-AppDevPackage.ps1`
4. Follow the same prompts as before.

This only affects this one script run — it does **not** change any
permanent setting on the machine; the next script run (or the next
`.ps1` file from anywhere else) is blocked by the default policy again,
same as before this workaround.

**If something goes wrong**: the PowerShell window's messages usually
say exactly what's missing (e.g. "certificate not trusted" means go back
to Part 1; "sideloading not allowed" means go back to Part 2). If it
still doesn't work, take a screenshot of the error and send it back for
help — don't try to guess-fix a certificate/security error yourself.

**Updates later**: once installed, getting a newer version is the same
Part 3 process with the new `.msix` — Part 1 and 2 don't need repeating
unless the certificate itself changes (it won't, unless Alan
regenerates it).

## Publishing — superseded: self-contained, unpackaged .exe
**Do not use this approach — kept for history and because the crash
investigation below is the reason MSIX packaging (above) exists.** If
anything in this section conflicts with the MSIX section above, the
MSIX section is current.


**Goal**: a folder containing `ImageCleanup.App.exe` + all dependency DLLs
+ the bundled Windows App SDK runtime, that runs on a machine with no
Visual Studio, no .NET SDK, and no separately-installed Windows App
Runtime. This is distribution prep only — no installer yet, just a
copyable folder (see "No installer or distribution path" under Known
gaps for what comes after).

- **CLI `dotnet publish` was tried first and hits the exact same wall as
  `dotnet build`** — confirmed directly:
  `dotnet publish -r win-x64 -p:WindowsPackageType=None -p:SelfContained=true
  -p:WindowsAppSDKSelfContained=true` from `src/ImageCleanup.App` fails with
  the identical `MSB4062` (`Microsoft.Build.Packaging.Pri.Tasks.
  ExpandPriContent` task can't load, missing `Microsoft.Build.Packaging.Pri.
  Tasks.dll`) as `dotnet build` always has — this DLL only ships with
  Visual Studio's MSBuild toolset, not the plain SDK, regardless of
  build vs. publish. **Must use Visual Studio's Publish feature** — see
  the step-by-step below.
- **MSBuild properties needed** (set via the Publish profile UI, not
  hand-edited into the .csproj — see steps below for why):
  `WindowsPackageType=None` (already the project default — unpackaged,
  no MSIX), `SelfContained=true` + `WindowsAppSDKSelfContained=true`
  (bundles the .NET runtime AND the Windows App SDK runtime into the
  output folder, so the target machine needs neither installed),
  `RuntimeIdentifier=win-x64` (see architecture note below),
  `PublishReadyToRun=true` (worth enabling — precompiles IL to native
  code for faster startup; the tradeoff is a larger output folder and a
  slightly longer publish, both acceptable for a one-time manual publish
  step like this).
- **Architecture: the project now consistently targets x64** — fixed
  after the "Any CPU silently resolves to x86" footgun (flagged when
  publish config was first researched) actually surfaced as a hard
  error: `ImageCleanup.sln`'s solution-to-project platform mapping used
  to send **both** `Debug|Any CPU` and `Release|Any CPU` to `x86` for
  the App project specifically, so when a Publish profile set
  `RuntimeIdentifier=win-x64` while the active solution configuration
  ("Any CPU", VS's default) still resolved the App project's actual
  `Platform`/`PlatformTarget` to `x86`, MSBuild correctly refused with
  `"The RuntimeIdentifier platform 'win-x64' and the PlatformTarget
  'x86' must be compatible."` — publish logs confirmed `Configuration:
  Release x86` even though the profile said win-x64. **Fixed at the
  root, not worked around in the wizard**: `ImageCleanup.sln`'s
  `ProjectConfigurationPlatforms` section now maps the App project's
  `Debug|Any CPU` and `Release|Any CPU` to `Debug|x64`/`Release|x64`
  instead of `x86` — so "Any CPU" (what F5 and a fresh Publish both use
  by default) now consistently means x64 for this project, matching the
  win-x64 publish target with nothing left to silently disagree. The
  explicit `x86`/`x64` solution configurations remain available
  unchanged (still selectable directly from VS's Solution Configuration
  dropdown) if x86 is ever needed for something specific — only the
  *default* changed. Core/Data/their test projects have no
  `Platforms`/`RuntimeIdentifiers` restriction at all (pure logic
  libraries, no RID dependency) and were confirmed unaffected — their
  `Any CPU` mapping was already `Any CPU` before and after this change,
  and `dotnet build`/`dotnet test` were re-run to confirm (212 tests
  still pass, 124 Core + 88 Data).
  - **F5 debugging still works exactly as before, functionally** — this
    was the main thing to protect, since F5 is how this app has
    actually been run/tested this entire project. The one real
    difference: F5 under the default "Any CPU" solution config now
    launches an x64 build instead of x86 (previously silently x86, per
    the footgun above) — nothing in the app's own code is
    architecture-specific (no P/Invoke, no native-pointer-size
    assumptions), so this is not expected to change observable
    behavior, only which architecture's DLLs get loaded. Needs Alan's
    confirmation via an actual F5 run, same as every other WinUI-layer
    change in this file — flagged in the manual verification checklist
    below rather than assumed risk-free.
  - Also fixed in the same pass: the actual `FolderProfile.pubxml`
    Alan generated while working through the Publish wizard was
    missing `WindowsAppSDKSelfContained` (the wizard didn't expose it
    as a checkbox, exactly the gap flagged when this was first
    researched) — added `<WindowsAppSDKSelfContained>true</
    WindowsAppSDKSelfContained>` and `<WindowsPackageType>None</
    WindowsPackageType>` directly to
    `src/ImageCleanup.App/Properties/PublishProfiles/
    FolderProfile.pubxml` so the profile actually produces the
    Windows-App-Runtime-independent output this whole effort is for,
    not just a .NET-self-contained-but-still-needs-WinAppRuntime build.
- **Publish profile file**: `src/ImageCleanup.App/Properties/
  PublishProfiles/FolderProfile.pubxml` now exists (created by Alan
  running the Publish wizard once) and is checked into git — the
  `.pubxml.user` sitting alongside it (machine-specific publish
  history/state) is not, matching the repo's existing blanket `*.user`
  gitignore rule. Re-publishing going forward is a single button click
  against this saved profile — no need to re-answer the wizard, and no
  need to manually switch platform in the wizard anymore now that Any
  CPU resolves to x64 by default.

### Step-by-step (Alan, in Visual Studio — Claude Code cannot drive this)
**A saved profile already exists** (`src/ImageCleanup.App/Properties/
PublishProfiles/FolderProfile.pubxml`, checked in) with `RuntimeIdentifier=
win-x64`, `SelfContained=true`, `WindowsAppSDKSelfContained=true`, and
`WindowsPackageType=None` already set correctly — so steps 1-5 below only
need to be redone if that profile is ever deleted or a second profile is
wanted; otherwise skip straight to opening it in Publish and clicking
**Publish**.
1. Open `ImageCleanup.sln` in Visual Studio.
2. In Solution Explorer, right-click the **ImageCleanup.App** project ->
   **Publish...**.
3. Choose **Folder** as the publish target (not ClickOnce, not Azure,
   not a Windows package/MSIX — a plain folder is exactly the
   "copyable folder" output this task asked for).
4. Pick a folder location for the profile (any local path — this is
   just where the wizard writes the profile file, not the publish
   output itself).
5. Once the profile is created, click the profile's **Edit** (pencil)
   / **Show all settings** to open the full settings dialog and set:
   - **Configuration**: Release
   - **Target Runtime**: `win-x64` (not "Portable" — Portable produces a
     framework-dependent build that still requires the .NET/Windows App
     Runtime installed on the target machine, which defeats the goal
     here)
   - **Deployment mode**: **Self-contained**
   - **Target location**: wherever the output folder should land (e.g.
     `bin\Release\net9.0-windows10.0.19041.0\win-x64\publish\` is the
     default if left unchanged — see "Output location" below)
   - Under **File publish options** (or by editing the generated
     `.pubxml` directly after this first pass, whichever is easier in
     the VS version installed): confirm/add
     `<WindowsAppSDKSelfContained>true</WindowsAppSDKSelfContained>` and
     `<WindowsPackageType>None</WindowsPackageType>` — the wizard may not
     expose `WindowsAppSDKSelfContained` as a checkbox in every VS
     version; if it's missing from the UI, open the generated `.pubxml`
     in a text editor and add the line manually inside the existing
     `<PropertyGroup>`, then re-run Publish (editing the `.pubxml` this
     way is normal/expected — it's just MSBuild XML, not a special file).
   - Optionally enable **ReadyToRun compilation** (`PublishReadyToRun=
     true`) in the same settings dialog, for faster startup — worth
     turning on for a distributed build.
6. Click **Publish** (or **Save** then **Publish** if it only saved the
   profile). This is a real build+publish and may take a few minutes,
   especially with ReadyToRun enabled the first time.
7. **If it fails with `"The RuntimeIdentifier platform 'win-x64' and the
   PlatformTarget 'x86' must be compatible"`**: this was a real bug hit
   during the first publish attempt from this repo, now fixed at the
   `.sln` level (see the Architecture bullet above) — `ImageCleanup.sln`'s
   `Any CPU` mapping for the App project now resolves to x64, not x86, so
   this shouldn't recur. If it does resurface, check the active
   **Solution Configuration** dropdown in the VS toolbar isn't explicitly
   pinned to an `x86` solution platform (as opposed to `Any CPU` or
   `x64`) — publish uses whatever solution configuration/platform is
   currently active, not just the profile's own settings.
8. If it instead fails with the plain MSB4062 seen in CLI attempts (no
   platform-mismatch wording, just the PRI packaging task failing to
   load): this would mean even Visual Studio's own MSBuild toolset is
   somehow missing the PRI packaging component (unlikely if F5 already
   works, since F5 needs the same component) — in that case, run the
   Visual Studio Installer and confirm the "Windows App SDK C# Templates"
   / "Universal Windows Platform development" workload is installed,
   which is what provides `Microsoft.Build.Packaging.Pri.Tasks.dll`.
9. Confirm the output: open the target/output folder from step 5 and
   check for `ImageCleanup.App.exe`, a large set of `.dll` files
   (including `Microsoft.WindowsAppSDK.*`, `Microsoft.WindowsAppRuntime.*`
   runtime DLLs — proof `WindowsAppSDKSelfContained` actually took
   effect, not just the .NET runtime), the `Strings\` folder (dev/en/
   zh.json — already `CopyToOutputDirectory` content, should carry
   through publish unchanged), and no separate installer/MSIX file (this
   step deliberately doesn't produce one).

### Output location
Default (unless a custom Target location was set in step 5):
`src\ImageCleanup.App\bin\Release\net9.0-windows10.0.19041.0\win-x64\
publish\` — this whole folder is what gets copied to another machine to
test standalone. It should contain:
- `ImageCleanup.App.exe` — the entry point
- `ImageCleanup.App.dll`, `ImageCleanup.Core.dll`, `ImageCleanup.Data.dll`
  — this app's own assemblies
- Every third-party dependency DLL (CommunityToolkit.Mvvm,
  Microsoft.Data.Sqlite + its native `e_sqlite3.dll`, SixLabors.ImageSharp,
  MetadataExtractor, Microsoft.Extensions.DependencyInjection, etc.)
- The bundled Windows App SDK runtime DLLs (`Microsoft.WindowsAppRuntime.
  Bootstrap.dll` and friends) — this is the piece that makes the target
  machine not need Windows App Runtime pre-installed
- The .NET 9 runtime DLLs (self-contained — no separate .NET install
  needed on the target machine either)
- `Strings\dev.json` / `en.json` / `zh.json`
- `ImageCleanup.App.deps.json` / `.runtimeconfig.json` (standard
  self-contained-publish manifest files)

**Not yet done** (explicitly out of scope for this step, per the task):
no installer, no code signing — see the crash writeup immediately below
for the verification that *has* happened since, and what it found.

### Known crash: 0xC000027B in Microsoft.UI.Xaml.dll / combase.dll E_FAIL on the self-contained publish
**Status: mitigation applied (WindowsAppSDK patch-version bump), not yet
confirmed fixed — Alan needs to re-publish and re-test; see "What to do
next" at the end of this section.**

The first real publish output Alan produced (via the profile above) and
ran crashed immediately on launch. Windows Event Viewer showed two
correlated reports for every crash:
- **Application Error** (`Faulting module name: Microsoft.UI.Xaml.dll`,
  **Exception code: 0xC000027B**) — this is `STATUS_STOWED_EXCEPTION`
  ("an application-internal exception has occurred"), a generic WinRT
  wrapper around some other failure, not a diagnosis on its own.
- **Windows Error Reporting (WER)** for the same crash, naming
  `combase.dll` as `P4` with `P7: 80004005` (`E_FAIL`) — this is the
  actual failure: a COM/WinRT activation call inside `combase.dll`
  returned `E_FAIL` while the app was starting up, and that failure
  got stowed/rethrown as the `0xC000027B` seen in Event Viewer.

**This crash was reproduced directly** (not just inferred from Event
Viewer text) — the exact same folder Alan published to
`C:\Users\alanq\Downloads\ImageCleanupApp` was run directly, and the
process reliably exits with code `0xC000027B` every time, with Event
Viewer showing the identical `Microsoft.UI.Xaml.dll` / `combase.dll
E_FAIL 80004005` pair Alan reported. This let the actual investigation
happen empirically (running things and observing results) rather than
purely from documentation — see the findings below.

**The initial hypothesis (missing reg-free WinRT activation manifest)
was investigated and ruled out, not confirmed** — this matters, because
it means the fix isn't "add a manifest that was missing":
- The exe's manifest (WinUI/`WindowsAppSDKUndockedRegFreeWinRTInitialize`
  auto-generates this — nothing hand-written) was extracted and
  inspected directly. It's fully present and well-formed: 16
  `<asmv3:file>` entries (`Microsoft.UI.Xaml.dll`,
  `Microsoft.WindowsAppRuntime.dll`, `Microsoft.UI.Xaml.Controls.dll`,
  etc.), with **503** `<winrtv1:activatableClass>` entries under
  `Microsoft.UI.Xaml.dll` alone. Every one of those 16 files was
  confirmed physically present in the publish folder — no dangling
  reference to a missing DLL.
- The C# **auto-initializer** the Windows App SDK injects for exactly
  this scenario (a `[ModuleInitializer]`-attributed method in
  `Microsoft.Windows.Foundation.UndockedRegFreeWinRTCS.AutoInitialize`,
  added automatically to compilation via
  `Microsoft.WindowsAppSDK.UndockedRegFreeWinRT.CS.targets` whenever
  `WindowsAppSDKSelfContained=true` — exactly our config, no manual
  wiring needed or missing) was confirmed **actually compiled into**
  `ImageCleanup.App.dll` (its type/method names are present in the
  compiled assembly). This runs automatically before `Main`, forcing
  `Microsoft.WindowsAppRuntime.dll` to load, which is what's supposed to
  activate reg-free WinRT support for the whole process.
- So: the manifest is complete, all its referenced files exist, and the
  code that's supposed to activate reg-free WinRT support is present
  and wired up correctly. **Nothing was missing or misconfigured on the
  app's side of this mechanism.**

**What the investigation actually isolated**, via a direct A/B
comparison on the same machine:
- A **framework-dependent** build of the exact same code (not
  self-contained, not unpackaged-in-the-way-that-matters-here — it
  relies on the machine's already-installed/registered Windows App
  Runtime MSIX packages) **launches successfully**.
- The **self-contained, unpackaged publish** (same app code, same
  WindowsAppSDK DLL version, just running via the local
  reg-free-WinRT-activation path instead of real MSIX package
  registration) **crashes every time**, reproducibly, at the same fault
  offset.
- Copying the published folder to a different location (`C:\Temp`
  instead of `Downloads`) made no difference — ruling out a
  Downloads-folder-specific restriction (Mark-of-the-Web, Controlled
  Folder Access) as the cause.
- This isolates the failure specifically to **reg-free WinRT activation
  itself misbehaving at runtime** for this WindowsAppSDK version on this
  machine's current Windows build — not the app's own XAML/code (proven
  fine via the framework-dependent build), not a missing file, not a
  location/permissions issue.
- **Could not get a native call stack for the exact failing WinRT
  activation call** — this machine has no WinDbg/`cdb.exe` installed,
  and setting up Windows Error Reporting's `LocalDumps` registry key (to
  capture a persisted full crash dump for offline analysis) requires
  admin rights not available in this environment. If this ever needs
  deeper investigation, that registry key
  (`HKLM\SOFTWARE\Microsoft\Windows\Windows Error Reporting\LocalDumps\
  ImageCleanup.App.exe`, `DumpType=2` for a full dump) is the next
  concrete step, run by someone with admin access.

**Fix applied**: `ImageCleanup.App.csproj`'s `Microsoft.WindowsAppSDK`
package reference bumped from `1.6.250115003` (resolving to
`1.6.250205002` — over a year old at the time of this investigation) to
`1.6.250602001`, the newest release still on the 1.6.x line. This is a
same-major-version patch update — accumulated bug fixes only, no
`RuntimeCompatibilityChange`-flagged behavior changes (those start
appearing in the 1.7.x line) — chosen deliberately as the lower-risk
first move for a project this mature (212 tests, extensive manually-
verified feature history) rather than jumping straight to a 1.7.x major
upgrade. No specific 1.6.x release-note entry between `.250205002` and
`.250602001` names this exact crash — this is a "keep current, rule out
an already-fixed-upstream bug" mitigation, not a confirmed root-cause
fix. `dotnet build -p:Platform=x64` was re-run after the bump and
reaches the same expected MSB4062 wall with no new/different compile
errors, confirming the version bump doesn't break compilation; Core/Data
are unaffected (they don't reference WindowsAppSDK) and their 212 tests
still pass.

**If this doesn't resolve it**: the one Microsoft-documented fix that
actually matches this problem class — "Apps with an incorrect
activation manifest no longer crash in certain situations but instead
return an error" — shipped in **Windows App SDK 1.7.4**
(`RuntimeCompatibilityChange`: `DesktopSiteBridge_ActivationErrorCrash`),
not anywhere in the 1.6.x line. It's narrowly scoped to a component
called "DesktopSiteBridge" in the release notes, so it's not a certain
match for our crash either — but it's the closest documented precedent
found. A full 1.7.x upgrade is the natural escalation if the 1.6.x patch
bump doesn't fix it, but deserves its own dedicated pass (review every
`RuntimeCompatibilityChange` entry between 1.6 and 1.7 for anything that
could affect this app's existing behavior) rather than being bundled in
here speculatively.

**What to do next (Alan)**:
1. Re-publish via the existing `FolderProfile.pubxml` (Visual Studio ->
   right-click ImageCleanup.App -> Publish -> Publish) — this picks up
   the `1.6.250602001` package bump automatically.
2. Run the resulting `.exe` the same way as before (double-click from
   outside the dev environment).
3. **If it now launches successfully**: the patch bump fixed it — no
   further action, but worth noting in a future session so this
   write-up can be marked resolved rather than "mitigation applied."
4. **If it crashes again with the identical signature** (Event Viewer:
   `Microsoft.UI.Xaml.dll`, `0xC000027B`; WER: `combase.dll`,
   `80004005`) — this specific fix wasn't sufficient. Next step is the
   1.7.x escalation above, or capturing a real native crash dump (the
   `LocalDumps` registry key noted above, run by someone with admin
   rights on the target machine) to actually see the failing call
   stack instead of continuing to reason from external evidence.
5. **If it crashes with a *different* faulting module or exception
   code**: that means this specific issue is resolved and something
   *new* is now blocking startup — treat it as a fresh crash to
   diagnose (check Event Viewer the same way: Application Error for the
   faulting module/exception code, WER for the underlying HRESULT),
   not a continuation of this one.

## Notes
- **The "App cannot be built via CLI" constraint no longer fully holds,
  as of the MSIX packaging pivot** — this was true for this project's
  entire history up to that point, so it's worth stating precisely what
  changed and what didn't:
  - Under the *old* unpackaged config (`WindowsPackageType=None`),
    `dotnet build`/`dotnet publish` reliably hit `MSB4062` (missing
    `Microsoft.Build.Packaging.Pri.Tasks.dll` — a VS-toolset-only PRI
    packaging component for the *unpackaged* code path specifically).
    This was confirmed repeatedly across multiple sessions.
  - Under the *new* MSIX config (`EnableMsixTooling=true`, no
    `WindowsPackageType=None` override), `dotnet build -c Release
    -p:Platform=x64` **succeeds** — confirmed directly, not assumed —
    and `dotnet build ... -p:GenerateAppxPackageOnBuild=true` produces a
    real (unsigned) `.msix`. MSIX packaging apparently uses a different
    PRI-generation path (`makepri.exe`, bundled via the
    `Microsoft.Windows.SDK.BuildTools` NuGet package's own content)
    that doesn't depend on the VS-only DLL the unpackaged path needed.
  - **What's still true**: there's no test project for the App layer,
    and CLI building it still can't substitute for an actual F5 run
    (rendering, XAML layout, click-through behavior) — none of that
    changed. What changed is narrowly "the App project's C#/XAML now
    compiles from a plain CLI," which is still genuinely useful (it's
    how this session verified the packaging config without needing
    Alan's Visual Studio session first) but isn't the same as "fully
    CLI-testable."
  - If `WindowsPackageType=None` is ever reintroduced for any reason,
    expect the old `MSB4062` wall back — this isn't a permanent toolchain
    fix, it's specific to the packaged code path.
- ulong stored as signed long in SQLite; cast on read with (ulong)GetInt64().
- Always parse DateTime from SQLite with DateTimeStyles.RoundtripKind.
- **`NavigationView.PaneBackground` set directly (as a control property,
  in XAML or code) reproducibly crashes the WinAppSDK 1.6.250205002 XAML
  compiler** (`XamlCompiler.exe` exits 1, zero diagnostic output) —
  confirmed by bisection, see Status for the full writeup. If the pane's
  own fill needs to differ from the rest of `NavigationView`, override
  the `NavigationViewDefaultPaneBackground` theme resource key in
  `App.xaml`'s `ResourceDictionary.ThemeDictionaries` instead (a plain
  resource-dictionary entry — no property-setter codegen involved, not
  subject to this crash). Don't re-attempt the property directly without
  confirming a newer WindowsAppSDK actually accepts it first.
- ulong stored as signed long in SQLite; cast on read with (ulong)GetInt64().
- Always parse DateTime from SQLite with DateTimeStyles.RoundtripKind.
- **`NavigationView.PaneBackground` set directly (as a control property,
  in XAML or code) reproducibly crashes the WinAppSDK 1.6.250205002 XAML
  compiler** (`XamlCompiler.exe` exits 1, zero diagnostic output) —
  confirmed by bisection, see Status for the full writeup. If the pane's
  own fill needs to differ from the rest of `NavigationView`, override
  the `NavigationViewDefaultPaneBackground` theme resource key in
  `App.xaml`'s `ResourceDictionary.ThemeDictionaries` instead (a plain
  resource-dictionary entry — no property-setter codegen involved, not
  subject to this crash). Don't re-attempt the property directly without
  confirming a newer WindowsAppSDK actually accepts it first.

## Status
212 tests passing (124 Core, 88 Data), 0 failures.

**All three core features are feature-complete and manually verified
end-to-end on real data — this closes out the last remaining gap in the
original three-pillar roadmap (Duplicates, Quality, Organization all
complete). Since then: Settings (theme + language + maintenance
actions), full Light/Dark theme correctness (including NavigationView
pane elevation), complete Dev/English/Chinese localization coverage,
and a single-photo view for Quality/Organization have all also shipped
— see the dedicated entries further down for each. Only Distribution/
.exe packaging and video support remain from the original roadmap (see
Known gaps below).**
- **Duplicates** — recursive scan → exact/near-dup detection
  (SuggestionEngine) → independent staging (OrganizationStagingRepository)
  → Recycle Bin commit (CommitService).
- **Quality** — recursive scan → blurriest-first review (QualityReviewOrder)
  → independent staging (QualityStagingRepository) → Recycle Bin commit
  (CommitService).
- **Organization** — recursive scan → Year/Month/Category hierarchy
  planning (OrganizationPlanner, with conflict-resolved filenames and
  hybrid "01 - January"-style month folder naming) → TreeView preview with
  a checkbox per node (Year/Month/Category/File) and cascading selection
  (OrganizationSelectionNode) → real move execution of only the selected
  files, with a pre-execution move log (OrganizationExecutor) → full
  automated undo (OrganizationUndoService) that reverses a move log
  per-entry (never overwriting, safe to re-run against an already
  partially-undone log) and cleans up the empty Year/Month/Category
  folders left behind. No staging table of its own — see Known gaps below.

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
- Organization move execution (v1: all files — per-file/per-node selection
  added in a later session, see below). **This is the first feature in the
  app that moves
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
    plan, not the outcome). **At the time this was written, this log was
    the only safety net — no automated undo.** (Automated undo was added
    in a later session — see OrganizationUndoService further down.) A
    human could always read the log and manually move files back in the
    meantime.
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
- Scan performance investigation (user-reported "scanning feels slow" on
  larger folders). Added TEMPORARY per-step timing instrumentation to
  ScanSessionService.ScanFiles (Stopwatch per step, aggregated across the
  full scan, dumped via one `Debug.WriteLine` at the end prefixed
  `[ScanPerf]` — not yet removed, still in place for before/after
  comparison; remove/feature-flag once perf work is done). Confirmed the
  scan pipeline is fully sequential (no parallelization at all — one
  `foreach` over every file) and confirmed each cache-miss file was being
  decoded from disk three separate times (DHasher.ComputeFromFile,
  BlurDetector.ComputeBlurScore(path), LowDetailDetector.IsLowDetail(path)
  each independently called `Image.Load<L8>(path)`), plus
  FileCacheRepository opened a brand-new SqliteConnection for every single
  NeedsRescan/GetByPath/Upsert call (2-3 fresh connection opens per file,
  no shared connection or transaction).
- Fixed the triple-decode: ScanSessionService.ScanFiles now calls
  `Image.Load<L8>(path)` once per file and passes that shared instance to
  DHasher.Compute/BlurDetector.ComputeBlurScore/LowDetailDetector.IsLowDetail's
  existing pre-loaded-image overloads (no Core changes needed — those
  overloads already existed, just weren't being used from the scan path).
  4 new Core tests (SharedDecodeConsistencyTests) confirm byte-for-byte
  identical results between the old per-call-decode path and the new
  shared-decode path.
- Fixed the SQLite connection/transaction pattern: FileCacheRepository's
  GetByPath/Upsert/NeedsRescan gained optional trailing
  `SqliteConnection?`/`SqliteTransaction?` parameters (default null —
  every existing caller/test is unaffected and still gets a private
  open-and-dispose connection per call, so this was purely additive, not
  an API break). ScanSessionService.ScanFiles now opens one connection for
  the whole scan and commits in batches of 500 files
  (`SqliteBatchSize`) rather than one implicit transaction per file; a
  failure mid-batch only loses that batch (the transaction is disposed
  without committing in a `finally`, which rolls it back) — previously
  committed batches are untouched. 4 new Data tests confirm: writes inside
  an uncommitted transaction are visible to reads sharing the same
  connection/transaction, an uncommitted transaction rolls back on
  dispose, and a committed batch survives even when a later batch is
  abandoned mid-transaction.
- Real timing data from a 6183-file scan (193.5s total, still fully
  sequential at that point) showed BlurDetector (69s, 36%) and
  LowDetailDetector (58s, 30%) dominated — 66% combined, far more than the
  decode step (43s, 22%) the previous fix targeted. Root cause: both
  compute per-pixel statistics (Laplacian variance, pixel-value variance)
  over the *full-resolution* decode, and real photos from phones are often
  ~12MP (4032x3024). Neither signal actually needs full resolution — both
  are about broad tonal/edge structure, not fine per-pixel detail. Fixed
  by downscaling once (`ScanSessionService.MetricsMaxDimension = 400`,
  same `ResizeMode.Max` + bicubic pattern already used by
  Thumbnails/ThumbnailGenerator) and sharing that single downscaled image
  between BlurDetector and LowDetailDetector; DHasher is unaffected and
  still runs on the full-resolution decode (it already downsamples to 9x8
  internally regardless of input size, so there was no cost to save
  there). The resize is skipped entirely when the source is already
  ≤400px on its longest side. Added a `downscale=` step to the
  `[ScanPerf]` output so the next real scan shows this cost/savings
  directly. 7 new Core tests (DownscaledMetricsConsistencyTests) using
  synthetic high-resolution (2400x1600) images confirm: relative blur
  ordering (sharp > medium > uniform) survives the downscale, and
  LowDetail's boolean classification (solid color / noisy-near-uniform /
  high-contrast-quadrants / full-range-gradient) is unchanged before vs.
  after downscaling. Confirmed via grep that BlurScore has no absolute-
  threshold consumer anywhere in the app (QualityReviewOrder only sorts it
  relatively) and LowDetail's only consumer (SuggestionEngine) reads the
  boolean, not the raw variance — so `LowDetailDetector.
  DefaultVarianceThreshold` (50.0) did not need retuning against the
  synthetic evidence gathered. **Caveat: this repo has no real photos to
  validate against** — the synthetic-image tests are a reasonable proxy
  (mirroring the same resize call ScanSessionService uses) but the actual
  before/after BlurScore-ordering and LowDetail flags on Alan's real photo
  library are still unverified; worth spot-checking a few known-blurry and
  known-sharp real photos after this lands.
- Investigated a `System.IO.IOException` reported in the Visual Studio
  Output window immediately after a scan's `[ScanPerf]` line printed.
  Concluded this is very likely a benign first-chance-exception
  notification (Visual Studio's debugger reports every thrown exception in
  Output, including ones caught immediately, not just unhandled ones —
  same behavior called out for DbInitializer's PRAGMA-based column checks
  under Conventions above) rather than a real bug: both
  Data.Services.ThumbnailCache (which starts requesting thumbnails as soon
  as ScanCompleted fires — the timing matches "right after the scan
  completed") and ScanSessionService.ScanFiles' own per-file `catch { }`
  already handle IOException gracefully (transiently-locked files,
  concurrent thumbnail cache writes — see ThumbnailCache's existing
  comments). Not fixed further since no crash/unhandled-exception dialog
  was reported — only a first-chance notification. **Needs Alan to
  confirm**: no unhandled-exception dialog appeared (that would mean a
  real bug, not benign noise), and if the notification is too noisy while
  debugging, Visual Studio's Debug > Windows > Exception Settings can
  uncheck IOException without changing app behavior.
- Parallelized the per-file scan pipeline. ScanSessionService.ScanFiles was
  confirmed fully sequential (a single `foreach`); per-file work (decode,
  downscale, SHA256, EXIF, DHash, blur, low-detail) is now run via
  `Parallel.ForEach` capped at `MaxScanParallelism = Math.Min(Environment.
  ProcessorCount, 8)` — capped rather than left unbounded since this is
  also real disk I/O (every cache-miss file is opened/read at least twice:
  SHA256 + image decode) and each concurrent file holds its own ImageSharp
  decode buffer in memory; 8 is a reasonable ceiling regardless of core
  count on typical consumer hardware. SQLite writes are not safely
  concurrent (Microsoft.Data.Sqlite connections can't be shared across
  threads), so Upserts stay single-threaded via a producer/consumer
  pattern: parallel workers push completed FileRecords onto a
  `BlockingCollection<FileRecord>` (bounded at 1000, giving natural
  backpressure), and one dedicated writer — a real `Thread`, deliberately
  not `Task.Run`/ThreadPool, so it can never be starved behind the
  ThreadPool threads Parallel.ForEach is using — drains it and performs
  the existing batched-transaction Upserts (unchanged 500-file batch size)
  sequentially. NeedsRescan/GetByPath (reads) run per-worker on their own
  short-lived connections rather than through the writer's shared
  connection (which a second thread touching would violate
  SqliteConnection's no-cross-thread-use rule); Microsoft.Data.Sqlite
  applies an automatic busy-timeout, so a read racing the writer's
  periodic commit just waits briefly instead of throwing. The method joins
  the writer thread (and rethrows any write failure, wrapped, so it still
  surfaces the same way an unexpected DB error did before) before
  returning, so callers never see a FileRecord with an unset Id or read a
  results set the cache hasn't caught up to yet. `results` collects via a
  `ConcurrentBag<FileRecord>` (order doesn't matter — nothing downstream
  depends on scan order) converted to a List at the end.
  `[ScanPerf]`'s `total=` field is renamed `wallClock=` (what the user
  actually experiences) and a new `aggregateCpuTime=` field reports the
  sum of every per-step timer across all files/threads — explicitly
  documented in the log line itself that this can now exceed `wallClock`
  once work overlaps across threads, so the numbers don't read as a bug.
  All per-step Stopwatch usage switched from single Stopwatch
  instances (not thread-safe to share) to `Interlocked.Add`-accumulated
  `long` tick counters, one local Stopwatch per file per step.
  **Testing constraint**: ScanSessionService is App-layer/WinUI with no
  test project (same constraint noted throughout this file — the App
  can't be built or tested via CLI), so "parallel scan produces identical
  results to sequential" can't be verified by directly testing ScanFiles.
  Instead: (1) the per-file pure computations (hash/decode/blur/
  low-detail) were already proven deterministic/side-effect-free by
  existing Core tests, so which thread runs them doesn't affect their
  output; (2) added a new Data test,
  FileCacheRepositoryTests.ConcurrentReaders_WhileSingleWriterBatchesUpserts_ProducesCorrectFinalState,
  that reproduces the exact concurrency shape ScanFiles now uses — 4
  reader threads hammering NeedsRescan/GetByPath (each on its own
  connection) while one writer thread batches Upserts through a shared
  connection/transaction — and confirms the final DB state exactly
  matches what a purely sequential write of the same records would
  produce, with no exceptions from lock contention. This is the part of
  the new concurrency that was actually at risk (SQLite access patterns);
  the orchestration around it (Parallel.ForEach, BlockingCollection, the
  writer Thread) is standard library-provided concurrency infrastructure,
  not custom logic, so it wasn't treated as needing its own bespoke test.
- Organization per-file/per-node selective execution, replacing v1's
  all-or-nothing move. Added Core.Organization.OrganizationSelectionNode —
  a mutable, WinUI-free tree mirroring OrganizationTreeNode 1:1 that
  implements standard cascading-checkbox semantics without any UI
  dependency, specifically so the cascade logic is unit-testable
  independently of the TreeView control (10 new Core tests,
  OrganizationSelectionNodeTests, covering default-all-selected,
  cascade-down on Year/Month/Category/File, re-selection, and the
  "deselecting one file doesn't uncheck its parent — parent goes
  Indeterminate instead" guarantee at every level). Deliberately only
  File-kind nodes store a real selection flag (`_isSelected`); every group
  node (Year/Month/Category) derives `IsSelected`/`IsIndeterminate` live
  from its children on every read rather than storing and syncing its own
  copy — this is what makes "cascade down" (SetSelected recurses into
  Children) and "recompute up" (ancestors just re-derive next time
  they're read) trivially consistent with zero explicit
  parent-notification bookkeeping inside OrganizationSelectionNode itself.
  `CheckBoxState` (`bool?`) is null exactly when `IsIndeterminate`, for
  direct ThreeState-CheckBox binding.
  - Data.Services.OrganizationExecutor.Execute gained a third parameter,
    `IReadOnlySet<string>? selectedSourcePaths = null` — when provided,
    `ComputePlannedMoves` skips any PlannedFile whose SourcePath isn't in
    the set before the move log is even written, so **the move log only
    ever contains what was actually going to be attempted**, not the full
    original plan. Omitting it (existing callers, existing tests) preserves
    the original all-or-nothing behavior exactly — additive, not a
    breaking change. 4 new Data tests cover: only-selected-files move,
    move log contains only selected entries, an empty selection set moves
    nothing but still writes a (empty) log, and `null` still moves
    everything.
  - App layer: OrganizationNodeViewModel now wraps an
    OrganizationSelectionNode plus a `Parent` back-reference (set once at
    construction, mirroring the tree's shape) and exposes `bool? IsChecked`
    (a pure read of `CheckBoxState`) and `SetSelected(bool)`. Calling
    SetSelected re-raises PropertyChanged(IsChecked) down through every
    descendant (RefreshCheckedStateRecursively) and up through every
    ancestor (NotifyAncestorsCheckedStateChanged via the Parent chain), so
    every visible CheckBox in the tree — not just the one clicked —
    reflects the change immediately, and invokes an `onSelectionChanged`
    callback (threaded down to every node at construction, from
    OrganizationViewModel) so the ViewModel's SelectedFileCount/
    CanExecutePlan stay in sync without polling. OrganizationViewModel
    builds one `OrganizationSelectionNode` tree per rebuild alongside the
    existing OrganizationTreeNode tree (fresh plan → fresh selection,
    defaulting to fully-selected, preserving "organize everything" for
    anyone who never touches a checkbox) and exposes `PlannedFileCount`
    (total) / `SelectedFileCount` (checked) / `GetSelectedSourcePaths()`
    (case-insensitive HashSet, matching the existing path-comparison
    convention elsewhere in this ViewModel). `CanExecutePlan` now gates on
    `SelectedFileCount > 0` instead of the total plan count, so the
    Execute button disables itself if everything is deselected.
    ExecutePlanAsync passes `GetSelectedSourcePaths()` through to
    `OrganizationExecutor.Execute`.
  - OrganizationPage.xaml: added a `CheckBox` (`IsThreeState="True"`) as
    the first column of every tree row (Year/Month/Category/File alike —
    the same DataTemplate renders all kinds), bound `IsChecked` OneWay to
    the ViewModel (never TwoWay) with an explicit `Click` handler
    (`OnNodeCheckBoxClick`) instead. This is a deliberate WinUI-specific
    workaround: `IsThreeState="True"` is required for the control to be
    *capable* of rendering Indeterminate at all, but it also makes a raw
    user click cycle through all three states by default
    (unchecked→checked→indeterminate→unchecked), and Indeterminate must
    stay a derived, read-only display state that a user can never click
    their way into directly. The Click handler ignores whatever the
    control's own internal three-state cycle just produced and instead
    reads the ViewModel's last-known `IsChecked` (untouched by that
    internal cycle, since the binding is OneWay) to decide
    deterministically — anything not fully checked becomes fully checked;
    fully checked becomes fully unchecked — then `SetSelected` immediately
    re-notifies `IsChecked`, snapping the box's displayed value back to
    the correct one regardless of what the click cycled it to. The
    confirmation dialog (OnExecuteClick) now reads
    `ViewModel.SelectedFileCount`/`PlannedFileCount` and shows
    "N of M file(s)" when they differ, or just "M file(s)" when everything
    is selected (unchanged wording from before this session).
  - **Testing constraint, same as noted throughout this file**: WinUI
    layer (OrganizationNodeViewModel's Parent-chain notification,
    OrganizationPage's Click-handler workaround) has no test project and
    can't be verified via CLI — only the pure Core cascade logic
    (OrganizationSelectionNode) and the Data-layer executor filtering
    could be unit-tested directly; the WinUI wiring needs the manual
    verification checklist below.
- Automated undo for Organization moves, reading back the move log
  OrganizationExecutor already wrote. Added
  Data.Services.OrganizationUndoService (same layer as OrganizationExecutor
  — plain File.Move, no DB) — a stateless static class (unlike
  OrganizationExecutor's constructor-configured logDirectory, Undo always
  operates on a specific log path the caller already has, and ListMoveLogs
  takes its directory as a parameter, so there's nothing to inject).
  - `Undo(moveLogPath)` reads the log and, per entry, validates before
    touching anything rather than assuming a clean reversible state:
    checks whether the destination file still exists and whether the
    original source location already has something at it, and only then
    decides what to do. Five outcomes per entry (OrganizationUndoOutcome):
    **Reversed** (normal case — destination exists, source empty, moved
    back successfully), **AlreadyReversed** (destination gone AND the file
    is already sitting at the source — a prior undo run, full or partial,
    already handled this entry), **SkippedDestMissing** (destination gone
    AND source also empty — nothing safe to do, file may have been moved/
    deleted by something else since), **SkippedSourceOccupied**
    (destination still has the file, but something else now occupies the
    original source — refuses to overwrite it), and **Failed** (the normal
    case's actual File.Move threw — permissions, in-use file, etc). This
    is what makes re-running Undo against an already-fully-or-partially-
    reversed log safe: AlreadyReversed is a distinct, expected, non-error
    outcome, not something that surfaces as a failure or attempts a
    redundant move. `OrganizationUndoResult` aggregates counts
    (Reversed/AlreadyReversed/Skipped/Failed) plus a `Summary` string for
    the same confirm/summary dialog pattern Duplicates/Quality/Organization
    execution already use.
  - `ListMoveLogs(logDirectory?)` enumerates `move-log_*.json` files in the
    given directory (defaulting to OrganizationExecutor's own default),
    parsing just enough of each (Timestamp, DestinationRoot, Moves.Count)
    for a picker UI to show "logged when, how many files, to where" per
    entry, newest-first. A corrupt/unreadable log file is skipped rather
    than failing the whole listing — the same "one bad entry shouldn't
    abort the batch" philosophy used throughout this codebase
    (CommitService, OrganizationExecutor's own per-file try/catch).
  - 8 new Data tests: full successful reversal, destination-missing skip,
    source-occupied skip (confirms the occupying file is untouched and the
    original moved file is NOT lost — still sitting at the destination),
    idempotent re-run after a full reversal (AlreadyReversed, not an
    error), re-run after a manually-simulated *partial* reversal (only the
    still-outstanding entry gets reversed, the already-done one is
    recognized and skipped), ListMoveLogs newest-first with correct file
    counts, empty-directory listing, and a corrupt log file being skipped
    rather than throwing.
  - App layer: OrganizationViewModel gained `GetAvailableMoveLogsAsync()`
    and `UndoMoveLogAsync(moveLogPath)` (mirrors ExecutePlanAsync's
    IsIdle/StatusText/RefreshAsync-after pattern — undoing a move changes
    disk state just as much as making one, so the same
    ScanSessionService.RefreshAsync() call afterward applies). OrganizationPage
    gained an "Undo a Previous Move…" button opening a ContentDialog with a
    single-selection ListView of past logs (a lightweight custom picker,
    not a general file-open dialog, since the logs live in a fixed
    app-owned directory the user doesn't browse to), then the same
    confirm-before/summary-after ContentDialog pattern as Organize Files —
    the confirmation names the exact file count and explicitly calls out
    that mismatched entries will be skipped, not overwritten.
- Three bugs found in manual testing of the selective-execution/undo work
  above, all fixed:
  - **TreeView checkbox indentation regression**: adding the per-node
    CheckBox pushed every row noticeably right of where it sat before
    checkboxes existed. Root cause: WinUI's default CheckBox style
    reserves a much larger touch-target box around the glyph than the
    visible checkbox itself (sized for settings-page-style rows with a
    text label, not a dense TreeView row with no CheckBox content) — the
    TreeView's own per-level indent math never actually changed. Fixed by
    setting `MinWidth="0" MinHeight="0" Padding="0"` on the CheckBox in
    OrganizationPage.xaml, shrinking it to just its glyph footprint. Not
    independently testable (WinUI layout/rendering) — needs the manual
    checklist below.
  - **Undo picker showed the wrong time** (a ~1am move displaying as
    ~8:41am — a UTC-vs-Pacific-local offset). MoveLog.Timestamp is (and
    remains) stored as `DateTime.UtcNow` — correct practice for a durable
    record — but OrganizationPage's undo-picker ListView and confirmation
    dialog were formatting it directly instead of converting first. Fixed
    by calling `.ToLocalTime()` at the two display sites in
    OrganizationPage.xaml.cs only — the stored log, OrganizationViewModel,
    and OrganizationUndoService are all untouched, still entirely UTC.
    Added a Data-layer regression test
    (`ListMoveLogs_TimestampRoundTripsWithUtcKind_...`) confirming
    System.Text.Json's serialize/deserialize round-trip preserves
    `DateTimeKind.Utc` on `MoveLog.Timestamp` — that Kind is what makes
    `.ToLocalTime()` correct rather than a silent no-op (Unspecified Kind
    would make ToLocalTime() do nothing), so this guards the invariant the
    actual (WinUI, untestable) display fix depends on.
  - **Undo left empty Year/Month/Category folders behind**. Added
    `OrganizationUndoService.CleanupEmptyDestinationFolders` — after a
    file is successfully moved back (Reversed), and also when an entry
    turns out to already be reversed (AlreadyReversed, since an
    interrupted earlier undo run may have restored the file without ever
    cleaning up its now-empty folder), walks up from the destination
    file's containing folder deleting each one that's now empty, stopping
    the instant a folder isn't empty (a partial undo leaving sibling files
    behind must not have their shared folder removed) or as soon as the
    log's own `DestinationRoot` would be next (the user-chosen root is
    never deleted, empty or not). Best-effort: a folder that can't be
    deleted (permission, transient lock) is left in place rather than
    failing the undo over a cosmetic cleanup step. 2 new tests: full
    reversal removes the empty Year/Month/Category chain but leaves the
    destination root standing, and a partial reversal (one file
    deliberately left un-reversed via a simulated SkippedSourceOccupied)
    retains the shared Category folder since it's not actually empty.
- Settings page — replaces the NavigationView gear placeholder with a real
  page. `MainWindow.xaml`'s NavigationView now has `IsSettingsVisible="True"`;
  `MainWindow.xaml.cs`'s `OnNavSelectionChanged` checks
  `args.IsSettingsSelected` first and navigates to the new SettingsPage
  before falling through to the existing Tag-based switch for the other
  three items (that switch and its default case are otherwise unchanged).
  - Data.Models.AppSettings (`Theme` — enum `AppTheme { System, Light,
    Dark }`, defaults to System) + Data.Services.SettingsService
    (load/save JSON at `%LOCALAPPDATA%\ImageCleanup\settings.json`, same
    layer/style as OrganizationExecutor/ThumbnailCache — constructor takes
    an optional directory override for testability, defaults to the real
    LocalApplicationData path otherwise). A missing or corrupt settings
    file both just fall back to `new AppSettings()` (System theme) rather
    than throwing — same "don't fail the app over a non-critical read"
    philosophy as OrganizationUndoService's corrupt-log skip. 3 new Data
    tests (SettingsServiceTests): no-file-yet defaults, save-then-load
    round-trip, corrupt-file fallback.
  - App.xaml.cs registers `SettingsService` alongside `ScanSessionService`
    in the DI container, loads it once in `OnLaunched` (before
    `_window.Activate()`, so the saved theme is applied ahead of first
    paint — no flash of the wrong theme), and exposes a static
    `App.ApplyTheme(AppTheme)` helper that sets `RequestedTheme` on the
    shell window's root `FrameworkElement` (`ElementTheme.Light/Dark`, or
    `ElementTheme.Default` for System to defer back to the OS). WinUI
    re-themes that element and every descendant live — no restart
    required, confirmed this is the standard mechanism (not something
    that needs its own test; it's a single framework property set).
  - App layer: SettingsViewModel wraps SettingsService — `Theme` setter
    saves to disk and calls `App.ApplyTheme` immediately on every change;
    `ThemeIndex` is an int mirror for `RadioButtons.SelectedIndex`
    binding, same reasoning as `FileActionViewModel.SelectedActionIndex`
    (avoids any by-value re-match against an ItemsSource). `ClearCache()`
    deletes `cache.db` directly (best-effort, catches and reports via
    `StatusText` rather than throwing); `ClearMoveHistory()` deletes every
    `move-log_*.json` under the move-logs directory; `CountMoveLogs()`
    backs the confirmation dialog's "N move log(s)" wording. SettingsPage
    (RadioButtons for the three theme options, two buttons) mirrors the
    existing confirm-before/act-after ContentDialog pattern used
    throughout — Clear Move History's dialog is worded distinctly
    stronger ("NOT recoverable") than Clear Cache's routine confirm, per
    the difference in what each action actually costs the user.
  - Not done in this pass, out of scope for what was asked: no
    last-scanned-folder persistence (AppSettings only carries Theme so
    far — see "No settings/preferences persistence" below, still open for
    anything beyond theme).
  - **Bug fix, same session: Light theme rendered greyish/broken (Dark
    was fine).** Root cause: `MainWindow.xaml`'s root `Grid` had no
    explicit `Background` at all. **Gotcha worth remembering for any
    future WinUI window/dialog added to this app: an unpackaged WinUI 3
    `Window` has no default page background of its own** (no Mica/
    backdrop, no implicit fill) — without one, the root renders as
    whatever the composition swapchain's clear surface is, which
    happened to look passable under Dark (close to black-on-black) but
    showed up as a broken grey smear under Light, since nothing was
    actually painting a light surface. Fixed by setting
    `Background="{ThemeResource ApplicationPageBackgroundThemeBrush}"`
    on that root Grid — the standard WinUI token for "the app's base
    background," which flips correctly with `RequestedTheme`. Same pass
    also: replaced two hardcoded `Foreground="White"` badges
    (GroupDetailDialog's "★ Keep" badge, OrganizationPage's "renamed"
    badge) with `{ThemeResource TextOnAccentFillColorPrimaryBrush}` (the
    correct token for text on an accent-filled surface — confirmed via
    grep this was the only remaining hardcoded color anywhere in the App
    project); and added a couple of distinct theme-aware surface levels
    instead of everything sitting on one flat background — the shared
    folder-selection toolbar and the Duplicates/Quality staging panels
    now use `LayerFillColorDefaultBrush`, and GroupDetailDialog's
    per-file cards use `CardBackgroundFillColorDefaultBrush`. Confirmed
    `App.ApplyTheme` is the only place `RequestedTheme` is set anywhere
    in the app (grepped), applied to the actual visual-tree root
    (`ShellWindow.Content`), so it correctly cascades to every page and
    every `ContentDialog` sharing that `XamlRoot` — nothing overrides it
    independently. NavigationView's `SymbolIcon`s were confirmed (not
    changed) to already inherit theme-aware foreground from WinUI's
    default styling, with no local override anywhere. XAML/resource-only
    — no ViewModel or logic changes, no new tests (nothing here is
    Core/Data-testable; see the manual verification checklist below).
- **Localization infrastructure** — the mechanism only; full English/
  Chinese wording is a follow-up pass (see Known gaps below). Three
  language modes: Dev (today's exact wording, the default — anyone who
  never touches this setting sees unchanged behavior), English (a
  plain-language rewrite, not yet written), Chinese (a translation, not
  yet written).
  - **Where strings live**: `src/ImageCleanup.App/Strings/{dev,en,zh}.json`
    — one flat `"Key.Path": "value"` JSON object per language, bundled as
    app Content (`ImageCleanup.App.csproj` has a `<Content Include=
    "Strings\*.json">` item with `CopyToOutputDirectory=PreserveNewest`),
    not user data — deliberately not under `%LOCALAPPDATA%` the way
    `settings.json` is, since these are app-authored translations, not
    something a user edits. `dev.json` has 59 keys, extracted verbatim
    from every static XAML string and Page-code-behind dialog string that
    existed before this pass (button/header/placeholder text, dialog
    Title/Content/button text — Content templates use `{0}`/`{1}`
    positional placeholders for `string.Format`). `en.json`/`zh.json`
    ship as empty `{}` placeholders — JSON has no comment syntax, so
    "TODO, not yet translated" is recorded here in CLAUDE.md rather than
    in the files themselves.
  - **Key naming convention**: `Area.Thing` or `Area.DialogName.Part`,
    dot-separated, PascalCase segments — e.g. `Duplicates.ViewGroupButton`,
    `Organization.OrganizeConfirmDialog.Message`. Strings identical across
    features (e.g. "Remove", "Destination path…", the commit-confirm
    dialog trio shared by Duplicates/Quality) live under `Common.*`
    instead of being duplicated per feature — deliberate, since Duplicates
    and Quality's commit dialogs are behaviorally and textually identical
    today; a feature-specific string that later needs to diverge just
    gets its own key at that point, nothing here prevents that.
  - **Data.Services.LocalizationService** (same layer/style as
    SettingsService — constructor takes an optional directory override
    for testability, defaults to `AppContext.BaseDirectory/Strings` for
    real use). `SetLanguage(AppLanguage)` loads both the target language's
    dictionary AND Dev's (always, for fallback). `GetString(key)` returns
    the active language's value; if missing/empty, falls back to Dev's
    value for that key; if even Dev doesn't have it, returns the raw key
    as an absolute last resort (should never trigger once Dev is fully
    populated — this only guards a typo'd key, not the expected
    English/Chinese-not-translated-yet case, which is the Dev-fallback
    path). `GetString(key, params object[] args)` wraps `string.Format`
    for templated entries. A static `LocalizationService.Current`
    property holds the DI-registered singleton — set once in
    `App.xaml.cs.OnLaunched`, before any page is constructed — so XAML
    markup extensions (built by the XAML parser with no DI access) and
    static-context callers can reach it. Grepped to confirm the dictionary
    files and code stay in sync: every `Key=`/`GetString("...")` reference
    across the App project has a matching `dev.json` entry and vice versa
    (59/59) — worth re-running that check (`grep` both sides, `comm -3`)
    after adding any new string, since a typo'd key silently falls all
    the way through to "show the raw key" rather than erroring at
    compile time.
  - **App.Localization.LocExtension** — a WinUI custom `MarkupExtension`
    (`{loc:Loc Key=Some.Key}`) used for static Page XAML text. Chosen over
    x:Bind-to-a-ViewModel-property-per-string specifically to keep Page
    XAML readable — a markup extension is one attribute per string, not a
    new `LocalizedFoo` property added to every ViewModel for every label.
    Resolves via `LocalizationService.Current.GetString` **once, at
    XAML-parse time** — this is the mechanism's one real limitation, see
    below.
  - **Live-apply vs. restart-required — genuinely different from theme,
    not an oversight**: `{loc:Loc}` only evaluates once, when an element
    is constructed, and every Page uses `NavigationCacheMode.Enabled`
    (constructed once, reused for the app's lifetime) — so changing the
    language in Settings does **not** update already-rendered Page text
    without an app restart. Theme doesn't have this limitation because
    `ElementTheme`/`RequestedTheme` is a live-cascading WinUI mechanism
    with no once-only evaluation; there's no text equivalent short of
    rebuilding every bound string's own change-notification, which is
    exactly the "wall of binding boilerplate" this design deliberately
    avoided. **What DOES update immediately, no restart**: every
    `ContentDialog` in the app is constructed fresh in code-behind on
    each show (not cached like Pages) and calls
    `LocalizationService.Current.GetString(...)` directly in that
    code-behind method — so a language change is visible in the very next
    dialog shown, even though the page underneath it still shows the old
    language. `SettingsViewModel.Language`'s setter calls
    `LocalizationService.Current.SetLanguage(...)` immediately for
    exactly this reason. `Settings.LanguageHint`'s wording reflects this
    precisely ("Applies to new dialogs immediately; restart the app for
    it to apply everywhere else.") rather than overstating what actually
    happens.
  - **SettingsPage**: added a Language section (RadioButtons: Dev/
    English/Chinese) mirroring the Theme section's pattern exactly —
    `SettingsViewModel.Language`/`LanguageIndex` mirror `Theme`/
    `ThemeIndex`. Bug fixed in the same change: `Theme`'s setter
    previously called `_settingsService.Save(new AppSettings { Theme =
    _theme })` — constructing a **fresh** `AppSettings` on every save,
    which would have silently reset `Language` back to its default the
    next time `Theme` changed (and vice versa) once there were two
    fields. `SettingsViewModel` now holds one `_settings` instance loaded
    once at construction; both setters mutate that same instance and save
    it whole.
  - **Organization folder naming (Year/Month/Category) also respects the
    language setting** — these are real on-disk folder names, not just UI
    text (`OrganizationExecutor` writes files to
    `<dest>/<Year>/<Month>/<Category>/...` using exactly the string
    `OrganizationPlanner.BuildHierarchy` computed). `OrganizationPlanner.
    BuildHierarchy` gained an optional `Func<MetadataCategory, string>?
    categoryFolderName` parameter (same additive-optional-parameter
    pattern as `OrganizationExecutor.Execute`'s `selectedSourcePaths`) —
    omitted, it defaults to `category.ToString()`, the exact original
    behavior, so every existing caller/test is unaffected.
    `CategoryGroup.Label` changed from a computed `Category.ToString()`
    property to a settable `init` property populated from the resolved
    name, since a resolver can now return something other than the enum's
    own name. `OrganizationViewModel.RebuildAsync` supplies
    `ResolveCategoryFolderName`, which reads `Organization.FolderName.
    Photo`/`Organization.FolderName.NoMetadata` through
    `LocalizationService.Current` — under Dev language (the default)
    this resolves to literally `"Photo"`/`"NoMetadata"`, so real on-disk
    folder names are provably unchanged from before this pass.
  - **Flagged, not solved — Windows folder-name safety for the actual
    wording pass**: CJK characters themselves are perfectly valid in
    Windows/NTFS folder names (this is not a blocker for Chinese
    category names). The real constraint for whoever writes the
    Chinese/English category-name wording next: avoid the characters
    Windows forbids in any path segment (`< > : " / \ | ? *`), avoid a
    value that's only whitespace or trailing dots/spaces (Windows trims
    these and can produce a different or invalid name than intended), and
    avoid exactly matching a reserved device name (`CON`, `PRN`, `AUX`,
    `NUL`, `COM1`-`COM9`, `LPT1`-`LPT9`) case-insensitively — vanishingly
    unlikely for real Chinese/English category words, but cheap to check
    once actual wording is chosen. This wasn't guessed at or silently
    worked around; flagging it here is the deliverable for this part of
    the task.
  - **Deliberately out of scope for this infrastructure pass** (flagged,
    not silently skipped):
    - **ViewModel-internal computed status strings** (e.g.
      `ScanSessionService.StatusText`, `DuplicatesViewModel`/
      `QualityViewModel`/`OrganizationViewModel`/`SettingsViewModel`'s
      various `"Done — {0} file(s)..."`-style messages) were NOT routed
      through LocalizationService. Item 2 of the task that built this
      asked for "every current hardcoded XAML/code-behind string" —
      read literally, ViewModels are neither; they're business logic
      files, and these particular strings are woven through
      success/failure branches with live-computed values rather than
      being simple static literals. Touching them risked logic
      regressions for a pass explicitly scoped as infrastructure-only.
      Follow-up work.
    - **`FileActionViewModel.AvailableActions`** ("None"/"Keep"/
      "Delete"/"Move") were deliberately NOT localized, and this is a
      real landmine worth understanding before anyone tries: these exact
      strings are compared by value elsewhere as business logic (e.g.
      `DuplicatesViewModel`/`QualityViewModel`'s `OnFileActionChanged`
      checks `fa.SelectedAction is "Delete" or "Move"`, `KeepSelector`
      compares against `"Keep"`), and `SelectedAction` is the literal
      string bound `TwoWay` to the ComboBox. Translating the *display*
      text without first separating it from the *value* compared in
      logic would silently break Delete/Move/Keep detection the moment
      a non-Dev language was active. Fixing this properly means
      introducing a value/display-label split (e.g. the ComboBox binds
      to a translated display string while `SelectedAction` keeps
      comparing a stable, untranslated value) — real work, not a
      dictionary-entry addition, so it's flagged here rather than
      guessed at.
  - 8 new Data tests: `LocalizationServiceTests` (Dev value returned
    directly, English-key-present returns English, English-key-missing
    falls back to Dev, Chinese's empty dictionary falls back to Dev for
    every key, a key missing everywhere returns the raw key as last
    resort, format-args templating, a missing Strings directory doesn't
    throw, switching back to Dev after English returns Dev's value again)
    plus 3 new `SettingsServiceTests` (Language round-trips alone, and
    Theme+Language round-trip together — the latter is the regression
    test for the "fresh AppSettings on save" bug described above).
- **Action-string value/display split**, closing the gap flagged at the
  end of the localization-infrastructure session above (translating
  Delete/Keep/Move's display text would have silently broken the
  business logic that string-compared against those exact words).
  - Core.Grouping.ActionType — a new enum (`None`/`Keep`/`Delete`/`Move`)
    is now the one stable, language-independent value everything compares
    against. `KeepSelector.ResolveKeepConflicts` takes
    `IEnumerable<(string FilePath, ActionType Action)>` instead of
    `(string FilePath, string Action)` — the `KeepAction` string constant
    is gone, replaced by comparing directly against `ActionType.Keep`.
  - App.ViewModels.FileActionViewModel: `SelectedAction` (a string) is
    replaced by `SelectedActionType` (`ActionType`) as the property every
    other ViewModel now reads/sets — `DuplicatesViewModel.
    OnFileActionChanged`, `QualityViewModel.OnFileActionChanged`,
    `DuplicateGroupViewModel.Header`'s keep-file lookup, and both
    ViewModels' Move-target-flush-before-commit checks all compare
    `ActionType` values now, never a string. `AvailableActions` (bound to
    the ComboBox's `ItemsSource`) is now the LOCALIZED DISPLAY text for
    each action, in the same fixed order (`None`/`Keep`/`Delete`/`Move`)
    used by the new `SelectedActionIndex` — the two arrays are built from
    the same `ActionOrder` array so they can never drift out of sync.
  - App.ViewModels.ActionDisplay — the one shared place that maps
    `ActionType` to (a) `GetDisplayText`, the localized string shown to
    the user (`Common.Action.None/Keep/Delete/Move` in the string
    dictionaries — 4 new dev.json keys, added as part of this refactor,
    not the later wording pass, since the value/display split itself
    needed *some* Dev-language display text to exist), and (b)
    `ToStagingValue()`, an extension method that's deliberately just
    `ActionType.ToString()` — this is what still gets written to
    `OrganizationStaging`/`QualityStaging`'s `Action` column and switched
    on by `CommitService`, so the Data-layer persistence contract
    ("Delete"/"Move" strings in SQLite) didn't need to change at all;
    only the App-layer display path did. **Don't rename ActionType's
    members** without checking whether that breaks this — it's an
    implicit, ToString()-based contract, not a hardcoded mapping table.
  - `StagingEntryViewModel`'s constructor now takes `ActionType` instead
    of a string and stores `ActionDisplay.GetDisplayText(action)` as its
    `Action` property — this closes a second leak of raw
    "Delete"/"Move" text that was previously shown as-is in the staging
    review panel, not just the ComboBox.
  - `DuplicatesPage.xaml` and `GroupDetailDialog.xaml`'s action ComboBoxes
    switched from `SelectedItem="{x:Bind SelectedAction, Mode=TwoWay}"`
    to `SelectedIndex="{x:Bind SelectedActionIndex, Mode=TwoWay}"` — the
    same pattern QualityPage already used. Necessary because
    `AvailableActions` no longer holds a stable value SelectedItem could
    match by equality (it holds translated display text now), and this
    also incidentally fixes the container-recycling blank-ComboBox risk
    those two views were never patched for (see Quality's original fix,
    session history above) — SelectedIndex was already immune to that
    class of bug.
  - 9 new/updated Core tests: `KeepSelectorTests` converted from string
    tuples to `ActionType` tuples (same 6 cases, same assertions — proves
    the conflict-resolution behavior itself is unchanged, only its input
    type changed), plus a new `ActionTypeLocalizationResilienceTests`
    (`[Theory]`, 2 cases) that simulates the exact scenario this refactor
    protects against: the same Keep/Delete selections made under two
    completely different "display dictionaries" (English words vs.
    deliberately unrelated gibberish/emoji text) resolve to the identical
    conflict list either way, because `KeepSelector` only ever sees
    `ActionType` — the display dictionary is asserted to actually differ
    (so the test would have caught the old bug) but is never passed to
    the detection logic at all.
  - **Not App-layer-testable** (no test project exists for
    `ImageCleanup.App` — same constraint as everywhere else in this
    file): `FileActionViewModel`/`ActionDisplay`/the two ViewModels'
    `OnFileActionChanged` themselves. Verified instead by (1) a full
    `dotnet build` of the App project reaching its known MSB4062 wall
    (PRI packaging, Visual-Studio-only) with zero C#/XAML compile errors
    — proof every call site across `DuplicatesViewModel`/
    `QualityViewModel`/`DuplicateGroupViewModel`/both XAML files compiles
    against the new `ActionType`-based signatures — and (2) the Core-level
    resilience test above, which models the App layer's exact
    index-based selection pattern using only Core-testable types.
- **English (plain-language) wording** for all 63 dictionary keys (59
  from the original inventory + the 4 `Common.Action.*` keys added by
  the value/display split above) — `en.json` is now fully populated;
  `dev.json` was NOT touched (still today's exact technical wording,
  verified key-for-key identical to before this pass) and `zh.json` is
  still empty, pending a follow-up Chinese-translation prompt. Full
  before/after string list was reported back to Alan for review, not
  reproduced here — read `en.json` directly for the current wording, and
  git blame/history for what changed if that's ever needed later. Two
  choices flagged in that report for confirmation rather than assumed:
  - **`Organization.FolderName.NoMetadata`**: proposed "Other" (the
    English value now in `en.json`) as a plain alternative to the
    developer-facing "NoMetadata" — this is a REAL folder name written
    to disk by `OrganizationExecutor` under English mode, not just
    display text (see the Organization folder-naming hook from the
    localization-infrastructure session above), so this choice has
    functional consequences beyond wording and should be confirmed
    before anyone relies on it. "Unsorted" was considered and rejected
    as the proposal — these files ARE still organized by Year/Month, just
    lacking EXIF metadata, so "Unsorted" could misleadingly imply no
    organization happened at all.
  - **`Settings.Language.Dev`**: the radio-button label for switching
    *back* to Dev/technical wording — proposed "Technical (Default)"
    rather than literally "Dev", since "Dev" itself is developer jargon
    a non-technical user picking from this exact list wouldn't
    necessarily parse correctly.
  - General approach used throughout: "Commit"/"Commit Changes" →
    "Apply"/"Apply Changes"; BlurScore's tooltip reframed as photo
    sharpness rather than showing/naming the raw technical score;
    "Cache" avoided entirely in Settings' Clear Cache wording ("Clear
    What's Been Scanned"); confirmation/warning dialogs (Organize,
    Undo, Clear Move History) were rewritten in plain sentences but kept
    every piece of safety information the technical wording conveyed
    (not reversible via Recycle Bin, a record is kept, what gets
    skipped and why) — plain language, not watered down.
- **English wording review pass** (`en.json` only — `dev.json`'s
  existing values and `zh.json` untouched again, same rule as before).
  Six changes from Alan's review of the pass above:
  - `Organization.ChooseDestinationButton`: "Choose Where to Put
    Them…" → "Destination Folder…"
  - `Organization.UndoMoveButton` AND `Organization.NoMoveLogsDialog.Title`
    (both previously read "Undo an Earlier Organize", with/without the
    button's ellipsis): "Undo an Action…" / "Undo an Action"
  - `Organization.OrganizeFilesButton` (the button only — the confirm
    dialog's title and primary button were left as "Organize My Photos"
    / "Yes, Organize Them"): "Organize My Photos…" → "Organize…"
  - `Settings.ClearCacheButton` and `Settings.ClearCacheConfirmDialog.Title`:
    "Clear What's Been Scanned" → "Clear Previous Scans"
  - **The near-duplicate group label and the "staged changes" panel
    header were never actually in the dictionary** — both were
    ViewModel-computed strings (`DuplicateGroupViewModel.Header`,
    `DuplicatesViewModel`/`QualityViewModel.StagedCountText`) explicitly
    called out as deliberately out-of-scope in the original
    localization-infrastructure session ("ViewModel-internal computed
    status strings... item 2 said XAML/code-behind, not ViewModel
    logic"). Alan's review specifically asked for both to be fixed in
    English mode, so this pass brought them into the dictionary (5 new
    keys, added to **both** `dev.json` — verbatim current wording, e.g.
    `Duplicates.GroupKind.NearDuplicate` = "near-dup" — and `en.json`):
    `Common.StagedCountText` ("Review Staged Changes ({0})" dev → "Changes
    to Make ({0})" en — avoids "staged" per the request, without
    adopting one rigid replacement everywhere, per the request's own
    guidance), `Duplicates.GroupKind.Exact`/`.NearDuplicate` ("exact"/
    "near-dup" dev → "Exact match"/"Near duplicate" en — Exact was
    reworded too for consistency, not explicitly requested but left
    inconsistent otherwise), `Duplicates.NoneSelected` ("none selected",
    unchanged in English — already plain), and `Duplicates.GroupHeader`
    (the template combining all of the above: "{0} files ({1}) — Keep:
    {2}" dev → "{0} photos ({1}) — Keep: {2}" en).
  - `DuplicateGroupViewModel.Header`, `DuplicatesViewModel.
    StagedCountText`, and `QualityViewModel.StagedCountText` now read
    through `LocalizationService.Current.GetString` instead of a raw
    C# string interpolation — same pattern as everywhere else, no other
    behavior change. This precedent (a ViewModel status string getting
    pulled into the dictionary after all, once a concrete user-facing
    need showed up) is worth remembering if more of the still-deferred
    ViewModel status/summary strings need the same treatment later.
- **Layout bug fix**: QualityPage's per-row action ComboBox was missing
  `VerticalAlignment="Center"` (DuplicatesPage's equivalent ComboBox has
  always had it). Without it, the ComboBox defaults to Stretch and grows
  to match the row's 64px thumbnail height instead of centering
  alongside the single-line file path/BlurScore text — visually
  misaligned. Fixed by adding the same `VerticalAlignment="Center"`
  DuplicatesPage already uses. XAML-only, not CLI-verifiable — see the
  manual verification checklist below.
- **Chinese (Simplified) translation — localization is now complete
  across all three languages (Dev/English/Chinese).** `zh.json` filled
  in for all 68 keys (confirmed key-for-key identical to `en.json` and
  `dev.json` — no drift in either direction), translated from `en.json`'s
  plain-language English (not `dev.json`'s technical wording), in
  everyday Simplified Chinese vocabulary aimed at a non-technical older
  adult — standard UI conventions used where they exist (确定/取消 for
  OK/Cancel, matching how Windows itself labels those buttons) rather
  than literal/formal translations. `dev.json` and `en.json` were not
  touched.
  - **Folder-name safety, confirmed rather than assumed**:
    `Organization.FolderName.Photo` → "照片", `.NoMetadata` → "其他" —
    both are real on-disk folder names under Chinese mode (same
    `OrganizationViewModel.ResolveCategoryFolderName` hook noted in the
    localization-infrastructure session), and both were checked against
    Windows' forbidden-path-character set (`< > : " / \ | ? *`),
    trailing dots/spaces, and reserved device names — clean on all three;
    Simplified Chinese characters themselves are unrestricted in NTFS/
    Windows folder names. `Duplicates.GroupKind.Exact`/`.NearDuplicate`
    were checked too (per the same instruction) and confirmed **not** to
    feed any folder/filename logic — grepped for their only call site
    (`DuplicateGroupViewModel.Header`, pure display text) and confirmed
    `OrganizationViewModel.ResolveCategoryFolderName` only ever reads the
    two `Organization.FolderName.*` keys, nothing from `Duplicates.*` —
    so no folder-name-safety review was needed there, just translation.
  - **Template/placeholder grammar**: `Common.StagedCountText`
    ("Changes to Make ({0})") became "待处理的更改（{0}）" — kept the
    count in the same trailing-parenthetical position as English, which
    reads naturally in Chinese without needing to relocate {0} for
    grammar (unlike some other templates, e.g.
    `Organization.CountOfTotalFiles`, "{0} of {1} photo(s)", which DOES
    reorder — Chinese naturally phrases "M of N" as "N 张照片中的 M
    张", so `{1}` appears before `{0}` in the Chinese template; both
    positional arguments are still supplied in the original {0},{1}
    order by the calling code — only the template string's argument
    *order of reference* changed, which `string.Format` supports
    natively).
  - No strings were flagged as lacking a natural Chinese equivalent —
    every key had a plain, idiomatic translation available.
  - No code changes were needed for this pass (translation is pure
    content, same dictionary-loading/fallback mechanism built in the
    localization-infrastructure session already handles a third
    language with zero additional wiring) — `dotnet build`/`dotnet test`
    were run to confirm zero regressions, not because any regression was
    expected.
- **Two bugs found in Chinese-mode review, both fixed:**
  - **Incomplete localization coverage** — a full grep-style pass (every
    XAML `Content`/`Text`/`Header`/`Title`/`PlaceholderText`/button-text
    attribute, every code-behind dialog string, every ViewModel
    `StatusText` default/assignment) found 33 more strings never routed
    through `LocalizationService`, all in previously-deferred territory
    (see the localization-infrastructure session's explicit "ViewModel-
    internal computed status strings... out of scope" carve-out — this
    session closes that gap for good, since raw English leaking through
    in Chinese mode makes that carve-out no longer acceptable). All 33
    added to `dev.json`/`en.json`/`zh.json` (now **101 keys**, confirmed
    key-for-key identical across all three — verified by script, not by
    eye) and wired up:
    - `ScanSessionService.StatusText` (the shared toolbar's status
      line) — default "Ready" message, "Scanning…", the skipped-folders
      suffix, "no files found", "N files scanned", and the scan-failure
      message. The default-ready message is a template
      (`ScanSession.Ready` = "Ready — click \"{0}\" to start.") that
      substitutes `MainWindow.SelectFolderButton`'s own resolved text
      for `{0}`, rather than hardcoding the button's label a second
      time — so the two can never drift out of sync in any language.
    - `DuplicatesViewModel`/`QualityViewModel`'s empty-state and
      results-count `StatusText` messages, plus their shared
      Committing/Done/Failed status text (pulled into new `Common.*`
      keys, `Common.CommittingStatus`/`CommitDoneAll`/
      `CommitDonePartial`/`CommitFailedStatus`, since both features'
      commit flow produces identical wording — same reasoning as the
      earlier `Common.CommitConfirmDialog.*` sharing).
    - `OrganizationViewModel`'s full status lifecycle — initial
      placeholder, computing/moving/undoing progress text, the
      plan/move/undo success and failure messages (9 keys).
    - `SettingsViewModel`'s Clear Cache / Clear Move History
      success/failure `StatusText` (4 keys).
    - **The NavigationView's built-in Settings entry** (`IsSettingsVisible=
      "True"`) was still showing the literal English word "Settings" in
      every language, including Chinese — root cause: it's an implicit
      item WinUI constructs internally, not a normal
      `NavigationViewItem` declared in this app's XAML the way
      Duplicates/Quality/Organization are, so it was never touched by
      any `{loc:Loc}` binding. Fixed in `MainWindow.xaml.cs`: the item
      isn't guaranteed to exist right after `InitializeComponent()`
      (WinUI materializes it when the control applies its template,
      not necessarily synchronously in the constructor), so
      `Nav.SettingsItem` is set via a `Nav.Loaded` handler instead,
      casting to `NavigationViewItem` and setting `.Content` to
      `LocalizationService.Current.GetString("Nav.Settings")` — same
      once-at-construction-time resolution as every other `{loc:Loc}`
      binding, same restart-for-language-change caveat.
    - Flagged, not fixed (pre-existing, consistent with a scope
      boundary already established when the dialogs that embed these
      were first localized): `CommitResult.Summary`/
      `OrganizationExecutionResult.Summary`/`OrganizationUndoResult.
      Summary` — Data-layer-generated result strings substituted into
      already-localized dialog/status templates (e.g.
      `Organization.UndoDoneStatus` = "Undo complete — {0}") — remain
      English always, regardless of active language. This was already
      true before this session (the wording pass's
      `Organization.OrganizeCompleteDialog.Message` already embedded
      `result.Summary` un-localized) and wasn't introduced by anything
      here; noted as a real remaining gap for whoever picks up
      localizing the Data layer's result-summary generation next,
      not something this pass silently missed.
  - **NavigationView pane overlay bug** — expanding the pane covered
    the content area instead of pushing it aside. Root cause:
    `PaneDisplayMode="LeftCompact"` (chosen in the original NavigationView
    restructure, session history above) is inline only in its *collapsed*
    state — WinUI switches its *expanded* state to `CompactOverlay`
    (`SplitView.DisplayMode`) by design; this is documented,
    intentional behavior for `LeftCompact`/`LeftMinimal` (a transient,
    light-dismiss nav-rail flyout pattern), not a wiring mistake, so no
    single property tweak keeps `LeftCompact` while stopping the
    overlay. Investigated and rejected two alternatives before fixing:
    reaching into `NavigationView`'s control template to force the
    internal `SplitView.DisplayMode` directly (rejected — `NavigationView`
    reassigns that itself on every pane-state change as part of its own
    state machine, so a one-time external override would likely be
    silently reverted the next time the pane toggles); and
    `PaneDisplayMode="Auto"` (rejected — it only switches between the
    same `Left`/`LeftCompact`/`LeftMinimal` behaviors based on window
    width, so it doesn't add anything over picking a mode directly, and
    would make "collapsed by default" width-dependent instead of
    explicit). **Fix**: switched to `PaneDisplayMode="Left"` with
    `IsPaneOpen="False"` set explicitly — `Left` is the one mode that is
    inline (`Inline`/`CompactInline`) in *both* pane states, since it has
    no overlay code path in `NavigationView`'s template at all;
    `IsPaneOpen="False"` reproduces the same starts-collapsed appearance
    `LeftCompact` had. (`Left` was tried once earlier in this app's
    history and rejected for "clipping labels instead of hiding them" —
    that was before every `NavigationViewItem` had an `Icon` set, which
    this fix doesn't need to touch since icons were added in that same
    earlier session and are already in place.) XAML-only, not
    CLI-verifiable — the one point of residual uncertainty (exact
    rendered width of the `IsPaneOpen="False"` collapsed state under
    `Left`) needs Alan's visual confirmation, flagged explicitly in the
    manual verification checklist below rather than assumed correct.
- **Pane-animation background-flash fix**, found in review of the
  push-not-overlay fix above. Root cause confirmed: `NavigationView` and
  its child `Frame` both had no local `Background`, so each fell back to
  its own default template brush — not guaranteed to equal (and, per the
  visible symptom, evidently didn't equal) the root Grid's
  `ApplicationPageBackgroundThemeBrush`. This mismatch was invisible only
  by coincidence before the Light theme fix (session history above)
  replaced a uniform hardcoded black with real theme-aware brushes; once
  the brushes were real and distinct, the pane-width animation exposed
  the seam between them as a brief flash of the wrong-colored surface.
  **Fix**: `NavigationView.Background` and `Frame.Background` both set
  explicitly to `ApplicationPageBackgroundThemeBrush`, matching the root
  Grid — `Background` on the control fills the whole `NavigationView`
  rectangle in its default template (behind both the pane and content
  regions), so this one property is enough to cover any gap the
  animation transiently exposes between them.
  - **`NavigationView.PaneBackground` was tried first** (to target the
    pane's own fill specifically, rather than relying on the outer
    `Background` covering it) **and reverted — it reproducibly crashed
    the WinAppSDK 1.6.250205002 XAML compiler** (`XamlCompiler.exe`
    exits 1 with zero diagnostic output, no error message at all,
    confirmed by bisecting this file property-by-property against a
    clean `rm -rf obj` rebuild each time — `Background` alone compiles
    and reaches the project's normal MSB4062 wall cleanly every time;
    adding `PaneBackground` back in isolation reproduces the crash every
    time). Not usable in this SDK version for whatever underlying
    reason — don't reintroduce it without confirming a newer
    WindowsAppSDK actually accepts it first. This is also a concrete
    illustration of why the App project's "describe what should happen,
    defer visual verification" convention (Architecture section above)
    doesn't mean zero verification is possible from here — a `dotnet
    build` bisection caught and diagnosed a real compiler crash that a
    glance at the XAML alone would not have.
  - XAML-only, not CLI-verifiable for the actual animation smoothness/
    color-matching itself (only the fact that it now compiles was
    confirmed this way) — flagged in the manual verification checklist
    below.
- **NavigationView pane brightness in Dark theme** — the pane now reads
  as a slightly brighter, distinct surface from the main content area in
  Dark theme (previously identical). **Deliberately not done via
  `NavigationView.PaneBackground` set on the control** — that specific
  dependency-property path reproducibly crashes the WinAppSDK
  1.6.250205002 XAML compiler with zero diagnostics (documented in the
  pane-animation-seam fix above and confirmed again this session via the
  same property-by-property bisection). Instead, `App.xaml` now defines
  a `ResourceDictionary.ThemeDictionaries` block with a **Dark-only**
  entry that redefines `NavigationViewDefaultPaneBackground` — the named
  `ThemeResource` key `NavigationView`'s own default control template
  already pulls its pane fill from — aliased via `<StaticResource
  x:Key="NavigationViewDefaultPaneBackground" ResourceKey=
  "LayerFillColorDefaultBrush"/>` (the same "one step brighter than page
  background" surface already used elsewhere in this app — MainWindow's
  toolbar, Duplicates/Quality's staging panels — for the same elevation
  purpose). This is a fundamentally different code path from setting the
  property directly: a plain resource-dictionary entry with zero
  property-setter codegen involved, so it isn't subject to the
  `PaneBackground` crash — **confirmed by a clean `rm -rf obj` rebuild
  reaching the project's normal MSB4062 wall cleanly, twice in a row**,
  same verification discipline as the `PaneBackground` bisection itself.
  Light theme is untouched (no "Light" key defined), so it keeps
  NavigationView's normal default appearance. XAML-only, not
  CLI-verifiable for the actual visual result — flagged in the manual
  verification checklist below.
- **Single-photo view** for Quality and Organization — the simpler
  counterpart to Duplicates' "Compare Photos" (GroupDetailDialog) for
  features that only ever need to show one file bigger, not a multi-file
  comparison grid. New shared `Views/SinglePhotoDialog` (a `ContentDialog`)
  takes a plain `(string filePath, Func<byte[]?> generateBytes)` rather
  than binding to either caller's own row ViewModel type — Quality's rows
  are `FileActionViewModel`, Organization's File-kind tree nodes are
  `OrganizationNodeViewModel`, and neither should need to know about the
  other just to share this dialog. Uses the same
  `ThumbnailLoader.RequestThumbnail(Func<byte[]?>, ...)` delegate-injection
  pattern already used throughout this codebase (FileActionViewModel/
  OrganizationNodeViewModel/StagingEntryViewModel's own thumbnail
  loading), so it stays reusable by any future caller with nothing more
  than a path and a byte source. 320px sizing, matching
  GroupDetailDialog's `DetailThumbnail` convention exactly (same
  `ThumbnailCache` instance, same 320px cache key, kept separate from
  each row's own smaller default-size thumbnail).
  - `QualityViewModel`/`OrganizationViewModel` each gained a
    `GetDetailThumbnailProvider(filePath)` method (mirrors the existing
    `DetailThumbnailMaxDimension = 320` constant already established by
    `DuplicatesViewModel`) — a one-line wrapper handing back a delegate
    over their existing `_thumbnailCache`/`GetLastModified`, nothing
    novel.
  - `QualityPage.xaml`: a new "View Photo" button per row
    (`Common.ViewPhotoButton`), always visible (every Quality row is a
    real file). `OrganizationPage.xaml`: the same button added to the
    TreeView's per-node row, but its `Visibility` reuses the existing
    `ThumbnailVisibility` property (already "is this a File-kind node,
    not Year/Month/Category" — the exact check needed here too, so no
    new property was added just for this).
  - 2 new dictionary keys (`Common.ViewPhotoButton`, `SinglePhoto.Title`)
    added to all three language files — 103 keys now, confirmed
    key-for-key identical across `dev.json`/`en.json`/`zh.json`.
  - **Not automated-test-covered, disclosed rather than silently
    skipped**: this is pure App-layer WinUI dialog/button wiring plus
    two one-line ViewModel wrapper methods delegating to an
    already-thoroughly-tested `ThumbnailCache` — there was no natural
    Core/Data-testable unit of logic here to add a test for, consistent
    with every other WinUI-only feature in this file. The 103-key
    parity check was done the same way prior localization passes were
    verified (a script comparing key sets across all three JSON files),
    not as a persisted xUnit test, since Data.Tests has no reason to
    reference the App project's bundled `Strings/` content.
- **Localization — genuinely complete this time.** Two more leaks found
  in Chinese mode, same class of gap as the earlier "ViewModel-computed
  status strings were missed" sessions, both now closed:
  - **Organization's tree-node file-count text** (`OrganizationTreeNode.
    DisplayText`, e.g. "2024 (312 files)") lived in **Core**
    (`OrganizationTreeBuilder`), hardcoded English pluralization, with
    no way to reach `LocalizationService` (Core never references Data/
    App). Fixed with the same delegate-injection pattern already
    established for `OrganizationPlanner.BuildHierarchy`'s
    `categoryFolderName` parameter: `DisplayText` changed from a
    computed property to a settable one (mirroring `CategoryGroup.
    Label`'s earlier change for the same reason), and
    `OrganizationTreeBuilder.BuildTree` gained two optional parameters
    — `monthName` (`Func<int, string>`) and `formatGroupDisplayText`
    (`Func<string, int, string>`) — both defaulting to the exact
    original English behavior (`CultureInfo.CurrentCulture` month names,
    `"{label} ({count} file(s))"` pluralization) when omitted, so every
    existing test needed zero changes. `OrganizationViewModel` supplies
    localized versions of both, reading two new dictionary keys:
    `Organization.TreeNodeGroupLabel` (`"{0} ({1})"` composing the
    already-resolved label with `Organization.TotalFiles`' existing
    `"{0} file(s)"` wording — no new count-wording key needed, reused
    what already existed) and the month-name keys below.
  - **Month names were English everywhere** — the TreeView preview
    (`OrganizationTreeBuilder`'s Month node `Label`) AND, more
    seriously, **the real on-disk folder name** written by
    `OrganizationExecutor` (`OrganizationPlanner`'s hybrid `"01 -
    January"` format) — both used `CultureInfo.CurrentCulture` directly,
    same root cause as the file-count text above (Core has no
    LocalizationService access). Fixed the same way: added 12 new keys,
    `Common.Month.1` through `Common.Month.12`, to all three dictionaries
    (English month names for `dev`/`en`; `"1月"`–`"12月"` for `zh` — the
    numeral+月 form, not literal translated names like "一月", matching
    the established "everyday, non-technical vocabulary" convention this
    file's earlier Chinese-translation session already established).
    `OrganizationPlanner.BuildHierarchy` gained an optional `monthName`
    parameter (`Func<int, string>`, same default-preserves-old-behavior
    pattern as `categoryFolderName`), threaded into the existing
    `FormatMonthFolder` helper (now takes the resolver instead of calling
    `CultureInfo` itself) — this is the one with an actual functional,
    on-disk consequence, not just display text, same category as the
    Photo/NoMetadata folder-name localization from the original session.
    `OrganizationTreeBuilder.BuildTree`'s new `monthName` parameter
    (above) covers the TreeView preview side.
    `OrganizationViewModel.ResolveMonthName(int)` is the one new
    resolver method supplying both call sites — reads
    `Common.Month.{month}` through `LocalizationService`, same
    fallback-to-Dev behavior as every other key.
  - **Folder-name safety, confirmed rather than assumed** (same check
    applied to `Organization.FolderName.Photo`/`.NoMetadata` in the
    original Chinese-translation session): `"1月"`–`"12月"` contain none
    of Windows' forbidden path characters, no trailing dots/spaces, and
    aren't reserved device names — safe as real folder names.
  - **Chronological sort preserved regardless of language, confirmed by
    inspection, not just assumed**: the hybrid folder name's sort-safety
    comes entirely from the leading zero-padded `"NN - "` prefix
    (`FormatMonthFolder`'s `$"{month:D2} - {monthName(month)}"`) — the
    month-name suffix has never been what File Explorer sorts on, in
    English or any other language, so localizing that suffix has no
    bearing on sort order. This was true before this fix too (English
    month names don't sort alphabetically into chronological order any
    more than Chinese ones would — "April" sorts before "January"
    alphabetically) — the `"NN - "` prefix is doing 100% of the
    sort-correctness work, unchanged by this session.
  - **Full grep for remaining `CultureInfo`/hardcoded-English date or
    string formatting across the App project** — done, per the request,
    not skipped: the only remaining `CultureInfo` usages anywhere in
    `ImageCleanup.Core` (grepped directly) are the two default-fallback
    lambdas inside `OrganizationTreeBuilder.BuildTree` and
    `OrganizationPlanner.BuildHierarchy` themselves — these are
    *correct*, not leftover bugs: they're what a caller gets if they
    *don't* supply a `monthName` resolver (preserving every existing
    Core-level test's expected English output), and the App layer's
    `OrganizationViewModel` always supplies the localized resolver in
    practice, so this fallback path is never actually exercised when the
    app runs. No other `CultureInfo`, `GetMonthName`, or hardcoded
    English month-name literal exists anywhere in `ImageCleanup.App`.
  - 12 new keys × 3 languages = 36 new entries, plus
    `Organization.TreeNodeGroupLabel` × 3 = 39 total — confirmed
    key-for-key identical across `dev.json`/`en.json`/`zh.json` (116
    keys each, verified by script, same method as every prior parity
    check in this file).
  - **Not automated-test-covered for the localized-wiring itself**
    (`OrganizationViewModel.ResolveMonthName`/`FormatGroupDisplayText`) —
    same WinUI-layer constraint as every other `LocalizationService`
    consumer in the App project. What *is* tested: the Core-level
    delegate-injection mechanism itself (existing
    `OrganizationTreeBuilderTests`/`OrganizationPlannerTests` continue
    to pass unchanged against the new optional parameters, proving the
    default-fallback path is byte-for-byte identical to the old
    hardcoded behavior) — the same verification approach used for
    `categoryFolderName` originally.

### Known gaps / not yet started
**Current priority order for what's next** — only two items remain from
the original roadmap now that Duplicates/Quality/Organization/Settings
are all feature-complete, theme (Light/Dark, including pane elevation)
is fully working, localization (Dev/English/Chinese) has complete
coverage, and the single-photo view (Quality/Organization) closes out
this round of UI polish:
1. **Distribution/.exe packaging** — pivoted from unpackaged/self-contained
   (confirmed unreliable — reliable `0xC000027B`/`combase.dll E_FAIL`
   crash, see "Publishing — superseded" below for the full investigation)
   to **MSIX packaging** (see the current Publishing section above),
   which structurally avoids that crash class. Project is fully
   reconfigured (Package.appxmanifest, placeholder icons, per-architecture
   publish profiles) and the whole pipeline — build, sign, trust,
   install, launch — was verified working end-to-end this session with a
   throwaway test certificate. **Still needed**: Alan needs to run the
   real **Create App Packages** wizard in Visual Studio (generates his
   own, permanent signing certificate — the throwaway one used for
   verification was deleted) and do a real end-user-style install to
   confirm. Real branded icons (currently solid-color placeholders) and
   an actual installer/updater story are the remaining open items after
   that.
2. **Video duplicate/near-duplicate detection** — not started at all,
   and deliberately deferred until after 1 above. The app only scans
   image files today (see ScanSessionService's ImageExtensions list);
   no video sampling/hashing exists yet despite Core being scoped for it
   in the Architecture section below.

Other known gaps (not on the roadmap above, but still open):
- **General UI polish** — per-page accent coloring (currently uniform
  across Duplicates/Quality/Organization rather than each page having
  its own accent) and a sticky/always-visible "Select Folder" bar.
  Neither is part of the original three-pillar roadmap and both are
  lower priority than Distribution/Video above — revisit opportunistically,
  not on the critical path.
- **Organization**: even with selective move and automated undo both now
  in place, there is still no way to edit a conflict-resolved target
  filename before executing. Relatedly, there is still no staging table
  for Organization (OrganizationStagingRepository remains
  Duplicates-only) — Organization executes directly from the filtered
  plan after a confirm dialog rather than through a staging/review cycle
  the way Duplicates/Quality work; whether Organization ever needs
  staging is an open question, not a settled gap.
- **App still cannot build via CLI** — `dotnet build`/`dotnet run` fail
  with MSB4062 (PRI/MRT packaging task missing outside Visual Studio).
  Core/Data build and test fine via CLI; the App project requires Visual
  Studio F5. This has been true since early in the project and hasn't
  changed.
- **WinUI 3 vs. WPF**: raised early in the project as a possible migration
  given packaging friction, never revisited or decided. Core and Data have
  no UI framework dependency either way, so this is purely an App-layer
  question whenever it gets picked back up.
- **No traditional installer yet, but MSIX packaging (see Publishing
  above) now gets most of the way there for free** — an MSIX install IS
  a real Windows install: Start Menu entry, normal uninstall via Settings
  > Apps, no manual file copying. What's still missing is packaging that
  install experience up more smoothly for a non-technical end user (right
  now it's "trust a certificate, then run a PowerShell script," not a
  single double-click installer) and real branded icons instead of the
  current placeholders. A wrapping installer (WiX/Inno Setup) that
  handles the certificate-trust step automatically is the natural next
  step if the current PowerShell-script-based install (see "Installing
  ImageCleanup" above) proves too much friction for real end users.
- **Recursive scanning** is verified correct on nested folders (hidden/
  system/reparse-point skipping, graceful failure handling on
  inaccessible/missing directories — see Core.IO.ImageFileEnumerator) but
  not stress-tested at large scale (tens of thousands of files);
  performance at that scale is unverified.
- **No settings/preferences persistence beyond theme** — AppSettings/
  SettingsService now persist Theme (see Settings page above), but nothing
  else is remembered between launches (e.g. the last-scanned folder), so
  every session still starts from "Select Folder."
- Orphaned FileCacheRepository rows after an Organization move — no cache
  cleanup for moved files' old paths (harmless today; worth revisiting
  alongside the thumbnail-cache-eviction gap below if it bloats over time).
- Thumbnail cache eviction/cleanup — the disk cache under
  %LOCALAPPDATA%\ImageCleanup\thumbnails grows unbounded today.
- IsScreenshot / LowDetail signals aren't shown anywhere in the UI
  (BlurScore is, in Quality) — ScreenshotHeuristic itself is unused in
  favor of MetadataClassifier's HasExif-based approach (see Completed,
  session 12) but remains in Core in case it's useful elsewhere later.
- **NavigationView pane-animation seam — acknowledged, not worth fixing
  right now.** A faint gap/color flash during the pane collapse/expand
  animation is still minorly visible (most noticeable around the
  Duplicates/Quality staging panel area), even after the background-
  brush consistency fix (session history above) resolved the original,
  larger version of this same symptom. This residual is smaller and
  likely just animation-timing/compositing rather than a remaining color
  mismatch — the brushes involved (`NavigationView`/`Frame`/root Grid)
  are already confirmed consistent. Deliberately left alone: low-
  priority, purely cosmetic, and chasing WinUI animation-timing
  internals further risks leading back into the same kind of
  undiagnosable XamlCompiler/control-template territory that
  `PaneBackground` did (session history above) — not worth it for a
  barely-visible seam. Revisit only if it becomes more noticeable or
  someone has a concrete lead, not proactively.

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
- **Organization — per-file/per-node selective execution (new this
  session, and the highest-priority WinUI-specific check since none of
  the checkbox/click-cycle behavior is CLI-testable). Use a disposable
  test folder with several files across at least two months/categories,
  same caution as above (real file moves, not Recycle Bin).**
  - On first scanning, confirm every node (Year/Month/Category/File) shows
    a checked CheckBox by default — this is the "preserve organize
    everything" guarantee; nothing should start unchecked.
  - Uncheck a single File node under a Category that has 2+ files —
    confirm: that file's box is unchecked; the parent Category's box shows
    the *indeterminate* dash/fill visual, not a plain unchecked box; the
    Month and Year ancestors above it also show indeterminate, not
    unchecked; a sibling Category untouched by this stays fully checked.
  - Re-check that same File — confirm the Category/Month/Year all return
    to fully checked (indeterminate clears) once every descendant is
    checked again.
  - Uncheck a whole Category node — confirm every File under it becomes
    unchecked (cascade down), the Category itself shows plain unchecked
    (not indeterminate — it's *fully* deselected), and Month/Year above it
    show indeterminate (assuming other categories/months still have
    checked content).
  - Uncheck the top-level Year node — confirm every Month/Category/File
    beneath it becomes unchecked, and the "Organize Files…" button
    disables itself if this was the only Year in the tree (SelectedFileCount
    would be 0).
  - Click a checkbox repeatedly (5+ times) on the same node — confirm it
    only ever alternates between fully-checked and fully-unchecked, and
    never visibly "sticks" on the indeterminate dash from a direct user
    click (indeterminate should only ever appear as a result of a
    *descendant* being partially selected, never from clicking the node
    itself an odd number of times) — this is the specific WinUI
    ThreeState-CheckBox click-cycling workaround from this session; if it
    ever shows indeterminate right after you click a node directly, that
    workaround has a bug.
  - With some files deselected, click "Organize Files…" — confirm the
    confirmation dialog says "N of M file(s)" (not just "M file(s)").
    With everything selected (no changes), confirm it says "M file(s)"
    with no "of" wording (unchanged from before this session).
  - After confirming, verify: only the checked files actually moved;
    unchecked files are still sitting untouched in their original
    location; the new move-log_*.json under
    %LOCALAPPDATA%\ImageCleanup\move-logs\ lists *only* the files that
    were checked and moved — not the full original plan.
  - Re-scan (or switch tabs and back) after a partial-selection move —
    confirm the previously-unchecked, unmoved files still appear in a
    fresh Organization plan (since they're still on disk in their
    original location) and default back to checked in the new plan
    (selection state is per-rebuild, not persisted across rescans).
- Quality ComboBox blanking fix: scan a folder with several files in
  Quality, stage a Delete on one, commit it so the list shrinks by one
  row, then check every remaining row's action ComboBox — confirm none of
  them show blank; each should show its actual current action ("None"
  unless you'd changed it). Repeat with staging/removing a few different
  rows (not just the first or last) to exercise different container-reuse
  positions, since the bug was intermittent/position-dependent rather
  than affecting every row every time.
- Scan performance fixes: run a real scan (ideally the same real folder
  used for the earlier 6183-file/193.5s timing) and paste the
  `[ScanPerf]` Debug output line back — check that `decode=`,
  `downscale=`, `dhash=`, `blur=`, `lowDetail=`, `needsRescan=`,
  `getByPath=`, and `upsert=` all dropped noticeably vs. the prior
  numbers, and that `wallClock=` (previously `total=`) is meaningfully
  lower. Confirm Duplicates/Quality/Organization all still show correct
  results afterward (BlurScore ordering in Quality should still look
  blurriest-first by eye; Duplicates grouping/LowDetail exclusion should
  look unchanged) — the underlying fixes only changed *how* these values
  are computed, not what they mean, but this is the first real-photo
  check since the values themselves changed scale. If IOException
  first-chance notifications still appear in the Output window right
  after a scan, confirm no unhandled-exception dialog also appeared (that
  would mean the "benign" read above was wrong).
- Parallelized scan pipeline: run a real scan and paste the new
  `[ScanPerf]` line — `wallClock=` should now be substantially lower than
  `aggregateCpuTime=` (they were equal, by definition, back when the
  pipeline was sequential; a large gap between them is the actual proof
  parallelism is working, and `maxDegreeOfParallelism=` in the same line
  confirms what cap was used on your machine). Confirm file counts,
  cacheHits/cacheMisses, and the actual scanned results (Duplicates
  groups, Quality's list, Organization's tree) are unchanged from before
  parallelization — same files, same computed values, just faster. Run
  the same folder twice in a row (second run should be near-instant,
  cache hits) and confirm no crash, no duplicate/missing files, and no
  new IOException/SQLite-related first-chance exceptions beyond what was
  already investigated above. If you have a large enough real library,
  this is the number to report back — how much `wallClock=` dropped
  compared to the fully-sequential 193.5s baseline from two sessions ago.
- **Organization — automated undo (new this session). Use the same kind of
  disposable test folder as the move-execution checks above — this
  exercises real File.Move calls both directions.**
  - Run an "Organize Files…" move on a disposable test folder (partial
    selection is fine — undo should only need to reverse whatever was
    actually moved, per the move log). Click "Undo a Previous Move…" and
    confirm a dialog lists that move (timestamp, file count, destination)
    — if you've run more than one move, confirm the newest is listed
    first.
  - Select it and confirm — confirm the moved files land back at their
    original source paths, and the confirmation dialog's file count
    matched what was actually in that log (not the total plan size if you
    only moved a selection).
  - Re-run "Undo a Previous Move…" against the *same* log a second time
    immediately after — confirm the summary reports everything as
    "already reversed" (not "reversed" again, and not an error), and that
    no files are moved, deleted, or duplicated by the second run.
  - Simulate a partial-undo scenario: run a move with 2+ files, manually
    move just one file back to its original location yourself (outside
    the app, e.g. via File Explorer), then run "Undo a Previous Move…"
    against that log — confirm only the *other*, still-outstanding file
    gets moved back, and the one you manually restored is reported as
    "already reversed" rather than erroring or double-moving it.
  - Simulate a missing-destination scenario: after a move, manually
    delete one of the moved files from its destination location, then run
    undo against that log — confirm that entry is reported as skipped
    (not reversed, not a crash) and the summary/skip reasoning is visible
    somewhere reasonable (even if just in the aggregate count for now).
  - Simulate a source-occupied scenario: after a move, manually create a
    new/different file at one of the original source paths, then run undo
    — confirm that entry is skipped, the newly-created file at the source
    is untouched (not overwritten), and the originally-moved file is still
    sitting at its destination (not lost in the attempt).
  - Confirm Duplicates/Quality both reflect files being back in their
    original location after a successful undo, without needing a manual
    rescan (depends on UndoMoveLogAsync calling
    ScanSessionService.RefreshAsync(), same as ExecutePlanAsync).
  - Click "Undo a Previous Move…" when the move-logs directory is empty
    (e.g. a fresh install, or after manually clearing
    %LOCALAPPDATA%\ImageCleanup\move-logs\) — confirm a clear "no move
    logs found" message rather than an empty/broken picker dialog.
- **Three bug fixes from manual testing (new this session) — re-verify all
  three, since they were reported from real usage, not hypothetical.**
  - Checkbox indentation: open Organization on a real scanned folder and
    compare row indentation to before this fix (or just judge by eye) —
    every row (Year/Month/Category/File) should sit close to the left
    edge with the checkbox neatly inline, not pushed noticeably right of
    where the Duplicates/Quality tabs' own list rows sit.
  - Undo picker timestamp: perform an Organize Files move, note your
    system clock's local time at that moment, then open "Undo a Previous
    Move…" — confirm the listed timestamp (and the one in the follow-up
    confirmation dialog) matches your local wall-clock time, not a
    UTC-offset time (e.g. a 1am local move should show ~1am, not ~8-9am on
    US Pacific).
  - Empty folder cleanup: perform a full Organize Files move into a fresh
    destination folder, then fully undo it — confirm the Year/Month/
    Category folders it created are gone afterward, but the destination
    root folder you chose still exists (even though it may now be empty
    itself). Then repeat with a partial scenario (deselect some files
    before organizing, or simulate a skip during undo per the checklist
    above) — confirm a folder that still has a file left in it is NOT
    deleted.
- **Settings page (new this session) — verify the gear icon, theme toggle,
  and both maintenance actions.**
  - Launch the app — confirm a gear-icon "Settings" entry appears at the
    bottom of the NavigationView pane (the standard built-in entry, not a
    custom item), and clicking it shows the new Settings page (Appearance
    section with three theme radio buttons, Maintenance section with
    Clear Cache / Clear Move History buttons).
  - Theme toggle: click "Light", then "Dark", then "Use system setting" —
    confirm the whole app's visuals (not just the Settings page) switch
    immediately with no restart, and that switching back to Duplicates/
    Quality/Organization shows the new theme applied there too.
  - Close and relaunch the app after picking Light or Dark — confirm it
    opens directly in that theme (no flash of the wrong theme first, and
    no need to revisit Settings to reapply it) — this depends on
    App.xaml.cs loading settings.json and calling ApplyTheme before
    Activate().
  - Clear Cache: click it, confirm the dialog wording, confirm, then
    re-scan a previously-scanned folder — confirm it takes noticeably
    longer (full re-hash) rather than being an instant cache hit,
    confirming `%LOCALAPPDATA%\ImageCleanup\cache.db` was actually
    deleted and rebuilt.
  - Clear Move History: perform an Organization move first (so at least
    one move-log file exists), then open Clear Move History — confirm
    the confirmation dialog states the correct log count and uses
    noticeably stronger "not recoverable" wording than Clear Cache's.
    Confirm, then check `%LOCALAPPDATA%\ImageCleanup\move-logs\` is
    empty, and that Organization's "Undo a Previous Move…" now reports
    "no move logs found."
  - Nav round-trip: visit Settings, change the theme, switch to
    Duplicates/Quality/Organization and back to Settings — confirm the
    selected radio button still reflects your last choice (NavigationCacheMode.Enabled
    keeping SettingsViewModel alive, same as every other page).
- **Light theme fix (new this session) — this is the main thing to
  re-verify, since Light mode was reported as greyish/broken before this
  pass. Root cause: MainWindow.xaml's root Grid had no explicit
  Background at all — an unpackaged WinUI 3 Window has no default page
  background of its own, so it was rendering as whatever the composition
  swapchain's clear surface is, which happened to look plausible under
  Dark but showed up as a broken grey smear under Light. Fixed by setting
  `Background="{ThemeResource ApplicationPageBackgroundThemeBrush}"` on
  that root Grid, so it now genuinely repaints per theme rather than
  silently falling through to a fallback surface. Also replaced two
  hardcoded `Foreground="White"` badges (GroupDetailDialog's "★ Keep"
  badge, OrganizationPage's "renamed" badge) with
  `{ThemeResource TextOnAccentFillColorPrimaryBrush}` — the correct WinUI
  token for text on an accent-filled surface — and added a couple of
  distinct theme-aware surface levels that didn't exist before: the
  shared folder-selection toolbar and both staging/review panels
  (Duplicates, Quality) now use `LayerFillColorDefaultBrush` instead of
  sitting directly on the bare window background, and each file card in
  GroupDetailDialog now uses `CardBackgroundFillColorDefaultBrush`. No
  ViewModel or logic changed — this was XAML/resource-only.**
  - Switch to Light in Settings (or set Windows itself to Light and pick
    "Use system setting") — confirm the whole window shows a clean white/
    light background immediately, with no leftover grey/dark smear
    anywhere (this is the headline regression to check).
  - Check every page under Light: Duplicates, Quality, Organization,
    Settings, and the shared "Select Folder" toolbar at the top — text
    should be dark-on-light and clearly readable everywhere, not just in
    some regions.
  - Duplicates and Quality: stage at least one action so the staging/
    review panel at the bottom becomes visible — confirm it reads as a
    subtly distinct surface from the results list above it (not
    identical, not jarring) in both Light and Dark.
  - Duplicates: open "View Group" on any group — confirm each file's card
    in the dialog shows a faint card-level surface distinct from the
    dialog's own backdrop, in both themes, and that the "★ Keep" badge's
    white-ish text is still clearly readable against the accent-colored
    badge background in Light.
  - Organization: find (or create) a renamed file in the tree — confirm
    the "renamed" badge's text is still clearly readable against its
    accent-colored background in Light.
  - Collapse the NavigationView pane (hamburger toggle) under Light —
    confirm the Copy/Filter/Folder/gear icons are still clearly visible
    against the pane background (not washed out or invisible) — this was
    a "confirm, don't break" check since the icons already inherit
    theme-aware foreground from WinUI's default NavigationView styling
    and nothing in this codebase overrides it.
  - Toggle Light → Dark → Light a few times in a row without restarting —
    confirm every region (toolbar, page content, staging panels, dialogs)
    flips consistently every time, with nothing "stuck" showing the
    previous theme's colors.
  - Confirm Dark theme still looks exactly as good as it did before this
    change — this pass added an explicit background and a couple of
    surface brushes but didn't touch anything Dark-specific, so Dark
    should be unaffected, not just "not worse."
- **Localization infrastructure (new this session) — Dev-language
  behavior should be provably unchanged; English/Chinese are placeholder
  only in this pass (empty dictionaries), so the main thing to verify is
  that the MECHANISM works, not any actual translated wording (there
  isn't any yet).**
  - Launch the app with no settings.json present (or Language never
    touched) — confirm every page, dialog, and Organization folder name
    reads exactly as it did before this session (this is the "Dev is
    provably identical to today" bar the whole dictionary extraction was
    built to hit — if anything reads differently, that's a bug in the
    extraction, not an intentional change).
  - Open Settings — confirm a new "Language" section appears below
    "Appearance" with three options (Dev/English/Chinese), same
    RadioButtons style as the theme picker.
  - Switch to English or Chinese — confirm the Settings page's own
    status line shows the "applies to new dialogs immediately; restart
    for everywhere else" message, and that it does NOT crash or show
    blank/raw-key text anywhere (English/Chinese dictionaries are
    genuinely empty right now — every single string should silently
    fall back to Dev's wording, so switching language should currently
    be visually a no-op almost everywhere, not broken/blank).
  - After switching to English or Chinese, open any ContentDialog (e.g.
    Duplicates' Commit Changes confirmation, or Settings' Clear Cache
    confirmation) — per the fallback behavior, it should look identical
    to Dev (no translated text exists yet), but confirm it opens
    normally with no errors — this exercises the "dialogs update
    immediately, no restart needed" code path even though there's
    nothing different to see yet.
  - Restart the app after picking English or Chinese — confirm it
    still opens without error and the setting persisted (check
    `%LOCALAPPDATA%\ImageCleanup\settings.json` for a `"Language"` value
    matching your last choice).
  - Switch Theme after having changed Language (or vice versa) — confirm
    changing one setting does NOT silently reset the other back to its
    default (this was an actual bug fixed in this session — previously
    each setter saved a fresh `AppSettings` object, dropping whichever
    field it didn't touch). Check `settings.json` directly to confirm
    both fields are present and correct after alternating a few changes.
  - Organization: run a scan and preview an Organization plan under each
    language — confirm the tree still shows "Photo"/"NoMetadata" category
    folders exactly as before (Dev fallback, since `en.json`/`zh.json`
    don't have `Organization.FolderName.*` yet) — this is the folder-name
    localization hook mentioned above; nothing should look different yet,
    but it also shouldn't error or show a blank category name.
  - NavigationView collapsed/expanded pane, both languages — confirm
    "Duplicates"/"Quality"/"Organization" nav labels still render (via
    `{loc:Loc}` now instead of a literal string) with no blank/missing
    labels.
  - This is the first WinUI custom `MarkupExtension`
    (`App.Localization.LocExtension`) used anywhere in this codebase and
    cannot be confirmed via the CLI build (App project constraint, as
    always) — if the build fails in Visual Studio specifically on
    `{loc:Loc ...}` usages, that's the first thing to check; every other
    part of this session (LocalizationService, AppSettings, the
    Organization resolver hook) is Core/Data and already covered by the
    automated test run.
- **Action-string value/display split + English wording (new this
  session).** Dev-language behavior should still be provably unchanged;
  the main new things to verify are the ComboBox binding-mode switch
  (DuplicatesPage/GroupDetailDialog now use SelectedIndex, matching
  Quality) and that switching to English actually shows plain wording.
  - Under Dev language: scan a folder with duplicates, open Duplicates —
    confirm the action ComboBox on every row still behaves exactly as
    before (None/Keep/Delete/Move selectable, Keep-conflict reassignment
    still bumps the previous Keep file to Delete, the ★ badge/border
    still follow whichever file is Keep). This is the regression check
    for the SelectedItem → SelectedIndex binding switch — if anything
    shows blank or stops responding to selection, that's the first thing
    to suspect.
  - Same check in GroupDetailDialog (View Group → each file's ComboBox)
    and in Quality (unaffected by the binding switch, but still reads
    ActionType now under the hood — confirm Delete/Move staging and the
    Commit flow still work).
  - Stage a Delete/Move in Duplicates or Quality — confirm the staging
    review panel at the bottom shows "Delete"/"Move" text exactly as
    before under Dev (this is `StagingEntryViewModel.Action`, now
    resolved through the same localized-display path as the ComboBox —
    should be visually identical under Dev, just routed differently).
  - Switch to English in Settings, then re-check Duplicates/Quality/
    Organization/Settings — confirm the plain-language wording from this
    session's `en.json` appears (e.g. Settings' "Clear What's Been
    Scanned" instead of "Clear Cache", Organization's "Organize My
    Photos…" instead of "Organize Files…", the Quality tooltip talking
    about sharpness instead of "BlurScore"). Since switching language
    needs a restart for already-open pages (see the localization-
    infrastructure session above), restart the app after switching
    before checking.
  - **Organize a disposable test folder under English mode** (same
    "use throwaway files, not a real library" caution as every other
    Organization move check) — confirm the created category folder is
    actually named "Other" on disk (not "NoMetadata") for files without
    EXIF, since `Organization.FolderName.NoMetadata`'s English value is a
    real folder name, not just UI text. This is the one part of the
    English wording pass with an actual functional (not just cosmetic)
    consequence — worth confirming deliberately rather than assuming it
    looks right from the UI alone.
  - Confirm every confirmation/warning dialog under English (Organize,
    Undo, Clear Move History) still clearly conveys the serious/
    non-reversible parts of what it's about to do — read them as if
    unfamiliar with the app, not just skimming for length.
- **English wording review + Quality layout fix (new this session).**
  - Under English: confirm Organization's destination-picker button
    reads "Destination Folder…", the Organize button reads just
    "Organize…", and both the Undo button and the "no logs found"
    dialog title read "Undo an Action" (not "Undo an Earlier Organize").
  - Under English, in Settings: confirm both the button and its confirm
    dialog read "Clear Previous Scans" (not "Clear What's Been
    Scanned").
  - Under English, scan a folder with at least one near-duplicate group
    (not just exact duplicates) and open Duplicates — confirm the group
    header reads "Near duplicate" (not "near-dup"), and that an exact-
    match group reads "Exact match". Stage a Delete/Move on either page
    — confirm the panel above the staging list reads "Changes to Make
    (N)" under English, not "Review Staged Changes (N)" or any other
    use of the word "staged"/"staging".
  - **Quality row alignment** — this is the fix most worth a careful
    look, since it's a visual layout change with no automated coverage.
    Scan a folder and open Quality: confirm each row's thumbnail, file
    path, BlurScore number, and action ComboBox all sit on the same
    visual center line — the ComboBox should look the same height as it
    does in Duplicates' equivalent row, not visibly taller/stretched or
    offset from the rest of the row. Compare a Quality row directly
    against a Duplicates row side-by-side (switch tabs) if the
    difference isn't obvious at a glance.
- **Chinese (Simplified) mode (new this session) — localization's last
  remaining gap. This is the first time any non-Latin script has
  rendered anywhere in this app, so treat this as a genuinely new
  surface to check, not just "does the text look different."**
  - Switch to Chinese in Settings, restart the app (per the established
    "language change needs a restart to reach already-rendered pages"
    behavior) — confirm every page (Duplicates, Quality, Organization,
    Settings, the shared toolbar, the NavigationView tab labels) shows
    Chinese text with no mojibake (garbled/replacement-box characters),
    no missing glyphs, and no visibly wrong/fallback font — WinUI should
    pick a correct CJK-capable font automatically via its Fluent font
    fallback, but this has never been exercised in this app before now,
    so it's worth confirming rather than assuming.
  - Open every ContentDialog under Chinese (Commit confirm/complete,
    Organize confirm/complete, Undo picker/confirm/complete, Clear
    Previous Scans, Clear Organize History) — same check: correct
    glyphs, no truncation/clipping of the longer Chinese sentences
    (dialog width/wrapping was tuned against English text length, not
    tested against Chinese line-wrapping behavior).
  - Duplicates: scan a folder with both an exact-duplicate group and a
    near-duplicate group — confirm the group headers read "完全相同"
    and "相似" respectively (not the Dev/English words), and that the
    count/measure-word phrasing ("N 张照片") reads naturally, not just
    correctly-substituted.
  - Stage a Delete/Move under Chinese — confirm the panel header reads
    "待处理的更改（N）" with the count correctly substituted in the
    parenthetical position.
  - **Organization — the end-to-end folder-name check, on a disposable
    test folder only** (same caution as every other real-move check in
    this file): with at least one file that has EXIF (camera/phone
    photo) and one that doesn't (screenshot/download), run "整理…"
    (Organize) under Chinese mode and confirm the actual folders created
    on disk are named "照片" and "其他" (not "Photo"/"NoMetadata" or a
    garbled/mis-encoded name) — check this directly in File Explorer,
    not just in the app's own tree preview, since this is real
    filesystem behavior (`OrganizationExecutor`/`File.Move`), not just
    UI text rendering. Confirm Windows Explorer displays and sorts into
    those folders normally (open them, rename-test if you want extra
    confidence) — this is the one part of the Chinese pass with an
    actual functional, on-disk consequence, not just cosmetic text.
  - Confirm Organization's "N of M photo(s)" countText (used inside the
    move-confirmation dialog) reads grammatically in Chinese when a
    partial selection is organized (e.g. deselect a few files first) —
    this template reorders its two placeholders in Chinese ({total}
    before {selected}) rather than keeping English's order, so it's
    worth specifically confirming the substituted numbers land in the
    right places and the sentence still makes sense, not just that it
    doesn't crash.
- **Remaining localization coverage + NavigationView pane fix (new this
  session) — both are WinUI-layer/visual and neither is CLI-verifiable;
  treat "getting it right" as more important than a quick glance here.**
  - **No more raw English under Chinese mode** — this is the headline
    regression to re-check, since it's exactly what was reported
    broken. Switch to Chinese, restart, then work through every page
    checking specifically for leftover English text, not just that
    Chinese text is *present* somewhere:
    - The shared toolbar's status line in its untouched/empty state
      (right after launch, before selecting a folder) — should read
      entirely in Chinese, not "Ready — click..." in English.
    - Duplicates/Quality/Organization's own empty-state status line
      (switch to each tab before scanning anything).
    - Scan a folder and confirm the "N found" / "N scanned" / "N
      duplicate groups" / "N photos, blurriest first" status messages
      are fully Chinese, not a Chinese sentence with a stray English
      fragment.
    - Stage a commit, run it, and confirm the "Done — N processed" /
      "N succeeded, N failed" / failure messages are Chinese.
    - Run an Organize, then an Undo, and confirm every progress/done/
      failed status line for both is Chinese.
    - Settings: run Clear Cache and Clear Move History and confirm
      both success messages read in Chinese.
    - **Click the hamburger to expand the pane and look at the
      "Settings" entry's label specifically** — this is the one that
      was structurally different (an implicit NavigationView-generated
      item, not a normal menu item) and easy to miss on a casual
      glance; confirm it reads "设置", not "Settings".
    - Repeat spot checks in English mode too — this pass touched
      `en.json` as much as `zh.json`, so confirm the same status
      messages read in plain English there (e.g. Settings' "Ready —
      click..." message, Organization's progress text) rather than
      assuming only the Chinese side could have regressed.
  - **NavigationView pane expand/collapse — test in all three
    languages** (the fix itself is language-independent, but do this
    alongside the localization check since you'll already be switching
    languages):
    - On launch, confirm the pane starts collapsed to icon-only (Copy/
      Filter/Folder/gear icons, no labels) — same starting appearance
      as before this session's fix.
    - Click the hamburger to expand — confirm the main content area
      (the page Frame) visibly shifts/shrinks to make room for the
      now-wider pane, rather than the pane floating on top of the
      content with the content staying in place underneath it. This is
      the actual bug being fixed — compare directly against the
      before-fix screenshot/description if unsure ("overlay" = content
      still fully visible and interactive-looking behind a floating
      pane; "push" = content area genuinely narrower, pane and content
      side by side with no overlap).
    - Click the hamburger again to collapse — confirm content expands
      back to fill the freed space, and confirm clicking somewhere in
      the content area while the pane is expanded does NOT auto-close
      the pane (light-dismiss is an overlay-mode behavior; inline mode
      should have no light-dismiss — if clicking content collapses the
      pane, that's a sign it's still in an overlay-like mode and the
      fix didn't fully take).
    - Resize the window narrower and wider with the pane both collapsed
      and expanded — confirm nothing clips, overlaps, or looks broken
      at a few different widths, since `PaneDisplayMode="Left"` doesn't
      have `Auto`'s automatic width-based mode-switching and this
      hasn't been tested at extreme window sizes.
    - This is a genuinely uncertain fix in one respect, flagged
      explicitly rather than assumed: confirm the collapsed
      (`IsPaneOpen="False"`) state actually renders as a narrow
      icon-only rail (matching `LeftCompact`'s old collapsed width) and
      not some other width — the reasoning behind this fix is solid on
      the inline-vs-overlay question, but the exact collapsed pixel
      width under `PaneDisplayMode="Left"` wasn't something that could
      be confirmed without running the app.
- **Pane-animation background flash (new this session)** — do this
  alongside the pane expand/collapse check above, in both Light and
  Dark theme (the bug specifically only became visible after Light mode
  got real, non-black brushes, so both themes are worth checking, not
  just Light).
  - Click the hamburger to expand and collapse the pane a few times in
    a row, watching closely (may be easiest to see slowed down — resize
    the window or just repeat the toggle several times) — confirm there
    is no brief flash of a different-colored strip/gap between the pane
    and the content area mid-animation. Before this fix, that flash
    would show whatever color `NavigationView`'s default template
    brush happened to be, distinct from the app's actual background.
  - Confirm this holds in both Light and Dark theme, and confirm
    switching theme WHILE the pane is mid-animation (if timing allows)
    doesn't reveal any stale color either.
  - Confirm the pane and the content area still look like one seamless
    background when the pane is fully open or fully closed (not just
    mid-animation) — this fix touches `NavigationView.Background` and
    `Frame.Background`, both static-state checks worth a glance too,
    not only the animated transition.
- **Pane brightness in Dark theme (new this session)** — switch to Dark
  theme (Settings), expand the pane, and confirm it now reads as a
  visibly brighter/distinct surface from the main content area — not
  identical the way they were before this fix. Switch to Light theme
  and confirm the pane looks unchanged from before this session (only
  Dark was touched). Collapse/expand the pane a few times in Dark and
  confirm the brighter pane color is consistent in both the collapsed
  (icon-only) and expanded (icon+label) states, not just one of them.
- **Single-photo view (new this session) — Quality and Organization.**
  - Quality: scan a folder, open the Quality tab, click "View Photo" on
    any row — confirm a dialog opens showing that one photo at a larger
    size (comparable to Duplicates' "Compare Photos" per-file card
    size) with its file path shown below, and a Close button. Confirm
    the photo loads in progressively (not blocking dialog open) and
    that reopening the same file's dialog a second time shows the
    photo immediately (already cached, no re-generation) — same
    caching behavior Duplicates' detail view already has.
  - Organization: switch to the Organization tab, expand a Category
    node, and confirm every individual File node (not the Year/Month/
    Category group nodes themselves) shows a "View Photo" button —
    clicking it should open the same style of dialog as Quality's,
    showing that file. Confirm Year/Month/Category nodes do NOT show
    the button (only File-kind nodes should).
  - Confirm "View Photo" text (and the dialog's title) read correctly
    in all three languages — Dev "View Photo"/"Photo", English same,
    Chinese "查看照片"/"照片" — switch language in Settings, restart,
    and re-check both features.
  - Confirm opening a photo from Quality and then a different photo
    from Organization (or vice versa) both work correctly in the same
    app session — this exercises the shared `SinglePhotoDialog` from
    two different call sites with two different underlying ViewModel
    types, which is the main integration risk this design was meant to
    avoid (nothing in the dialog itself is type-specific, but worth
    confirming directly rather than assuming).
- **Platform-mismatch publish fix (new this session) — the two things
  to verify are that F5 still works and that Publish no longer hits the
  platform-mismatch error.**
  - Open `ImageCleanup.sln`, confirm the Solution Configuration dropdown
    in the toolbar is at its normal default ("Debug", "Any CPU"), and
    press F5 — confirm the app still launches and runs exactly as
    before (scan a folder, click through Duplicates/Quality/
    Organization/Settings). This is the main regression risk from
    changing the `.sln`'s `Any CPU` mapping — it's expected to now build
    x64 instead of x86 under the hood, but should be behaviorally
    identical since nothing in the app is architecture-specific.
  - Run **Build > Configuration Manager** (or just glance at the
    Solution Configuration dropdown while "Any CPU" is selected) and
    confirm the ImageCleanup.App row now shows **x64** as its platform,
    not x86 — this is the direct confirmation the `.sln` fix took
    effect in the IDE, not just in the raw file.
  - Publish using the existing `FolderProfile.pubxml` (right-click
    ImageCleanup.App -> Publish -> Publish) — confirm it no longer
    fails with `"The RuntimeIdentifier platform 'win-x64' and the
    PlatformTarget 'x86' must be compatible"`, and confirm the publish
    log now shows `Configuration: Release x64` (not `x86`) for the App
    project.
  - Confirm the publish otherwise completes and produces the same
    output described in "Output location" above (the `.exe`, dependency
    DLLs, bundled WindowsAppSDK/`.NET` runtime files, `Strings\`) — this
    is the actual end-to-end proof the platform fix plus the
    `WindowsAppSDKSelfContained`/`WindowsPackageType` additions to the
    `.pubxml` (also made this session) together produce a working
    self-contained output, not just that the error message went away.
- **Startup crash fix — WindowsAppSDK version bump.** ***Superseded by
  the MSIX packaging pivot below — the unpackaged approach this checklist
  item verifies is no longer the recommended path at all, so this is
  historical only. Skip straight to "MSIX packaging" below.*** Original
  text kept for the record: re-publish via `FolderProfile.pubxml` and
  check Event Viewer for the same `Microsoft.UI.Xaml.dll`/`0xC000027B`
  and `combase.dll`/`80004005` signature if still investigating the
  unpackaged path specifically for some reason.

- **MSIX packaging (new this session) — this is the current, correct
  path; everything above about the unpackaged/self-contained approach
  is superseded.** The core pipeline (build → sign → trust → install →
  launch) was already verified end-to-end this session using a
  throwaway test certificate — it worked, and the crash this whole
  pivot exists to avoid did not reproduce. What's left is Alan doing the
  *real* version of that with his own permanent certificate:
  - Open `ImageCleanup.sln`, confirm `Package.appxmanifest` appears in
    Solution Explorer with no errors, double-click it — the visual
    manifest designer should open showing Display Name "ImageCleanup",
    Publisher "ImageCleanup", version 1.0.0.0, and a Packaging tab.
  - Right-click ImageCleanup.App → Publish → **Create App Packages** →
    Sideloading → create a new certificate (should pre-fill Subject as
    `CN=ImageCleanup`, matching the manifest — if it doesn't, something's
    out of sync and worth investigating before continuing) → x64 only →
    "Never" generate an app bundle → Create.
  - Confirm the output folder contains a `.msix`, a `.cer`, a
    `Dependencies\` folder, and `Add-AppDevPackage.ps1` — per "Output &
    distribution" above.
  - **Simulate the real end-user experience** (ideally on a second
    machine, or at minimum a separate Windows user account, so the
    dev machine's own trust/registration doesn't mask a problem a real
    end user would hit): follow "Installing ImageCleanup" above exactly
    as written — trust the `.cer`, confirm/enable sideloading if needed,
    run `Add-AppDevPackage.ps1`. Confirm the app installs without error
    and appears in the Start Menu.
  - Launch it from the Start Menu (not from Visual Studio) — confirm it
    opens normally with **no crash**, and do a basic smoke test (select a
    folder, confirm scanning works) to confirm this isn't just "didn't
    crash on launch" but genuinely functional.
  - Confirm normal Windows uninstall works: Settings → Apps → find
    "ImageCleanup" → Uninstall — should remove cleanly, same as any
    other installed app.
  - If every check above passes: update the Known gaps entry for
    Distribution/.exe packaging to reflect this is now verified, not
    just configured — that status line currently says "still needed:
    Alan needs to run the real wizard," which should change once this
    is actually done.
  - If installation or launch fails: capture the exact error (PowerShell
    script output, or Event Viewer if it's a launch-time crash) —a
    failure here would be a genuinely new finding (this exact pipeline
    already succeeded once this session with a throwaway cert on this
    same machine), so don't assume it's the same class of problem as the
    old unpackaged crash without checking the actual error first.
- **Localization — final two Chinese-mode gaps (new this session):
  Organization's file-count text and month names, both TreeView preview
  AND real on-disk folders.** Switch to Chinese in Settings, restart
  (per the established restart-for-already-open-pages caveat), scan a
  folder, and open Organization:
  - Confirm every tree node's text (Year/Month/Category — e.g. "2024
    (312 张照片)") reads entirely in Chinese, including the count
    wording — no stray "files" or "file(s)" in English anywhere in the
    tree.
  - Confirm Month nodes show Chinese month names ("3月", not "March" or
    "三月") in the TreeView preview.
  - **Run an actual Organize on a disposable test folder** (same
    "throwaway files only" caution as every other real-move check in
    this file) under Chinese mode, and check the created folders
    directly in File Explorer (not just the app's tree preview) —
    confirm the month folders are named like "03 - 3月" (the hybrid
    zero-padded-number-plus-localized-name format), not "03 - March".
    This is the one part of this fix with a real, on-disk consequence,
    not just cosmetic text — worth checking directly in Explorer rather
    than trusting the in-app preview alone.
  - Confirm File Explorer still sorts these Chinese-named month folders
    chronologically (03 before 04, etc.) exactly as the English-named
    ones always did — the sort-correctness comes entirely from the
    leading zero-padded number, so this should be unaffected, but worth
    a direct look since it's the property this whole hybrid-naming
    scheme exists to guarantee.
  - Repeat the same TreeView/status-text check under English mode too
    (confirm "2024 (312 photo(s))"-style wording, not the old "review
    staged"-era phrasing) — this pass touched `en.json` as much as
    `zh.json`, so both are worth a glance, not just Chinese.
  - Switch back to Dev language and confirm the tree/month-name text
    reads exactly as it always has (plain English "2024 (312 files)",
    "March") — this is the "Dev is provably unchanged" bar every
    localization pass in this file has been held to; if anything looks
    different under Dev, that's a bug in this fix, not an intentional
    change.
- **F5 debugging after MSIX packaging (new this session) — this is the
  main thing to verify, since it was a real, reported crash blocking
  day-to-day development, not a hypothetical.** See "F5 debugging needs
  its own local deploy" under Publishing above for the full writeup.
  - In Visual Studio, open **Build → Configuration Manager** and confirm
    the **Deploy** checkbox is now checked for **ImageCleanup.App**
    across Debug/x64 (the config F5 actually uses by default) — this
    confirms the `.sln` fix took effect in the IDE, not just in the raw
    file.
  - Press **F5**. Confirm the app builds, deploys, and launches
    normally — **no `COMException: Class not registered
    (0x80040154)`**, no crash of any kind on startup.
  - Do a basic smoke test while debugging (select a folder, confirm
    scanning/Duplicates/Quality/Organization all still work) — this
    confirms it's a genuinely working debug session, not just "didn't
    crash within the first second."
  - Set a breakpoint somewhere in App-layer code (e.g.
    `OrganizationViewModel.RebuildAsync`) and confirm it's hit normally
    — full debugging (not just launch-and-run) is the actual point of
    F5, worth confirming explicitly rather than assuming it follows from
    "the app launched."
  - Stop debugging, then **re-run Create App Packages** (Publish →
    Create App Packages → reuse the existing certificate) and confirm
    it still succeeds and produces an installable `.msix` exactly as
    before — the claim that the F5 fix doesn't touch the Release
    packaging path is based on reasoning about what each changed file
    is read by (`.sln` Deploy flags and `launchSettings.json` are both
    IDE/debug-launch-only, confirmed via a CLI `dotnet build`
    comparison), not an actual Visual-Studio-driven re-verification —
    worth closing that gap directly since packaging is the whole reason
    this pivot happened in the first place.
  - If F5 still crashes with the same `0x80040154` error after both
    fixes: check that the Debug|x64 configuration is what's actually
    active in the Solution Configuration dropdown (not Debug|x86 or
    Debug|Any CPU somehow still resolving differently than expected),
    and confirm `Properties\launchSettings.json` is actually present in
    `src\ImageCleanup.App\` and shows up in Solution Explorer under the
    project (sometimes needs **Show All Files** toggled if it doesn't
    appear automatically).
