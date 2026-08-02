# Phase 2b — Restore safety

Design record, 2026-07-20. Branch `feat/phase2b-restore-safety`.

> This document predates the rename of Appcopier to WinRestoreKit and is kept as a
> historical record. Product names, namespaces and paths below refer to the project as
> it was at the time of writing.

## The problem

Phase 2a made restore *report* honestly. It did not make restore *behave* safely, and the restore path
is the mirror image of the pre-2a backup path in every respect that matters:

| Path | What happens today |
| --- | --- |
| `RestPageView.cs:49-63` | `btnOK_Click` sets the path, switches view, and awaits the restore. No confirmation of any kind — the click that selects a folder is the click that overwrites the machine. |
| `ConfPageView.cs:254-285` | `PerformRestoration` calls `RestoreAsync` on every selected module. Nothing is snapshotted first, so a bad `.reg` import is irreversible. |
| `WindowsHelper.cs:242-294` | `ImportRegistryKey` runs `regedit /s` and reports "applied". Nothing is read back, so an import that silently did nothing reads identically to one that worked. |
| `BGoogleChrome.cs:60-65` and 3 others | The live browser profile is overwritten with no process check and no prompt. `Utils.CloseProcess` exists, is bounded and tested, and the restore path does not call it. |
| `WindowsHelper.cs:382-403` | `RestartExplorer` kills **every** `explorer.exe` and calls `Process.Start("explorer.exe")` once *per killed process* — N open File Explorer windows produce N shells. |
| `ConfPageView.cs:288-307` | The restore writes no log. There is no record of what a restore changed. |

**A restore you cannot undo, did not agree to, and have no record of is the same class of defect as a
backup that misreports success** — the user finds out afterwards, which is exactly when the state that
would have told them what happened is gone.

## Scope

This phase makes destructive restore operations *consented to*, *reversible*, and *recorded*.

**In scope:** a pre-restore snapshot and the gate that acts on its outcome; a real confirmation stating
what will be overwritten; read-back verification of registry imports; closing target apps before
overwriting their profiles; `restore_log.txt`; the `RestartExplorer` kill guard; the `MainForm` QR-code
timer.

**Explicitly not in scope** — considered and deferred, not overlooked:

| Deferred to | Item |
| --- | --- |
| 2c (module bugs) | `WTelemetry` `ControlSet001`, `AStoreApps` dead restore, `WThemes` stock wallpapers, `RestAppsForm` handler wiring, `OSHelper` null dereference |
| Own item | full app-level persistent logging, including `LogHelper.Log`'s accidental invoke-safety |
| Own phase (modernization) | `HttpClient`, update-checker rewrite, per-monitor DPI, dark mode |
| Not pursued | a bespoke delete-then-import rollback engine (Decision 1) |

The QR-code timer floated between 2b and 2c in `docs/ROADMAP.md`. It is pulled in here because this
phase adds the app's first consequential modal dialog, and the timer's defect *is* a dialog defect: an
ownerless `MessageBox` raised from a thread-pool thread. Reviewing both together is cheaper than
reviewing dialog ownership twice. Its `LogHelper` sub-item stays out — that belongs to logging.

## Decisions

**1. The snapshot is a normal backup into a fresh folder; rollback is the existing restore UI.**

The alternative considered and rejected was a dedicated snapshot format plus a rollback engine that
deletes keys and files before re-importing. That is a *faithful* rollback, and it buys fidelity by
adding new destructive operations — registry key deletion — to the phase whose purpose is to make
destructive operations safe. The snapshot as ordinary backup inherits every 2a guarantee for free:
export verification, honest per-module results, `backup_log.txt`. That inheritance is what makes
Decision 5's gate possible at all — a snapshot whose failure could not be *detected* would be
decoration.

**2. The fidelity caveat is stated, never softened.**

Both restore mechanisms are additive. `regedit /s` merges; it does not replace. `Utils.CopyFolder`
writes each source file with `FileMode.Create` and leaves destination files absent from the source in
place (`WindowsHelper.cs:52-72`). So a snapshot can put back what a restore *overwrote* and cannot
remove what a restore *added*. One canonical constant, `RestorePlan.FidelityCaveat`, appears in the
confirmation dialog, the snapshot's `backup_log.txt` header, and `restore_log.txt`:

> The snapshot can put back settings this restore overwrites. It cannot remove registry values or
> files that this restore adds — restoring the snapshot merges it over the current state rather than
> resetting to it.

A tool that offered "rollback" without this sentence would be making the same class of claim 2a exists
to eliminate: describing a guarantee stronger than the one it holds.

**3. Modules whose restore changes nothing are excluded from the snapshot.**

`BackupBase` gains `virtual bool RestoreMakesChanges => true`. Only `AStoreApps` overrides it to
`false`: its restore opens a dialog and returns `Skipped`. Without this, a restore that includes
"Remember installed apps" pays a full `winget export` — measured at ~29 s in 2a, and permitted up to
ten minutes — to protect against a restore that writes nothing.

The default is `true`, so a future module is snapshotted unless its author deliberately opts out. This
is the same judgement call as `absenceIsNormal`, with the same failure modes in both directions, and it
is called out here so a future author recognises it as one.

**4. Confirmation is a small custom Form, not a `MessageBox`.**

Three reasons, any one sufficient. (a) The payload is up to 23 modules with their registry keys and
folder paths; `MessageBox` has no scrolling and no structure. (b) Per-browser close consent needs
checkboxes — the `MessageBox` alternative is serial Yes/No prompts, which is precisely the dialog
fatigue *and* the worker-thread dialog pattern this phase removes. (c) The caveat, the snapshot
destination, and the consent controls have to be visibly co-present; consent given on one screen and
caveated on another is not informed consent.

`Forms/RestoreConfirmForm` is deliberately thin: it renders text and structure supplied by the pure
`RestorePlan` and contains no composition logic. Buttons are "Restore" and "Cancel" with **Cancel as
the accept button** — the safe default, following the update prompt's `MessageBoxDefaultButton.Button1`
idiom at `DataHelper.cs:103`.

**5. A failed snapshot stops the restore and asks again. It never proceeds silently.**

`SnapshotGate` is a pure function over the snapshot's `ModuleResult`s. Any `Failed` module, or a
failure to create the snapshot folder at all, requires a second explicit confirmation naming what
failed, defaulted to No (`MessageBoxDefaultButton.Button2`). Declining reports through the existing
`DidNotRun` state — "the pre-restore snapshot failed and you chose not to continue" — rather than
inventing a new outcome. Proceeding is recorded in `restore_log.txt` as having run *without* a complete
snapshot.

Silently proceeding is not an option on the table, because a restore without a snapshot is exactly the
unsafe behaviour this phase exists to remove; doing it after *offering* a snapshot would be worse than
never offering one, since the user would believe they had a fallback.

**6. Read-back verification upgrades "applied" asymmetrically.**

After `regedit /s` exits 0, `ImportRegistryKey` probes the target key:

| Post-import `ProbeKey` | Result | Reason |
| --- | --- | --- |
| `Present` | `Succeeded` | "applied {key}; the key is present after the import" |
| `Absent` | **`Failed`** | "regedit reported success but {key} is not present after the import" |
| `Indeterminate` | `Succeeded` | "applied {key}; could not confirm the key is present afterwards" |

The asymmetry is the decision. **Absence is affirmative evidence the import did not take.
Indeterminate is no evidence at all**, and must not convert a probably-good import into a reported
failure — the concrete case is an unelevated probe of an `HKLM` key that regedit has just written under
elevation. This is the opposite mapping to the *export* path (`WindowsHelper.cs:143`), where
`Indeterminate → Failed` is correct because there the probe is the only evidence supporting the claim.
Same tri-state, opposite direction, for the reason 2a gave: what the evidence has to carry differs.

Presence proves the key exists, not that its values match the backup. The wording therefore stays
"applied" and never becomes "verified" or "restored"; 2a's no-"verified" invariant test is extended
over the new strings rather than relaxed.

**7. Close-before-overwrite is orchestrated centrally. Module signatures do not change.**

No `RestoreContext` parameter and no 23-signature migration. Modules *declare* (Decision 8); the
orchestrator *decides*. `RestoreDispatch.Decide(module, consent, isRunning, closeResult)` is pure and
returns `Run` / `Skip(step)` / `Fail(step)`.

- **Browsers** (`chrome`, `msedge`, `firefox`) require consent. Unchecked → the module is `Skipped`
  with "you chose not to close {browser}, so its profile was not restored", mirroring the backup side
  exactly. There is deliberately **no** "restore over the live browser anyway" option; that is the
  hazard being removed, not a preference being offered.
- **`APinnedApps`** (`StartMenuExperienceHost`) does not require consent: the process auto-respawns
  within seconds, so it is closed **just-in-time immediately before its own dispatch** rather than up
  front, and the dialog carries an informational line instead of a checkbox.
- Consented browsers are closed **before the snapshot runs**, so the snapshot's own
  `BackupAsync` — which contains its own running-process prompt at `BGoogleChrome.cs:32` — finds
  nothing running and never prompts. One consent covers both halves of the operation.
- The dispatch loop re-checks `IsProcessRunning` per consented module and re-closes if the user
  reopened the browser mid-run. Consent persists for the run; the process state does not. **That
  re-check may only narrow the set, never widen it**: which modules are restored at all was already
  settled by `RestoreScope` from the up-front close, and re-deriving it here is the defect recorded
  in "Record of corrections" below. A module the scope refused is refused before the re-check runs.

The close outcome becomes a visible step, folded in via `ModuleResult.Aggregate(closeStep + steps)`.
`Aggregate` remains the single construction path, so a failed close dominates by Rule 2 without any
new rule.

**8. Modules declare what their restore touches, and forgetting is visible.**

```csharp
public virtual IReadOnlyList<RestoreTarget> RestoreTargets => RestoreTarget.Undeclared(GetType().Name);
public virtual IReadOnlyList<RestoreCloseRequirement> ProcessesToCloseBeforeRestore => ...empty;
public virtual bool RestoreMakesChanges => true;
```

The default is a loud `Undeclared` marker, rendered in the confirmation dialog as "(this item does not
declare what it overwrites)". Same philosophy as 2a Decision 1: a forgetful future author produces a
visible wart in the text users read before consenting, not silence. All 23 shipped modules carry real
declarations, and a test enumerates every registered module and asserts none is `Undeclared`.

Per-module `WarningMessage` (`CWiFiConf`, `DPrinters`, `APinnedApps`) is folded into that module's block
in the confirmation text. Today it is shown only while browsing the tree — never at the moment it
applies.

**9. `restore_log.txt` lives in the snapshot folder.**

Not in the restored-from folder. Three reasons: there is exactly one snapshot per restore attempt, so
nothing is ever overwritten; the snapshot folder *is* the rollback artifact, and the record of what
changed belongs beside the thing that undoes it; and `RestPageView` already reads folder logs verbatim,
so surfacing it costs nothing. `BackupLog.Compose` gains an optional `extraHeaderLines` parameter — safe
because its only reader dumps text verbatim — and a thin `RestoreLog.Compose` supplies the restored-from
path, the timestamp, the gate outcome, and the caveat. If the snapshot folder does not exist (the gate
was overridden after a folder-creation failure) the log falls back to `restore_log {timestamp}.txt` in
the restored-from folder; if that also fails it goes to `LogMessage`. It never throws.

**10. `RestartExplorer` kills the shell once, restarts once, and reports.**

`public static ExplorerRestartResult RestartExplorer()`. It reuses `Utils.CloseProcess("explorer")` —
already guarded, bounded, and tested in 2a — then probes: if Windows' own `AutoRestartShell` has already
brought the shell back, it does **not** start another, because a second `explorer.exe` launched against
a running shell opens a stray file-browser window. That is the current N-shells bug in miniature, and
the fix must not reintroduce it at N=2. Otherwise, one guarded `Process.Start`.

The button handler runs it via `Task.Run` — the close carries a 5 s budget that must not freeze the UI
thread — and reports failures in a dialog instead of the current silent log line. The existing
`logger.Log($"...{ex.Message}")` at `WindowsHelper.cs:400` becomes `LogMessage`: it is exactly the
format-string hazard 2a documented, sitting in the one method most likely to produce a brace-bearing
exception message.

**11. The QR timer is marshalled and disposed.**

`MainForm.GetCodeIcons` sets `timer.SynchronizingObject = this`, which moves `QRTimerElapsed` and its
`MessageBox` to the UI thread and gives the dialog an implicit owner — today it can paint *behind* the
main window while the app stays clickable, so the user sees nothing happen and clicks again. A
`FormClosing` handler stops and disposes the timer, which is currently in neither `components` nor any
teardown path.

## Measured facts

Same protocol as 2a: measure before freezing reason strings; where a measurement cannot be taken before
merge, record a waiver showing the affected rule degrades to **under-claiming**, and carry it onto the
smoke matrix.

| # | To measure (elevated Windows 11) | What depends on it |
| --- | --- | --- |
| N1 | `regedit /s` exit codes for missing, truncated, and partially-applied files | Decision 6's wording (carried forward from the 2a waiver) |
| N2 | After killing every `explorer.exe`: does Windows auto-restart the shell, and within what window? | Decision 10's `RestartedByWindows` branch |
| N3 | `StartMenuExperienceHost` respawn latency vs. a typical `LocalState` copy | Decision 7's just-in-time close |
| N4 | Post-import `ProbeKey` on an `HKLM` key from an unelevated run | Decision 6 — must land `Indeterminate`, never a false `Failed` |
| N5 | `regedit /s` of an `HKCU` export while that key is open in another process | Decision 6 — does exit 0 + `Present` hold? |

> **Waiver.** N1–N5 are unmeasured at the time of writing. All five degrade toward under-claiming:
> N1 and N5 already report "applied" rather than "verified"; N2 being wrong wastes at most one stray
> Explorer window, versus N today; N3 being wrong makes the copy `Failed` with a locked-file count,
> which is honest; N4 in the wrong direction under-claims a successful import. Each carries onto the
> manual smoke matrix below.

## The types

```csharp
public enum RestoreTargetKind { RegistryKey, Folder, Command }
public enum RestoreBlock      { None, ConsentWithheld, CouldNotClose }
public enum RestoreAction     { Run, Skip, Fail }
public enum SnapshotVerdict   { Complete, NothingCaptured, ModulesFailed, FolderNotCreated }
public enum ShellOutcome      { RestartedByWindows, Restarted, FailedToStart, NotAttempted }

public sealed class RestoreTarget           // Kind + Path, factory-validated non-empty
public sealed class RestoreCloseRequirement // ProcessName + DisplayName + NeedsConsent
public sealed class RestoreConsentEntry     // one checkbox: ProcessName + DisplayName + Label
public sealed class RestorePlan             // pure: ConfirmationText, ConsentEntries,
                                            //       InformationalCloseLines, FidelityCaveat
public static class SnapshotNaming          // NameFor(DateTime) + Unique(name, exists)
public sealed class RestoreScopeEntry       // Module + Block + BlockedBy + NeedsSnapshot + WillBeRestored
public static class RestoreScope            // For(modules, consented, closedUpFront) -> the one scope
                                            // both halves read; DescribeBlock(entry) -> StepResult
public sealed class RestoreDecision          // Action + CloseStep + JustInTimeClose
public static class RestoreDispatch          // Decide(moduleTitle, requirement, consentGiven,
                                             //        isRunning, closeResult) -> Run | Skip | Fail
                                             // Fold(closeStep, moduleResult) -> ModuleResult
public sealed class ExplorerRestartResult    // CloseResult + ShellOutcome + Error + Describe()

internal sealed class SnapshotDecision       // Verdict + RequiresOverride + Failures + Summary + Describe()
internal static class SnapshotGate           // FolderNotCreated(error); Evaluate(outcomes) -> SnapshotDecision
internal static class RestoreLog             // Compose(...) over BackupLog.Compose; FallbackFileName(when)
```

The last three are `internal`, not `public` as drafted: they traffic in `ModuleOutcome`, which is itself
internal, and a public signature over an internal type does not compile. The test project sees them
through the existing `InternalsVisibleTo("Appcopier.Tests")`, so nothing is lost.

`RestoreDispatch.Decide` takes a **module title plus the values it decides on**, not a `BackupBase`. A
module is a live object with an `IsInstalled()` and a `Backup(path)` on it; taking one would make the
decision table constructible only by instantiating real modules, which is the opposite of what a pure
decision function is for. Every input — consent, running state, close outcome — is passed in, so the
whole table is exercisable off a real machine.

`RestorePlan` exposes `RestoreConsentEntry` (the deduplicated per-process checkbox) alongside
`InformationalCloseLines` (the closes the user is told about but not asked about). They are separate
properties because a checkbox is a question, and there is nothing to decline about a process that comes
straight back on its own.

Every 2b outcome flows through the existing `StepResult` / `ModuleResult.Aggregate` / `RunSummary`
machinery. No new result-construction path is introduced, and `ResultState` gains no `Partial`.

## Snapshot naming

`Data.NowShort` is stamped **once at process start** (`DataHelper.cs:34`). Reusing it would write the
snapshot into the same folder as this session's backup, and two restores in one session would collide
with each other. `SnapshotNaming.NameFor` therefore takes a fresh `DateTime` and includes seconds:

```
yyyy-MM-dd - HH.mm.ss (pre-restore)
```

Seconds because two restores inside one minute is the *expected* pattern when the first goes wrong.
`Unique(name, exists)` appends " (2)", " (3)" on collision. The "(pre-restore)" suffix makes the folder
self-describing in `RestPageView`, which lists directory names verbatim.

## The restore pipeline

`ConfPageView.HandleRestorationAfterSelection()` becomes eight stages. `RestPageView.btnOK_Click` is
untouched — it sets the path, switches view, and awaits, which is what keeps the confirmation on the UI
thread ahead of any dispatch to the pool.

1. Build `RestorePlan` from `selectedConfigs`, `CurrentRestorePath`, and a fresh snapshot name.
2. Show `RestoreConfirmForm`. Cancel → log line, return. **Nothing is created before consent.**
3. Close consented browsers once, up front (`Task.Run` per process), then build the run's
   `RestoreScope` from that one close result. Declined or unclosable → the module leaves the snapshot
   set *and* is refused at dispatch, from the same entry. Snapshotting a locked profile only
   manufactures a gate failure; restoring one that was not snapshotted is the defect below.
4. Create the snapshot folder and run the backup pipeline over the scope's snapshot set.
5. `SnapshotGate` — proceed, or require the override confirmation of Decision 5.
6. Dispatch loop, with `RestoreDispatch.Decide` ahead of each module.
7. Write `restore_log.txt`.
8. Show the `RunSummary` (unchanged mechanics).

The whole pipeline gets the disable-form/`finally` guard `btnBackup_Click` already carries
(`ConfPageView.cs:101-119`). Today a second restore can be started while the first is running.

## Module migration by shape

| Shape | Modules | `RestoreTargets` | Close requirement |
| --- | --- | --- | --- |
| S1 single-key | the 10 `RegistryModule` subclasses | `RegistryKey(Key)` — implemented **once**, in `RegistryModule` | — |
| S2 multi-key | `WPersonalization`, `WTelemetry`, `WUpdates`, `DPrinters`, `GGaming` | one per `Keys` entry | — |
| S3 folder | `APinnedApps` | `Folder(Folder)` | `StartMenuExperienceHost`, no consent |
| S4 browser | `BGoogleChrome`, `BMicrosoftEdge`, `BMozillaFirefox` | `Folder(Folder)` | consent required |
| S5 mixed | `WThemes` | its two folders + its `Keys` | — |
| S6 netsh | `WNetworkConf`, `CWiFiConf` | `Command(description)` | — |
| S7 winget | `AStoreApps` | `Command(...)` + `RestoreMakesChanges => false` | — |

## Testing

1. `ImportRegistryKey`'s three read-back rows, the unknown-hive fallback, and the extended
   no-"verified" invariant, using the existing `FakeTool` seam plus an injected probe delegate.
2. `ExplorerRestartResult.Describe()` across every `{CloseResult × ShellOutcome}` pair, and the pure
   restart decision function as a table.
3. Every registered module: no `Undeclared` target, browsers declare consented closes,
   `AStoreApps.RestoreMakesChanges == false`, each `RegistryModule` subclass's target equals its `Key`.
4. `RestorePlan`: every title, key, and folder path present; folded `WarningMessage`s; snapshot name;
   the caveat verbatim; consent entries produced exactly for `NeedsConsent` processes and deduplicated
   across modules; the `Undeclared` marker renders.
5. `SnapshotNaming` format, freshness, and collision suffixing.
6. `SnapshotGate` rows: folder-create failure, any `Failed`, all-`Skipped`, empty snapshot set.
7. `RestoreDispatch.Decide` full table, and that folding a failed close step preserves Rule 2.
8. `RestoreScope`, over every combination of module shape × consent state × `CloseResult`: the
   invariant that **a module the snapshot leaves out is a module the restore refuses** — for every
   entry, `NeedsSnapshot == false && RestoreMakesChanges == true` implies `WillBeRestored == false`.
   Asserted as a sweep rather than a row list, because the defect it guards was not any single wrong
   row: each half was right on its own, and only the combination was wrong. A test that enumerated the
   cases someone thought of would have passed on the broken code. Plus `DescribeBlock`'s two wordings,
   which must read identically to `RestoreDispatch`'s own refusals — same refusal, different evidence.
9. `RestoreLog.Compose` content rows, the without-snapshot variant, and the fallback filename.
10. `BackupLog.Compose`'s `extraHeaderLines` extension, with the existing format as a regression row.

**The gap.** The suite covers the decision logic — plan composition, gate rules, dispatch decisions,
naming, log composition, read-back classification — and **none of the evidence those decisions
consume**. Every close, kill, import, and probe in production comes from process and OS calls the suite
fakes. The dialog itself, the thread affinity of the confirmation, and Explorer/Start-menu process
behaviour are observable only on real elevated hardware. This is the same gap 2a recorded, moved one
layer along; the compensating verification is the same shape.

**Compensating verification — manual elevated smoke matrix:**

| # | Scenario | Expected |
| --- | --- | --- |
| 1 | Restore with Chrome running, consent checked | Chrome closes once; no prompt during the snapshot; profile restored; close step in `restore_log.txt` |
| 2 | Same, consent unchecked | Module `Skipped` with the backup-mirror wording; Chrome untouched |
| 3 | Cancel the confirmation | No snapshot folder created; nothing changed; log line only |
| 4 | Force a snapshot failure (deny write on `app\`) with Chrome consented and running | Override dialog appears with No focused; No → "did not run" summary that also **names Chrome as having been closed to take the snapshot** — the user gave up an open browser for a restore that then did not happen |
| 4b | Restore with a large Chrome tree consented, so the close overruns its 5 s budget | The module is treated one way in both halves: either snapshotted **and** restored, or refused in both. Never restored without a snapshot folder containing it (the defect in "Record of corrections") |
| 5 | Override with Yes | Restore proceeds; `restore_log.txt` records the missing snapshot |
| 6 | Two restores in one session | Two distinct "(pre-restore)" folders |
| 7 | Restore the "(pre-restore)" folder itself | Rollback works end-to-end through the normal flow; both logs shown |
| 8 | Registry restore, elevated then unelevated | "key is present after the import"; unelevated `HKLM` shows "could not confirm", never a false `Failed` (N4) |
| 9 | Restart Explorer button | Exactly one shell returns, zero stray windows (N2, both branches). The button hides **only** when a shell actually came back; on `FailedToStart` it reports the failure in a dialog and stays visible, so the retry is still available |
| 10 | `APinnedApps` restore | Start menu blinks, respawns, layout applied (N3) |
| 11 | QR hover; then close the app with the timer pending | Dialog in front and owned; no crash on close |

## Risks

- **Restore runtime roughly doubles**, since every restore now runs a backup first. Bounded by Decision
  3; the worst remaining case is a browser profile copy the user explicitly consented to protect.
- **Confirmation fatigue.** This phase adds the app's first blocking restore dialog. If it grows noisy
  users will click through it, which would leave them worse off than no dialog — they would have
  consented without reading. Mitigation: one screen, terse module lines, caveat and consent visually
  dominant.
- **Explorer double-shell** if N2's detection races the auto-restart. Bounded to one stray window by
  probe-before-start; today's behaviour is N windows.
- **`RestoreMakesChanges` set wrong on a future module** silently exempts it from snapshotting. The
  enumerating test forces a deliberate declaration; the spec flags it as an `absenceIsNormal`-class call.
- **Re-aggregation wording drift**: folding a close step into a module's steps moves single-step modules
  onto the "completed N operations" branch. Acceptable, but the `Failed`-dominates path is asserted
  explicitly so the fold cannot quietly change severity.
- **N1 remains unmeasured** from the 2a waiver. Read-back wording rests on assumed `regedit /s`
  semantics; the degradation is under-claiming, and the measurement is on this phase's matrix.

## Record of corrections

Kept so overturned claims are not reintroduced.

- *"Rollback should delete keys before re-importing the snapshot."* Rejected as the default: it adds
  registry deletion — a new destructive primitive — to the phase whose purpose is to make destructive
  operations safe. The additive-merge limitation is disclosed instead (Decision 2).
- *"The snapshot can reuse `Data.NowShort`."* Wrong. It is stamped once per process, so it would write
  into this session's backup folder and collide across restores.
- *"Ask per module whether to close the browser."* Rejected: serial prompts from module code is the
  worker-thread dialog pattern this phase removes, and it splits consent from the caveat.
- *"The snapshot set and the dispatch decision can each read the process state for themselves."* This
  was never written as one claim, which is why it survived review: Decision 3 said declined and
  unclosable modules leave the snapshot set, and Decision 7 said the dispatch loop re-checks
  `IsProcessRunning` per consented module. Both are individually defensible. Together they are a hole.
  `Utils.CloseProcess` reports `StillRunning` whenever its shared five-second budget expires — routine
  for a Chrome tree of twenty-odd processes — and those processes are gone seconds later. So the module
  was dropped from the snapshot on the up-front reading, and then, tens of seconds later, found
  not-running by the fresh reading and **restored anyway**: a live profile overwritten with nothing on
  disk to undo it, while `restore_log.txt` recorded that a snapshot had completed. The two halves were
  not disagreeing about a decision; they were disagreeing about a fact, and the more dangerous reading
  won because it was the one taken last.

  The correction is that **one decision serves both halves**. `RestoreScope.For` is taken once, from
  the up-front close, and both the snapshot set and the dispatch loop read the same entries. The
  invariant it enforces: *a module the snapshot leaves out is a module the restore refuses.* The
  per-module re-check of Decision 7 survives, but only as a further refusal — it can fail a module the
  scope allowed, never rescue one the scope refused, because a module the scope allowed has a snapshot
  on disk and a module it refused does not.

- *"Fixing the snapshot SET was enough."* It was not, and the same defect was still reachable one
  layer down. `RestoreScope` settles which modules the snapshot must capture; nothing checked what the
  snapshot then **reported**. `SnapshotGate.Evaluate` counted `Succeeded`, collected `Failed`, and
  silently dropped `Skipped` — so a module could be in the snapshot set, come back `Skipped`, and be
  restored with the gate reporting `Complete` and `restore_log.txt` stating that a snapshot completed.

  The reachable chain ran through module code, which is why reviewing the pipeline alone missed it.
  The snapshot calls `BackupAsync`, and the three browser modules prompt there when they find their
  process running — a process this pipeline had already closed, but which relaunches during a profile
  copy (background mode, or the user reopening it). That prompt is a `MessageBox` raised from a
  thread-pool thread while the window is disabled, so it paints behind the app; answering "no" returns
  `Skipped`, which the gate dropped.

  Both halves are corrected. Modules no longer prompt when the caller has already taken consent
  (`BackupBase.AllowPrompts`), which the snapshot sets — so during a snapshot a browser module can
  only succeed or fail, never decline. This is the rule this spec already stated for the restore path,
  now also true of the snapshot that precedes it. And the gate reports every skip by name rather than
  dropping it, with `PartiallyCaptured` separated from `Complete`: a run that saved four of five items
  must not read the same as one that saved all five.

- *"An empty snapshot set means nothing needed capturing."* Two different facts share that shape.
  Selecting only Chrome and leaving its consent box unticked empties the set because everything was
  **refused**, and the gate then told the user "none of the selected items change anything when
  restored" — false, and in the log as well as the dialog. `RestoreScope` knows which it is, so the
  blocked count is passed to the gate and the two cases now read differently.

- *"Consent is enough to justify closing the application."* It is not, and this phase made the case
  worse rather than better. What the user ticks in the tree is independent of what the chosen backup
  folder contains — nothing cross-checks them — so a user who backed up Settings only, then later
  ticks Chrome along with everything else, had Chrome force-killed with every open tab lost, a full
  copy of their live profile written into a snapshot they never asked for, and then a summary line
  reading "nothing was backed up for this item". Before this phase nothing closed the browser at all,
  so consenting to a close made them strictly worse off than not being asked.

  The correction is that a module states whether the backup folder holds anything for it
  (`BackupBase.HasBackupIn`, default `true`), and `RestoreScope` refuses it **first** — ahead of the
  consent and close checks, because it is the one refusal that costs the user nothing. The process is
  then never closed, the profile never snapshotted, and the module reports the same
  "nothing was backed up for this item" it always did, only now before anything was destroyed to
  learn it. The default is `true` deliberately: a module that has not been taught to check must not
  be silently skipped, since being wrong that way costs one unnecessary close while being wrong the
  other way cancels a restore the user asked for.

- *"The suite's intermittent test host crash was thread-pool starvation under parallel collections."*
  Wrong, and the workaround built on it (an assembly-wide `DisableTestParallelization`) was removed
  rather than kept. `LogHelper`'s target is static, and `LogHelperTests` set it to a `RichTextBox` and
  never cleared it, so every later test logged into a control on a thread with no message pump —
  `Invoke` waiting on a pump that never runs, and a dead host if the control was finalized meanwhile.
  It aborted roughly two runs in five, always with a `CopyFolderTests` locked-file test in flight,
  because those log once per failed file. Reproduced on `main`, so it predates this branch. With the
  leak fixed the suite is stable parallel and about three times faster.

- *"Post-import `Indeterminate` should fail, like the export path."* Wrong, and backwards: on export the
  probe is the only evidence; post-import, exit code 0 already supports "applied". Failing there would
  report a false failure on every unelevated `HKLM` import — the cry-wolf direction.
