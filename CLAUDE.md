# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

WinRestoreKit is a Windows Forms desktop app (.NET 8, C#) that backs up and restores Windows 11 settings locally - an offline alternative to the built-in Windows Backup app. Backups are lightweight: each module exports registry keys (as `.reg` files) and/or copies folders/files into a timestamped folder.

## Build

Three SDK-style projects, all targeting `net8.0-windows`. Use the dotnet CLI:

```
dotnet build src\WinRestoreKit.sln
dotnet test src\WinRestoreKit.sln
```

- **`src/WinRestoreKit.Core`** - the engine: `BackupBase`, all of `Conf/`, most of `Results/`, and the
  `Utils`/`Data`/`OsHelper`/`LogHelper` helpers. It deliberately does **not** set `UseWindowsForms`,
  and that is load-bearing: being unable to compile against WinForms is what keeps a `MessageBox` out
  of a backup module by construction rather than by review. Extracted in Phase 4 PR 2.
- **`src/WinRestoreKit`** - the WinForms app: `MainForm`, `Views/`, `Forms/`, `Program`, `RunSummary`,
  and the three sinks/seams that hand the engine its UI (below). References Core.
- **`src/WinRestoreKit.Tests`** - xUnit. References the app project only; Core arrives transitively.

**The engine reaches the user through three registered seams, never by referencing UI.** All three are
filled in by `Program.RegisterUiSeams()` before the message pump starts: `LogHelper`'s `ILogSink`
(implemented by `RichTextBoxLogSink`), `Utils.UrlFailureUi` (the could-not-open-link dialog), and
`Conf.AppStoreApps.RestoreDialog` (opens `RestAppsForm`). Unregistered, each fails safe on purpose -
logging goes nowhere, the link failure only logs, and the app-restore module reports **`Failed`**,
which is deliberately not `Skipped` because `Skipped` is already that module's genuine success reason.

Most engine types are `internal` and **must stay that way** - `WinRestoreKit.Core.csproj` declares
`InternalsVisibleTo` for both `WinRestoreKit` and `WinRestoreKit.Tests`. If something needs `public` to
compile, the project reference is wrong, not the modifier.

Output lands in `src\WinRestoreKit\bin\<Configuration>\net8.0-windows\`. This dev build is framework-dependent, so running it needs the **.NET Desktop Runtime 8** (`Microsoft.WindowsDesktop.App` 8.0.x) installed.

Releases are different: they ship **self-contained single-file**, so end users install nothing. The `/release` skill has the exact publish command and the flags it depends on - all of them matter, and the artifact must come out as exactly one ~69 MB `WinRestoreKit.exe`. Never ship the framework-dependent `bin\Release\` exe; on its own it cannot start. Do not add `PublishTrimmed` - WinForms resolves types by reflection and is not trim-safe.

The only runtime NuGet dependency is Newtonsoft.Json, declared as a `<PackageReference>` in both the app and Core projects at the same version (`packages.config` is gone). Tests are xUnit, in `src/WinRestoreKit.Tests`. There is no linter.

`src/WinRestoreKit/Properties/AssemblyInfo.cs` is hand-maintained and the **app** `WinRestoreKit.csproj` sets `<GenerateAssemblyInfo>false</GenerateAssemblyInfo>` - this is load-bearing for the update checker (see "Data flow and paths"). It must not move to Core: the file must stay at its exact path with its exact line format because it is the update checker's raw fallback source. `WinRestoreKit.Core.csproj` leaves `GenerateAssemblyInfo` at its default on purpose, since no client fetches anything from it and the generated `SupportedOSPlatform` attribute is what keeps CA1416 honest there. Never set `Version`/`AssemblyVersion`/`FileVersion`/`InformationalVersion` in the csproj, and never add an `AssemblyInformationalVersion` attribute; both would create a second, silently diverging version source. The csproj carries comments explaining this and…

The app declares `requestedExecutionLevel level="highestAvailable"` in `app.manifest` - registry export/import shells out to `regedit.exe`, so meaningful manual testing requires an elevated Windows session. The unit tests deliberately cover only logic that runs without elevation.

Note: `src/WinRestoreKit/bin/` and `src/WinRestoreKit/obj/` are untracked and gitignored.

## Roadmap

`docs/ROADMAP.md` holds the phased plan for the app and the reasoning behind it. Phase 1 (.NET 8 migration)
is done; Phase 2 is the safety overhaul, Phase 3 is module coverage. Read it before proposing work that
spans more than one file - it records what was deliberately deferred and why, including a list of known
module bugs that are *not* regressions.

## Architecture

### Backup module system (the core pattern)

- `src/WinRestoreKit.Core/BackupBase.cs` - abstract base every backup module inherits: `Title`, `Info`, `WarningMessage`, `RequiresExplorerRestart`, `IsInstalled()`, `Backup(path)`, `Restore(path)`, plus `BackupAsync`/`RestoreAsync` wrappers (Task.Run around the sync methods). `Backup`/`Restore` return a `ModuleResult`, not `void` - see "Reporting outcomes" below.
- `src/WinRestoreKit.Core/Conf/*.cs` - one class per backup area. Filename prefix letter encodes the category: `A` = Apps, `C` = Credentials, `D` = Devices, `E` = Developer, `G` = Gaming, `W` = Windows settings. (There is no `B`/Browser anymore - those modules were retired in Phase 3a and the roadmap says not to add new ones.) Most modules call `Utils.ExportRegistryKey()` / `Utils.ImportRegistryKey()` (regedit `/e` and `/s`) and/or `Utils.CopyFolder()`.
- `src/WinRestoreKit.Core/Conf/RegistryModule.cs` - base for the ten modules that capture exactly one registry key to `{Title}.reg`. Subclasses supply data (`Key`, `AbsenceIsNormal`) and inherit the decision logic, so the skipped-vs-failed rule is written once. **Prefer inheriting this over hand-rolling `Backup`/`Restore`** when a module is a single-key export.
- `src/WinRestoreKit.Core/Conf/FileModule.cs` - base for modules that copy **named files** into `{Title}\`. It is a whitelist by construction: it copies what `Files` lists and **never enumerates a directory**, which is how `ESsh` excludes private keys structurally rather than through a filter that has to be kept correct. Use it, not `FolderModule`, whenever the containing folder holds anything that must not be captured. Its naming seam is `BackupFileNameFor`, defaulting to the file's *base name* - never the full path, which would carry the backing-up account's user name into the artifact name and stop resolving under any other account. A module with two same-named files (three Windows Terminal installs all call theirs `settings.json`) **must** override it, or th…
- **A loop over N targets must build N distinct filenames.** Build the path with `BackupBase.RegFileNameFor(key)`; never `Title + ".reg"` inside a `foreach` over `Keys`. `WThemes` did the latter, which was harmless only while it had one key - a second export would delete the first via `TryDeleteExport` and write over it while *both* steps reported success, and the restore would import that one file once per key while the post-import probe found every key present, because the keys exist on the live machine regardless of what the file contained. Every row green, one key never captured. `BackupFileNamingTests` catches this by giving a module a synthetic extra key and observing the filename `RestoreAsync` actually computes - not by calling the seam, which a broken call site would still pass.
- **Keyless artifacts are named by a `const` on the class that writes them**, not through that seam: it derives `.reg` names from a registry key, and something like `AStoreApps`' `.json` export has no key. `AppStoreApps.ExportFileName` is the pattern. The rule is the same either way - a name kept away from its producer drifts. That one was spelled four ways at once, including in the `Info` text the user reads.
- **Changing a module's registry key changes its backup filename**, because the name is derived from the key. That orphans the file in every existing backup, which then restores as `Skipped("nothing was backed up for this item")`. Decide deliberately and disclose it; do not reach for a filename fallback without checking what the old file *contains*, since a `.reg` written for the old key applies to the old key no matter which key you pass `regedit`.

Adding a new module requires touching **two** places:
1. Create the class in `Conf/` inheriting `BackupBase` - or `RegistryModule` for a single-key export (namespace `Conf`).
2. Register it in `ModuleCatalog.CreateAll()` (`src/WinRestoreKit.Core/Conf/ModuleCatalog.cs`) with its category node name ("Settings", "Apps", "Devices", "Gaming", "Credentials", "Developer"). There is no category enum - `FindOrCreateNode` creates the node from the first call, so consistent spelling is the whole mechanism. `BackupPageView.InitializeConfigurations` just loops over the catalog, and `ModuleCatalogTests` cross-checks the catalog against the concrete `BackupBase` subclasses in the assembly, so a module added to Core but not registered turns red by name. Consider also whether it belongs in a `BackupPresets` list.

…and it must **declare what its restore touches** (see "Restore safety" below). `RegistryModule` subclasses inherit that declaration from their `Key`; everything else states it explicitly. `RestoreDeclarationTests` enumerates every registered module and fails if one does not.

### Restore safety (read before writing a module that restores)

A restore snapshots what it is about to overwrite, asks the user to confirm it, and logs what it did.
None of that works unless modules declare their own restore surface, so `BackupBase` carries three
virtuals:

- `RestoreTargets` - the registry keys, folders, or commands this module's restore writes to. These
  are read out to the user in the confirmation dialog *before* anything is overwritten. The default is
  a loud `Undeclared` marker, not an empty list: a module that forgets shows a visible wart in the text
  users read before consenting, rather than quietly claiming to touch nothing.
- `ProcessesToCloseBeforeRestore` - the app that owns those files. Set `NeedsConsent` when the user must
  agree to close it (browsers); leave it false only for a process that restarts itself, which is closed
  just-in-time instead of being offered as a choice. Never overwrite a live profile without one of these.
- `RestoreMakesChanges` - leave it `true` unless the module's restore genuinely writes nothing (only
  `AStoreApps`, which opens a dialog). Setting it false exempts the module from being snapshotted, so
  getting it wrong means a restore that cannot be undone. This is the same class of judgement call as
  `absenceIsNormal`.

A declaration must contain **no null entries**. The dialog renders a per-entry marker for one, which is
loud on purpose, but it is a wart shown to a user deciding whether to consent - not a supported way to
declare a target. `RestoreDeclarationTests` fails on it.

**Anything a restore writes must be inside the pre-restore snapshot.** The snapshot is taken by running
the module's own `Backup`, so a restore path that writes somewhere `Backup` does not read is invisible to
it - and `SnapshotGate` will still report the restore as fully undoable, because it has no way to know.
That asymmetry is what pushed the `WTelemetry` legacy-filename fallback out of Phase 2c: a caveat inside
a step reason does not correct a verdict the user reads as "this can be undone".

The orchestration lives in `BackupRestoreOrchestrator` (`src/WinRestoreKit/Orchestration/`), not in modules and no longer in a view: consent is gathered once on the UI thread through the `IRunUi` seam and
`RestoreDispatch.Decide` turns it into a per-module Run/Skip/Fail. **Never show a dialog from module
code on the restore path** - modules run on thread-pool threads, where a `MessageBox` has no owner and
can paint behind the main window.

### Reporting outcomes (read before writing a module)

This app's core failure mode was announcing success it had not verified. The rules below exist to keep
that from coming back; each was written after the corresponding mistake was actually made.

- **Build `StepResult`s and fold them with `ModuleResult.Aggregate`.** That is the only public
  construction path - there are deliberately no `ModuleResult.Succeeded/Skipped/Failed` factories,
  because one of them would be used to bypass the aggregation rules within a week.
- **Every sub-operation declares whether its target may legitimately be absent.** Absent + normal is
  `Skipped`; absent + not normal is `Failed`; a target that could not be *probed* is always `Failed`.
  "I could not tell" is a tool failure, not an absence. Getting this flag wrong is the cry-wolf
  failure in one direction and a hidden problem in the other.
- **Never claim more than you verified.** Registry exports are checkable (exit code *and* the file
  exists, is non-empty, and has a valid header). Imports are checked by reading the key back afterwards,
  but `regedit /s` still returns 0 on files it only partially applied and a present key does not prove
  its values match - so restore-side reasons say **applied**, never *verified* or *restored*.
- **Post-import probing is the mirror image of pre-export probing, on purpose.** Exporting, a key that
  cannot be probed is `Failed`, because the probe is the only evidence for the claim. Importing, a key
  that cannot be probed is still `Succeeded` ("could not confirm"), because exit code 0 already supports
  "applied" and failing there would cry wolf on every unelevated `HKLM` import. Only an *absent* key
  after an import is a failure.
- **An exit code is not evidence.** Measured on Windows 11: `regedit /e` on a nonexistent key exits 0
  and writes nothing; `netsh wlan export` printed "saved successfully" with exit code 0 while writing
  nothing. Always check the artifact the command was supposed to produce.
- **Log data-bearing text with `LogHelper.LogMessage`, never `LogHelper.Log`.** `Log` treats its first
  argument as a format string, so a registry path or exception message containing `{` throws inside
  the logger and the line is routed to `Console.WriteLine` - invisible in a WinForms app. The message
  is not lost loudly; it is lost silently.
- **Don't identify files by a name pattern you did not write.** `CWiFiConf` matched `WLAN*.xml` while
  `netsh` writes `<adapter name>-<SSID>.xml`, so restore found 0 of 19 profiles. Match on content when
  another tool chose the filename.

The csproj no longer needs a `<Compile Include>` entry - the SDK project globs `**/*.cs` automatically. (Older docs describing a third csproj step predate the .NET 8 migration.)

### UI navigation

`MainForm` is the shell - a left rail (Home · Back up · Restore · History, About in the footer) and a content host - and `NavigationService` (`Helpers/NavigationService.cs`) owns which view is in `pnlForm`, with `Show` for rail navigation, `Push`/`Pop` for going deeper and back, and `IRefreshableView` for views that must re-read disk on every visit. Views live in `Views/`: `HomePageView` (am I okay?), `BackupPageView` (presets + the module tree; renamed from `ConfPageView` in Phase 4 PR 7), `RestoreWizardStep1View`/`RestoreWizardStep2View` (pick a backup, then its contents), `HistoryPageView` (the merged backup/undo-point timeline, which replaced `RestPageView`), `AboutPageView`, and the shared `RunResultsPanel` that renders a run's per-module outcomes in-page. `Forms/RestAppsForm` is a dialog for reinstalling apps from a winget export, and `Forms/RestoreConfirmForm` is the consent dialog. Every view is built with `TableLayoutPanel`/`Dock`/`AutoSize` - absolute positioning is gone, because the process runs `HighDpiMode.PerMonitorV2`.

### Data flow and paths

- `DataHelper.Data` (`src/WinRestoreKit.Core/Helpers/DataHelper.cs`) centralizes paths and URLs. Backups go to `<exe dir>\app\<yyyy-MM-dd - HH.mm>\` (`Data.DataRootDir`); each backup folder gets a `backup_log.txt` listing what was backed up plus a machine-readable `backup_manifest.json`, both read by Home and the History timeline. `DataRootDir` resolves the exe directory from `Environment.ProcessPath` - **measured** under a real single-file self-contained publish, because the modes need not agree there and nothing in build or test exercises it. The trailing separator is part of the field's contract. Read the comment there before touching the line.
- `LogHelper` (singleton) composes the line and hands the text to an `ILogSink`; the app registers `RichTextBoxLogSink`, which owns the `InvokeRequired`/`Invoke` marshaling. `SetTarget(richTextBox)` still exists as an app-side extension, so call sites read as before. With no sink registered, logging is silent rather than fatal - every test class outside `LogHelperTests` runs that way while product code logs freely.
- Open web links with `Utils.OpenUrl`, never `Process.Start` directly. The app runs elevated, and `ShellExecute` passes that elevated token to the browser it launches; `OpenUrl` goes through `explorer.exe` so the browser runs as the user, rejects anything that is not an `http`/`https` URL (a shell launch would otherwise execute it), and cannot throw - it is called from a timer thread where .NET 8 turns an escaping exception into process termination.
- Update check (`UpdateCheck.CheckForUpdatesAsync`, app-side since Phase 4 PR 2 - it is almost all MessageBoxes and it calls `Program`) asks the **GitHub Releases API** for the newest release and takes `tag_name` via `Data.ParseLatestReleaseTag`. On ANY failure of that path - non-2xx including the shared-IP rate-limit 403, timeout, malformed JSON, or an empty tag - it falls back to `Data.Uri.URL_ASSEMBLY`, which fetches `src/WinRestoreKit/Properties/AssemblyInfo.cs` from `nicolasestrem/WinRestoreKit`, and string-parses `[assembly: AssemblyFileVersion("x.y.z")]` with `Data.ParseLatestVersion`. That fallback is inherited from Appcopier, but WinRestoreKit starts at 0.0.1 and has no deployed clients of its own, so it is kept on current merit rather than for compatibility: the rate-limit 403 is common, and once the repository is public but before the first Release is published the Releases API has nothing to return while main already carries an AssemblyFileVersion, making the raw path the only one that answers. Note that NEITHER source answers while the repository is private: both requests are unauthenticated, so both 404 and the check reports a failure. The update path only works once the repository is published. `Program.GetCurrentVersionTostring()` reads that same attribute off the running assembly by reflection. Both sides then go through `Version.ToString(3)`, so three-part AssemblyFileVersion values are required. Note the asymmetry: the FALLBACK reads the same attribute this app reads, so a difference there is always a real version difference, but the PRIMARY tag is a separate hand-entered value that never reads the attribute, so a tag that disagrees with the shipped `AssemblyFileVersion` produces a permanent phantom update. Keeping them equal is the release process's job, not the code's.

### Namespace quirk

Namespaces do not follow folder structure and are flat: `WinRestoreKit` (core + helpers like `Utils`, `LogHelper`, plus app-side `Ui`/`Theme`/`NavigationService`/`BackupRestoreOrchestrator`), `Conf` (all backup modules and `ModuleCatalog`), `Views`, `DataHelper`. Match the existing namespace of the folder you're working in.

They also **straddle the two assemblies** since the Core extraction - `WinRestoreKit` and `DataHelper` each have types in both `WinRestoreKit.Core.dll` and `WinRestoreKit.dll` (e.g. `Utils` in Core, `RunSummary` in the app, both in namespace `WinRestoreKit`). That is legal and deliberate: renaming namespaces to match the split would have made a rename-only refactor into a whole-tree edit. It is also why the `InternalsVisibleTo` pair is mandatory rather than a convenience.

## Project automation (`.claude/`)

- **Hooks** (`.claude/settings.json` + `.claude/hooks/*.ps1`): edits to `bin/`/`obj/` are blocked (generated build artifacts), and every `.cs` edit triggers a `dotnet build src\WinRestoreKit.sln` compile check. The build check builds in place (safe now that `bin`/`obj` are gitignored) and exits 0 silently when the dotnet SDK isn't present, so it never produces false failures on a machine without the toolchain.
- **Skills**: use `new-backup-module` when adding a `Conf/` module (it covers the registration points); `/release` (user-invoked only) walks the version-bump/tag/release flow, including the AssemblyInfo format constraints the update checker depends on.
- **Subagent**: run `windows-safety-reviewer` after changing `Utils`, `Conf/` modules, or restore logic - it audits destructive operations (silent registry imports, process kills, profile overwrites) and silent-failure handling.
