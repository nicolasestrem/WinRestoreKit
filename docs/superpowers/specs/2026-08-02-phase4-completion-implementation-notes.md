# Phase 4 completion — implementation notes (PRs 5–9 + the modernization items)

> This document predates the rename of Appcopier to WinRestoreKit and is kept as a
> historical record. Product names, namespaces and paths below refer to the project as
> it was at the time of writing.

Written 2026-08-02, immediately after the work landed, for whoever picks this up next. The design is in
[`2026-07-21-phase4-ui-revamp-design.md`](2026-07-21-phase4-ui-revamp-design.md) and is still the
authority on *intent*. This document records what actually happened when it was built: the decisions the
design deferred, the two places the implementation departed from the plan, how each piece was verified,
and — the part that matters most six months from now — **what was not verified and why**.

Read the "Traps" section before changing any of this.

## What shipped

Seven workstreams, one commit each, each leaving the app building and the suite green:

| Commit | What |
|---|---|
| `7c064ee` | Orchestrator extracted from the view behind `IRunUi` (design PR 5) |
| `435a9a2` | `ModuleCatalog`, `RunResultsPanel`, Backup page rebuilt (design PR 6, first half) |
| `9f094ed` | Backup presets (design PR 6, second half) |
| `1865664` | Three-step Restore wizard; `ConfPageView` → `BackupPageView` (design PR 7) |
| `8bd8116` | History timeline; `RestPageView` deleted (design PR 8) |
| `35e79e2` | Update check on the GitHub Releases API; `WebClient` retired (roadmap items) |
| `d5019c9` | `Theme`, dark mode, PerMonitorV2 DPI (design PR 9) |

The phase is complete: every screen in the Path D target UX exists, the summary `MessageBox` is gone,
the browse-time warning modal is gone, rollback is visible in the UI for the first time since it was
built in Phase 2b, and the build has **no warnings left at all** (both `SYSLIB0014` and the `WFAC010`
DPI warning are gone).

## The shape of the change

Before, `ConfPageView` was the application: it owned the module tree, the backup run, the restore run,
consent, the snapshot gate, the results dialog and the activity log. `RestPageView` picked a folder and
called back into it. That is why the engine's honesty work was invisible — one `UserControl` held every
decision and the test suite could not instantiate it.

After, the layering is:

```
MainForm (shell: rail + content host)
  └── NavigationService            Show / Push / Pop, IRefreshableView on the way in
        ├── HomePageView           "am I okay?"
        ├── BackupPageView         presets + tree, owns a BackupRestoreOrchestrator
        ├── RestoreWizardStep1View pick a backup
        ├── RestoreWizardStep2View pick its contents, owns a BackupRestoreOrchestrator
        ├── HistoryPageView        backups + undo points, one timeline
        └── AboutPageView

BackupRestoreOrchestrator ──IRunUi──▶ whichever view is running it
        │
        └── Appcopier.Core: RestoreScope, RestorePlan, SnapshotGate, RestoreDispatch,
                            BackupLog, RestoreLog, BackupManifest, ModuleCatalog,
                            RestoreContents
```

`IRunUi` is the whole trick. It has seven members and they are all *presentation*: progress text, a
dialog owner, render-a-summary, ask-for-consent, confirm-snapshot-override, report-plan-composition-
failure, show-the-Explorer-restart-row. The orchestrator cannot reach a control, and a view cannot reach
a restore except by handing itself to an orchestrator. Two views implement it identically, which is the
point — the wizard runs a restore with exactly the same consent path the backup page used to.

## Decisions the design left open

**The orchestrator is app-side, not Core.** The design explicitly said "PR 5 must state which, rather
than discovering it". It stays app-side behind `IRunUi`, because `RunSummary` exposes a
`MessageBoxIcon` and therefore stays app-side with its test. Moving `RunSummary` to Core would have
forced `summary.Icon` into a method call at three call sites including a test, editing a test to achieve
nothing. App-side types are still testable here: `Appcopier.Tests` references the app assembly and
`AssemblyInfo.cs` carries the `InternalsVisibleTo`.

**`ConfPageView` was rebuilt in place, then renamed.** The design offered a fallback of building
`BackupPageView` as a new file alongside. It was not needed. The page was rebuilt in place in PR 6 while
still carrying its restore role, and renamed in PR 7 once that role was deleted — so the rename diff is
a rename and nothing else. Git recorded it as a rename (`R` in `git status`), which keeps the history
readable.

**Wizard step 2 defaults to every present module ticked.** The design flagged this as flippable. It was
left as restore-what's-in-it. Modules the folder holds nothing for are not merely unticked — they are
**disabled and greyed**, so the "nothing was backed up for this item" surprise is now impossible to walk
into rather than merely discouraged.

**Presets kept the design's proposed membership** — `DeveloperMachine` is Terminal/VSCode/SSH/env/hosts
(plain env, not the filtered variant: a developer restoring their machine wants `PATH`), and
`MinimalPrivacySafeExclusions` is `WUpdates`, both env variants and `CWiFiConf`. Both lists are pinned
as literals in `BackupPresetsTests`, which also asserts every name resolves to a registered module — so
renaming a module without updating its preset turns red instead of silently shrinking the selection.

## Two departures from the plan, both deliberate

**1. One branch, not seven.** The plan called for a branch and PR per workstream. The workstreams are a
dependency stack — B needs A's `IRunUi`, C needs B's `RunResultsPanel`, D deletes what C made read-only,
E needs every layout container from B–D — so seven branches cut from `main` would have been a merge
stack pretending to be parallel work. They are seven commits on one branch instead, each green, each
reviewable on its own. Nothing about the content changed.

**2. The baseline test count in the plan was stale.** The plan said 706. `main` was actually at **749**
when this started; the suite finished at **778**. The +29 are `ModuleCatalogTests` (4),
`BackupPresetsTests` (3), `RestoreContentsTests` (11) and `ReleaseTagParsingTests` (11). No existing test
was modified except one line in `OsVersionTests` that names `ConfPageView.IntroTemplate` and had to
follow the rename. `RestoreDeclarationTests`' `Assert.Equal(29, modules.Count)` was not touched, relaxed
or recomputed, as instructed.

## How each piece was verified

Automated, every workstream: `dotnet build` + `dotnet test`, Debug and Release.

**The two destructive diffs got a safety review.** PR 5 and PR 7 are the only ones that touch code which
imports a `.reg`, kills a process or overwrites a profile.

- **PR 5** came back clean with two informational notes. The reviewer verified move-fidelity by
  extracting the pre-move file from `git show HEAD:` and hand-diffing every moved method — stronger
  evidence than a clean compile, and it confirmed all the load-bearing comments travelled.
- **PR 7** came back with **zero findings** across twelve checks, including that the orchestrator was
  byte-identical to the previous commit, that the trailing-separator path contract holds, and that
  consent remains the first side-effecting stage in `RunRestoreCore`.

**Runtime verification was done by launching the real app**, not by trusting the compiler. WinForms
layout errors are runtime errors; a `TableLayoutPanel` with a bad row index throws when it lays out, not
when it builds. Each UI workstream was launched and observed. Two techniques are worth recording because
they will be useful again:

- **Pixel sampling proves a palette is actually painted.** After the theme work, the app was launched
  under the OS dark setting and the centre pixel measured `32,32,32`; forced light, it measured
  `243,243,243`. Those are exactly `Theme.Dark.Surface` and `Theme.Light.Surface`. That is a real
  assertion about rendering, not a claim that the code compiled.
- **Last-access time proves `DataRootDir` resolved correctly under single-file publish.** A synthetic
  backup was planted in `publish-verify\app\`, the published exe was started, and the manifest's
  last-*access* timestamp jumped to the moment of startup — six seconds after it was written. That
  proves the single-file artifact read the folder next to the exe rather than a temp extraction
  directory, which is the regression this repo has a long comment about and which no unit test can
  reach.

**The release artifact was built and run.** One `Appcopier.exe`, 72,040,495 bytes (~68.7 MB), started and
painted correctly. The release contract holds.

## What was NOT verified — read this before shipping

**No elevated end-to-end run happened.** Not one real backup, not one real restore, not one pass through
the consent dialog against live data. The app declares `requestedExecutionLevel="highestAvailable"`, so
launching it non-interactively fails with `ERROR_ELEVATION_REQUIRED`, and the UAC prompt lives on the
secure desktop where it cannot be automated. Every runtime observation above was made against a build
temporarily manifested as `asInvoker`; **the manifest was reverted every time and is `highestAvailable`
in every commit** (verify with `git diff main -- src/Appcopier/app.manifest`, which is empty apart from
the intentional `dpiAware` removal).

So the following still need a human in an elevated session, and they are exactly the checks the design
document asks for:

1. A real backup from "Minimal privacy-safe" — confirm `WUpdates`, both `EEnvironment*` and `CWiFiConf`
   are genuinely not ticked, that results render in-page with correct chips, and that
   `backup_manifest.json` describes the run.
2. A real restore through the wizard — step 2 chips against a real backup, the consent dialog opening
   modal with **Cancel focused and every box unticked**, the snapshot-override prompt defaulting to
   **No**, and `restore_log.txt` landing in the snapshot folder.
3. The undo path — History showing the new `(pre-restore)` row, and "Undo this restore" reopening the
   wizard on that snapshot.
4. **The DPI matrix: 100% / 150% / 200%, plus dragging the window between two monitors at different
   scales.** This is the single largest untested surface in the phase. The containers are in place and
   the flip is correct in principle, but "no absolute positions remain" is a structural claim, not a
   visual one.
5. A live OS light↔dark toggle while the app is open.

**Also unverified: the two converted dialogs were never opened.** `RestoreConfirmForm` and
`RestAppsForm` were rewritten from absolute positioning to `TableLayoutPanel`, and both compile, but
neither was displayed — they open only in response to a click deep in a flow that needs elevation.
`RestoreConfirmForm` is the consent dialog, so its *semantics* were re-checked by reading (`AcceptButton`
and `CancelButton` are both `btnCancel`, `ActiveControl = btnCancel` is still assigned after `SetStyle`,
boxes are still created unchecked) but its *layout* has not been seen by human or machine. Open it once
before trusting it.

Finally: the image-inspection tooling used for the earlier screenshots stopped being available partway
through the History work. From that point verification was launch-and-probe — process alive, main window
present, `Responding: True`, pixel sampling — which proves a screen constructs and paints without
throwing but says nothing about whether it *looks* right.

## Traps

**Do not add a `MessageBox` back to the results path.** `IRunUi.ShowSummary` logs the headline and detail
and then hands the outcomes to a `RunResultsPanel`. The whole point is that a 24-succeeded/1-failed run
must not read as broadly green. The two surviving `MessageBox` calls on the backup page are *input
validation* ("nothing has been selected"), not results, and that distinction is the rule.

**Skipped is amber in both palettes and must stay that way.** It is enforced in three places now
(`Ui.Caution`, `Theme.Light`, `Theme.Dark`) and commented in all of them. Green means "I got your data";
amber means "there was nothing to get". The engine fought for that distinction in Phase 2a and the
styling is not allowed to give it back.

**The theme walker will flatten anything you do not opt out.** `Theme.Apply` walks the control tree and
paints by control kind, so a `Label` you gave a background colour becomes transparent on the next theme
change. Chips, backup cards and the primary action button opt out by being `AccentLabel`,
`AccentButton` or `AccentPanel`. These are marker *types* rather than a registry of instances on
purpose: chips are rebuilt on every render, so a `HashSet<Control>` of opted-out controls would grow
without bound and keep disposed controls alive.

**`Theme.Use` must run before any view is constructed.** Views read `Ui.*` — which forwards to
`Theme.Current` — as they build. `MainForm` calls `Theme.Use(Theme.IsDarkOs())` immediately after
`InitializeComponent()` and before the first `new SomePageView()`. Move it later and the app opens light
on a dark machine until something forces a re-apply.

**`ApplyTheme` walks each page explicitly, and that is not redundant.** Only the page currently in
`pnlForm` is part of the form's control tree; the others are held in fields and re-inserted on
navigation. A walk of `this` alone would leave four screens in the previous palette.

**The `SystemEvents` subscription is static and will leak the form.** `MainForm` unsubscribes in
`FormClosed`. If you add another subscriber, do the same.

**Never set the DPI mode anywhere else.** It is set in exactly one place —
`Application.SetHighDpiMode(HighDpiMode.PerMonitorV2)` as the *first* `Application` call in
`Program.Main`. The `<dpiAware>` element was deliberately removed from `app.manifest` because a manifest
setting is authoritative and silently overrides the runtime call. The csproj carries a comment saying
the same thing about `ApplicationHighDpiMode` and friends. Two sources of truth here produce a bug that
only reproduces on someone else's monitor.

**The manifest is written from `running`, never from `selection` — and that was a bug fix, not a
preference.** `RunBackupCore` used to call `WriteBackupManifest` twice, the second time with the
caller's mutable list. Workstream A carried it over verbatim because A was a move, not a rewrite; it
was removed immediately afterwards in its own commit. Two reasons it had to go. The second write was a
redundant atomic temp+move of identical content, and — the real problem — it read `selection` *after*
the awaits, which is the exact hazard the `running` snapshot two lines above exists to eliminate. If
the two ever diverged, the manifest would describe a different module set than the folder holds and a
different one than the `RunSummary` reports, since that pairs against `running`. The manifest is the
artifact Home and History are told to trust, so it has to agree with what actually ran.

This was flagged by review on PR #14 at 22:37:01Z with the fix spelled out ("remove lines 228–229").
**PR #14 merged at 22:39:31Z, twenty seconds after the inline comment**, so the finding shipped. Worth
knowing as a process fact, not just a code one: the review bots on this repo post their last pass
within a few minutes of the final push, and two of those passes landed at or after the merge.

**Adding a module is still two places, but one of them moved.** It is now
`ModuleCatalog.CreateAll()` in Core, not `ConfPageView.InitializeConfigurations`. `ModuleCatalogTests`
cross-checks the catalog against the concrete `BackupBase` subclasses in the assembly, so forgetting it
fails by name. Then ask whether the module belongs in a `BackupPresets` list — the skill's checklist now
asks this.

**`HasBackupIn` and `HasArtifactIn` ask different questions, and swapping them is a live hazard.**
Both take a backup folder and return whether there is something in it, which is exactly why the first
version of the restore wizard used the wrong one.

- `HasBackupIn` decides **whether to close an application**. It fails open — the base default is
  `true`, and `FolderModule`/`FileModule` return `true` without probing whenever nothing needs
  killing — because a false negative there cancels a restore the user asked for.
- `HasArtifactIn` decides **what the wizard offers**. It is `bool?`: true, false, or null for
  "cannot tell". Nothing may guess on a module's behalf.

Reading the fail-open seam as a content check meant all thirty modules answered yes, so a backup
holding one module's files rendered thirty enabled, pre-ticked rows and the run snapshotted and
attempted every one. The greying users saw was decorative.

Two rules follow, and both are enforced by tests rather than by memory:

1. **Every module must give a definite answer.** `ArtifactProbeTests.EveryCatalogModuleGivesADefiniteAnswer`
   sweeps the whole catalog and fails by name on a null. A module that genuinely cannot look must
   override to a literal and say why — `AppStoreApps` is the only one, because its restore opens a
   dialog with its own folder picker, so the folder being examined is not the one it reads.
2. **A probe must spell artifact names the way the writer does.** A probe that looks for
   `Themes_Themes\` while the backup wrote `Themes-Themes\` reports "(nothing in this backup)" over a
   full folder *and disables the checkbox*, which is worse than the bug it replaced. This is why
   `BackupFolderNameFor` now exists beside `RegFileNameFor`: three modules were each composing that
   name inline and the probe would have been the fifth copy.

The rule the wizard applies: the manifest decides for every module it names, and the probe decides
for the rest. Manifest-silent plus a null probe means "not part of that run" — which is why rule 1
matters. Get it wrong and a module vanishes from the wizard on any second backup in one app session,
because that reuses the folder and rewrites the manifest to describe only the latest run.

## Known rough edges

The cosmetic ones below are not blocking and were left alone as outside what the plan asked for. The
first item is different in kind: it is a reviewed P1 that shipped, and it is still live.

### Inherited and still open — a reviewed P1 from PR #13 that shipped

**A reused backup folder keeps artifacts from the previous run, and the manifest does not mention
them.** `CurrentBackupPath` is built from `Data.NowShort`, which is stamped once per process, so a
second Backup click in the same session writes into the *same* folder. `InvalidateBackupManifest`
clears the stale manifest — that part was fixed during PR #13 — but nothing clears the stale
**module artifacts**. So if the first run backed up ten modules and the second backs up three, the
folder still holds all ten sets of files while the manifest honestly describes only three. A restore
from that folder probes for artifacts, finds the seven the manifest never mentions, and applies them.

Codex raised this as a P1 on PR #13 at 22:39:37Z. **The PR merged at that same timestamp**, so nobody
saw it. It is unrelated to Phase 4's UI work and is untouched by it — recorded here only so it stops
being invisible.

It was deliberately **not** fixed in this phase, because both candidate fixes are design decisions
rather than repairs: giving every run a fresh directory changes where backups land and interacts with
the once-per-process stamping the manifest-invalidation comment reasons about at length, and clearing
each module's prior artifacts means teaching the app to delete backup data, which deserves its own
review. Pick one deliberately; do not let it be decided by whoever touches `RunBackupCore` next.

**Partly contained since, from an unrelated direction.** The PR #15 review rewrote how the restore
wizard decides what a folder holds (see below), and the rule it landed on is that the manifest wins
for every module it names. A module the manifest calls `skipped` is therefore not offered *even when
stale files for it are sitting in the folder*, which is this exact scenario. That narrows the blast
radius to the wizard only — it is not a fix. The backup folder still accumulates, `RestoreScope` still
probes the filesystem, and anything that restores without going through step 2 is unchanged.

### Cosmetic

- **Warning text in wizard step 2 can clip.** The inline warning under a row is a fixed-width read-only
  `TextBox`; on a narrow window the text runs past the visible area. It is still selectable and the
  consent dialog repeats every warning in full, so nothing is *lost* — it just looks cut off. Making it
  wrap to the row width is the fix. Much rarer since the PR #15 review: warnings now render only for
  rows that can actually run, so a typical backup shows one or two instead of a dozen.
- **`RunResultsPanel` re-renders on every resize.** Multiline `TextBox` height cannot be derived from
  content, so reason heights are measured with `TextRenderer.MeasureText` and the rows are rebuilt when
  the panel resizes. Correct, slightly wasteful, and only noticeable if someone drags the window while
  reading results.
- **The Explorer-restart row persists across runs.** `IRunUi.SetExplorerRestartVisible` is only called
  by the restore path, so a backup run immediately after a restore that needed a restart still shows the
  row. This is the pre-existing behaviour of the button it replaced, faithfully preserved. Resetting it
  at the start of each run would be a one-line improvement.
- **Step 1 cards show the folder name and the created date, which for a normal backup are both dates.**
  It reads slightly redundantly. They are genuinely different things — the folder name is what the run
  was called, the date is when the manifest says it finished, and they diverge for a backup copied from
  another machine — but the card could say so more clearly.

## One behavioural note about the update check

The Releases API is now the primary source and `AssemblyInfo.cs` the fallback. At the time of writing,
the repo's newest **published release** is `0.30.0` while `AssemblyInfo` on `main` says `0.31.0` — the
version bump landed without a matching GitHub release.

For released users this is strictly better: someone running 0.30.0 is now correctly told "No new updates
available", where previously they would have been offered 0.31.0, a version with nothing to download.
The trade is that anyone running an unreleased build from `main` will be *offered* 0.30.0, because the
comparison is equality (`latest == current`), not "is newer than". That comparison was kept deliberately
— the design says all five message texts and the comparison tail survive verbatim, and both sides still
go through `Program.NormalizeVersion`, which is the thing that actually prevents phantom updates.

If that ever becomes a real complaint, the fix is a version comparison rather than an equality check, and
it should be its own change with its own tests. **Publishing a GitHub release for each version bump makes
the question moot**, which is what the `/release` skill already walks you through.
