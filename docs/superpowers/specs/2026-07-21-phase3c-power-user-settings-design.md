# Phase 3c — power-user settings

Branch `feat/phase3c-power-user-settings`, 2026-07-21. Planned with a multi-agent exploration pass and
implemented by four parallel agents under strict file ownership, with the shared registration and test
rosters kept by the lead so no two agents could disagree about a count.

> This document predates the rename of Appcopier to WinRestoreKit and is kept as a
> historical record. Product names, namespaces and paths below refer to the project as
> it was at the time of writing.

Scope came from `docs/ROADMAP.md` and was not widened: four new modules in the existing "Settings"
category, two retargets, and one strengthened warning.

## Goal

Cover the state a power user would actually miss and that the previous 25 modules did not touch —
power plans, per-user fonts, mapped network drives, regional and input settings — and finish the two
retargets Phase 3a foreshadowed, disclosing their consequences for existing backups rather than
papering over them.

Two decisions were taken by the user before implementation, 2026-07-21:

- **`WUpdates` drops the WSUS-era `\AU` policy key** rather than demoting it.
- **`WPowerPlans` restore is activate-only**; `.pow` files are exported but never imported.

## Measurement pass (before any code)

The project's recorded lesson is *execute, don't reason, about formats* — six defects in six rounds of
Phase 3b, none of them found by reading. So every fact the module designs rest on was measured on
Windows 11 Pro 10.0.26200 on 2026-07-21 before the first line was written. What the measurements
changed is recorded beside each one.

| # | Measured | Result | Effect on the design |
| --- | --- | --- | --- |
| M1 | `powercfg /list`, `/getactivescheme` | Three schemes; active marked by a trailing `*`; labels are localizable prose | Parser extracts GUIDs by regex and reads the `*`; scheme names are captured for display only, never for logic |
| M2 | DeliveryOptimization keys | `...\CurrentVersion\DeliveryOptimization`, `...\Config` and `Policies\...\DeliveryOptimization` all **absent**; a registry-wide search for `DODownloadMode` found **0 matches** | The key is absent on a stock machine, so `AbsenceIsNormal` is `true` |
| M2b | `C:\Windows\PolicyDefinitions\DeliveryOptimization.admx` | The only key the policy definition declares is `SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization`, value `DODownloadMode` | Settled the key path from the authoritative source instead of from memory |
| M3 | Per-user fonts | `HKCU\...\Windows NT\CurrentVersion\Fonts` **exists with 0 values**; `%LOCALAPPDATA%\Microsoft\Windows\Fonts` **absent** | Key absence is *not* normal (it exists in every profile); folder absence *is* normal |
| M4 | Taskbar pins | `...\Explorer\Taskband` present (`Favorites`, `FavoritesResolve` REG_BINARY + three DWORDs); `%APPDATA%\...\User Pinned\TaskBar` present with 32 `.lnk` files | Both targets are real and worth capturing; absence flags set from this |
| M5 | `Utils.RestartExplorer` | → `CloseProcess("explorer")` → `process.Kill()`. Explorer is **killed, not closed gracefully** | Restore-then-restart is safe (a killed Explorer never flushes). The residual hazard is a sign-out without restarting, which is disclosed rather than engineered around — see below |
| M6 | `HKCU\Control Panel\International` | 40 values; subkeys `Geo`, `LanguageComponentsAvailable`, `User Profile`, `User Profile System Backup` | One key captures all of it; the subkeys are deliberately not listed separately |
| M7 | `HKLM\...\CurrentVersion\WindowsUpdate` | Present, and carries `SusClientId`, `SusClientIdValidation`, `TraceId` alongside ten subkeys | New disclosure: the export carries this PC's Windows Update identity |
| M8 | `HKLM\Software\Policies\...\WindowsUpdate\AU` | **Absent** | Confirms the key was chronically skipped, supporting the decision to drop it |

Two of these changed the plan rather than confirming it. **M2** turned "add DeliveryOptimization config"
into a specific, authoritative key with `AbsenceIsNormal = true`, after the plan's first-guess paths were
measured absent and a registry-wide value search came back empty — the module would otherwise have
exported a key that does not exist while reporting a normal skip, which is the silently-wrong direction.
**M7** was not in the plan at all: the key `WUpdates` has always captured turns out to carry machine-unique
Windows Update identifiers, which is a cross-machine restore hazard nobody had written down.

## What landed

No new shared infrastructure. No new base class, no new `RestoreTarget` kind, no `Utils` change. Every
module sits on an existing base or an existing hand-rolled precedent, which was the goal — Phase 3a's
recorded mistake was building a `CommandModule` base that fit one of its three intended consumers.

| Module | Shape | Captures | Absence normal? |
| --- | --- | --- | --- |
| `WPowerPlans` | hand-rolled command module (`WNetworkConf` precedent) | one `.pow` per scheme + a JSON manifest naming the active one | n/a — a machine always has schemes |
| `WFonts` | hybrid (`WThemes` precedent) | `%LOCALAPPDATA%\Microsoft\Windows\Fonts` + `HKCU\...\Windows NT\CurrentVersion\Fonts` | folder yes, key **no** |
| `WMappedDrives` | `RegistryModule` | `HKCU\Network` | yes |
| `WRegional` | `MultiKeyRegistryModule` | `HKCU\Control Panel\International`, `HKCU\Keyboard Layout` | no, both |
| `WTaskbar` | `RegistryModule` → hybrid | + `...\Explorer\Taskband`, + the pinned-shortcuts folder | keys no, folder yes |
| `WUpdates` | `MultiKeyRegistryModule` | − `\AU`, + the DeliveryOptimization policy key | parent no, DO yes |

### `WPowerPlans` — capturing stdout

`ProcessOutcome` carries no stdout and `RunToolAsync` lands captured output on disk **only on exit code
0**, so reading powercfg's listing means giving it a file and reading it back. The scratch file goes to
`%TEMP%`, not the backup folder: the restore path also needs a capture (the `/getactivescheme`
read-back) and must not write into the backup it is restoring from, and the backup folder is enumerated
by other modules and by `RestPageView`, so a scratch file there is a stray artifact somebody has to
reason about later.

The consequence is a three-way distinction the caller has to keep: the tool failed, the tool worked but
its output could not be read back, and the tool worked and said nothing we understood. All three are
`Failed`, with different reasons — in particular **zero schemes parsed from a successful run is a
failure, not an empty result.** Windows always has at least one power scheme, so powercfg cannot
honestly report none; finding no GUIDs means the output was not understood, and "I could not tell" is a
tool failure rather than an absence.

Identity is the GUID and nothing else. powercfg's labels are localized, so a parser keyed off
`"Power Scheme GUID:"` works on the machine it was written on and silently finds zero plans everywhere
else — the same defect class as `CWiFiConf`'s `WLAN*.xml` glob, which matched 0 of 19 real exports.
Scheme names are captured for display only and no decision reads them. The active marker is the
trailing `*`, checked on the trimmed line's last character rather than by searching for `*` anywhere,
because a user-created plan may legitimately have one in its name — and the manifest asks
`/getactivescheme` directly anyway, because that command's entire job is the one answer restore acts on.

### `WTaskbar` — why Explorer is not closed before the restore

`RestartExplorer` → `CloseProcess` → `Process.Kill()`. Explorer is killed, never asked to exit, and a
killed Explorer does not flush its in-memory pin list back to `Taskband`. That is what makes
restore-then-Restart-Explorer safe, and it was measured (M5) rather than assumed.

Closing it *before* the restore would not be safe. AutoRestartShell relaunches the shell within about a
second, so a pre-restore kill would very likely have a fresh Explorer running — holding the **old**
`Taskband` it just read — before the import finished. That is the same overwrite hazard plus a blanked
desktop while the restore runs. The residual case, a user who restores and then signs out without
restarting, is disclosed in `WarningMessage` rather than engineered around with a kill that makes it
worse.

### Orphaned filenames

Two retargets, and only one orphans anything. The asymmetry is the point:

- **`WTaskbar` orphans nothing.** The Advanced key keeps `Taskbar.reg` via a `RegFileNameFor` override
  matching the key case-insensitively — the `WThemes` pattern, with the key declared once as a `const`
  used by both the `Keys` list and the override so the two cannot drift. Existing backups restore the
  taskbar settings exactly as before and report "nothing was backed up" for the two new targets.
- **`WUpdates` orphans one file**, deliberately:
  `Windows Update_HKEY_LOCAL_MACHINE_Software_Policies_Microsoft_Windows_WindowsUpdate_AU.reg`
  (verified against the pre-3c key spelling in `git show main:` and the `GetSafeFileName` transform,
  not taken from the comment that claims it). It is *worse*-reported than WTelemetry's orphan, not
  better: because the key left `Keys` entirely, the restore does not even emit a skipped row for it —
  the file is simply never looked at. A filename fallback would also be the wrong tool here for the
  reason `BackupBase` records: the file's contents name `\AU`, so applying it writes `\AU` whichever key
  it is handed, and nothing in this module's `Backup` reads `\AU` any more — that write would land
  outside the pre-restore snapshot while `SnapshotGate` still called the restore undoable.

## Snapshot coverage

CLAUDE.md's invariant is that anything a restore writes must be inside the pre-restore snapshot, and the
snapshot is taken by running the module's own `Backup`. Each module is checked against that here rather
than assumed.

- **`WMappedDrives`, `WRegional`, `WUpdates`** — restore imports exactly the keys `Backup` exports.
  Structural closure; nothing to argue.
- **`WFonts`, `WTaskbar`** — restore writes the folders and keys `Backup` reads, in the same lists.
  Structural closure. The `CopyFolder` merge limitation (a restore cannot remove a file the backup did
  not contain) is the standing Phase 2b caveat the confirmation dialog already discloses, not a new one.
- **`WPowerPlans`** — argued explicitly, because the roadmap flagged it. `Backup` records the currently
  active scheme in its manifest; restore's *only* write is the active-scheme selection; so everything
  restore changes is inside the snapshot, and restoring the snapshot re-activates the prior plan. That
  is full closure, and it is full closure **because** import was rejected: `powercfg /import` creates
  GUID-keyed scheme objects this app has no mechanism to delete, so a snapshot could not undo them while
  `SnapshotGate` would still report the restore as undoable. That is the same asymmetry that pushed the
  `WTelemetry` legacy-filename fallback out of Phase 2c — a caveat inside a step reason does not correct
  a verdict the user reads as "this can be undone".

  The `%TEMP%` scratch file `CaptureAsync` writes is outside the snapshot and deliberately so: it is
  not machine state the restore is changing, it is removed in a `finally`, and putting it in the backup
  folder would mean the restore path writing into the backup it is reading from.

## Verification

`dotnet build src\Appcopier.sln` clean, 0 warnings. `dotnet test src\Appcopier.sln`:

```
Passed!  - Failed:     0, Passed:   702, Skipped:     0, Total:   702, Duration: 174 ms
```

627 → 688 for the implementation, then 688 → 700 for the review fixes. The new files are
`PowerPlansTests`, `PowerUserModuleTests`, `TaskbarRetargetTests`, `UpdatesRetargetTests` and
`ExplorerRestartPromptTests`, plus `TheTaskbarFileNameIsKeptForTheKeyThatAlreadyUsesIt` in
`BackupFileNamingTests` and the roster updates.

Hand-kept rosters updated: module count 25 → 29; `MultiKeyModules_DeclareOneTargetPerKeyInOrder` gains
`WRegional`; `CommandModules_DescribeWhatRunsInPlainLanguage` gains `WPowerPlans`;
`ModuleShapeTests.EveryRegisteredModule_HasATitle` gains all four. The RegistryModule-subclass count
stayed at **11** and is the one worth calling out: `WTaskbar` left the family and `WMappedDrives` joined
it, so the assertion was unchanged while its membership moved. Two literal assertions were added beside
it (`DoesNotContain … is WTaskbar`, `Contains … is WMappedDrives`) so the next net-zero move cannot pass
silently — the same "generic test agrees with whatever the fields say" trap `ModuleTargetTests` documents.

The four new modules and both retargets enter `BackupFileNamingTests`' automatic sweeps for free, because
those discover any module holding a public `Keys` field or a protected `Key` property. That is how the
Taskbar hybrid is checked against the collision defect the file was written for, without anyone
remembering to add it.

### Measured out-of-band (no test can cover these unelevated)

- Every new registry target exports non-empty with a valid header via `reg export`, unelevated:
  `HKCU\Network` 2,840 B; `Control Panel\International` 26,516 B; `Keyboard Layout` 702 B; the HKCU
  fonts key 1,582 B; `Taskband` **468,944 B**; `Explorer\Advanced` 2,726 B; the WindowsUpdate parent
  50,124 B.
- `powercfg /setactive` **succeeds unelevated** (exit 0, verified by read-back), while
  `powercfg /export` **requires elevation**. The app runs elevated, so both work in production — but
  the asymmetry means the restore path is the one that works in the weaker environment, which is the
  right way round for a safety-critical operation.

## What the review changed

Two reviewers ran over the branch: the mandated `windows-safety-reviewer` and a silent-failure hunter.

**A correction to the record first, because it is the kind of error that compounds.** An earlier draft
of this section credited the reviewers with finding the zero-byte `.pow` defect. They did not — the
implementing agent found it, measured it, and fixed it inside the original commit, and both reviewers
then read code that already contained the fix and its measurement comment. The lead's own independent
measurement of `powercfg /export` is a genuine second observation; the reviewers agreeing with a
comment they had just read is not a third. Recorded because "found independently by N reviewers" is a
claim about evidence strength, and inflating it is exactly the kind of unearned confidence this
project's rules exist to prevent.

### The Restart Explorer button, hidden by a partly-failed restore — the serious one

`ConfPageView` gated the button on `result.State == ResultState.Succeeded`. Correct while every module
declaring `RequiresExplorerRestart` wrote exactly one thing: `Failed` then really did mean nothing was
written, and offering a restart would have been a no-op dressed up as a fix. `ModuleResult.Aggregate`
lets any one failed step dominate, so once those modules became multi-step the premise was gone.

The failure: restore a backup whose `Taskbar.reg` is unreadable. The pinned-shortcuts folder copies
fine, `Taskband` imports fine — the pin list is live on disk — and the third step fails, so the module
reports `Failed` and the button vanishes. The user has just read a `WarningMessage` telling them to
press that button before signing out. They sign out, the running Explorer flushes its in-memory pin
list over the restored `Taskband`, and 32 pins are gone. **The bug removed the control that makes a
restore stick, in precisely the case where the user had been told to use it.**

`WThemes` has been a folder-plus-two-keys hybrid since 2c and declares the same flag, so it carried
this too. The rule is now "did this module write anything" — any step `Succeeded`, which includes
`Applied` — and it moved into `ExplorerRestartPrompt` alongside `SnapshotGate` and `RestoreDispatch`,
for the same reason those exist: while the decision lived inline in a `UserControl` the suite cannot
instantiate, nothing could pin it. `ExplorerRestartPromptTests` now does, including the negative
direction the old gate got right (nothing written ⇒ no button).

### Taskbar pins do not survive a different account, and nothing said so

The reviewer dumped the live `Favorites` blob this module now captures — 29,531 bytes — and found it is
a shell ItemID list carrying account-bearing absolute paths
(`AppData\Roaming\…\Quick Launch\TaskBar\<App>.lnk`) plus the profile display name. Restore onto a
differently-named account or a rebuilt profile and the folder copy genuinely copies 32 files, both
imports genuinely apply, `Aggregate` returns `Succeeded` — while Explorer cannot resolve the blob,
prunes the pins, and the taskbar comes back empty.

What makes it a defect rather than a limitation is that **the same commit disclosed this identical
hazard twice and skipped it only here**: `WFonts` ("this app cannot detect that - the row still
reports success") and `APinnedApps` both carry it. Now in `WarningMessage` and `Info`, pinned.

### `WUpdates` named the risky case as the safe one

The first draft closed with "restoring it onto the same PC it came from is what this item is for."
That is reassurance for the sequence this app exists to serve — back up, reinstall Windows, restore —
and it is wrong for it: a reinstalled Windows issues a **new** `SusClientId`, so restoring the old one
re-points the fresh install at an identity already registered with Windows Update. Same physical
machine, same confusion the warning describes for a different PC. The warning now names the reinstall
case and says plainly that nothing here is needed to make Windows Update work on a fresh install. Both
halves are pinned, including a negative assertion against the removed sentence.

### A comment promising a cross-check that does not exist

Both reviewers caught this independently, and it is the one place they genuinely converged.
`WriteManifestAsync`'s comment said the listing's `*` marker is parsed too, "so the stronger source
wins, and disagreeing with it is not something to paper over" — which reads as a promise that the two
sources are compared. `PowerSchemeEntry.IsActive` is read by nothing in production; a disagreement is
parsed and silently discarded. The comment now states exactly what happens, including that no
cross-check exists and where it would go. That is what stops the next author trusting a guarantee
nobody implemented.

### The `ValidateExportArtifact` branch

Both reviewers independently reached the conclusion the lead had already reached and fixed: a real
gap, and the `Utils.ExportRegistryKey` precedent is **not** wrong. The asymmetry is justified by the
downstream, not by inconsistency — a bad `.reg` left on disk is re-validated by `ImportRegistryKey`'s
pre-flight before regedit ever sees it, so that path fails closed. A bad `.pow` has no downstream at
all: restore never reads one, so its only consumer is a human following this module's own instruction
to import it by hand. It now routes through `AbandonIfFailed`, pinned in both directions (failed ⇒
removed, succeeded ⇒ kept — a cleanup rule that deleted unconditionally would pass every failure test
and destroy every backup).

### Verdicts recorded as clean

Negative results are results, and the phase's own lesson is that a count which does not move can still
hide a change. Both reviewers cleared, against the code rather than the comments: the snapshot-closure
argument for `WPowerPlans` (no path found where `/setactive` succeeds and the snapshot cannot undo it;
the `%TEMP%` scratch file does not break closure — not user state, GUID-named, deleted in a `finally`,
and only written on exit 0); the decision not to close Explorer before a taskbar restore (the `Kill`
claim verified at `WindowsHelper.cs:768`); the orphaned `\AU` filename, computed rather than reasoned
about and matching character for character including the old key's mixed-case `Software`; restore
ordering in both hybrids, folders-then-keys in `RestoreTargets` and `RestoreAsync` alike, read at
access time so the dialog cannot describe a set the restore does not write; every `AbsenceIsNormal`
flag against live measurement; stdout encoding (UTF-8 both sides, and GUIDs are ASCII in every OEM code
page so identity survives even when a localized display name does not); the `EndsWith("*")` heuristic
(a plan named with an asterisk renders as `(My Plan *)`, ending in `)`); `ConfirmActiveSchemeAsync`'s
tri-state; `Aggregate` masking (a failed folder copy cannot be hidden by succeeding exports); zero
`LogHelper.Log` calls on data-bearing text across all seven files; no `MessageBox` on any module
restore path; no empty catch; no `RestoreTarget` list null or Undeclared.

### Raised and deliberately not fixed

- **An unelevated backup produces a false "cannot be fully undone" warning for power plans.**
  `/export` needs elevation while `/list`, `/getactivescheme` and `/setactive` do not, so an unelevated
  run yields N failed exports plus a successfully written manifest ⇒ `Aggregate` Failed ⇒ `SnapshotGate`
  warns and prompts for an override. The warning is false in substance: the manifest *is* the entire
  undo, it exists, and `/setactive` reverses unelevated. No data loss in either direction, which is why
  it is not fixed here — the honest fix needs `SnapshotGate` to distinguish undo-critical steps from
  the rest, and that is a Phase 2b safety mechanism that deserves its own review rather than an
  amendment inside a coverage phase. Recorded because "trains users to click through the one prompt
  that must stay meaningful" is a real cost, not a cosmetic one. The app ships elevated, so the normal
  path is unaffected.
- **`File.Exists` as the manifest gate** reports "nothing was backed up for this item" for a manifest
  that exists but cannot be opened — a claim about the backup's contents when the truth is that it
  could not be read. Inherited rather than introduced: `RegFile` gates identically, so every registry
  module shares it. Fixing it in one module would make the two disagree about what absence means.
- **A truncated-but-non-empty `.pow` passes** `ValidateExportArtifact`, which checks existence and
  non-zero length only. There is no `.pow` equivalent of `RegFile.Validate` and writing one would mean
  parsing an undocumented binary format.
- **`WFonts` captures an MSIX package-scoped subkey** under the fonts key (a Windows Terminal font
  registration pinned to an exact package version). `regedit /e` takes subtrees wholesale; restoring it
  onto a different Terminal build re-creates an inert registration. Harmless, and narrowing the export
  would mean per-value filtering the base does not do.
- **A partial manifest write and a leaked `.partial` scratch file** — both fail safe (truncated JSON
  cannot parse, so the restore fails rather than acting on it) and both are cosmetic.

## What PR #9 review changed

A third round, from the two bots on the open PR. Both independently caught the same documentation
defect, and it was mine.

- **`CHANGELOG.md` had two identical `### Added — power-user settings (Phase 3c)` headers**, and
  Phase 3b's entire Developer-tooling body was sitting under the second one — attributed to the wrong
  phase, with the Phase 3b header gone. Self-inflicted, and worth recording how: the section was
  inserted with two successive edits, and the second anchored on the Phase 3b header string that the
  first had deliberately left in place, so it consumed the header it was only meant to insert above.
  A `grep -n "^### "` over the file would have shown it immediately; reading the diff hunk did not,
  because each hunk was locally correct. In a repo whose roadmap process is built on phase labels,
  this is the same class as a stale comment in the document whose whole job is being the accurate
  record — which Phase 2b's own notes call out.
- **The `WTaskbar` restore-ordering comment asserted a mechanism that does not operate.** It claimed
  the shortcuts must land before the `Taskband` blob or Explorer would prune unresolvable pins.
  Explorer reads `Taskband` at startup — which is why the module sets `RequiresExplorerRestart` — and
  this restore deliberately does not restart it, so within one `RestoreAsync` nothing reads either
  half while the other is written and the order has no functional effect on a normal run. The
  ordering is kept as genuine insurance against `AutoRestartShell` firing between the two steps, and
  the comment now says *defensive* rather than *causal*. A false mechanism in a comment is worse than
  no comment: it stops the next author working out the real one.
- **`WFonts.IsInstalled()` consulted the registry key**, which is measured present with zero values on
  every profile — so it was a constant `true`. `IsInstalled` drives "select installed", whose only job
  is to tell machines that have something here from machines that do not, so always answering yes made
  the convenience meaningless and padded every such backup with an empty item. Now the folder alone,
  which is the honest signal. Deliberately *not* mirroring the `AbsenceIsNormal` flags: those answer
  "is a missing target a fault while backing up", which the key and folder answer differently; this
  answers "is there anything here worth offering", which only the folder knows.
- **The import-by-hand advice omitted the GUID.** Verified against powercfg's own help rather than
  assumed: `POWERCFG /IMPORT <FILENAME> [<GUID>]` — "If no GUID is specified, a new GUID will be
  created." So a user following the old message would have imported the plan under an identity the
  manifest does not name, and the very next restore would fail in the same branch and hand them the
  same non-working instruction. Advice that silently fails to fix the thing it is offered for is this
  project's failure mode arriving through text instead of code. Extracted to `ImportByHandAdvice` so a
  test can read the sentence **without launching powercfg** — the first draft of that test drove
  `RestoreAsync` and did shell out, which would have broken this suite's standing rule that it invokes
  no external tool. Caught by the test duration moving, and reverted.
- Accepted: hoisting the null checks in `ExplorerRestartPrompt.IsNeeded` out of the loop.

One reviewer also confirmed the snapshot-closure argument from an angle the class doc did not state:
`WPowerPlans`' backup path emits only `Succeeded` or `Failed` steps — never `Skipped` — so
`Aggregate`'s any-failure-dominates rule means the module can report `Succeeded` **only** when every
`.pow` export *and* the manifest write succeeded. There is therefore no path where `SnapshotGate` calls
the snapshot captured while the manifest is silently absent.

## Deferred, with reasons

- **Importing power plans.** Rejected on snapshot grounds above, not on difficulty. The `.pow` files
  are exported and the restore names them, so the capability exists for a user who wants it
  deliberately; what is refused is the app doing it behind a snapshot that cannot undo it.
- **The non-policy Delivery Optimization settings.** The Settings-app toggle without a policy writes
  somewhere `DeliveryOptimization.admx` does not document, and a registry-wide search for
  `DODownloadMode` found nothing on this machine, so there was nothing to measure. Adding a guessed
  second key would mean exporting a path that is absent everywhere while reporting a normal skip — the
  silently-wrong direction. The gap is disclosed in the module's warning instead.
- **`WM_SETTINGCHANGE` broadcast**, still. `WRegional` and `WMappedDrives` join `EEnvironment` in
  needing a sign-out for their restores to take full effect. It is one mechanism serving three modules
  now, which strengthens the case for doing it as its own reviewed change rather than inside a
  coverage phase.
- **Cross-machine portability warnings as a mechanism.** `WUpdates` (machine identity), `APinnedApps`
  (build-specific database) and `WFonts` (user-name paths) now each carry a hand-written
  same-PC-versus-different-PC caveat, joining `WTelemetry` and `WThemes`. Five modules stating the same
  class of fact in five voices is the shape of something that wants to be structural — the roadmap's
  "Cross-machine portability" section is where that belongs.
