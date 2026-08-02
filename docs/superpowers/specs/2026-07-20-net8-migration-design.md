# Phase 1 — .NET 8 Migration Design

Date: 2026-07-20
Status: implemented
Scope: `src/Appcopier` — framework migration only

> This document predates the rename of Appcopier to WinRestoreKit and is kept as a
> historical record. Product names, namespaces and paths below refer to the project as
> it was at the time of writing.

## Context

Appcopier is a WinForms utility that backs up and restores Windows 11 settings by exporting registry keys
to `.reg` files and copying folders. It had been effectively unmaintained since January 2024 and still
targeted .NET Framework 4.8 with a classic (non-SDK) csproj, meaning `dotnet build` did not work at all and
the project could only be built from a Visual Studio Developer environment.

Three priorities drove this work, in order: **safety** (the app writes to the registry and overwrites user
profile data, largely without confirmation or rollback), **modernization** (get onto a toolchain that is
maintained and testable), and **coverage** (more of the settings a developer actually cares about).

The chosen sequencing was **migrate first**. The safety overhaul and new backup modules both mean touching
restore logic, and doing that on a framework with no test harness and no CLI build is how regressions get
shipped. Phase 1 buys `dotnet test`, `dotnet build`, and a hook that compiles on every edit — then Phases 2
and 3 change behavior on top of that.

The user chose **.NET 8** (LTS) rather than .NET 10, accepting the tradeoff below.

## What Phase 1 does

- Rewrite `Appcopier.csproj` as SDK-style targeting `net8.0-windows` (165 lines → 38).
- Replace `packages.config` + `HintPath` with a `PackageReference` on Newtonsoft.Json 13.0.3.
- Delete the 12 framework `<Reference>` entries. Two of them (`System.Deployment`,
  `System.Data.DataSetExtensions`) do not exist on .NET 8 at all; all were verified unused or supplied by the
  shared framework.
- Delete `App.config` (only declared a .NET Framework `<supportedRuntime>`).
- Fix the runtime breaking changes listed below.
- Add an xUnit test project at `src/Appcopier.Tests`.

## What Phase 1 explicitly does NOT do

This migration is **behavior-preserving**. The following were deliberately deferred, and several were
actively defended against during implementation:

- Dark mode, theming, any color or layout change.
- Any change to DPI awareness. In particular `Program.cs` keeps its explicit
  `EnableVisualStyles()` / `SetCompatibleTextRenderingDefault(false)` pair and does **not** adopt
  `ApplicationConfiguration.Initialize()`, which is the usual .NET 8 WinForms idiom. That call emits a
  generated config that would silently flip DPI mode from the System-aware setting declared in
  `app.manifest` to PerMonitorV2. The csproj carries a comment saying so, because the "helpful" fix is
  non-obvious to undo.

  **Be aware that the build emits `warning WFAC010`, which explicitly tells you to remove the high-DPI
  settings from `app.manifest` and configure them via the `ApplicationHighDpiMode` property.** Do not act on
  it in Phase 1 — that is exactly the behavior change being deferred. It is left unsuppressed rather than
  hidden behind `NoWarn` so the decision stays visible whenever DPI work is picked up deliberately.
- The error-handling overhaul: snapshot-before-restore, restore confirmations, and the silent-catch pattern
  throughout `Conf/` and `Utils` (a failed `regedit` import currently produces a log line and nothing else).
- Rewriting the update checker to use the GitHub Releases API.
- Any change to a `Conf/` module's backup or restore behavior.
- Async redesign.
- `WebClient` → `HttpClient` (two sites in `DataHelper.cs`, now warning SYSLIB0014). `WebClient` still
  functions on .NET 8; rewriting it would change timeout, proxy, and TLS behavior on the deployed update
  path. Left as a warning on purpose — do not enable `TreatWarningsAsErrors`.

## The AssemblyInfo constraint, and why it shapes the csproj

This is the single most important thing to understand before editing the project file.

Every already-installed copy of Appcopier v0.30.0 checks for updates by downloading
`.../main/src/Appcopier/Properties/AssemblyInfo.cs` as **raw text** from GitHub and string-parsing the
`[assembly: AssemblyFileVersion("x.y.z")]` line out of it with `IndexOf('(')` / `LastIndexOf(')')` substring
math (`Helpers/DataHelper.cs`, `CheckForUpdates`). That code is deployed on user machines and cannot be
changed retroactively. Therefore:

- `AssemblyInfo.cs` must stay at that exact path with that exact line format. Moving, renaming, or
  reformatting it silently disables update checks for every existing user — with no build error and no
  visible symptom locally.
- The version must stay **three-part** (`0.31.0`, never `0.31.0.0`), or deployed copies see a phantom
  "update available" forever.

The SDK's default `GenerateAssemblyInfo=true` generates its own assembly attributes, which collide with the
hand-written file as six `CS0579` duplicate-attribute errors. The trap is that the obvious way to silence
those errors is to delete or relocate `AssemblyInfo.cs` — precisely the change that breaks the update
checker. So the csproj sets `<GenerateAssemblyInfo>false</GenerateAssemblyInfo>` and the file is compiled by
the default glob. For the same reason, **never** set `Version`, `AssemblyVersion`, `FileVersion`, or
`InformationalVersion` in the csproj: that creates a second version source that can diverge from the one the
remote checker reads.

Two consequences of that switch are worth knowing:

1. **`SupportedOSPlatform` is suppressed with it.** In the SDK's targets, emission of
   `[assembly: SupportedOSPlatform("windows7.0")]` is gated behind `GenerateAssemblyInfo == 'true'`. With it
   off, the platform-compatibility analyzer treats this Windows-only WinForms app as cross-platform and
   emits 244 `CA1416` warnings for ordinary `MessageBox`/`Registry`/`TreeNode` calls. The fix is to declare
   the attribute directly in `AssemblyInfo.cs` (the file whose job is assembly-level attributes), which
   restores exactly the metadata the SDK would have emitted. Warnings dropped 247 → 3. A blanket
   `<NoWarn>CA1416</NoWarn>` was rejected because it would permanently blind the analyzer to real
   portability mistakes. The `<AssemblyAttribute>` MSBuild item does *not* work as a workaround — it is
   consumed by the same target the switch disables.
2. **The version read is now structural, not incidental.** `Program.GetCurrentVersionTostring()` previously
   did `new Version(Application.ProductVersion).ToString(3)`. That still returns `"0.30.0"` on .NET 8, but
   only by luck: .NET 5+ WinForms resolves `ProductVersion` from `AssemblyInformationalVersion` first and
   only falls back to the Win32 resource (derived from `AssemblyFileVersion`) because no informational
   version attribute exists today. The moment anyone adds one, the SDK appends `+<git-sha>`,
   `new Version("0.30.0+abc123")` throws `FormatException`, and `CheckForUpdates` dies inside its own
   `try/catch` showing "Checking for App updates failed" to every user — a failure invisible at build time.
   The method now reads `[assembly: AssemblyFileVersion]` off the assembly by reflection, so both sides of
   the update comparison read the same attribute and cannot diverge.

## Runtime breaking changes found and how they were handled

| Change | Impact | Resolution |
| --- | --- | --- |
| `Process.Start(url)` no longer shells out (`UseShellExecute` now defaults to `false`) | 5 sites throw `Win32Exception`. `MainForm.cs` (QR-code prompt) runs on a `System.Timers.Timer` thread with no `try/catch` — **terminates the process**. Three About-page links raise the unhandled-exception dialog. `DataHelper.cs` is caught but shows a misleading "update check failed" after the user already agreed to download. | Wrapped each in `ProcessStartInfo { UseShellExecute = true }`. |
| `Application.StartupPath` now returns a trailing separator | `DataRootDir` became `...\app\` with a doubled separator, propagating into every `regedit` command line across all 23 modules, into `backup_log.txt`, into the log UI, and into a triple separator in `RestAppsForm`. Win32 normalizes these, so it was not yet broken — but it leaked into user-visible strings and was one strict-path API away from real failure. | `Path.Combine(Application.StartupPath, "app") + @"\"`. The trailing-separator contract was preserved so no caller needed changing and the on-disk layout is identical. |

Of the nine static `Process.Start` calls, the four not listed above were verified as **correct already and
deliberately left alone**: `ConfPageView` and `RestPageView` already passed `UseShellExecute = true` to open
the backup folder, and the two in `WindowsHelper` launch real executables by name (`explorer.exe`, and the
redirected process in `RunWT`), which resolve without the shell.

The three process launches built through a `Process` instance rather than the static helper were likewise
left untouched, and deliberately so: the `regedit` export in `WindowsHelper` and the two `netsh` calls
(`CWiFiConf`, `WNetworkConf`) all redirect standard output, which *requires* `UseShellExecute = false`.
"Fixing" them to match the URL sites would throw `InvalidOperationException` and break registry export and
WiFi/network backup.

Also verified rather than assumed, because the flat-namespace quirk made it genuinely risky: `Views/*.cs`
and `Forms/RestAppsForm.cs` all declare `namespace Views`, which does not match their folders. SDK default
resx naming would normally produce `Appcopier.Views.*`, breaking `ComponentResourceManager` at runtime on
the About page. `EmbeddedResourceUseDependentUponConvention` (default true) resolves this correctly — the
built assembly's manifest resource names were inspected and the one real resource payload
(`btnGithub.Image`) was loaded in a probe process to confirm.

`app.manifest` carries over byte-for-byte: `requestedExecutionLevel=highestAvailable`, `longPathAware`, and
`dpiAware` were all confirmed embedded in the built exe.

## Distribution — resolved: self-contained single file

The plain `net8.0-windows` build is framework-dependent (`Appcopier.exe` + `Appcopier.dll` +
`runtimeconfig.json` + `deps.json` + `Newtonsoft.Json.dll`) and needs the .NET Desktop Runtime 8 installed.
Since v0.30.0 users get a .NET Framework exe that needs no runtime at all, shipping that would have been a
real regression in install friction, so **releases publish self-contained single-file instead** and end
users still install nothing. The `/release` skill carries the command.

Measured, not estimated:

| Publish | Size | Files |
| --- | --- | --- |
| Framework-dependent (dev build) | ~0.2 MB | 5 + runtime install |
| `PublishSingleFile` alone | 156 MB | 6 — five WPF native DLLs are **not** bundled by default |
| `PublishSingleFile` + `IncludeNativeLibrariesForSelfExtract` + `EnableCompressionInSingleFile` | **69 MB** | **1** |

Both extra flags are load-bearing. Without `IncludeNativeLibrariesForSelfExtract` the "single file" publish
still emits `D3DCompiler_47_cor3.dll`, `PenImc_cor3.dll`, `PresentationNative_cor3.dll`,
`vcruntime140_cor3.dll` and `wpfgfx_cor3.dll` alongside the exe, and shipping the exe alone then produces a
download that cannot start — the exact failure mode this decision was meant to avoid.

`PublishTrimmed` is deliberately **not** used. WinForms resolves types by reflection through the designers
and `ComponentResourceManager`, so trimming removes code reached only at runtime; the result is missing
resources and blank forms at launch rather than a build error.

Verified on the compressed single-file artifact: the embedded manifest still matches the source exactly
(`highestAvailable`, `longPathAware`, `dpiAware`), so elevation survives bundling.

## Test harness

xUnit at `src/Appcopier.Tests`, run via `dotnet test src\Appcopier.sln` (16 tests at time of writing).

Scope is bounded by what the app actually is: registry export/import shells out to `regedit.exe` under
`highestAvailable` elevation, so anything touching real registry or profile state is not unit-testable and
stays manual. Phase 1's tests therefore target the one piece of pure logic that is both reachable and
genuinely dangerous — the update-checker version contract described above.

They pin both sides of that contract: that parsing the **real** `AssemblyInfo.cs` (copied to the output
directory, so the tests keep testing the true input after a version bump) yields a clean three-part version,
that it matches the compiled assembly's own `AssemblyFileVersion`, and that it agrees with
`Program.GetCurrentVersionTostring()`. That last one is the load-bearing invariant: `CheckForUpdates`
compares the two with `==`, so if they can ever differ for an up-to-date client, every user is offered a
phantom update forever.

Several tests deliberately assert *current* behavior for malformed input rather than desirable behavior. The
parse is fragile raw index arithmetic; hardening it belongs to a later phase. The point is that any change
to it surfaces as a failing test instead of a silent shift on machines we cannot reach.

Phase 2 should extend this harness to the restore-safety logic it introduces.

## Roadmap

### Phase 2 — safety overhaul

The app performs destructive operations largely without confirmation or rollback. Planned:

- Snapshot before restore, so a bad `.reg` import can be undone.
- Explicit confirmation on destructive restores, with a clear statement of what will be overwritten.
- Replace the silent-catch pattern in `Conf/` and `Utils`: a failed `regedit` import currently writes a log
  line and reports success to the user.
- Guard the unchecked `Process.Kill()` calls in `Utils.CloseProcess` / `RestartExplorer`.
- Fix `Utils.RunWT`: it is `async void` with a `WorkingDirectory` that may not exist, so the resulting
  `Win32Exception` is rethrown on the sync context and crashes the app. Pre-existing, not a migration
  regression.

### Phase 3 — module coverage

Aimed at the settings this app's actual users care about and currently cannot back up.

**Developer tooling:** Windows Terminal settings, VS Code (settings/keybindings/extension list), `.ssh`
config and keys, user environment variables, WSL distro configuration, and the `hosts` file.

**Power-user settings:** power plans, installed fonts, mapped network drives, scheduled tasks, file
associations, regional and input settings, display layout.

**Browsers are explicitly deprioritized** — the user relies on Chrome profile sync, which already solves
this better than a local `.reg` export can, and browser profile directories are large, lock-prone, and
frequently corrupted by partial restore.
