# WinRestoreKit Roadmap

The plan for WinRestoreKit, and the reasoning behind it. Written 2026-07-20, after the project's Appcopier release had gone unmaintained since January 2024 (v0.30.0).

Each phase is a separate spec, branch, and PR. Phase specs live in `docs/superpowers/specs/`.

| Phase | Scope | Status |
| --- | --- | --- |
| 1 | .NET 8 migration, test harness, repo/tooling cleanup | **Done**: [spec](superpowers/specs/2026-07-20-net8-migration-design.md) |
| 2a | Make failure representable and reported | **Done**: [spec](superpowers/specs/2026-07-20-phase2a-honest-failures-design.md) |
| 2b | Restore safety: snapshot, rollback, confirmation | **Done**: [spec](superpowers/specs/2026-07-20-phase2b-restore-safety-design.md) |
| 2c | Known module bugs | **Done**: [spec](superpowers/specs/2026-07-20-phase2c-module-bugs-design.md) |
| 3a | Module bases: refactor & retire | **Done**: [spec](superpowers/specs/2026-07-21-phase3a-module-bases-design.md) |
| 3b | Module coverage: developer tooling | **Done**: [spec](superpowers/specs/2026-07-21-phase3b-developer-tooling-design.md) |
| 3c | Module coverage: power-user settings | **Done**: [spec](superpowers/specs/2026-07-21-phase3c-power-user-settings-design.md) |
| 4 | UI revamp (task-first redesign) + modernization | **Done**: [spec](superpowers/specs/2026-07-21-phase4-ui-revamp-design.md) |

Phase 2 was originally written as one phase. It is four independent workstreams, and splitting it was
the first decision of the 2a design. 2a is the foundation: until failure can be *expressed*, none of the
others can be verified. Modernization moved out of Phase 2 entirely: dark mode and
rollback-a-bad-registry-import share no code, and bundling them means the reviewer scrutinising
destructive operations is also diffing ARGB values.

## Direction

Three priorities, in order: **safety and correctness**, **modernization**, **better coverage**. Sequencing is
migrate-first, the platform move landed before the behavior work, so the safety changes are written once,
against the runtime they will live on, instead of being written twice.

Deliberately **not** pursued for now: scheduled/automatic backups, backup compression, backup diffing,
cloud targets. They are real ideas, but they add surface area to a tool whose core operations are not yet
trustworthy.

## Phase 1: .NET 8 migration (done)

Retargeted .NET Framework 4.8 → `net8.0-windows`, SDK-style csproj, `PackageReference`, xUnit harness,
`bin`/`obj` untracked, tooling updated. Behavior-preserving by design.

Three runtime breaks were fixed. All of which compile cleanly and fail only when run:

- `Process.Start` no longer shells out by default, so URL links threw. The QR-code prompt would have
  terminated the process outright (timer thread, no `try`/`catch`).
- `Application.StartupPath` gained a trailing separator, doubling it throughout backup paths.
- `Application.ProductVersion` prefers `AssemblyInformationalVersion` on .NET, whose `+<sha>` suffix would
  have made `new Version(...)` throw.

Releases ship **self-contained single-file** (~69 MB, one `.exe`, no runtime install). See the spec for the
measured size comparison and the flags that matter.

## Phase 2: safety and correctness

The highest-value work remaining. The app runs elevated and performs destructive operations, and until
2a it could not tell you when they failed.

**A backup tool that misreports success is worse than no backup tool**, the user finds out at restore time,
which is exactly when they have no fallback.

### Phase 2a: honest reporting (done)

Full design: [`superpowers/specs/2026-07-20-phase2a-honest-failures-design.md`](superpowers/specs/2026-07-20-phase2a-honest-failures-design.md).

The root defect is structural, not a collection of missing checks: `BackupBase.Backup(string)` returns
`void`, `Utils` swallows every exception, so the call chain is *incapable* of expressing failure. 2a
threads a `ModuleResult` (`Succeeded` / `Skipped` / `Failed` + reason) through `BackupBase` → 23 modules
→ `Utils` → the views, and everything below depends on it landing first.

All of the following landed. Kept as a record of what the phase actually addressed:

- ~~`Views/ConfPageView.cs` shows "Back up done." and "Restore done." unconditionally~~, both now
  reflect real per-module outcomes, via a four-state summary that also distinguishes "nothing was
  present to back up" from "the run never happened".
- Replace the silent-catch pattern throughout `Conf/` and `Helpers/WindowsHelper.cs`: failures currently
  write a log line and return as if successful.
- `Utils.ExportImportRegistryKey` never checks an exit code and never verifies the `.reg` file exists, so a
  missing or corrupt file imports silently. Measured 2026-07-20: `regedit /e` on a nonexistent key exits
  **0 and writes no file**, so the file check is mandatory, not belt-and-braces.
- `backup_log.txt` records what was *selected*, not what *succeeded*. It should record outcomes.
- `CWiFiConf` restore matches `WLAN*.xml` but `netsh` writes `<interface>-<SSID>.xml`: measured 0 of 19
  files matched. Pulled into 2a because honest reporting would otherwise render total data loss as a
  tidy "Skipped".

Deferred out of 2a: full persistent file logging. `LogHelper` writes only to a `RichTextBox`, so every
error trace dies with the window. 2a fixes only the format-string hazard that would silently swallow
reason strings containing braces; the rest is its own workstream.

### Phase 2b: restore safety (done)

Full design: [`superpowers/specs/2026-07-20-phase2b-restore-safety-design.md`](superpowers/specs/2026-07-20-phase2b-restore-safety-design.md).

2a made restore *report* honestly; 2b makes it *behave* safely. All of the following landed:

- ~~Snapshot current state before any restore~~: a restore now runs an ordinary backup of the items it
  is about to overwrite into a `(pre-restore)` folder first, so rollback is the existing restore flow.
  The decision not to build a delete-then-import rollback engine is recorded in the spec, it buys
  fidelity by adding registry *deletion* to the phase whose purpose is to make destruction safe. The
  additive-merge limitation is disclosed to the user instead of being papered over.
- ~~Real confirmation before destructive restore~~: a dialog listing every item's registry keys,
  folders and commands, defaulting to Cancel, and carrying the per-module `WarningMessage`s that were
  previously shown only while browsing the tree.
- ~~Guard the unchecked `Process.Kill()` in `RestartExplorer`~~, it killed every Explorer process and
  started a shell once *per kill*. It now closes once, starts at most one shell, starts none when
  Windows already restarted it, and returns a result instead of `void`.
- ~~Systematically close a target app before overwriting its profile~~: consent is gathered once, on
  the UI thread, in the confirmation dialog, and flows into a pure per-module dispatch decision.
  Declining skips the module; a process that will not close fails it.
- ~~Write a restore-time log~~: `restore_log.txt`, written into the snapshot folder beside the artifact
  that undoes the restore, and surfaced in `RestPageView`.
- Read-back verification of registry imports, deferred *into* 2b by the 2a spec, also landed. The
  mapping is deliberately asymmetric: a key absent after an import is a failure, a key that cannot be
  probed is not. The reasoning is in the spec, and it is the opposite of the export path's mapping.

Also pulled in, the QR-code timer below, because this phase added the app's first consequential modal
dialog and the timer's defect was a dialog-ownership defect.

### MainForm's QR-code timer

Found while hardening the link handlers. The first two items were **fixed in 2b**; the third belongs to
the persistent-logging workstream and is still open.

- ~~`MainForm`'s `System.Timers.Timer` has no `SynchronizingObject`~~, so `QRTimerElapsed` ran on a
  thread-pool thread and its `MessageBox` had no owner, it could paint *behind* the main window while
  the app stayed clickable, so a user saw nothing happen and clicked again, stacking up hidden dialogs.
- ~~That same timer is never stopped or disposed~~, it was in neither `components` nor any teardown
  path, so an `Elapsed` still pending at close ran against a disposed control.
- `LogHelper.Log` is invoke-safe only by accident: `Control.InvokeRequired` returns false when the
  target has no created handle, so in that state it touches the `RichTextBox` from whatever thread
  called it. The catch-all hides it. This is part of the persistent-logging work above.

### Follow-ups left by Phase 2b

Raised by the safety review of the 2b branch. None was reachable in ordinary use; each was recorded
because the reason it was unreachable is a coincidence rather than a guarantee.

Two of the six were in fact **fixed inside the 2b commit itself** and this prose was never updated to
match: `CHANGELOG.md` recorded both correctly, so the record contradicted itself for the length of one
commit. Corrected below in 2c. It is worth noting how it happened, the entries were written when they
were deferred, the deferral was reversed during implementation, and nobody re-read the list. That is
the same class of drift as a stale comment, in the document whose whole job is being the accurate record.

- **The Explorer auto-restart probe is taken with no settle delay.** `RestartExplorer` asks
  `IsProcessRunning("explorer")` on the line after `CloseProcess` returns, but Windows relaunches the
  shell through winlogon some hundreds of milliseconds later, so the probe reads absent, Appcopier
  starts a shell, and Windows starts a second one. The guard bounds the damage to one stray window
  (versus N before 2b) and the risk is disclosed, but as written the `RestartedByWindows` branch may
  be close to dead code. **Measure N2 on the smoke matrix before changing anything**: if the
  relaunch is faster than assumed, the code is already right and a speculative delay would only slow
  the button down.
- **`AllowPrompts` is shared mutable state.** It is set on the module instance before each backup and
  restored in a `finally`, which is correct but depends on one caller remembering. `BackupAsync(path,
  allowPrompts)`: or a scoped guard: removes the class rather than the instance, and would make it
  testable at the module level instead of only through a `UserControl` the suite cannot instantiate.
  Deferred because it is a 23-signature change that 2b explicitly chose not to make.
- ~~**`results` is index-aligned against `selectedConfigs` but produced by iterating `scope`.**~~
  **Was already fixed in 2b**, not deferred: `ConfPageView` projects `restoredModules` from `scope`
  and pairs against that everywhere, so the alignment is structural. `selectedConfigs` is no longer
  used for pairing at all.
- ~~**`RestorePlan` composition sits outside any catch**, and `Render` dereferences each
  `RestoreTarget` unguarded.~~ **Fixed in 2c.** A null entry now renders its own marker: a different
  sentence from the undeclared marker, because "the module declared nothing" and "one line of the
  declaration is broken" are different facts, and the composition is wrapped in a catch that
  abandons the restore rather than half-describing it. Nothing is written when Appcopier cannot say
  what it would write.
- ~~**`SnapshotGate.Evaluate` counts an all-null outcome list as `considered == 0`**~~ **Fixed in
  2c.** A null entry is counted before the null check and folded into the existing failure branch, so
  it forces the prompt instead of vanishing. `ModuleOutcome.Pair` still never emits nulls; the point
  was to make the invariant structural rather than coincidental.
- ~~**A null entry in `ProcessesToCloseBeforeRestore` is handled inconsistently**~~ **Was already
  fixed in 2b**, not deferred. All four readers now guard identically, and the one that was missing
  carries a comment explaining the symmetry.

### Phase 2c: known module bugs

Each of these becomes *visible* once 2a lands, which is why they follow rather than lead.

- ~~`WTelemetry` hardcodes `ControlSet001` instead of `CurrentControlSet`~~ **Fixed in 2c.** The entry
  above understated it: this is not "wrong on systems booted from a different control set", it is
  *silently* wrong on them. `ControlSet001` normally still exists as a stale hive after such a boot, so
  the key probed present, the export succeeded and the row was green over configuration the running
  system was not using, the silent-wrong-data direction, not cry-wolf, which is why it survived. The
  fix also raises the stakes of a restore, from an inert write to a live service key, so the module
  gained a `WarningMessage`. **The filename is derived from the key, so this orphans the DiagTrack file
  in pre-2c backups**, which now report "nothing was backed up for this item". A restore-side fallback
  was designed and then deferred out of 2c on two grounds recorded in the spec, it would write outside
  the pre-restore snapshot while the gate still reported the restore undoable, and the old file's
  *contents* name `ControlSet001`, so applying it would re-commit the defect. An honest fallback has to
  rewrite the payload, not just find the file.
- `WNetworkConf.ExecuteNetshCommand` does `new StreamWriter(outputFilePath)` on both paths, and `Restore`
  passed `null`. **Fixed in 2a after a safety audit disproved the reasoning for deferring it.** The
  defect was worse than recorded here: `process.Start()` ran *before* the throw, so netsh was already
  applying the backup's addresses, DNS servers and interface metrics when the exception fired: and
  was never waited on or killed. The user was told the restore failed while their networking was being
  reconfigured. "Broken, not dishonest" was exactly backwards.
- ~~`CWiFiConf` restore imports only `xmlFiles[0]`~~ **Fixed in 2a**, along with the filename-filter
  half of the pair: correcting only one would have left the module still restoring nothing useful.
- ~~`AStoreApps` restore is dead code; the real `winget import` is commented out.~~ **This entry was
  false at the time 2c started, and is corrected rather than fixed.** 2a deleted the commented-out
  block; restore is a deliberate, tested delegation: `RestoreAsync` returns a completed task so
  `ShowDialog` runs on the STA UI thread rather than a thread-pool thread, and it reports `Skipped`
  because the installs happen from choices made inside the dialog. `winget import` is also the wrong
  feature, the dialog exists so the user can reinstall a *subset*, which import cannot express. Left as
  a record that a stale roadmap entry cost a planning cycle before anyone read the code.
- ~~`Utils.RunWTAsync` waits on `wt.exe`, which is a launcher rather than the work~~ **Measured and
  fixed in 2a.** Filed here as a suspicion, then confirmed on a real backup, 2026-07-20, the app
  reported `Remember installed apps FAILED: winget reported success but wrote no file` and wrote
  `backup_log.txt` at 07:35:54.295, and winget wrote a complete, valid 113-package export to that
  same path at 07:36:23.164: **29 seconds after the app had already declared it missing**. `wt.exe`
  forwards the command and exits, so `WaitForExit` was returning on a process that had done nothing
  but pass along an argument. A backup that worked was reported as failed. `Utils.RunWingetAsync`
  now runs `winget.exe` directly, so the wait and the exit code belong to the process doing the
  work. This is the cry-wolf direction of the phase's failure mode, and it was invisible from the
  reporting layer: no care taken there could fix being asked about a file still being written.
- ~~`Utils.RunWT` is `async void`~~ **Fixed in 2a**, it is now `RunWTAsync` returning a
  `ProcessOutcome`. `async void` returns to its caller at the first `await`, so `AStoreApps` logged
  success before winget had started, it was structurally incapable of reporting a real result, which
  made it a prerequisite for the phase rather than cleanup.
- ~~`OSHelper` dereferences registry values with no null check.~~ **Fixed in 2c**, and it was the most
  severe item on this list rather than the tidiest. It runs from `ConfPageView`'s constructor, which is
  evaluated as the *argument* to `Application.Run` and therefore outside the message pump, and there
  was no `ThreadException` or `AppDomain.UnhandledException` handler anywhere in the tree. A missing
  `UBR` value, which is real on sysprepped and container images, terminated the process via WER with no
  window, no dialog and no log line. `Program.Main` now reports and rethrows.
- ~~`WThemes` backs up `%Windir%\Web\Wallpaper` … but not the actual active wallpaper.~~ **Fixed in
  2c.** Measured 2026-07-20: that folder is 20 files / 20.0 MB, about 95% of the module's bytes, and
  was its only write to a directory shared by every account on the PC. It now captures
  `HKCU\Control Panel\Desktop`, so the *pointer* to the wallpaper survives and not just the pixels,
  which the module was already copying. Two things are disclosed rather than fixed, the key carries
  display-specific passengers (`WindowMetrics` with its `AppliedDPI`, the `Colors` subkey) that
  `regedit` cannot leave behind, and the pointer is an absolute path containing the user name, so
  under a different account name the desktop comes back black while the row still reads Succeeded.
- ~~`Forms/RestAppsForm` wires `Click` to the `SelectedIndexChanged` handler, and its filename casing is
  inconsistent~~ **Fixed in 2c**, and the first half was understated here as a wiring inconsistency: it
  was silent data loss. The handler starts by clearing the checked-list, and the combo is a
  `DropDownList`, so *opening the dropdown* discarded every app the user had ticked: in the one dialog
  whose purpose is choosing a subset. The filename had four spellings, not two; the fourth was in the
  `Info` text, the only one a user reads.
- Two further `RestAppsForm` defects this list never recorded, both fixed in 2c: `btnRestore_Click`
  re-parsed the export to build an argument the install loop ignored, and the loop was `async void`
  called un-awaited with nothing disabling the button, so a second click started a concurrent
  *elevated* install run. Its parse-error log line also went through `LogHelper.Log`, whose first
  argument is a format string, carrying a JSON parse error, the one message needed to diagnose a
  broken export was the one guaranteed to be discarded.

Modernization was originally listed here and has moved to [Phase 4](#phase-4--modernization). It shares
no code with the safety work, and mixing UI theming into a review of destructive registry operations
serves neither.

## Phase 3: module coverage

23 modules existed going in, strong on core Windows personalization/privacy and Wi-Fi/winget, and largely
absent on the state a power user would actually miss. Split into three sub-phases in the 2026-07-21
planning pass (multi-agent design plus an adversarial critique that confirmed twelve defects in the first
draft; the corrected plan is what the sub-phases below implement).

### Phase 3a: module bases: refactor & retire (done)

Full design: [`superpowers/specs/2026-07-21-phase3a-module-bases-design.md`](superpowers/specs/2026-07-21-phase3a-module-bases-design.md).

- ~~**Refactor first.** The modules are near-identical copy-paste; `WNetworkConf` and `CWiFiConf` each
  carry their own `netsh` helper.~~ Done in 3a: `MultiKeyRegistryModule` and `FolderModule` bases, one
  shared `Utils.RunToolAsync` runner and `ValidateExportArtifact` ladder. A planned `CommandModule` base
  was **dropped by the critique**, it fit one of its three intended consumers; the runner was the real
  shared seam. winget deliberately keeps its own runner (its visible console window is the app-restore
  dialog's progress reporting).
- ~~**Browsers are deprioritized** … fix or retire them~~ **Retired**, by user decision 2026-07-21:
  sync solves it better, and fixing meant per-browser exclusion lists plus the missed Firefox Local
  half. Old backups keep their browser folders on disk; the app no longer restores them (disclosed in
  CHANGELOG).
- ~~`DUSB` targets a near-empty key~~ **Retired** in 3a, the Info text promised far more than the key held.
- The 2b-deferred `AllowPrompts` cleanup resolved itself, the retirement removed the flag's only
  readers, so the mechanism was deleted outright rather than redesigned.

### Phase 3b: developer tooling (done)

Full design: [`superpowers/specs/2026-07-21-phase3b-developer-tooling-design.md`](superpowers/specs/2026-07-21-phase3b-developer-tooling-design.md).

~~New `FileModule` base + `RestoreTarget.File` kind, new "Developer" tree category (prefix `E`):
Windows Terminal settings, VS Code settings/keybindings/snippets, `.ssh` **config and known_hosts only**
(private keys are deliberately excluded from plaintext backups: user decision), user environment
variables (`HKCU\Environment`), `hosts` file. Terminal and VS Code declare consented closes, both rewrite
their own settings files while running, so an unclosed app can silently overwrite a restored file minutes
later.~~ All landed. Notes on what the implementation decided that the entry above did not anticipate:

- **`FileModule` is a whitelist by construction**, it copies the files it is given and never enumerates
  a directory. That is what makes the private-key exclusion structural rather than a filter someone has
  to keep correct, and it is pinned from both directions by `DeveloperModuleTests`.
- **`ETerminal` covers three installs, not one** (Store, Preview, unpackaged: user decision). All three
  files are called `settings.json`, so it is the first consumer of the file-side naming seam; without the
  override the second export would overwrite the first while both reported success. Same defect class as
  the WThemes one 2c fixed, caught before shipping this time because the rule was already written down.
- **`EVSCode` is hand-rolled, not a `FileModule`**: two files plus the `snippets` *folder*. Teaching the
  base about folders for one consumer is the dropped-`CommandModule` mistake from 3a; `WThemes` is the
  precedent for a heterogeneous module instead.
- **`EEnvironment` is a plain `RegistryModule`.** The category describes what the user backs up, not
  which base it needs. Two things disclosed rather than engineered, the restore is an additive merge
  (`PATH` is where users will expect otherwise), and no `WM_SETTINGCHANGE` broadcast is sent, so running
  shells keep their old values. The broadcast is deferred as its own review, not forgotten.
- **`EHosts` is the only module in the category that writes machine-wide.** No pre-flight elevation
  probe: an unelevated write already fails honestly through the copy primitive, and a second check would
  be a second place that has to agree with the first about what "can write" means.
- **`EEnvironment` ships in two variants**, added after review. `EEnvironmentFiltered` exports the same
  key and drops values whose *names* look like credentials, naming every one it dropped. The plain module
  is unchanged and both are separate ticks, so the tree checkbox is the opt-in and the default behaviour
  did not move. The spec records why the original "no filter, disclose instead" reasoning was superseded
  rather than simply wrong, it assumed the filter would replace the plain export.
- **`Utils.CopyFile` writes in place, not atomically**, after a reversal recorded in the spec. A
  temp-file-and-rename replaces the directory entry and so breaks a hard link or symlink: silently, and
  unrecoverably, because the pre-restore snapshot captures file *contents* and cannot restore link
  structure. The torn-file failure that atomicity prevented is by contrast reported and snapshot-undoable.
  Guarding a loud recoverable failure at the cost of a silent permanent one is the wrong trade. Pinned by
  a hard-link test verified to fail against the atomic version.

Deferred with reasons recorded in the spec: WSL config, VS Code extension list + reinstall dialog,
VS Code Insiders/VSCodium and per-profile settings, the `WM_SETTINGCHANGE` broadcast.

### Phase 3c: power-user settings (done)

Full design: [`superpowers/specs/2026-07-21-phase3c-power-user-settings-design.md`](superpowers/specs/2026-07-21-phase3c-power-user-settings-design.md).

~~All under the existing "Settings" category (`W` prefix): power plans, per-user fonts, mapped network
drives, regional/input settings, plus the `WTaskbar` and `WUpdates` retargets.~~ All landed. Two
decisions were taken by the user before implementation, both in the safe direction:

- **`WPowerPlans` restore is activate-only.** `powercfg /import` creates GUID-keyed scheme objects the
  app has no mechanism to delete, so the pre-restore snapshot could not undo them while `SnapshotGate`
  would still report the restore as undoable, the same asymmetry that pushed the `WTelemetry`
  filename fallback out of 2c. The `.pow` exports are still written, and a restore that cannot find
  the recorded plan fails and names the file to import by hand.
- **`WUpdates` drops `\AU` rather than demoting it**, orphaning that key's export in existing backups.
  Disclosed in `CHANGELOG.md` by exact filename, per the WTelemetry precedent.

What the implementation found that the entry above did not anticipate:

- **The measurement pass changed two designs before any code was written.** `DeliveryOptimization` was
  measured absent at all three plausible paths, and a registry-wide search for `DODownloadMode` returned
  zero matches; the key was settled from `DeliveryOptimization.admx` instead, which declares exactly one
  (`SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization`) and makes `AbsenceIsNormal` true. Guessing
  it would have exported a nonexistent key while reporting a normal skip, the silently-wrong direction.
- **`WUpdates` has always captured this PC's Windows Update identity** and never said so. The key it
  already exported carries `SusClientId`, `SusClientIdValidation` and `TraceId`. This was not on the
  list; it turned up while measuring the key to decide the `\AU` question, and is now a `WarningMessage`.
- **`WTaskbar` deliberately does not close Explorer before restoring.** `RestartExplorer` goes through
  `CloseProcess`, which calls `Kill()`: a killed Explorer never flushes its in-memory pin list, which
  is what makes restore-then-restart safe. Closing it *first* would be worse: AutoRestartShell relaunches
  the shell within about a second, so a fresh Explorer would be holding the old `Taskband` before the
  import finished. The residual case: restoring and then signing out without restarting: is disclosed
  in the warning rather than engineered around with a kill that makes it worse.
- **The retarget orphans nothing**, unlike `WUpdates`. The Advanced key keeps `Taskbar.reg` through a
  `RegFileNameFor` override, the WThemes pattern, pinned by a literal test in `BackupFileNamingTests`.
- **`WFonts` answers the absence question differently for its two halves**, and the measurement is why:
  the HKCU fonts key exists with zero values on an account that has installed no per-user fonts, while
  `%LOCALAPPDATA%\Microsoft\Windows\Fonts` does not exist at all. Key absent is a fault; folder absent
  is normal.
- **`RestoreDeclarationTests`' RegistryModule count stayed 11 across the phase**: `WTaskbar` left the
  family and `WMappedDrives` joined it. A membership change that nets to zero is invisible in an
  assertion, so the two moves are now pinned as literals beside the count.

**Excluded from Phase 3 with recorded reasons:** scheduled tasks (honest restore needs SID/path
rewriting and system-task filtering; creates elevated executable entries), file associations (the
`UserChoice` hash is anti-tamper: a registry merge passes the post-import probe while Windows rejects
the association, a guaranteed dishonest green row), display layout (monitor-EDID-keyed, inherently
non-portable). `APinnedApps` copies a build-specific Start menu database that is notoriously
non-portable between machines: kept, with its warning strengthened in 3c rather than retired, because
same-machine restore is its honest use case.

## Phase 4: UI revamp and modernization (done)

Full design: [`superpowers/specs/2026-07-21-phase4-ui-revamp-design.md`](superpowers/specs/2026-07-21-phase4-ui-revamp-design.md).

**Implementation notes, written after the phase closed:**
[`superpowers/specs/2026-08-02-phase4-completion-implementation-notes.md`](superpowers/specs/2026-08-02-phase4-completion-implementation-notes.md).
Read that one before changing any of this, it records the decisions the design deferred, the traps
(the theme walker, the single DPI source of truth, the amber-not-green rule), and, most importantly,
**what has not yet been verified in an elevated session**.

Phase 4 was originally scoped as the four modernization items at the bottom of this section. It opened
instead with a **complete UI/UX revamp**, decided 2026-07-21 after a four-direction design pass. The
revamp absorbed the DPI and dark-mode items, so those are no longer independent; the two networking
items still are, and can land in any order alongside.

### The UI revamp: "jobs, not modules"

The reason it moved to the front of the phase: Phases 2a-3c rebuilt the engine's honesty and safety, and
the presentation layer now actively *hides* that work. A warning fires as a modal on every tree click, so
it is trained away. A `RichTextBox` is simultaneously help text and activity log, so selecting a module
wipes the log line that recorded a failure. `RunSummary` composes an honest four-state headline and it is
poured into a `MessageBox` that flattens 29 outcomes into one paragraph, gets dismissed on reflex, and
cannot be re-read. Rollback has existed since 2b and **no screen mentions it**. The engine is not the
bottleneck on trustworthiness any more; the surface is.

The chosen direction rebuilds around the user's three jobs rather than the module inventory: **Home**
(backup age, failures with reasons intact, undo points), **Back up** (presets, with the existing tree
preserved behind an Advanced view), a **Restore wizard** that starts from the backup rather than the
module tree, and **History** (backups and snapshots on one timeline). It stays on WinForms/.NET 8 and
adds no NuGet packages.

Three alternatives were considered and are recorded in the spec: a conservative in-place restyle, a
Windows 11 Settings-style shell (whose visual language this direction absorbs), and a WPF migration. The
WPF direction was rejected for the UI stack but **its Core-extraction milestone was adopted**: the
engine moves into a UI-free `Appcopier.Core` class library early, which is worth doing under any
direction and keeps a future framework move cheap.

**PR 2 (Core extraction) shipped 2026-07-21.** `Appcopier.Core` holds `BackupBase`, `Conf/`, most of
`Results/` and the `Utils`/`Data`/`OsHelper`/`LogHelper` helpers, and it does not reference WinForms,
so the UI-freeness is enforced by the compiler rather than by review. What the spec called "not a pure
refactor" was accurate: five UI dependencies had to be inverted first, and a sixth the spec had missed
turned up in the process: `Data.CheckForUpdates` called back into `Program`, which is a library
depending on its own application. It moved app-side with the MessageBoxes. `RunSummary` stayed app-side
as planned. The `Application.StartupPath` → `AppContext.BaseDirectory` change in `DataRootDir` was
measured under a real single-file publish rather than reasoned about, per the spec's instruction.

The open question the spec deferred to this PR: whether the three unpinned reflection sweeps needed
count pins: was answered differently: a single test asserting the app assembly contains no concrete
`BackupBase` subclass pins the membership rule directly, never needs renumbering, and does not depend on
`RestoreDeclarationTests`' `Assert.Equal(29, …)` surviving a future edit.

**PRs 3-9 shipped, completing the phase.** PR 3 added `backup_manifest.json` (engine only). PR 4 added
the shell, `NavigationService` and Home. PR 5 moved the backup/restore orchestration out of the view
into `BackupRestoreOrchestrator` behind an `IRunUi` seam: a verbatim move, so the safety reviewer
could confirm every stage comment and consent invariant travelled with it. PR 6 rebuilt the Back up page
(module registration extracted to `ModuleCatalog`, presets, the in-page `RunResultsPanel`, warnings
inline instead of modal). PR 7 inverted Restore into the two-step wizard over `RestoreContents` and
renamed `ConfPageView` to `BackupPageView`. PR 8 added the History timeline and deleted `RestPageView`.
PR 9 added the `Theme` token class with light and dark palettes, the live `SystemEvents` switch, and,
only after the last absolute-positioned Designer was converted, the PerMonitorV2 flip.

Both consent constraints below survived unchanged, and both were checked by `windows-safety-reviewer`
on the two diffs that touch destructive paths (PR 5 and PR 7), which returned no findings on either.

Two constraints worth restating here because they bind everything else:

- **Informed consent does not move.** `RestoreConfirmForm` stays modal, Cancel-defaulted, with unchecked
  consent boxes and every sentence authored by `RestorePlan`; the snapshot-override prompt stays modal
  and still defaults to No. Modality is removed everywhere it was noise and kept everywhere it is the
  feature.
- **Honest reporting cannot be softened by presentation.** Restore-side wording stays "applied", never
  "verified". The one engine addition: `backup_manifest.json`, written beside `backup_log.txt`: exists
  because Home and History cannot make honest status claims by parsing prose logs. An absent or
  unparsable manifest renders as *unknown*, never as inferred green.

### Modernization items

- ~~Rewrite the update checker against the GitHub Releases API~~: **done.** `UpdateCheck` now reads
  `tag_name` from the Releases API and falls back to the old `AssemblyInfo.cs` parse on *any* primary
  failure (non-2xx including the shared-IP rate-limit 403, timeout, malformed JSON, empty tag). The
  Phase 1 compatibility constraint holds: deployed clients still parse that file, so both the file
  format and `ParseLatestVersion` are untouched, and the fallback keeps exercising them.
- ~~Replace obsolete `WebClient` with `HttpClient`~~: **done.** Both `SYSLIB0014` warnings are gone and
  `WebClient` no longer appears anywhere in the solution; `Data.IsInet` uses a shared static
  `HttpClient` with a 5s timeout and the synchronous `Send`.
- ~~Per-monitor DPI awareness~~: absorbed into the UI revamp. It has to land *after* the layout work
  rather than beside it: absolute positions do not survive a `WM_DPICHANGED` rescale, and layout
  containers do, so flipping the manifest first would only produce fallout attributable to two causes.
- ~~Dark mode~~: absorbed into the UI revamp, for the same reason in reverse: theming a layout that is
  about to be rewritten means doing it twice. Recorded here because the constraint is easy to get wrong:
  `Application.SetColorMode` is **.NET 9+** and is not available on `net8.0-windows`, so this is
  hand-rolled from a token class plus `DwmSetWindowAttribute` for the title bar. `MessageBox` and system
  scrollbars stay light regardless, which is disclosed rather than chased.

## Cross-machine portability

Several modules are machine-specific (Start menu database, printers, USB, display), and nothing warns the
user when restoring onto different hardware. Worth addressing once Phase 2 makes failures visible, the two
problems share a mechanism.

Phase 4 takes the first half of this, as a side effect rather than as its goal. `backup_manifest.json`
records the machine name, user name and OS build a backup was made under, and the restore wizard's
contents step reads them back, so "this backup came from a different account" can be said **before**
anything is overwritten rather than discovered afterwards. That is provenance, not detection, it tells the
user the restore is cross-machine, and it still cannot tell them that a specific module silently produced
the wrong result, the pinned-apps and user-fonts cases both report success while Windows quietly drops
what was restored. Closing that gap needs per-module portability declarations, which Phase 4 does not add.
