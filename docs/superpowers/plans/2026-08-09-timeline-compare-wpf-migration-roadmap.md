# Timeline + Compare WPF Migration Roadmap

> **For agentic workers:** Execute the linked implementation plans in order. Each linked plan requires `superpowers:subagent-driven-development` (recommended) or `superpowers:executing-plans` and contains its own checkbox steps, tests, review gate, and commits.

**Goal:** Replace WinRestoreKit’s WinForms shell with the approved Timeline + Compare WPF application while preserving Core behavior, restore safeguards, snapshot compatibility, and the self-contained Windows executable.

**Architecture:** Introduce a framework-neutral `WinRestoreKit.Application` layer between `WinRestoreKit.Core` and the two temporary shells. Build and verify the WPF shell side by side, migrate one complete workflow at a time, then remove WinForms only after parity and real-desktop verification.

**Tech Stack:** .NET 8, C#, WPF, MVVM, xUnit, Windows UI Automation, self-contained `win-x64` single-file publishing.

## Global Constraints

- Preserve existing backup and restore engine semantics, snapshot format, local storage model, and safety gates.
- Keep the WinForms application runnable until Timeline, Compare/Confirm, Backup/Progress, History, Settings, About, and dialogs have verified WPF equivalents.
- `WinRestoreKit.Application` must reference neither WinForms nor WPF.
- WPF views and view models must not parse registry exports or invent comparison values.
- Comparison is read-only and uses `HasArtifactIn` followed by `HasDriftedFrom`; one module failure must not erase other results.
- Restore selection remains whole-module and incomplete-snapshot consent remains mandatory immediately before restore writes.
- Do not retain unsafe incomplete folders merely to make failed attempts durable; non-persistable failures are current-session events only.
- Support Follow system, Light, and Dark themes, 100–200% DPI, a 1024 px minimum usable width, reduced motion, keyboard operation, and UI Automation.
- Preserve `highestAvailable`, `longPathAware`, `WinRestoreKit.ico`, the exact `Properties/AssemblyInfo.cs` version source, and `GenerateAssemblyInfo=false` on the shipping app.
- Final publishing remains self-contained `win-x64`, single-file, native self-extracting, compressed, and untrimmed.
- Final cutover removes obsolete WinForms views, forms, controls, resources, helpers, tests, and compatibility paths; no shims remain.

---

## Execution Order

- [ ] **Stage 1: Build the neutral application layer and side-by-side WPF shell**

  Execute: [`2026-08-09-wpf-foundation-application-shell.md`](2026-08-09-wpf-foundation-application-shell.md)

  Gate: both the existing WinForms application and the new WPF shell build and launch; shared orchestration compiles without a UI-framework reference; theme/settings/about/update seams have focused tests.

- [ ] **Stage 2: Replace Home and primary History with the Timeline event model**

  Execute: [`2026-08-09-timeline-event-model.md`](2026-08-09-timeline-event-model.md)

  Gate: Timeline and advanced History consume one event source; ordering and event states are deterministic; verified/partial snapshots are selectable; failed/unreadable events are diagnostic-only; compressed selection cleans up its private read scope.

- [ ] **Stage 3: Add honest comparison, restore selection, and confirmation**

  Execute: [`2026-08-09-compare-confirm-restore.md`](2026-08-09-compare-confirm-restore.md)

  Gate: arbitrary selected snapshots produce Changed, Same, Unavailable, and Not captured module rows; the default All-modules filter cannot hide unsupported probes; restore selection and consent use existing safety contracts; comparison remains read-only.

- [ ] **Stage 4: Migrate Create Snapshot, progress, results, and app restore**

  Execute: [`2026-08-09-backup-progress-results.md`](2026-08-09-backup-progress-results.md)

  Gate: Create snapshot → selection → progress → result → Timeline works in WPF; run admission, pause, cancel, compression, manifest/log, late-cancel wording, dialogs, and session-event behavior match current contracts.

- [ ] **Stage 5: Verify parity and perform the clean WPF cutover**

  Execute: [`2026-08-09-wpf-cutover-release-verification.md`](2026-08-09-wpf-cutover-release-verification.md)

  Gate: real Windows desktop verification passes before deletion; WPF is the sole shipping `WinRestoreKit` application; relevant tests pass; all obsolete WinForms artifacts are gone; the publish directory contains exactly one verified `WinRestoreKit.exe`.

## Cross-Stage Review Rules

1. Run each linked plan’s focused test command before its commit.
2. Run `dotnet build src/WinRestoreKit.sln -c Debug` at every stage gate.
3. Run `dotnet test src/WinRestoreKit.sln -c Debug --no-build` after the corresponding successful build.
4. Review each stage against `docs/superpowers/specs/2026-08-09-timeline-compare-wpf-shell-design.md` before starting the next stage.
5. Stop the cutover if a workflow exists only in WinForms, a comparison value lacks Core evidence, a restore gate can be bypassed, or the WPF executable has not passed real-desktop verification.

## Completion Evidence

The migration is complete only when the Stage 5 plan records:

- the full build and test outputs;
- the runtime smoke-test matrix for Timeline, Compare, Confirm, Backup, Progress, Results, History, Settings, About, and dialogs;
- keyboard, UI Automation, reduced-motion, theme, responsive-width, and DPI results;
- screenshot-baseline comparisons;
- the exact publish command and one-file directory listing;
- executable metadata, checksum, launch evidence, manifest/elevation behavior, packaged resource behavior, and update-version coherence; and
- a repository search showing no obsolete WinForms project, source, designer, resource, control, helper, or compatibility path remains.
