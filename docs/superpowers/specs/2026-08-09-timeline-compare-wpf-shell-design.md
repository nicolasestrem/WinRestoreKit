# WinRestoreKit Timeline + Compare WPF Shell Design

**Date:** 2026-08-09  
**Status:** Approved visual direction; written specification awaiting final review  
**Approved direction:** Timeline + Comparison Workspace  
**Target framework:** WPF on .NET 8 for Windows

## 1. Objective

Replace the current WinForms shell with a modern WPF application organized around two connected ideas:

1. time selects the snapshot; and
2. comparison selects what will be restored.

The redesign must preserve the existing backup and restore engine, snapshot format, safety gates, local storage model, and self-contained `win-x64` executable. It must not reproduce the approved visual as a fragile owner-drawn WinForms skin.

## 2. Why the Current Shell Is Being Replaced

The current shell uses a permanent left rail, a large status headline, a four-cell metric strip, action buttons, and a lower dashboard grid. Typography and palette changes cannot overcome that repeated dashboard structure. The redesign therefore changes the information architecture rather than reskinning the same control tree.

The rejected directions established these constraints:

- no persistent navigation rail;
- no generic sidebar + hero + metric cards + activity list composition;
- no giant failure headline that replaces the normal workflow;
- no decorative glass, glow, purple gradients, or multicolored category tiles;
- no browser-only effects presented as feasible WinForms work; and
- no invented comparison values unsupported by Core.

## 3. Scope

### 3.1 In scope

- a WPF shell and WPF replacements for every current WinForms workflow;
- Timeline as the home experience;
- comparison of the current PC against an arbitrary verified snapshot;
- honest module-level comparison states;
- selection of whole modules into a restore set;
- a confirmation stage that exposes restore impact and existing safety gates;
- migration of backup selection, progress/results, history, settings, and about screens;
- Follow system, Light, and Dark themes;
- keyboard, scaling, reduced-motion, and UI Automation support;
- WPF runtime, accessibility, and screenshot verification; and
- preservation of self-contained single-file publishing.

### 3.2 Deliberately staged

Semantic before/after values and item-level restore are not required for the first WPF release. An optional Core-owned comparison adapter contract will allow modules to add verified semantic details later without redesigning the shell.

### 3.3 Out of scope

- changing the backup payload or manifest format;
- rewriting `WinRestoreKit.Core` backup or restore behavior;
- silently parsing registry exports in the WPF layer;
- presenting guessed or synthesized before/after values;
- replacing the local snapshot model with a database or cloud service;
- adopting WinUI 3, MSIX, or a new installer in this redesign; and
- retaining a permanent mixed WinForms/WPF production shell.

## 4. Framework and Project Architecture

### 4.1 Framework decision

WPF is the approved balance of polish, accessibility, deployment stability, and migration cost. It provides templated controls, retained-mode drawing, transitions, scalable typography, shadows, and responsive layout without the packaging changes required by WinUI 3.

### 4.2 Project layout

During migration, add a side-by-side WPF application project under `src/WinRestoreKit.Wpf/`. It references `src/WinRestoreKit.Core/` and uses the existing Core contracts rather than copying engine code.

At cutover:

- `WinRestoreKit.Wpf` becomes the only shipping application project;
- its assembly and executable names remain `WinRestoreKit`;
- the old WinForms application project, views, forms, designer files, and WinForms-only controls are removed;
- the solution, publish command, tests, documentation, and release workflow target the WPF project; and
- no compatibility shell or deprecated WinForms entry point remains.

The temporary side-by-side period exists only to preserve a runnable baseline during migration.

### 4.3 Layering

- **WinRestoreKit.Core:** snapshot discovery, payload preparation, modules, comparison evidence, backup/restore orchestration inputs, results, and safety decisions.
- **WPF application services:** thin adapters for Core operations, WPF dispatcher ownership, dialog ownership, and application lifetime.
- **View models:** presentation state, commands, selection, cancellation, and navigation state. They do not import or restore data directly.
- **Views and controls:** rendering, input, automation peers, responsive layout, and theme resources. They contain no backup or restore policy.

## 5. Application Structure and Navigation

The shell has no permanent sidebar. A compact top bar contains the wordmark, current workflow indicator, Settings, and **Create snapshot**.

The primary restore workflow is a continuous three-stage workspace:

1. **Timeline** — select a verified snapshot.
2. **Compare** — compare the selected snapshot with the current PC and select modules.
3. **Confirm** — review selected modules and restore impact, then explicitly start restore.

Timeline is the default home state. Secondary destinations such as raw run logs, advanced module inventory, About, and theme settings are compact commands rather than permanent navigation destinations.

Creating a snapshot opens the migrated backup-selection workflow. Progress and results return to the timeline as a new verified snapshot or failed-attempt event.

## 6. Timeline

### 6.1 Data source

Timeline reads the existing backup-folder and manifest data. It orders events by actual creation time and distinguishes:

- verified snapshots;
- incomplete or partial snapshots;
- failed backup attempts; and
- unreadable entries.

A failed attempt remains visible for diagnosis but is never selectable as a restore source. Unreadable entries expose their real error and never masquerade as empty snapshots.
A failed attempt survives application restart only when the existing backup flow retained a folder, manifest, or log that can be discovered safely. Folder-creation failures and cancellations whose cleanup removed the output are shown as current-session events only. Timeline must never weaken cleanup or retain an unsafe incomplete folder merely to make an event durable.

### 6.2 Interaction

- Left and Right arrow keys move among timeline events.
- Enter selects a verified or partial snapshot and opens Compare.
- Failed and unreadable events open diagnostic detail only.
- The visual timeline has an equivalent list presentation for narrow layouts and UI Automation.
- Selecting a compressed snapshot prepares a private read scope through the existing payload-validation path.
- Selection and comparison are read-only and never mutate the live system or snapshot directory.

## 7. Comparison Workspace

### 7.1 First-release granularity

Each comparison row represents one registered module. A row has exactly one evidence state:

- **Changed:** Core proved current state differs from the selected snapshot.
- **Same:** Core proved current state matches the selected snapshot.
- **Unavailable:** Core cannot establish a trustworthy comparison and supplies a reason.
- **Not captured:** the selected snapshot lacks a usable artifact for the module.

The default filter is **All modules** so modules that cannot yet prove Changed or Same remain visible. Users may switch to **Changed only** after results are available.

### 7.2 Comparison pipeline

1. The selected snapshot payload is prepared for read.
2. Module registrations are enumerated in catalog order.
3. Artifact availability is derived from manifest evidence and the existing `BackupBase.HasArtifactIn(preparedPayloadPath)` fallback, using the same precedence as restore-content discovery. A proven absence becomes Not captured; an indeterminate or throwing probe becomes Unavailable.
4. For a module with a usable artifact, Core calls its existing `BackupBase.HasDriftedFrom(preparedPayloadPath)` contract. `true` maps to Changed, `false` maps to Same, and `null` maps to Unavailable. Registry-backed modules use Core's normalized fresh-export comparison; modules that deliberately cannot make a trustworthy comparison remain Unavailable.
5. These probes execute asynchronously with bounded concurrency and cancellation. No comparison calls `Backup`, writes into the selected snapshot, or asks the WPF layer to parse artifacts.
6. Each module produces an independent result; one exception becomes one Unavailable row rather than aborting the workspace.
7. Results stream into the view model without reordering the catalog.
8. Canceling comparison stops pending probes and disposes temporary payloads.

### 7.3 Optional semantic adapters

Core may define an optional comparison-provider contract for modules capable of producing verified semantic details. Such a provider returns structured labels and snapshot/current values with explicit confidence and restore granularity.

The WPF layer never derives semantic values from registry or payload text. When a provider is absent, the detail tray shows module-level evidence, captured artifacts, comparison availability, and restore impact only.

### 7.4 Restore selection

The first WPF release selects whole modules. A module can be added to the restore set only when the selected snapshot contains a usable restore artifact. Unavailable comparison does not automatically prohibit restore, but the row must clearly state that equivalence could not be measured.

The restore set exists in memory until confirmation or cancellation. Changing the selected timeline snapshot clears the restore set after explicit confirmation if it is non-empty.

## 8. Detail Tray and Confirm Stage

Selecting a comparison row opens a temporary detail tray containing:

- module title and group;
- evidence state and reason;
- captured artifact summary;
- optional semantic values supplied by Core;
- affected applications or processes;
- Explorer restart or sign-out requirements declared by existing module/Core contracts. The first WPF release does not infer or advertise reboot impact without a structured Core declaration;
- an explicit Add to restore or Remove from restore action.

The Confirm stage lists all selected modules, grouped by impact. It identifies process closures and machine-state changes before execution. It then invokes the existing snapshot gate and restore orchestration path.

Partial or incomplete snapshots remain visually distinct. Existing incomplete-snapshot consent is mandatory immediately before any restore write. The WPF shell cannot bypass, pre-answer, or downgrade that decision.

## 9. Backup, Progress, Results, and History

### 9.1 Create snapshot

**Create snapshot** is always available in the top bar when no run is active. It opens the migrated backup-selection workflow with the same module catalog, scope behavior, destination containment rules, naming rules, and compression options as the current application.

### 9.2 Progress and results

A running operation replaces the workspace content with progress while retaining the shell. It uses existing run-control and outcome semantics. Cancellation, rollback, application closure, Explorer restart, and shutdown behavior remain engine-owned.

After completion, the result appears on the timeline:

- a verified snapshot becomes a selectable snapshot event;
- an incomplete or partial result is labeled honestly;
- a failed attempt is diagnostic-only; and
- late cancellation must not relabel an already completed run.

### 9.3 History

Timeline subsumes the primary History navigation. Advanced history remains available as a searchable list with exact timestamps, machine names, paths, manifest state, sizes, and logs. It is an alternate representation of the same event model, not a separate source of truth.

## 10. Visual System

### 10.1 Character

The application should resemble a first-party Windows utility: calm, precise, and task-focused. It must not read as a gamer dashboard, generated SaaS template, or themed legacy form.

### 10.2 Themes and color

Replace Voltage and Flux naming with:

- Follow system;
- Light; and
- Dark.

Use neutral Windows surfaces, one mineral-blue action color, and one restrained coral warning color. Status always includes text and an icon; color alone carries no meaning.

### 10.3 Typography and icons

- Segoe UI Variable for interface text.
- A packaged monospace face only for logs, registry paths, and technical identifiers.
- Fluent system icons paired with labels for primary actions.
- No icon-only destructive control.

### 10.4 Geometry and effects

- 6–10 px corner radii for controls and temporary surfaces.
- Mostly flat panes with borders or tonal separation.
- Shadows only on temporary layers such as dialogs and the detail tray.
- No Mica dependency, blur, glow, purple gradient, or decorative chart.

### 10.5 Motion

Selection, filtering, and tray transitions use 120–180 ms motion. Follow Windows reduced-motion settings and remove nonessential animation when reduced motion is enabled.

## 11. Responsive and Accessible Behavior

- Support 100%, 125%, 150%, 175%, and 200% DPI.
- Support a minimum usable window width of 1024 px.
- At narrow widths, comparison panes stack vertically rather than clipping.
- Timeline, comparison rows, restore selection, and confirmation have visible keyboard focus.
- Arrow keys move through the timeline; standard navigation moves through rows and actions.
- Every custom visual exposes UI Automation names, roles, states, and actions.
- The timeline has a list fallback that provides equivalent information and actions.
- Text contrast meets at least 4.5:1 for normal text.
- Focus, selected, unavailable, changed, and warning states remain distinguishable without color.

## 12. Errors, Cancellation, and Cleanup

- Payload preparation failures disable Compare and Restore for the affected entry and display the real reason.
- Per-module comparison exceptions produce Unavailable rows with diagnostic text and logs.
- The shell does not convert errors into “nothing found.”
- Comparison cancellation is explicit, awaited, and disposes any private payload read scope.
- Navigating away from an active comparison requests cancellation before discarding the view model.
- Restore cancellation continues to use existing rollback and outcome semantics.
- User-visible errors use owner-bound WPF dialogs or inline error states; no ownerless message boxes remain.
- Errors continue through existing logging and result contracts.

## 13. Migration Strategy

1. Add the WPF shell project, shared theme resources, application services, and view-model test project support.
2. Build the event model and Timeline against existing manifests.
3. Add read-only arbitrary-snapshot module comparison and cancellation.
4. Add restore-set construction and Confirm using existing restore gates.
5. Migrate backup selection, progress/results, and advanced history.
6. Migrate Settings, About, dialogs, and remaining app-owned seams.
7. Verify feature parity, runtime behavior, accessibility, scaling, themes, and publishing.
8. Make WPF the only shipping application project and remove the WinForms shell cleanly.

WinForms remains runnable during steps 1–6. No new product feature is added only to the old shell during migration unless required for a safety fix.

## 14. Testing and Verification

### 14.1 Preserve existing coverage

All existing Core, module, payload, manifest, orchestration, restore-safety, and outcome tests remain green. Tests that only assert WinForms control construction are replaced by tests of the new observable WPF contract rather than mechanically ported.

### 14.2 New deterministic tests

Add tests for:

- timeline ordering and stable tie-breaking;
- verified, partial, failed, and unreadable event classification;
- failed attempts being non-restorable;
- arbitrary snapshot selection and payload preparation;
- Changed, Same, Unavailable, and Not captured comparison states;
- per-module exception isolation;
- comparison cancellation and temporary-payload cleanup;
- restore-set add/remove/clear behavior;
- restore-set invalidation when changing snapshot;
- confirmation impact grouping;
- incomplete-snapshot consent before execution;
- semantic-provider presence and absence; and
- theme and settings persistence decisions.

### 14.3 WPF runtime verification

Run the built application on a real Windows desktop and verify:

- startup and main-window construction;
- Timeline selection and accessible list parity;
- comparison loading, filtering, selection, and cancellation;
- keyboard-only Timeline → Compare → Confirm traversal;
- owner-bound confirmation and error dialogs;
- backup, progress, results, and restore smoke paths;
- light, dark, and follow-system switching;
- reduced-motion behavior;
- 100%, 150%, and 200% DPI; and
- narrow-width stacked comparison layout.

### 14.4 Visual regression

Capture deterministic screenshot baselines for:

- Light and Dark themes;
- normal, failed-attempt, partial-snapshot, and unreadable states;
- Timeline, Compare, and Confirm stages;
- 100%, 150%, and 200% DPI; and
- minimum supported width.

### 14.5 Release verification

Publish the final WPF application as self-contained `win-x64`. Verify the produced executable on a clean machine or equivalent Windows environment, including:

- single-file startup;
- elevated manifest behavior;
- packaged fonts and icons;
- title-bar and theme behavior;
- backup destination resolution from the executable directory; and
- update-check version coherence.

## 15. Acceptance Criteria

The redesign is complete only when all of the following are true:

1. Timeline + Compare is the shipping home and restore workflow.
2. The application has no permanent sidebar or dashboard stat-card home.
3. The WPF shell exposes all current product workflows.
4. Comparison displays only evidence Core can support.
5. Restore selection is whole-module and requires explicit confirmation.
6. Existing snapshot gates and restore safety behavior remain enforced.
7. Failed attempts are visible but non-restorable.
8. Light, Dark, and Follow system themes meet the approved visual system.
9. Keyboard, UI Automation, reduced motion, DPI, and narrow-width requirements pass runtime verification.
10. Existing relevant tests and all new deterministic tests pass.
11. The final self-contained executable passes real Windows smoke verification.
12. The obsolete WinForms shell, controls, dialogs, and compatibility paths are removed.

## 16. Approved Decisions

- Structural direction: Timeline combined with Before / After comparison.
- Framework: WPF on .NET 8.
- Migration: side-by-side until parity, followed by clean WinForms removal.
- Comparison depth: staged; module-level first, optional semantic adapters later.
- Restore granularity: whole module in the first WPF release.
- Visual system: Windows-native neutral surfaces, mineral blue, restrained coral, Light/Dark/System themes.
- Distribution: preserve the self-contained single-file executable.
- Safety: comparison is read-only; existing snapshot and restore gates remain authoritative.
