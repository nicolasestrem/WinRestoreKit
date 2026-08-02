# Phase 2c — Module bugs

Design record, 2026-07-20. Branch `fix/phase2c-module-bugs`. Landed as `7df6409` (the registry
filename seam) and `6bd98f7` (the module fixes).

> This document predates the rename of Appcopier to WinRestoreKit and is kept as a
> historical record. Product names, namespaces and paths below refer to the project as
> it was at the time of writing.

## The problem

2a made failure representable and 2b made restore consented, reversible and recorded. Neither could
see the defects below, because every one of them lives *underneath* the reporting layer: the module
asks the wrong question, the machinery answers it faithfully, and the row is green. `docs/ROADMAP.md`
has carried them as known module bugs — deliberately not called regressions — since the migration.

| Where | What happens |
| --- | --- |
| `WTelemetry.cs` (pre-2c) | Exports `HKLM\SYSTEM\ControlSet001\Services\DiagTrack`. `CurrentControlSet` is what Windows actually booted; `ControlSet001` is merely the usual answer. When they differ the stale hive normally still **exists**, so the probe says `Present`, the export writes a file, and the module reports success over configuration the running system is not using. Restore writes it back into the same unused hive and reports success again. |
| `BackupBase` / six modules + `WThemes` | Six modules each carried a private copy of the `{Title}_{GetSafeFileName(key)}.reg` line. `WThemes` did not: it built the name from `Title` alone *inside* a `foreach` over `Keys`, so every key resolved to one file. **Latent** — it shipped exactly one key, so it never fired. |
| `OSHelper.cs` (pre-2c) | `key.GetValue("UBR").ToString()` and three sibling dereferences, with no null check and no null check on the opened key either. Reached from `ConfPageView`'s constructor. |
| `WThemes.cs` (pre-2c) | Copies `%Windir%\Web\Wallpaper` — stock Windows images — into and out of a directory shared by every account on the PC, and captures the wallpaper's pixels without the setting that points at them. |
| `RestAppsForm.cs` (pre-2c) | The repopulate handler was subscribed to the `DropDownList` combo's `Click` as well as `SelectedIndexChanged`, and it begins by clearing the `CheckedListBox`. Opening the dropdown wiped every app the user had ticked, inside a dialog whose only purpose is ticking a subset. The export filename existed in four spellings, one of them in the `Info` text the user reads. The install loop was `async void`, called un-awaited, so a second click started a second concurrent **elevated** winget loop. |

The `OSHelper` case is the only one that is not a reporting defect, and it is the worst of them.
`ConfPageView`'s constructor runs inside `MainForm`'s constructor, which is evaluated as the
*argument* to `Application.Run` — i.e. before the message pump exists. WinForms'
`ThreadExceptionDialog` catches only what surfaces *inside* the pump, and nothing in the tree
registered `Application.ThreadException` or `AppDomain.UnhandledException`. A missing `UBR` value —
real on sysprepped and container images — therefore terminated the process through WER: **no window,
no dialog, no log line.** The app simply does not start, and leaves nothing behind saying why.

**The common shape: a module that measures the wrong thing reports success at exactly the same
volume as one that measures the right thing.** 2a's rules make a *tool* failure visible. They cannot
make a *question* wrong.

## Scope

**In scope:** one seam for key-derived `.reg` filenames; the `WTelemetry` control-set correction and
its disclosure; the `OSHelper` dereferences plus a startup-failure report in `Program.Main`; the
`WThemes` wallpaper-folder drop and the `Control Panel\Desktop` key that replaces it; the
`RestAppsForm`/`AStoreApps` cluster; and the two cheap 2b follow-ups (`RestoreTarget.UnnamedTargetMarker`,
the fail-closed `RestorePlan` construction, `SnapshotGate`'s null-outcome handling).

**Explicitly not in scope** — considered and deferred, not overlooked:

| Deferred to | Item |
| --- | --- |
| Own item | A `WTelemetry` legacy-filename fallback (Decision 1). Needs to rewrite the payload and be covered by the snapshot; neither is a naming change. |
| Own item | An Explorer settle delay after `RestartExplorer` — gated on measuring N2 first. |
| Own item | `BackupBase.AllowPrompts` extended across the restore path (a 23-signature change). |
| Own item | Full app-level persistent logging, inherited from 2b. |
| Own item | `SystemParametersInfo` to apply wallpaper/DPI changes without a sign-out; copying the wallpaper *source image*; value-level narrowing of `Control Panel\Desktop` (Decision 2). |
| **Still live** | `RestAppsForm`'s ignored restore path: the dialog's own dropdown lets the user install from backup B while the tree is restoring backup A. 2c fixed the dialog's internals, not the fact that it does not honour `CurrentRestorePath`. |
| Own phase | `IWingetRunner` — a seam for winget matching `IRegistryTool`. |

## Decisions

**1. The `WTelemetry` legacy-filename fallback is deferred out of 2c. The orphaned file is disclosed
instead.**

The `.reg` filename is derived from the key, so correcting `ControlSet001` → `CurrentControlSet`
renames the file this module writes. Every backup taken before this version therefore holds a
`Telemetry_HKEY_LOCAL_MACHINE_SYSTEM_ControlSet001_Services_DiagTrack.reg` that no restore will now
look for. Restoring one of those reports
`Skipped("nothing was backed up for this item")` for that key while the sibling `DataCollection` key
restores normally — which reads as though nothing was captured, when the file is sitting in the
folder under its old name.

The rejected alternative was to ship the fallback in this phase. Two reasons, the second discovered
during review and stronger than the first:

- The pre-restore snapshot is driven by `module.Backup`, which after the fix exports only
  `CurrentControlSet`. A fallback that wrote to `ControlSet001` would write somewhere **the snapshot
  never captured**, while `SnapshotGate` returned `Complete` and `RestorePlan`'s caveat promised the
  restore was undoable. A caveat inside a step reason does not correct a verdict.
- The old file's *contents* are headed `[HKEY_LOCAL_MACHINE\SYSTEM\ControlSet001\...]`. `regedit /s`
  applies a file by the paths written inside it, not by any key passed alongside — so the fallback
  would apply the payload to the stale hive **regardless of which key was named**, re-committing the
  exact defect the correction exists to remove, and reporting success for it.

An honest fallback must rewrite the payload, not locate a file. That is a design problem, and it is
filed rather than half-built.

The correction also raises the stakes of restoring this module: the write now lands on a *live*
service, and the post-import check can only confirm the key is present, not that its values describe
a service this build can start. Hence a new `WarningMessage`:

> This restores settings for a Windows system service, for all users of this PC. Restoring it from a
> backup taken on a different PC or a different Windows version can leave the diagnostics service
> unable to start, and this app cannot detect that.

**2. `Control Panel\Desktop` is exported whole, with disclosure rather than narrowing.**

`WThemes` gains `HKEY_CURRENT_USER\Control Panel\Desktop`. The measured contents of that subtree
(below) include passengers this module has no interest in: `WindowMetrics` with its explicit
`AppliedDPI` and six LOGFONT blobs, a `Colors` subkey holding the real system colours,
`PerMonitorSettings`, `MaxMonitorDimension`, `DpiScalingVer`.

The rejected alternative was to export only `WallPaper`, `WallpaperStyle` and `TileWallpaper`.
**There is no narrower export available.** `regedit /e` takes subkeys wholesale and `regedit /s`
cannot select individual values, so value-level capture needs a different write mechanism entirely —
deliberately not built in this phase. The passengers are therefore named in `WarningMessage` rather
than papered over, together with the fact that `RequiresExplorerRestart` does **not** apply DPI or
system-colour changes; those need a sign-out.

**3. `%Windir%\Web\Wallpaper` is dropped rather than kept for the rare user who customised it.**

Measured on this machine, 2026-07-20: 20 files, 20.0 MB, against 4 files / 1.0 MB in
`%AppData%\Microsoft\Windows\Themes`. Roughly **95% of this module's bytes were stock images**
identical on every Windows 11 install — and it was the module's only write to a directory shared by
every account on the PC. Its most dangerous write was also its least useful.

What a user who *had* customised that folder loses: the custom files placed in
`C:\Windows\Web\Wallpaper` are no longer captured, and a restore no longer puts them back. They keep
the currently-applied background (its transcoded pixels live in the per-user Themes folder, which is
still copied) and now also keep the pointer to it, which they did not before. What they lose is the
*library* of alternates they had installed machine-wide — recoverable only from wherever they
originally got those images.

**4. The filename seam owns key-derived `.reg` names only.**

`BackupBase.RegFileNameFor(key)` (`BackupBase.cs:58`) plus a hoisted
`protected static GetSafeFileName` (`BackupBase.cs:84`). Every multi-key module's export and import
now composes its path through it (`DPrinters.cs:66,79`, `GGaming.cs:62,75`,
`WPersonalization.cs:64,77`, `WTelemetry.cs:102,115`, `WUpdates.cs:61,74`, `WThemes.cs:137,161`).

`AStoreApps`' `.json` export is deliberately **not** routed through it. That seam derives a name from
a registry key, and this artifact has no key. Its name is a const on the class that writes it —
`AppStoreApps.ExportFileName`, with `AppStoreApps.ExportPathIn(folder)` as the one composition point
— and `RestAppsForm.ExportPathFor` reads it, so producer and reader cannot disagree again.

Byte-for-byte output is preserved. `RegistryModule` overrides the seam back to `{Title}.reg`
(`RegistryModule.cs:51`) for its ten subclasses, and `WThemes` keeps `Themes.reg` for its
pre-existing key (`WThemes.cs:179-180`), so existing backups stay restorable. The six private
`GetSafeFileName` copies differed only in `Replace` ordering, which cannot matter: every replacement
maps to the same character and none produces a character another searches for.

**5. `HasBackupIn` is overridden for neither `WThemes` nor `AppStoreApps`, for different reasons.**

2b introduced `HasBackupIn` so a module can refuse a restore before anything is closed or
snapshotted. It looks like the obvious use of this phase's new seams. It is wrong in both places.

- **`AppStoreApps`** — its restore does not read the export in the selected folder at all. It opens
  `RestAppsForm`, which offers **every** backup folder from its own dropdown. Answering "no" here
  would make `RestoreScope` drop the module, so the dialog would never open for a user whose selected
  folder happens to hold no export — while the dialog itself would have been perfectly able to offer
  every other backup. It would also break
  `RestoreDeclarationTests.ModulesThatCloseNothing_AssumeTheBackupHasSomethingForThem`. (This is the
  same "still live" defect recorded in Scope, viewed from the other side: the fix is to make the
  dialog honour the restore path, not to refuse the module.)
- **`WThemes`** — a probe would have to answer for a folder copy *and* two `.reg` files whose names
  differ between pre-2c and post-2c backups. Returning false on a partially-present backup would skip
  a module that can still restore some of what it holds, and the per-step `Skipped("nothing was
  backed up for this item")` already reports each missing piece by name. The default `true` is the
  under-claiming direction here: one unnecessary consideration, versus cancelling part of a restore
  the user asked for.

**6. Startup failure is reported and rethrown, not swallowed.**

`OsHelper.GetVersion` splits into a reader and a pure `ComposeVersion(build, ubr)`, with a
`Func<RegistryKey>` opener seam so the *reading* path is drivable. Degraded shapes get
self-describing tokens rather than fragments: `BuildUnknown` = `"(build unknown)"` when the registry
was readable but carried no build, `BuildUnreadable` = `"(build unreadable)"` when the open threw.
The two are deliberately different strings — at this point in startup the distinction is otherwise
unobservable, because `LogHelper` only writes once its target is set and `ConfPageView` sets that
target on the line *after* the one that calls `GetVersion`. The greeting string is the only channel
that reaches anyone.

`UBR` absent is **not** padded to `.0`; that would state a revision the machine never reported.
`IsWin11` is deleted outright — its only consumer was the eagerly-initialised
`public static readonly string thisOS` that nothing read, and a throw inside a static field
initializer surfaces as `TypeInitializationException` and leaves the type unusable for the life of
the process. `OsVersionTests.OsHelper_HasNoEagerStatics` pins that absence.

`Program.Main` wraps `Application.Run(new MainForm())` in a catch that shows
`DescribeStartupFailure(ex)` and **rethrows**. Rethrown rather than swallowed or turned into
`Environment.Exit`: the rethrow is what leaves the WER / Event Log record with the real stack in it.
`Application.SetUnhandledExceptionMode` is deliberately not called, which keeps the handler scoped to
startup instead of silently becoming the app's global exception policy.

**7. The app-restore dialog's rules are pure functions; only the wiring stays in the form.**

`RouteProblem`, `ShouldDeferClose`, `ComposeListState`, `ComposeOutcome`, `Describe` and
`AppExport.Parse` are all `internal static` and testable without a message loop. Three of them encode
a decision worth naming:

- `RouteProblem(export, hasBeenShown)` — a problem found during construction is **deferred to
  `OnShown`**. Raised during construction it passed `this` as owner, forcing handle creation on an
  unshown form; the dialog got an effectively invisible owner and could paint *behind* the main
  window, which during an orchestrated restore is disabled. `Absent` maps to `None`, never `Defer`:
  most backup folders legitimately hold no app export, and deferring a spurious dialog only moves it.
- `ShouldDeferClose(installing, reason)` — `UserClosing` **only**. The old guard checked the
  installing flag alone, so it vetoed a mid-install `Application.Exit`, a close of the owning
  `MainForm`, and MDI teardown — the app simply appeared to hang — and refused `WindowsShutDown`
  until Windows overrode it, having first shown the user a blocked-shutdown screen naming Appcopier.
  A stop is requested on *any* close; what the `CloseReason` decides is whether the close **waits**.
- `ComposeOutcome(requested, attempted, failures)` — three numbers, not two. Stopping after five of
  twenty leaves fifteen never attempted; calling those failures is a false alarm and folding them
  into the five hides a real one. It says "installed", not "restored": `winget install` on a present
  package upgrades it, and nothing pins the backed-up version.

Cancel is repurposed into "Stop after the current app" rather than joined by a third button, and the
stop is honoured **between** packages. Killing a half-finished winget install would leave a partially
installed package no summary could describe honestly; the constant `StoppingText` says the wait is
bounded by a timeout it deliberately does not quote, because a duration copied out of
`WindowsHelper` would be a second source of truth for it.

`btnRestore.Enabled=false` moves into the Designer, so `ComposeListState` can only ever turn the
button **on** — the safe direction if the rule is ever bypassed.

**8. The two 2b follow-ups fail closed.**

- `RestoreTarget.UnnamedTargetMarker` = `"(this item overwrites something it does not name)"`,
  deliberately a *different* sentence from `UndeclaredMarker`. "Declared nothing at all" and "one
  entry of the declaration is broken" are different facts, and the text the user consents against
  must not conflate them.
- The `RestorePlan` constructor is wrapped in a catch in `ConfPageView`: composing the plan reads four
  virtual members off every selected module, any of which a future module can throw from, and the
  chain up to the `async void` click handler had no catch. The plan **is** the description the user
  consents against, so no description means no consent means nothing is touched. The marker is the
  fix and the catch is the backstop, in that order — reaching the catch abandons a restore the user
  asked for.
- `SnapshotGate` counts a null outcome **before** the null check and treats it as a failure. Skipping
  it first made an all-null list indistinguishable from an empty one, which reported "none of the
  selected items change anything when restored" — the exact false sentence 2b's `blockedCount` branch
  was added to remove, reached through a different door.

## Measured facts

Same protocol as 2a and 2b: measure before freezing reason strings; where a measurement cannot be
taken before merge, say so and carry it onto the smoke matrix.

**Measured 2026-07-20, this machine (Windows 11 Pro 10.0.26200):**

| # | Measurement | What depends on it |
| --- | --- | --- |
| M1 | `C:\Windows\Web\Wallpaper`: **20 files, 20.0 MB**. `%AppData%\Microsoft\Windows\Themes`: **4 files, 1.0 MB**. ~95% of the module's bytes were stock images. | Decision 3 |
| M2 | `HKCU\Control Panel\Desktop` values: `WallPaper` (capital P) = `C:\Users\<name>\AppData\Roaming\Microsoft\Windows\Themes\TranscodedWallpaper`; `WallpaperStyle`=10; `TileWallpaper`=0; `Win8DpiScaling`=0; `ScreenSaveActive`=1; `SCRNSAVE.EXE` present but empty; `TranscodedImageCache` and `_000`/`_001`/`_002`; `DpiScalingVer`; `MaxMonitorDimension`; `MaxVirtualDesktopDimension`. | Decision 2; the `Info` text's user-name claim |
| M3 | Subkeys of the above: `Colors`, `PerMonitorSettings`, `WindowMetrics`, `MuiCached`. `WindowMetrics` values: BorderWidth, CaptionFont, CaptionHeight, CaptionWidth, IconFont, IconTitleWrap, MenuFont, MenuHeight, MenuWidth, MessageFont, ScrollHeight, ScrollWidth, Shell Icon Size, SmCaptionFont, SmCaptionHeight, SmCaptionWidth, StatusFont, PaddedBorderWidth, **AppliedDPI**, MinAnimate. `Colors` holds real system colours (Window=`255 255 255`, WindowText=`0 0 0`, Menu, Hilight). | Decision 2 and the `WarningMessage` wording |
| M4 | **`LogPixels` was NOT present.** | Recorded in "Record of corrections" — a review claim that was checked and did not hold |
| M5 | `HKLM\SYSTEM\Select\Current` = **1**, and only `ControlSet001` exists on this host. | Why the `WTelemetry` bug was invisible here: the two paths name the same hive |

**Inferred, not measured.** The claim that a pre-2c theme restore never changed the desktop
background is an *inference*: only the pixels were captured, and pixels alone cannot tell Windows
which image to display, so a restore had nothing to point the desktop at. That is consistent with the
observed behaviour but was never traced end to end. It is recorded as inference in the code comment
too (`WThemes.cs`, `LoadSettings`).

**Unmeasured, and on the smoke matrix.** Whether a cross-DPI restore of `Control Panel\Desktop`
visibly breaks fonts via `AppliedDPI`/`WindowMetrics`; and whether
`%AppData%\Microsoft\Windows\Themes` exists on a fresh profile before any theme is applied. Both are
named on the matrix below, and the second gates an `absenceIsNormal` decision, where a wrong answer
degrades toward **cry-wolf** — the bad direction.

Carried forward unmeasured from 2b: **N1** (`regedit /s` exit codes for missing, truncated and
partially-applied files) and **N2** (whether Windows auto-restarts the shell after every
`explorer.exe` is killed, and within what window). N2 in particular, because the Explorer settle
delay is recorded above as deferred *pending that measurement*.

## The types

Nothing new is added to the result vocabulary. 2c adds seams and pure rules, not outcomes.

```csharp
// BackupBase — the filename seam
protected virtual string RegFileNameFor(string key)      // "{Title}_{safe(key)}.reg"
protected static  string GetSafeFileName(string value)   // hoisted from six private copies

// AStoreApps — the keyless artifact, named where it is written
public const  string ModuleTitle      = "Remember installed apps";
public const  string ExportFileName   = ModuleTitle + ".json";
public static string ExportPathIn(string backupFolder)
internal static StepResult Verify(ProcessOutcome, string)   // was private; now static over ModuleTitle

// OsHelper — reader / formatter split, plus the opener seam
internal const  string BuildUnknown    = "(build unknown)";
internal const  string BuildUnreadable = "(build unreadable)";
public   static string GetVersion()
internal static string GetVersion(Func<RegistryKey> openKey)   // THE SEAM
internal static string ComposeVersion(string build, string ubr)  // pure, total

// Program
internal static string DescribeStartupFailure(Exception ex)      // total on null

// RestAppsForm — the rules, all pure
internal enum   AppExportState  { Ok, Absent, Unreadable }
internal enum   ProblemRouting  { None, ShowNow, Defer }
internal sealed class AppExport   // Read(path) / Parse(json, path), both total
internal sealed class ListState
internal sealed class OutcomeMessage
internal static ListState      ComposeListState(AppExport)
internal static ProblemRouting RouteProblem(AppExport, bool hasBeenShown)
internal static bool           ShouldDeferClose(bool installing, CloseReason)
internal static string         Describe(ProcessOutcome)
internal static OutcomeMessage ComposeOutcome(int requested, int attempted, IReadOnlyList<string>)
internal const  string         StoppingText

// 2b follow-ups
public const string RestoreTarget.UnnamedTargetMarker
internal const string ConfPageView.IntroTemplate   // greeting composition, testable without the control
```

`AppExportState` is three states, not two, for the reason 2a gave about `absenceIsNormal`: an
**absent** export is the ordinary case (this dialog is reachable standalone, and most backup folders
hold no app export) while an **unreadable** one is a fault. Collapsing them would raise an error
dialog on the ordinary case — the same collapse this codebase keeps having to un-collapse.

`GetVersion(Func<RegistryKey>)` takes a delegate rather than an interface because there is exactly
one operation and one production implementation. It is a *parameter*, not a static field: a static
field would trip the no-eager-statics guard, for the reason that guard exists. The seam does not
eliminate the untestable surface, it **confines** it — what stays uncovered is one expression,
`OpenCurrentVersionKey`'s single `OpenSubKey` call.

## Testing

**421 → 504 tests, all passing.** (Stage A took 406 → 421; stage B took 421 → 504.)

Not all of that is regression coverage, and the distinction was itself a review finding. Naming it:

**Genuine regression coverage — each fails on the pre-2c code.**

| Test | Guards |
| --- | --- |
| `ModuleTargetTests.Telemetry_ExportsTheControlSetWindowsIsRunning` | Decision 1's key |
| `ModuleTargetTests.NoModuleExportsANumberedControlSet` | The whole class of defect, not the one instance |
| `ModuleTargetTests.Themes_WritesNothingMachineWide` | Decision 3 — the dropped folder |
| `ModuleTargetTests.Themes_ClaimsMatchWhatItActuallyTouches` | The `WarningMessage` disclosures of Decision 2 |
| `ModuleTargetTests.Themes_DeclaresItsProfileFolderThenBothKeys` | The declaration keeps up with the module |
| `BackupFileNamingTests.AModuleGivenAnExtraKey_ReadsADifferentFileForIt` | The latent `WThemes` filename defect — see below |
| `BackupFileNamingTests.NoTwoModulesWriteTheSameRegFileName` | Cross-module collisions |
| `BackupFileNamingTests.TheThemesFileNameIsKeptForTheKeyThatAlreadyUsesIt` | Existing backups stay restorable |
| `OsVersionTests.GetVersion_KeyHasBuildButNoUbr_ReturnsTheBuildAlone` | **The actual null deref.** |
| `OsVersionTests.GetVersion_KeyIsAbsent_ReturnsTheUnknownToken`, `..._KeyCannotBeOpened_DoesNotPropagateTheException` | The other three unguarded dereferences |
| `OsVersionTests.OsHelper_HasNoEagerStatics` | The `thisOS` static-initializer hazard staying deleted |
| `RestorePlanTests.NullTargetEntry_RendersItsOwnMarkerInsteadOfThrowing` | Decision 8's marker |
| `SnapshotTests.Gate_AllNullOutcomes_NeverClaimTheSelectionChangesNothing` | Decision 8's gate hole |

`AModuleGivenAnExtraKey_ReadsADifferentFileForIt` deserves its own note: it observes the filename
`RestoreAsync` **actually computes**, rather than calling the seam directly. A test that calls the
seam passes even when a module's call site still concatenates its own name — which is exactly where
the defect lived.

**The `OsVersionTests` split matters.** `ComposeVersion_*` cover **rendering** — that no degraded
shape comes out as `"Build "` or `"Build 26100."` or a greeting with a double space. They are
happy-path-shaped: they could not have failed on the original code, because the original code never
reached a formatter. The test that fails on the real defect is
`GetVersion_KeyHasBuildButNoUbr_ReturnsTheBuildAlone`, which drives the **reading** path through the
opener seam against a real key created and deleted under
`HKCU\Software\Appcopier\Tests\<guid>` — no elevation, nothing the user owns.

**Happy-path / characterisation smoke.** `AppRestoreDialogTests`' `Parse`, `ComposeListState`,
`ComposeOutcome` and `Describe` rows, `AStoreApps.Verify`'s ladder, `VersionParsingTests`'
`DescribeStartupFailure` rows, `GetVersion_OnThisHost_MatchesOneOfTheAllowedShapes`. These pin
behaviour that is new or was previously unreachable rather than previously wrong. `RouteProblem_*`
and `ShouldDeferClose_*` sit in between: the rules are new, but each encodes a defect that existed —
they fail on a reimplementation that gets the rule wrong, not on the original code, which had no such
function to get wrong.

**Mutation-verified.** Reverting each of the following fails a named test: the control-set fix, the
wallpaper folder, the `WThemes` warning text, the `WThemes` call site in `BackupAsync` (fails
`AModuleGivenAnExtraKey_ReadsADifferentFileForIt` with "the collection contained 2 matching items",
while the other five modules stay green), and the UBR dereference.

**The gap.** Two whole categories are untested, and neither can be closed in xUnit here.

- **Anything needing a message loop.** The `RestAppsForm` event wiring — specifically the *absence*
  of the `Click` subscription that wiped ticks — the re-entrancy guard, the `OnShown` deferral, and
  the `FormClosing` guard. The pure rules underneath all four are covered; that the form calls them,
  and calls them from the right event, is not. This is the highest-value untested surface in the
  phase, because the tick-wiping defect *was* a wiring defect and its replacement is also wiring.
- **Anything needing elevation or a real tool.** No `regedit` or `winget` behaviour is exercised.
  `RegFile.Validate` rejects zero-byte files before the tool is reached, so the naming tests run no
  `regedit` at all.

**Compensating verification — manual elevated smoke matrix:**

| # | Scenario | Expected |
| --- | --- | --- |
| 1 | Back up Themes | **Two** distinct `.reg` files (`Themes.reg` and the `Control Panel\Desktop` one), and **no** `Themes_..._Web_Wallpaper\` folder |
| 2 | Restore Themes onto a machine at a **different DPI** | **Unmeasured.** Does `AppliedDPI`/`WindowMetrics` visibly break fonts or scaling? **This gates any future value-level narrowing** (Decision 2) |
| 3 | Fresh user profile, before any theme is applied | **Unmeasured.** Does `%AppData%\Microsoft\Windows\Themes` exist? Gates `absenceIsNormal` for that folder; wrong answer degrades toward **cry-wolf** |
| 4 | Restore Themes under an account with a different user name | Desktop may come back black; the row still says Succeeded — the limitation `Info` and `WarningMessage` now state |
| 5 | Back up Telemetry, open the export | Its `[...]` header names `CurrentControlSet`, and the filename matches |
| 6 | Restore a **pre-2c** backup's Telemetry | `Skipped("nothing was backed up for this item")` for DiagTrack; `DataCollection` restores normally (Decision 1's accepted consequence) |
| 7 | Open the app-restore dialog, tick apps, then open the backup dropdown | Ticks **survive** |
| 8 | Select a different backup in that dropdown | The list repopulates from the new folder |
| 9 | No backups on disk / an export with zero packages | Restore stays greyed |
| 10 | Double-click Restore | Exactly one install loop |
| 11 | Close the dialog mid-install | Close waits; the loop stops after the current package; the summary is still shown |
| 12 | `Application.Exit` (or close `MainForm`) mid-install | No longer hangs — the close is not vetoed |
| 13 | Point the dialog at a corrupt `.json` export | The warning appears **after** the window is visible and in front of it, never behind |
| 14 | Normal start | Greeting reads as a sentence, e.g. "…your Windows 11 Build 26100.4652 on this or another system." |
| 15 | VM with `UBR` renamed away | Greeting shows the build alone; no crash, no `(build unknown)` |
| 16 | Unreadable `CurrentVersion` key | `(build unreadable)`, app starts. **Staging note:** `highestAvailable` lets the app re-grant its own read, so use a restricted token or point the seam at `HKLM\SECURITY` |
| 17 | Deliberate throw from `ConfPageView`'s ctor | The startup dialog appears naming the exception type, then the process exits leaving a WER/Event Log record |
| N1 | *(carried from 2b)* `regedit /s` exit codes: missing, truncated, partially applied | Read-back wording in Decision 6 of 2b |
| N2 | *(carried from 2b)* Kill every `explorer.exe`: does Windows restart the shell, and within what window? | **The deferred settle delay is explicitly waiting on this** |

## Risks

Ranked, most consequential first. Where the detector is prose rather than a test, it says so.

1. **The deferred `WTelemetry` fallback gets un-deferred without closing the snapshot gap.** A future
   contributor reads "restoring a pre-2c backup skips DiagTrack", writes the obvious fix, and ships a
   restore that writes to a key the snapshot never captured while `SnapshotGate` reports `Complete`.
   *Detector: prose only* — the comment block in `WTelemetry.LoadSettings`, the removed-companion
   note in `BackupBase.cs:61-73`, and Decision 1 here. **Nothing automated can catch this**, because
   the failing property is a relationship between what a module exports and what its restore writes,
   and no test in the suite asserts over that pair.
2. **`Control Panel\Desktop` restored across DPI or from High Contrast breaks the desktop
   cosmetically or worse.** Disclosed, not prevented. *Detector: matrix rows 2 and 4, both
   unmeasured.* The `WarningMessage` is asserted verbatim by
   `Themes_ClaimsMatchWhatItActuallyTouches`, so the *disclosure* cannot silently disappear — but no
   test observes the effect, and the row will read Succeeded either way.
3. **The pre-2c orphaned DiagTrack file reads as "nothing was ever captured."** A user restoring an
   old backup is told less than the truth. *Detector: prose* (CHANGELOG disclosure) *plus* matrix row
   6.
4. **A restored wallpaper pointer that does not resolve reports Succeeded over a black desktop.**
   Structural: `ImportRegistryKey` checks the exit code and key presence, and a path value's
   resolvability is not something a key probe can see. *Detector: matrix row 4, and the `Info`/
   `WarningMessage` text.*
5. **`RestAppsForm`'s wiring regresses.** Re-adding a `Click` subscription, or moving the problem
   dialog back out of `OnShown`, is caught by nothing in the suite. *Detector: prose comments at the
   call sites plus matrix rows 7, 11, 12, 13.* The comment in the constructor states the absence as a
   deliberate fact, which is the only form of protection available without a UI harness.
6. **`RestAppsForm`'s ignored restore path is still live.** Restoring backup A from the tree can
   install apps from backup B, because the dialog's dropdown is independent of `CurrentRestorePath`.
   Not a 2c regression — pre-existing, and now the only remaining item from the original module-bug
   list. *Detector: none.* Filed in Scope.
7. **A future module's `RegFileNameFor` override collides with another module's names.** *Detector:
   test* — `NoTwoModulesWriteTheSameRegFileName` enumerates registered modules.
8. **N1 and N2 remain unmeasured**, inherited from 2b. N1's degradation is under-claiming (restore
   reasons already say "applied"). N2 blocks a deferred item rather than a shipped one.

## Record of corrections

Kept so overturned claims are not reintroduced.

- *"WThemes was silently losing a registry key in shipped backups."* **False, and this is the most
  important entry here.** One planning draft narrated a data-loss incident that never happened. The
  filename defect was **latent**: `WThemes` shipped exactly one key, so `Title`-derived naming and
  key-derived naming produced the same file and the loop could not collide with itself. It was armed
  only by *this phase's* fix for the module's other defect, which adds a second key. What the commit
  message describes — the second export deleting the first via `TryDeleteExport`, both steps green,
  the restore importing one file once per key while the post-import probe finds every key present
  because the keys exist on the live machine anyway — is what **would have** happened had the key
  been added without the seam. It is a counterfactual, and it is written as one. A spec that narrates
  an unverified incident commits this project's own failure mode to the permanent record.
- *"`LogPixels` is in `Control Panel\Desktop`, so the DPI passenger list should name it."* Checked
  against the live key on 2026-07-20 and **it did not hold**: `LogPixels` is absent by default and
  appears only once a per-user DPI override is set. `AppliedDPI` under `WindowMetrics` is the value
  that actually ties the captured metrics to the capture-time DPI, and it is what the
  `WarningMessage` rests on. The review claim was plausible and wrong; measuring cost one command.
- *"`RegFileNamesToTryOnRestore` should ship alongside `RegFileNameFor` as a pair."* Added in
  `7df6409` and **removed before merge** in `6bd98f7`. The argument for it was symmetry: a
  single-string seam cannot express a restore-side fallback, and discovering that later would mean
  forking the seam or bypassing it. The argument against it won: **nothing on the restore path called
  it.** Every module composes its import path from `RegFileNameFor` directly, so an override would
  have been a silent no-op — a trap wearing the costume of a safety feature, and precisely the
  writer/reader drift the seam exists to prevent. Its one candidate consumer, the `WTelemetry`
  fallback, turned out to need the payload rewritten rather than the file found (Decision 1), so the
  pair would not have served it either. The absence is now documented in place
  (`BackupBase.cs:61-73`) so the same symmetry argument does not re-add it.
- *"`HasBackupIn` should be overridden for `AppStoreApps` now that `ExportPathIn` exists."* Wrong in
  both directions at once: it would make `RestoreScope` refuse the module, so the dialog — which can
  offer every *other* backup — would never open, and it would break
  `RestoreDeclarationTests.ModulesThatCloseNothing_AssumeTheBackupHasSomethingForThem`. Recorded as
  Decision 5.
- *"The theme restore never changed the desktop background because only the pixels were captured."*
  Retained as **inference, explicitly labelled**, not as a traced cause. It is consistent with the
  observed behaviour and with what the key contains, and it was never reproduced end to end. The code
  comment says so too.
- *"`ComposeVersion` tests cover the null-dereference fix."* They do not. They cover **rendering** of
  degraded shapes, which is new behaviour, and would have passed against the original code had it
  reached a formatter — it never did. The test that actually fails on the original defect is the one
  driving the reader through the `Func<RegistryKey>` opener seam. Left in the spec because the
  distinction between regression coverage and happy-path smoke is exactly what a test count hides.
- *"Skipping a null entry before counting it is harmless tidiness."* It was the 2b gate hole reached
  through a different door: an all-null outcome list became indistinguishable from an empty one and
  reported "none of the selected items change anything when restored." Counted first now, and a null
  entry adds a line of its own so it can never displace a real, named failure.
