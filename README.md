# ImageCleanup

A Windows desktop app for deduplicating, quality-reviewing, and organizing
image (and eventually video) folders.

This README has two parts: one for developers working on the code, one
for anyone just installing and using the app. Jump to whichever applies
to you.

---

## For developers

**Tech stack**: C# / .NET 9, WinUI 3 (Windows App SDK), MVVM.

**Project layout**: `ImageCleanup.Core` (pure logic, no UI/IO deps),
`ImageCleanup.Data` (SQLite cache, staging, services), `ImageCleanup.App`
(the WinUI 3 app itself). See [CLAUDE.md](CLAUDE.md) for the full
architecture, conventions, and project history — this README only
orients you, it doesn't duplicate that detail.

### Prerequisites

Visual Studio 2022 or later, with:
- **.NET desktop development** workload
- **Windows application development** workload (provides the WinUI 3 /
  Windows App SDK C# templates and single-project MSIX packaging tools)
- Optional: **Desktop development with C++** — not required to build or
  run, but recommended for full `.appxsym` symbol-package generation
  during packaging (without it you'll see a harmless `mspdbcmf.exe not
  found` warning, not an error)

### Build & run (development)

Open `ImageCleanup.sln` in Visual Studio and press **F5**. The app is
packaged (MSIX) — Visual Studio handles deploying and launching it
correctly as long as the solution's Deploy setting is enabled (it is,
by default, in this repo).

`dotnet build`/`dotnet run` do **not** work for `ImageCleanup.App` from
a plain terminal in every configuration — there's a real history here
(PRI packaging tooling, platform mismatches, reg-free WinRT crashes)
that led to the current MSIX-packaged setup. See CLAUDE.md's
**Publishing** and **Notes** sections if you want the full story;
short version: build and run the App project through Visual Studio.

### Tests

```
dotnet test
```

This runs the `ImageCleanup.Core.Tests` and `ImageCleanup.Data.Tests`
suites (212 tests as of this writing) — both are plain .NET class
libraries with no WinUI dependency, so they build and run fine from the
CLI. There's no automated test project for the App (WinUI) layer; see
CLAUDE.md for how App-layer changes get verified instead.

### Want more detail?

**[CLAUDE.md](CLAUDE.md)** has everything else: full architecture,
coding conventions, the complete session-by-session project history
(including every bug found and fixed), known gaps, and a manual
verification checklist for anything that can't be automated. Start
there for anything not covered above.

---

## For everyone else (installing the app)

**You do not need Visual Studio or any developer tools to use this
app — just follow the steps below.** You'll need a folder someone gave
you (containing a `.msix` file, a `.cer` file, and a few other files) —
if it came as a `.zip`, unzip it first so you have a plain folder you
can open in File Explorer.

You only need to do Part 1 and Part 2 once, ever (unless told
otherwise). After that, installing an update is just Part 3 again.

### Part 1 — Trust the certificate (first time only)

1. Open the folder you were given. Find the file that ends in `.cer`
   (for example `ImageCleanup.App_1.0.0.0_x64.cer`).
2. Double-click it. A window titled **Certificate** opens.
3. Click **Install Certificate...**.
4. Choose **Local Machine**, then click **Next**. (If Windows asks "Do
   you want to allow this app to make changes to your device?", click
   **Yes** — this always happens when installing a certificate, it's
   normal.)
5. Choose **Place all certificates in the following store**, click
   **Browse...**, select **Trusted Root Certification Authorities**,
   click **OK**, then **Next**, then **Finish**.
6. A **Security Warning** popup asks you to confirm. This is just
   Windows double-checking — since you got this file directly from
   someone you trust, click **Yes**.
7. Click **OK** on the "The import was successful" popup.

### Part 2 — Allow apps from outside the Microsoft Store (only if needed)

Skip this and try Part 3 first — many computers already allow this.
Only come back here if installing fails with a message mentioning
"sideloading" or "developer mode."

1. Click **Start**, type **Settings**, and open it.
2. Go to **System** → **Advanced**, then find the **For developers**
   section. (On some Windows versions, **For developers** appears
   directly instead.)
3. Turn on **Developer Mode**.
4. Click **Yes** on the confirmation that appears.

### Part 3 — Install the app

1. Go back to the folder you were given.
2. Right-click **Add-AppDevPackage.ps1** and choose **Run with
   PowerShell**.
3. A blue PowerShell window walks you through the rest automatically —
   just follow any prompts (press Enter or type **Y** to confirm
   something if asked).
4. When it's done, close that window. **ImageCleanup** now appears in
   your Start Menu like any other app.

**If double-clicking the script gives an error about scripts being
"disabled on this system" or "not digitally signed"** — some computers
block running scripts by default. This is normal, not a sign of a
problem, and easy to work around for just this one script:

1. In the folder you were given, click once in the empty area of the
   File Explorer address bar (at the top of the window).
2. Type `powershell` and press **Enter**. A PowerShell window opens,
   already in the right folder.
3. Type (or copy/paste) this exact line and press **Enter**:
   ```
   powershell -ExecutionPolicy Bypass -File .\Add-AppDevPackage.ps1
   ```
4. Follow the prompts the same as before.

This only affects this one script, this one time — it does **not**
change any permanent setting on your computer, and your computer will
be back to blocking scripts by default the next time, same as before.

**Seeing a security warning?** That's expected for an app installed
this way (not from the Microsoft Store) — it doesn't mean anything is
wrong, as long as the folder came from someone you trust.

**Something not working?** The PowerShell window's messages usually say
what's missing — "certificate not trusted" means revisit Part 1,
"sideloading not allowed" means revisit Part 2. Otherwise, take a
screenshot of the error and ask for help rather than guessing.
