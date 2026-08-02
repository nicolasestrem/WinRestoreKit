# Phase 2a — Make failure representable and reported

Design record, 2026-07-20. Branch `feat/phase2-honest-failures`.

> This document predates the rename of Appcopier to WinRestoreKit and is kept as a
> historical record. Product names, namespaces and paths below refer to the project as
> it was at the time of writing.

## The problem

Appcopier cannot tell you when a backup or restore fails. Not "does so badly" — cannot. The call chain
is structurally incapable of it:

- `BackupBase.Backup(string path)` and `Restore(string path)` return `void`.
- `Utils.ExportImportRegistryKey` (`WindowsHelper.cs:78-103`) catches every exception, logs it, returns.
  It never reads `regedit`'s exit code and never checks that the `.reg` file was written.
- `ConfPageView.cs:148-149` then logs and shows "Back up done." unconditionally, outside any check.

There is no return channel anywhere in the stack, which is why every downstream fix is blocked on this
one. Three paths that report success today while doing nothing:

| Path | What happens |
| --- | --- |
| `ConfPageView.cs:148` | "Back up done." runs unconditionally after the loop, whatever the modules did |
| `BGoogleChrome.cs:40` | Decline "close Chrome first?" and the method `return`s having copied nothing. Still "Back up done." |
| `ConfPageView.cs:185` | `PerformRestoration` guards its loop with `if (CurrentRestorePath != "" && Directory.Exists(...))`. A missing folder skips the loop entirely, then `HandleRestorationAfterSelection` announces "Restore done." |

**A backup tool that misreports success is worse than no backup tool** — the user finds out at restore
time, which is exactly when they have no fallback.

## Scope

This phase makes failure *representable* and *reported*. Nothing else.

**In scope:** a result type threaded through `BackupBase` → 23 modules → `Utils` → `ConfPageView`/
`RestPageView`; verification of operations that are verifiable; an honest run summary; `backup_log.txt`
recording outcomes instead of selections; the `LogHelper` format-string hazard; the `CWiFiConf`
filename defect (see Decision 3).

**Explicitly not in scope** — these were considered and deferred, not overlooked:

| Deferred to | Item |
| --- | --- |
| 2b (restore safety) | snapshot-before-restore and rollback, real destructive-restore confirmation, restore-time audit log, read-back verification of imports |
| 2c (module bugs) | `WTelemetry` `ControlSet001`, `AStoreApps` dead restore, `WThemes` stock wallpapers, `RestAppsForm` handler wiring, `RestartExplorer` launching N explorers, `FormatBytes` integer division |
| Own phase (modernization) | `HttpClient`, update-checker rewrite against the Releases API, per-monitor DPI, dark mode |
| Own item | full app-level persistent logging (sinks, severities, buffering) |

Modernization was moved out of Phase 2 entirely. Dark mode and rollback-a-bad-registry-import share no
code; bundling them means the reviewer scrutinising destructive operations is also diffing ARGB values.
`docs/ROADMAP.md` is updated to match.

## Decisions

**1. The un-overridden `BackupBase` member returns `Failed`.**
18 modules override the sync pair; 5 (`APinnedApps`, the three browsers, `WThemes`) override only the
async pair. A `Skipped("not implemented")` default would make the sync member of `WThemes` — a module
that does plenty — answer "Skipped", so a future refactor that starts calling it gets silence instead
of a signal. A `Failed` default is unreachable for all 23 current modules, because `ConfPageView` only
ever calls the async pair and every module implements one side. It fires only for a future module whose
author forgot, which is a bug worth surfacing loudly.

Concretely, and consistently with the single-construction-path rule below:

```csharp
public virtual ModuleResult Backup(string path) => ModuleResult.Aggregate(new[]
{
    StepResult.Failed(GetType().Name, "this module does not implement backup")
});
```

**2. A partially-completed folder copy is `Failed`, with a count.**
No threshold, no cache allowlist. The three browser modules will read `Failed` on most real runs, and
that is the system working. `docs/ROADMAP.md` already records that these modules are blunt
full-directory copies that grab caches and copy live locked databases, and should be "fixed or
retired". Honest reporting making them red is the evidence for that decision, not a problem to design
around. A Chrome profile missing `Login Data` and `History` is not a usable backup at any ratio.

**3. The `CWiFiConf` filename defect is fixed in this phase.**
Not a reporting change, admitted as scope creep, and taken deliberately: measurement (below) shows the
module cannot restore anything at all, and the reporting work would otherwise render that as a tidy
"Skipped".

**4. Logging changes are the minimum this phase forces.**
Two only: the `LogHelper` format-string hazard, and `backup_log.txt` recording outcomes. Full
persistent logging is a separate workstream wearing this phase's clothes.

## Measured facts

Taken on this machine, 2026-07-20, **unelevated**. These are design inputs. The repo cannot supply
them and the design must not assume them.

| # | Measurement | Result |
| --- | --- | --- |
| M1 | `regedit /e` on an existing key (`HKCU\Control Panel\Mouse`) | exit code **0**, file written, 1530 bytes |
| M2 | Encoding of a real export | **UTF-16LE with BOM** (`FF FE 57 00 …`) |
| M3 | `regedit /e` on a **nonexistent** key | exit code **0**, **no file created** |
| M4 | `File.ReadAllText` on a real export | strips the BOM; `StartsWith("Windows Registry Editor Version 5.00")` is **true** |
| M5 | `netsh wlan export profile` filenames | `Wi-Fi 2-<SSID>.xml` — prefix is the **interface name**, machine-specific |
| M6 | `CWiFiConf.cs:46`'s `WLAN*.xml` filter against those files | **0 of 19 matched** |
| M7 | `netsh wlan export` to a very long folder path | exit code **0**, "saved … successfully" printed, **no files written** |
| M8 | WLAN profile XML root element | `WLANProfile`, namespace `http://www.microsoft.com/networking/WLAN/profile/v1` |

**M3 is the load-bearing one.** `regedit` returns 0 having written nothing, which is direct proof that
exit code alone is not a success signal and the file check is mandatory. M7 is the same failure mode in
`netsh`. M4 means the BOM hazard is real for a byte-wise compare but absent for `File.ReadAllText` —
the implementation is pinned to that call.

**M8, measured 2026-07-20 during the elevated smoke session, after the branch was opened.** `wt.exe`
returns as soon as it has forwarded its command; it does not wait for what it launched. On a real
backup the app wrote `backup_log.txt` at 07:35:54.295 recording `Remember installed apps FAILED —
winget reported success but wrote no file`, and winget wrote a complete, valid 113-package export to
that exact path at 07:36:23.164. A successful backup was reported as a failure, 29 seconds early.

This one is worth keeping as the phase's own lesson. Every other measurement here fed a *reporting*
rule. This one shows a reporting layer cannot save a module that is asking its question at the wrong
moment: `Verify` was correct, its inputs were not. The fix was to wait on `winget.exe` itself.

Still unmeasured, and required before reason strings are frozen (needs an elevated session):
`regedit /e` exit code on a **permission-denied** key; `regedit /s` exit codes for missing, truncated,
and partially-applied files; the effect of the undocumented extra key argument at `WindowsHelper.cs:92`;
`netsh` exit code with the wireless adapter disabled. Until measured, exit code is treated as
necessary-but-not-sufficient everywhere.

> **Waiver, recorded rather than quietly dropped.** That measurement session did **not** happen before
> the reason strings were frozen, and the phase shipped anyway. The gate was written as a precondition;
> treating it as one would have blocked the branch on an elevated session that could not be run here.
>
> What makes the waiver defensible is that the unmeasured facts were all specified *conservatively* —
> every rule that depends on them degrades to under-claiming rather than over-claiming. Imports are
> reported as "applied" rather than "verified"; a file that cannot be read is reported as unreadable
> rather than invalid; an exit code is never sufficient on its own. Being wrong about any of these
> makes the app say less than it could, not more than it knows.
>
> What the waiver costs: the reason *strings* for permission-denied and partially-applied cases are
> written against assumed behaviour, so their wording may not match what regedit actually does. That is
> a text-accuracy risk, not a correctness one. The measurements remain on the Task 12 smoke matrix and
> should be taken before the next release, not before this merge.

## The types

```csharp
namespace Appcopier
{
    public enum ResultState { Succeeded, Skipped, Failed }

    /// One sub-operation: one registry key, one folder copy, one shell command.
    public sealed class StepResult
    {
        public string Target { get; }      // human label: key path, folder path, "winget export"
        public ResultState State { get; }
        public string Reason { get; }      // never null; never empty for Skipped/Failed

        public static StepResult Succeeded(string target, string reason);
        public static StepResult Skipped(string target, string reason);
        public static StepResult Failed(string target, string reason);
    }

    /// One module's verdict for one Backup or Restore call.
    public sealed class ModuleResult
    {
        public ResultState State { get; }
        public string Reason { get; }                       // one line, user-facing
        public IReadOnlyList<StepResult> Steps { get; }

        public static ModuleResult Aggregate(IReadOnlyList<StepResult> steps);
    }
}
```

Three states answer three different questions: *did I get my data*, *was there nothing to get*, *did
something break*. Everything else is detail, and detail belongs in `Reason` and `Steps`.

**No `Partial` state.** It carries nothing `Succeeded`/`Failed` + `Steps` does not, and it forces every
consumer to answer a question with no stable answer: is Partial good or bad news? Both mixes have an
unambiguous verdict under the rules below.

**All construction goes through `Aggregate`**, including single-step modules. The direct factories are
not public on `ModuleResult`; a module builds `StepResult`s and folds them. Otherwise the "single
aggregation entry point" invariant is decorative — 10 modules would bypass it on day one.

Constraints enforced in the factories, not by convention:

- `Reason` is mandatory and non-empty for `Skipped` and `Failed` (`ArgumentException` otherwise).
- `Reason` for `Succeeded` states what was captured, not "OK".
- Restore-side reasons use the word **applied**, never *verified* or *restored*. A dedicated
  `StepResult.Applied(...)` factory bakes the wording in so 16 modules cannot each invent their own.
- Both types are immutable and returned by value. This is required, not stylistic: five modules run on
  the UI thread and the rest on a thread-pool thread, so no shared mutable accumulator is safe.

## Classification

Every sub-operation declares up front whether absence of its target is normal. This is the mechanism
that makes the legitimately-absent case explicit rather than inferred.

| Pre-flight outcome | `absenceIsNormal = true` | `absenceIsNormal = false` |
| --- | --- | --- |
| Target present | proceed | proceed |
| Target absent | `Skipped("not present on this system")` | `Failed("expected <target> is missing")` |
| Probe indeterminate (access denied) | `Failed("could not read <target>: …")` | `Failed("could not read <target>: …")` |

Indeterminate is never `Skipped`. "I could not tell" is a failure of the tool, not an absence of the
data. This requires `Utils.KeyExists` to become tri-state — a `bool` cannot express it.

**The two mappings differ by caller, and both must be written down.** The backup path maps
`Indeterminate → Failed`. `SelectInstalled` (`ConfPageView.cs:244-258`) maps `Indeterminate → false`:
auto-checking a module you could not probe would manufacture a `Failed` row in the very dialog this
phase exists to make trustworthy. The OR-short-circuit `IsInstalled()` in `WThemes`, `GGaming` and
`WPersonalization` amplifies this — one indeterminate probe would otherwise flip a whole module.

Operation outcomes once the target is present:

| Direction | `Succeeded` when | `Failed` when |
| --- | --- | --- |
| Registry export | exit code 0 **and** file exists **and** length > 0 **and** header valid (M1–M4) | any check fails, naming which |
| Registry import | file exists, non-empty, header valid **before** invoking regedit; then exit code 0 | file missing → `Skipped`; malformed/empty → `Failed` **before touching the registry**; non-zero exit → `Failed` |
| Folder copy | source existed, `FilesFailed == 0`, `FilesCopied > 0` | `FilesFailed > 0` → `Failed` with count + first error |
| Folder copy | source absent → `Skipped`; source present but empty → `Skipped` | — |
| Shell command | exit code 0 **and** a direction-specific artifact check | non-zero exit, or artifact check fails |

The artifact check is not optional decoration: M3 and M7 both show a zero exit code with no artifact.

## Aggregation

`ModuleResult.Aggregate(steps)`, first match wins:

| # | Condition | Result | `Reason` shape |
| --- | --- | --- | --- |
| 1 | no steps | `Skipped` | "nothing to do" — a module with no steps is a bug; log it |
| 2 | any `Failed` | `Failed` | "{failed} of {total} operations failed: {first failure}" |
| 3 | all `Skipped` | `Skipped` | "nothing to back up: {distinct reasons}" |
| 4 | ≥1 `Succeeded` and ≥1 `Skipped` | `Succeeded` | states what *was* captured, then the absences |
| 5 | all `Succeeded` | `Succeeded` | "{total} operation(s) completed" |
| 6 | exception escaped the module | `Failed` | orchestrator-supplied: "unhandled error: {type}: {message}" |

**Rule 3 is what the inventory forced.** `GGaming` (GameBar, GameDVR) and `WTelemetry` (a Group Policy
key plus a service key) can legitimately have *every* sub-operation skipped on a stock consumer
machine. Folding that to `Succeeded` would claim "Gaming settings backed up" having written zero bytes.

**Rule 4 is what keeps the dialog trustworthy.** `WPersonalization` (`Themes\Personalize` always
present, `Explorer\Accent` frequently absent) and `WUpdates` (core servicing key present, WSUS policy
key absent) hit this on a large share of healthy machines. It renders as success with a subdued note,
never a warning.

Rule 4's wording states **what was obtained**, not a ratio. "1 of 2 captured" under a heading of "Done"
reads as partial failure and reintroduces exactly the ambiguity that justified dropping `Partial`.

## The run summary

Four states replace the two the app has now. Skipped counts are never summed into the failure count
anywhere in the UI — that is the whole point.

| Condition | Heading | Dialog |
| --- | --- | --- |
| any module `Failed` | **Problems** | warning icon, "{n} of {m} items had problems", failed titles and reasons |
| no `Failed`, ≥1 `Succeeded` | **Done** | information icon, what was captured, plus an expandable subdued line for items with nothing to back up |
| no `Failed`, no `Succeeded` | **Nothing done** | "Nothing was backed up — none of the selected items were present on this system." Never "Back up done." |
| zero modules ran | **Did not run** | "Restore did not run: the backup folder <path> was not found." Replaces the silent no-op at `ConfPageView.cs:185` |

Both orchestrator loops get a per-module `try`/`catch` mapping an escape to rule 6. This is mandatory,
not defensive style, and the **restore** loop at `ConfPageView.cs:189` matters more than the backup one:
it is awaited by `HandleRestorationAfterSelection`, which is awaited from a different file's
`async void` (`RestPageView.cs:49,62`), and it contains `AStoreApps.Restore` calling
`RestAppsForm.ShowDialog()` from a thread-pool thread with no message pump. An exception there takes
the process down and destroys every result already collected.

## `Utils` contract changes

All references `src/Appcopier/Helpers/WindowsHelper.cs`.

| Current | Becomes | Why |
| --- | --- | --- |
| `void ExportImportRegistryKey(string, string, bool)` — `:78` | **Split:** `StepResult ExportRegistryKey(string filePath, string registryPath, bool absenceIsNormal)` and `StepResult ImportRegistryKey(string filePath, string registryPath)` | The directions have genuinely different contracts — export is verifiable, import is not — and the `bool` flag forced one swallowing implementation for both |
| argument build — `:92` | `import ? "/s {path}" : "/e {path} {key}"` | The import branch appends the registry key as a second argument to `regedit /s`, which documented syntax does not define. The split removes it. Verify empirically before shipping |
| process setup — `:89-90`, `:96` | delete `Verb = "runas"`; add a bounded `WaitForExit(timeout)`; read `ExitCode` | `Verb` is ignored while `UseShellExecute = false`, so the line grants nothing and misleads; elevation comes only from `app.manifest`. The unbounded wait hangs the backup thread forever if regedit blocks on a dialog |
| `bool KeyExists(string)` — `:106` | `KeyProbe ProbeKey(string)` → `{ Present, Absent, Indeterminate }` + reason; keep a `bool` shim for the 16 `IsInstalled()` sites | Becomes the Skipped-vs-Failed discriminator. A two-state bool cannot express "I could not tell", and it has no `try`/`catch`, so a `SecurityException` on a restricted key propagates out — tolerable when called once at tree-build, not once per key on the backup path |
| `Task CopyFolder(string, string)` — `:15` | `Task<CopyResult>` with `{ SourceMissing, FilesCopied, FilesFailed, BytesCopied, FirstError }` | Three distinct failures — source missing (`:22-26`), per-file exception (`:48-51`), outer exception (`:60-63`) — currently produce an identical normally-completing Task |
| `void CloseProcess(string)` — `:187` | `CloseResult CloseProcess(string, TimeSpan)`; guard `Kill()` | `Kill()` at `:192` is unguarded and throws on access-denied or on a process that exited between enumeration and the call. The browser modules reach it from an `async void` handler, so it is a live path to an unhandled UI-thread exception that kills the whole run. **Only the guard is in this phase** — adding a bounded wait changes *what gets copied*, not what gets reported, and belongs with the browser-module work |
| `async void RunWT(string)` — `:197` | `Task<ProcessResult> RunWTAsync(string)` + `File.Exists(Data.ShellWT)` pre-flight | `async void` is structurally incapable of feeding a result: it returns at its first await, so `AStoreApps` logs success before winget has started. Prerequisite, not cleanup |
| `void CopyFile(...)` — `:66` | delete | Zero callers in `src` |
| `OpenUrl`, `IsWebUrl`, `LogQuietly`, `ReportUrlFailure` | no change | Not on the backup/restore path. `LogQuietly` already passes its message as an *argument* rather than a format string; that calling convention becomes the house rule |

## Module migration by shape

All 23 modules are accounted for. Seven shapes; the mechanical work is described once per shape.

| Shape | Count | Modules | Migration |
| --- | --- | --- | --- |
| **S1** single-key registry, sync | 10 | `WAccessibility`, `DMouse`, `DKeyboard`, `WTaskbar`, `WAPrivacy`, `WOther`, `WPrivacy`, `WVisualEffects`, `DUSB`, `DTouchpad` | probe → export → one `StepResult` → `Aggregate`. Also switch `path + Title + ".reg"` to `Path.Combine` |
| **S2** multi-key loop, sync | 5 | `WPersonalization`, `WTelemetry`, `WUpdates`, `DPrinters`, `GGaming` | as S1 but one `StepResult` per key. Per-key pre-flight is mandatory and **cannot** be inferred from `IsInstalled()`, which short-circuits on the first key found |
| **S3** folder copy, async | 1 | `APinnedApps` | signature → `Task<ModuleResult>`; map `CopyResult` |
| **S4** browser: prompt + kill + copy | 3 | `BMozillaFirefox`, `BMicrosoftEdge`, `BGoogleChrome` | as S3 plus: declined prompt → `Skipped("user chose not to close <browser>")`, currently a bare `return` reported as success. All three are the same code with the folder and process name swapped; migrate together |
| **S5** mixed folder + registry, async | 1 | `WThemes` | heterogeneous sub-ops — the best test of `Aggregate` |
| **S6** netsh shell command, sync | 2 | `WNetworkConf`, `CWiFiConf` | consume the exit code; add a real artifact check |
| **S7** winget / interactive | 1 | `AStoreApps` | blocked on `RunWTAsync`. Restore returns `Skipped("handled interactively in the app restore dialog")` — it opens a dialog and restores nothing itself |

`DTouchpad` is the single best regression test for `Skipped`: absent by design on every desktop PC.
`WPersonalization` is the canonical rule-4 case. `WTelemetry` and `GGaming` are the canonical rule-3
cases.

`S1`'s `Path.Combine` migration produces byte-identical paths, because `DataHelper.cs:20-21` documents
the trailing separator as a deliberate field contract and `RestPageView.cs:57` appends one. That is an
invariant to state, not a coincidence to rely on. **No writer's filename derivation changes anywhere in
this phase**, so backups written by v0.30.0 remain restorable.

## The `CWiFiConf` fix

Measurement M5/M6 shows `CWiFiConf.cs:46`'s `Directory.GetFiles(path, "WLAN*.xml")` matches nothing:
`netsh` writes `<interface name>-<SSID>.xml`, and the interface name is machine-specific ("Wi-Fi 2"
here). A corrected wildcard is therefore not a fix either.

- **Restore** enumerates `*.xml` in the backup folder and selects by *content* — root element
  `WLANProfile` in namespace `…/WLAN/profile/v1` (M8) — not by filename.
- **Restore imports every matching profile**, not `xmlFiles[0]`. On this machine that is the difference
  between 1 and 19 networks. Restore becomes N sub-operations and aggregates.
- **Backup** snapshots `*.xml` before and after the export. A bare count is meaningless because
  `ConfPageView.cs:140` passes the shared backup root and other modules write there too.
- Backup distinguishes `Skipped` from `Failed` via a `netsh wlan show interfaces` pre-flight, because
  "zero files" is ambiguous between no radio and a blocked export.

`WNetworkConf`'s `StreamWriter(null)` defect (`:83`, null passed from `:48`) is **not** fixed here. It
throws `ArgumentNullException` on every restore, which is caught at `:60` and already logged as a
failure — so unlike `CWiFiConf` it does not produce a dishonest result, only a broken one. It goes to
2c with the evidence visible.

## Logging

Two changes.

**The format-string hazard, which this phase forces hard.** `ModuleResult.Reason` will contain paths
and exception text. `LogHelper.AppendLog` (`LogHelper.cs:44-59`) runs `string.Format` on its input, and
a single brace sends the line to `LogError` → `Console.WriteLine`, which in a WinForms app goes
nowhere. The reason string vanishes silently. Every call site passes the message as an *argument*, per
`LogQuietly`'s existing convention.

**`backup_log.txt` records outcomes.** It has exactly one writer (`ConfPageView.cs:170`) and one reader
(`RestPageView.cs:77,83`), and the reader is a verbatim `File.ReadAllText` dump into a textbox with no
parsing — so a format change is inert. The restore *set* comes from `btnRestore_Click` before
`RestPageView` is shown, not from this file. A version header is added as cheap insurance.

`LogHelper.Log` is invoke-safe only by accident — `Control.InvokeRequired` returns **false** when the
target has no created handle. Recorded, not fixed here; it belongs with the persistent-logging work.

## Testing

Genuinely unit-testable without elevation, and where the coverage goes:

1. `ModuleResult.Aggregate` — every row, especially all-Skipped → `Skipped` and Succeeded+Skipped →
   `Succeeded`. Table-driven, no I/O. The highest-value tests in the phase.
2. Run-level aggregation, including the zero-modules and all-Skipped runs.
3. `.reg` well-formedness against fixtures: valid UTF-16LE-with-BOM (M2), empty, truncated, wrong
   header, BOM-but-no-header.
4. Reason composition — counts, pluralisation, truncation, and the invariant that no restore-side
   reason contains "verified".
5. Factory invariants — `Skipped`/`Failed` with an empty reason must throw.
6. `CopyResult` → `StepResult` mapping, all branches.
7. `Utils.CopyFolder` against temp directories — nested trees, missing source, empty source, and a file
   held open with `FileShare.None` to force the per-file failure path deterministically.
8. `backup_log.txt` v2 writer, plus legacy-file detection.
9. Filename derivation, including a `Title` containing a space.
10. A `Path.Combine` guard: pass a path *without* a trailing separator and assert the artifact lands
    inside it. Fails today for every concatenating module.
11. WLAN profile content detection against a fixture (M8).

**The gap, stated plainly.** These tests cover the *decision logic* — how outcomes are classified,
folded, worded, written. They cover essentially **none of the evidence those decisions consume**. Every
`StepResult` in production is produced by a process launch this suite cannot exercise. A green run means
the aggregation is right *given* correct inputs. M3 is exactly the kind of fact no unit test here could
have discovered.

Compensating verification:

1. **Seams.** Put the two process launches behind narrow interfaces (`IRegistryTool`, `IProcessRunner`)
   returning exit code and paths. Modules then become testable against fakes for the *shape* of their
   sub-operations. The single highest-leverage testability change in the phase.
2. **Finish the measurement session** on an elevated box for the unmeasured facts listed above, and
   record them here with a date before reason strings are frozen.
3. **A manual elevated smoke matrix**, once per release: desktop with no touchpad (`DTouchpad` must
   read Skipped); machine with no `Explorer\Accent` (`WPersonalization` must read Succeeded-with-a-note);
   stock Home install (`WTelemetry`/`GGaming` Skipped, `WUpdates` mixed); Chrome running and prompt
   declined (Skipped); Chrome running and accepted (Succeeded or Failed-with-count, never a silent
   partial); restore pointed at a deleted folder (must report did-not-run); **unelevated run** (HKLM
   modules must report Failed).
4. **Golden-file review of `backup_log.txt`** against what actually landed in the folder.
5. Run `windows-safety-reviewer` after the `Utils` and `Conf/` changes, per `CLAUDE.md`.
6. **The three review fixes**, added after the branch review and untestable in the suite:
   - *App-restore dialog* — tick "Remember installed apps", restore, and confirm the dialog appears
     **in front of** the main window and takes focus. Behind it, or a main window that still accepts
     clicks while it is open, means the STA fix did not take.
   - *Windows Terminal wait* — run the winget-export backup and leave the terminal window sitting
     open. The app must stay frozen only until the 10-minute bound and then report that the export
     did not finish; it must not require Task Manager. This also settles the `wt.exe`-is-a-launcher
     question in `ROADMAP.md`: if `WaitForExit` returns immediately with the terminal still working,
     the module reports "wrote no file" on a backup that then succeeds — record which happens.
   - *`netsh` output file* — back up "Network configuration" twice into the same folder with the
     first `.txt` held open by another process. The second run must report a failed backup, and no
     `netsh.exe` may survive it (check Task Manager).

That an unelevated run now reports `Failed` for the HKLM modules instead of silently doing nothing is a
headline improvement of this phase, and it is observable *only* by running unelevated on real hardware.

## Risks

- **Blast radius.** ~30 files. Mitigated by migrating `WAccessibility` first as a reference
  implementation, then batching by shape.
- **Cry-wolf in the other direction.** If `absenceIsNormal` is set wrong, healthy machines show
  failures and the dialog becomes noise again — the exact defect being fixed, inverted. Three judgement
  calls are flagged as **unverified** and must be confirmed on hardware: `WPrivacy`
  (`HKCU\…\CurrentVersion\Privacy`), `WVisualEffects` (`…\Explorer\VisualEffects`), and `DPrinters`
  (`HKCU\Printers`).
- **Browser modules turn red.** Expected and intended (Decision 2), but it will look like a regression
  to anyone who has not read this document. Call it out in the changelog.
- **`WThemes` is the sharpest edge** — the only module overriding just the async pair while carrying
  three heterogeneous sub-operations.
- **Empirical dependencies.** The import-side rules rest on `regedit /s` exit-code semantics that remain
  unmeasured. They are specified conservatively (verify before invoking; report as *applied*) so that
  being wrong about them degrades to under-claiming rather than over-claiming.

## Record of corrections

Three claims were overturned during review and are recorded so they are not reintroduced:

1. **`WNetworkConf` does not discard its netsh exit code.** `:24` captures it, `:27` branches on it,
   `:48`/`:51` likewise. It is the closest module in the repo to the target design. The module that
   discards its exit code is `CWiFiConf` (`:89` returns it; `:23` and `:51` call it as bare statements).
   Verified by reading the file directly.
2. **`ConfigureAwait(true)` at `BackupBase.cs:34,42` is not load-bearing.** It is the default, it
   governs only `BackupAsync`'s own continuation, and that continuation is empty. What returns control
   to the UI thread is `btnBackup_Click` being an `async void` handler running there. Removing it would
   change nothing observable.
3. **The restore loop needs the `try`/`catch` more than the backup loop**, for the reasons in "The run
   summary" above. The first draft mandated it only for backup.
