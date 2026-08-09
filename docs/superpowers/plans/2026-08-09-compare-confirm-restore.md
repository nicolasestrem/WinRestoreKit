# Compare, Confirm, and Restore Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let a user compare any Timeline-selected verified or partial snapshot with the current PC, select only whole modules with usable captured artifacts, and start the unchanged safe restore pipeline through owner-bound WPF confirmation dialogs.

**Architecture:** Keep all comparison evidence and module catalog projection in framework-neutral `WinRestoreKit.Application`; WPF consumes immutable evidence records and never reads manifests, payloads, registry exports, or artifacts. The Timeline transfers its prepared read scope to the Compare navigator; the workspace owns it until comparison completes or is cancelled, then disposes it. Confirm is a WPF presentation of declared restore impacts, while the existing Application `BackupRestoreOrchestrator` remains the sole authority for `RestorePlan`, `RestoreScope`, `SnapshotGate`, `RestoreDispatch`, result logging, and `ExplorerRestartPrompt`.

**Tech Stack:** .NET 8 for Windows, C#, WPF/MVVM, `System.Threading`, xUnit 2.9.3, and the existing `WinRestoreKit.Core` restore contracts.

## Global Constraints

- Execute this plan after `2026-08-09-wpf-foundation-application-shell.md` and `2026-08-09-timeline-event-model.md`; do not cover backup creation, results migration, or final project cutover here.
- Windows/.NET 8 only; preserve `WinRestoreKit.Core` backup/restore semantics and the on-disk snapshot and manifest formats.
- `WinRestoreKit.Application` references neither WinForms nor WPF. It may use Core internals only through the Foundation-provided friend assembly relationship; WPF consumes Application public contracts rather than Core internal catalog or manifest types.
- Keep WinForms runnable during this stage. Do not delete or alter the existing WinForms restore wizard/forms except for moving a duplicate test only when the WPF test covers the new Application behavior.
- The four comparison states are exactly `ComparisonState { Changed, Same, Unavailable, NotCaptured }`. Do not replace `Unavailable` with `Same`, omit it from the default view, or invent before/after values.
- Use manifest evidence before `BackupBase.HasArtifactIn(preparedPayloadPath)`, with the exact `RestoreContents` precedence: manifest `Succeeded` proves an artifact; manifest `Skipped` or `Failed` proves none; only an absent/silent/unknown manifest entry reaches `HasArtifactIn`. A `false` probe becomes `NotCaptured`; a `null` probe becomes `NotCaptured` when a manifest exists (its silence says the module was not in that run), but remains usable when no manifest exists—the same `RestoreContents` legacy fallback—so the subsequent drift result determines `Changed`, `Same`, or `Unavailable`. A thrown artifact probe is `Unavailable` for that module.
- After a usable artifact is established, map `BackupBase.HasDriftedFrom(preparedPayloadPath)` `true` to `Changed`, `false` to `Same`, and `null` or an exception to `Unavailable`. One module error must log and affect only that row.
- Comparison is read-only. It must not call `Backup`, `Restore`, `RestoreScope.HasBackup`, or write in the selected snapshot directory. A compressed read scope is temporary and must be disposed exactly once after every success, error, cancellation, or discarded snapshot selection.
- Comparison runs at a bounded concurrency of four probes, honours cancellation before starting pending work, awaits already-running workers before releasing the payload, and preserves catalog order even when results complete out of order.
- Restore selection is whole-module only. A row may enter the restore set only when `ModuleComparison.HasUsableArtifact` is true; an `Unavailable` comparison with a proven readable artifact remains selectable but must remain visibly unavailable.
- The default filter is **All modules**. **Changed only** is an optional view filter applied after evidence arrives; it must not mutate comparison results or the restore set.
- Changing to a different Timeline snapshot when the restore set is non-empty requires an owner-bound, default-cancel discard confirmation. Accepting clears the set before constructing the new workspace; declining disposes the incoming payload and keeps the old snapshot and set unchanged.
- Do not create a semantic comparison-provider contract in this stage. Core has no current verified semantic values or item-level restore contract; the tray shows only captured-artifact evidence and existing `BackupBase` restore declarations.
- Confirm shows only existing impacts: `RestoreTargets`, `ProcessesToCloseBeforeRestore`, `WarningMessage`, `RequiresExplorerRestart`, and `RestorePlan.FidelityCaveat`. Do not add a reboot field or infer a sign-out field by parsing warning text; display an existing warning verbatim when present.
- The mandatory partial/failed pre-restore-snapshot decision remains at the existing `SnapshotGate` point immediately before restore writes. `IRunDialogService.ConfirmSnapshotOverride` must show an owner-bound Yes/No dialog whose default is No; WPF must never pre-answer or bypass it.
- The Foundation move replaces `RunSummary.Icon` with `RunSeverity { Information, Warning, Error }` and replaces the WinForms-only owner with `IRunUi.DialogOwner` of type `object`. WPF supplies its main `Window` through that property for the existing Core `AppStoreApps.RestoreAsync(path, ui.DialogOwner)` / `RestoreDialog : Action<string, object>` seam; do not add a duplicate shell app-restore dialog abstraction or compatibility overload.
- Keep tests in `src/WinRestoreKit.Tests`; use Foundation's `WpfTestHost.Run(...)` STA helper for every test that constructs a WPF `Window`, `UserControl`, or dialog.

---

## File and Interface Map

### Inputs from earlier plans

```csharp
// src/WinRestoreKit.Application/Snapshots/SnapshotEventKind.cs
public enum SnapshotEventKind { Verified, Partial, Failed, Unreadable }

// src/WinRestoreKit.Application/Snapshots/SnapshotEvent.cs
public sealed class SnapshotEvent
{
    public SnapshotEventKind Kind { get; }
    public DateTime Created { get; }
    public string DisplayName { get; }
    public string CanonicalPath { get; }
    public string DiagnosticReason { get; }
    public string MachineName { get; }
    public long SizeBytes { get; }
    public bool IsSizeComplete { get; }
    public bool IsRestorable { get; } // true only for Verified and Partial
    internal ManifestData Manifest { get; }
}

// src/WinRestoreKit.Application/Snapshots/SnapshotPayloadPreparationService.cs
public Task<SnapshotPayloadPreparation> PrepareAsync(
    SnapshotEvent snapshot, CancellationToken cancellationToken);

// SnapshotPayloadPreparation is IDisposable. Error != null means no usable payload;
// disposing it releases the Core BackupPayload.ReadScope, including temporary extraction.

// src/WinRestoreKit.Wpf/Navigation/ITimelineNavigator.cs
internal interface ITimelineNavigator
{
    void OpenCompare(SnapshotPayloadPreparation preparation);
    void ShowSnapshotDiagnostic(SnapshotEvent snapshot);
}
```

```csharp
// Foundation-owned, in namespace WinRestoreKit.
internal interface IRunUi
{
    void SetProgressText(string text);
    void SetProgressPercent(int percent);
    void SetProgressDetail(string groupInfo, string elapsed, string remaining,
                           string throughput, long bytesWritten, int errors, int warnings);
    void ShowSummary(RunSummary summary, string caption,
                     IReadOnlyList<ModuleOutcome> outcomes);
    object DialogOwner { get; }
    IReadOnlyList<string> ShowConsentDialog(RestorePlan plan); // null = cancel
    bool ConfirmSnapshotOverride(string text, string caption); // false = do not continue
    void ShowPlanCompositionError(string text, string caption);
    void SetExplorerRestartVisible(bool visible);
}

internal sealed class BackupRestoreOrchestrator
{
    internal BackupRestoreOrchestrator(IRunUi ui, RunControl runControl = null);
    internal Task RunRestore(IReadOnlyList<BackupBase> modules, string backupPath);
}

// src/WinRestoreKit.Wpf/Services/IRunPresentation.cs
internal interface IRunPresentation
{
    void SetProgressText(string text);
    void SetProgressPercent(int percent);
    void SetProgressDetail(string groupInfo, string elapsed, string remaining,
                           string throughput, long bytesWritten, int errors, int warnings);
    void ShowSummary(RunSummary summary, string caption, IReadOnlyList<ModuleOutcome> outcomes);
    void SetExplorerRestartVisible(bool visible);
}

// src/WinRestoreKit.Wpf/Services/IRunDialogService.cs
internal interface IRunDialogService
{
    IReadOnlyList<string> ShowRestoreConsent(RestorePlan plan);
    bool ConfirmSnapshotOverride(string text, string caption);
    void ShowPlanCompositionError(string text, string caption);
}

// src/WinRestoreKit.Wpf/Services/WpfRunUi.cs
internal WpfRunUi(Dispatcher dispatcher, IRunPresentation presentation,
                  IRunDialogService dialogs, Func<Window> ownerProvider);
```

```csharp
// Foundation-owned: src/WinRestoreKit.Wpf/Infrastructure/DelegateCommand.cs
internal sealed class DelegateCommand : ICommand
{
    internal DelegateCommand(Action<object> execute, Predicate<object> canExecute = null);
    public bool CanExecute(object parameter);
    public void Execute(object parameter);
    public event EventHandler CanExecuteChanged;
    internal void RaiseCanExecuteChanged();
}
```

Use this command and the adjacent Foundation `ObservableObject` from every Compare/Confirm ViewModel. Command delegates start a ViewModel-owned `async Task` wrapper that catches/reports its own errors; do not introduce another relay/async-command base type.

### Files created by this plan

| File | Responsibility |
| --- | --- |
| `src/WinRestoreKit.Application/Modules/BackupModuleRegistration.cs` | Public, immutable application projection of a registered Core module and its category. |
| `src/WinRestoreKit.Application/Modules/BackupModuleCatalog.cs` | Produces catalog-order module projections without exposing Core's internal `ModuleCatalog`/`ModuleRegistration`. |
| `src/WinRestoreKit.Application/Comparison/ComparisonState.cs` | The four immutable evidence-state names. |
| `src/WinRestoreKit.Application/Comparison/ModuleComparison.cs` | Immutable per-module artifact and drift evidence consumed by WPF. |
| `src/WinRestoreKit.Application/Comparison/SnapshotComparisonService.cs` | Read-only manifest-first, cancellable, bounded-concurrency snapshot comparison. |
| `src/WinRestoreKit.Wpf/ViewModels/ComparisonFilter.cs` | UI-only `All` / `ChangedOnly` filtering mode. |
| `src/WinRestoreKit.Wpf/ViewModels/ModuleImpactViewModel.cs` | Direct projection of existing restore targets, process requirements, Explorer flag, and warning text. |
| `src/WinRestoreKit.Wpf/ViewModels/ModuleComparisonRowViewModel.cs` | One ordered comparison row, its evidence state, detail data, and restore-set action. |
| `src/WinRestoreKit.Wpf/ViewModels/RestoreSetViewModel.cs` | In-memory, whole-module restore selection with no persistence. |
| `src/WinRestoreKit.Wpf/ViewModels/ComparisonWorkspaceViewModel.cs` | Compare lifecycle, filtering, cancellation, selected detail tray, and scope ownership. |
| `src/WinRestoreKit.Wpf/ViewModels/ConfirmViewModel.cs` | Selected-module impact groups, restore launch, progress/result presentation, and cancellation request. |
| `src/WinRestoreKit.Wpf/Navigation/CompareWorkflowNavigator.cs` | `ITimelineNavigator` implementation that owns snapshot replacement/discard behavior and shell transitions. |
| `src/WinRestoreKit.Wpf/Services/RestoreRunDialogService.cs` | Owner-bound implementation of the Foundation `IRunDialogService` for restore-specific dialogs. |
| `src/WinRestoreKit.Wpf/Services/ICompareDialogService.cs` | Typed, testable compare navigation dialogs: discard restore set and display a snapshot diagnostic. |
| `src/WinRestoreKit.Wpf/Services/CompareDialogService.cs` | Main-window-owned WPF implementation of `ICompareDialogService`; its explicit owner is passed to every modal diagnostic and discard confirmation. |
| `src/WinRestoreKit.Wpf/Views/ComparisonWorkspaceView.xaml` and `.xaml.cs` | Accessible All/Changed-only workspace, ordered rows, temporary detail tray, and restore-set action. |
| `src/WinRestoreKit.Wpf/Views/ConfirmView.xaml` and `.xaml.cs` | Impact-grouped confirmation screen and restore/progress controls. |
| `src/WinRestoreKit.Wpf/Views/Dialogs/RestoreConsentDialog.xaml` and `.xaml.cs` | Owner-bound final consent dialog built from the actual `RestorePlan` created by the orchestrator. |
| `src/WinRestoreKit.Tests/BackupModuleCatalogTests.cs` | Application projection order/category regression coverage. |
| `src/WinRestoreKit.Tests/SnapshotComparisonServiceTests.cs` | Pure comparison state, precedence, isolation, ordering, cancellation, and cleanup tests. |
| `src/WinRestoreKit.Tests/RestoreSetViewModelTests.cs` | Whole-module restore-set rules. |
| `src/WinRestoreKit.Tests/ComparisonWorkspaceViewModelTests.cs` | Filter, row-order, detail tray, and cancellation/selection behavior. |
| `src/WinRestoreKit.Tests/ConfirmViewModelTests.cs` | Existing-impact grouping and real orchestrator-entry coverage. |
| `src/WinRestoreKit.Tests/RestoreConsentDialogTests.cs` | STA WPF construction, default-safe consent, and modal owner wiring. |

### Files modified by this plan

| File | Change |
| --- | --- |
| `src/WinRestoreKit.Wpf/ViewModels/ShellViewModel.cs` | Replace the Foundation-only Timeline placeholder with a `CurrentWorkflow` host and explicit Timeline/Compare/Confirm transitions. |
| `src/WinRestoreKit.Wpf/MainWindow.xaml` | Add data templates for Timeline, Compare, and Confirm view models; retain the compact Foundation shell chrome. |
| `src/WinRestoreKit.Wpf/MainWindow.xaml.cs` | Compose `CompareWorkflowNavigator` with the main window as the dialog owner and supply it to the Timeline view model. |

### Contracts produced for later plans

```csharp
// src/WinRestoreKit.Application/Modules/BackupModuleRegistration.cs
public sealed class BackupModuleRegistration
{
    public BackupBase Module { get; }
    public string Category { get; }
    public string Title { get; }
}

// src/WinRestoreKit.Application/Modules/BackupModuleCatalog.cs
public static class BackupModuleCatalog
{
    public static IReadOnlyList<BackupModuleRegistration> CreateAll();
}

// src/WinRestoreKit.Application/Comparison/ComparisonState.cs
public enum ComparisonState { Changed, Same, Unavailable, NotCaptured }

// src/WinRestoreKit.Application/Comparison/ModuleComparison.cs
public sealed class ModuleComparison
{
    public BackupBase Module { get; }
    public ComparisonState State { get; }
    public bool HasUsableArtifact { get; }
    public string ArtifactSummary { get; }
    public string Reason { get; }
}

// src/WinRestoreKit.Application/Comparison/SnapshotComparisonService.cs
public sealed class SnapshotComparisonService
{
    public Task<IReadOnlyList<ModuleComparison>> CompareAsync(
        SnapshotEvent snapshot, IReadOnlyList<BackupBase> modules,
        CancellationToken cancellationToken);
}
```

The service also has one **internal** overload accepting the Timeline-owned `SnapshotPayloadPreparation` and `IProgress<ComparisonProgress>`. It is a resource-ownership handoff, not a compatibility overload: the public exact signature creates/disposes its own preparation, while WPF uses the supplied scope so Timeline extraction is neither duplicated nor leaked.

---

### Task 1: Expose the module catalog to framework-neutral consumers

**Files:**
- Create: `src/WinRestoreKit.Application/Modules/BackupModuleRegistration.cs`
- Create: `src/WinRestoreKit.Application/Modules/BackupModuleCatalog.cs`
- Test: `src/WinRestoreKit.Tests/BackupModuleCatalogTests.cs`

**Interfaces:**
- Consumes: Core-internal `Conf.ModuleCatalog.CreateAll()` and `ModuleRegistration.Module`/`Category`, accessed only within the Foundation-created `WinRestoreKit.Application` friend assembly.
- Produces: `BackupModuleCatalog.CreateAll()` returning one immutable `BackupModuleRegistration` per registered module, in the exact Core catalog order, with its existing category and `BackupBase.Title`.

- [ ] **Step 1: Write the failing catalog-projection test**

```csharp
[Fact]
public void CreateAll_PreservesCoreCatalogOrderCategoryAndTitle()
{
    IReadOnlyList<BackupModuleRegistration> actual = BackupModuleCatalog.CreateAll();
    IReadOnlyList<ModuleRegistration> core = ModuleCatalog.CreateAll();

    Assert.Equal(core.Count, actual.Count);
    for (int index = 0; index < core.Count; index++)
    {
        Assert.Same(core[index].Module.GetType(), actual[index].Module.GetType());
        Assert.Equal(core[index].Category, actual[index].Category);
        Assert.Equal(core[index].Module.Title, actual[index].Title);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails because the public Application catalog does not exist**

Run:

```powershell
dotnet test src/WinRestoreKit.Tests/WinRestoreKit.Tests.csproj --filter "FullyQualifiedName~BackupModuleCatalogTests"
```

Expected: compilation fails because `BackupModuleCatalog` and `BackupModuleRegistration` are undefined.

- [ ] **Step 3: Add the immutable projection and order-preserving wrapper**

```csharp
// BackupModuleRegistration.cs
namespace WinRestoreKit;

public sealed class BackupModuleRegistration
{
    internal BackupModuleRegistration(BackupBase module, string category)
    {
        Module = module ?? throw new ArgumentNullException(nameof(module));
        Category = category ?? string.Empty;
        Title = module.Title ?? string.Empty;
    }

    public BackupBase Module { get; }
    public string Category { get; }
    public string Title { get; }
}

// BackupModuleCatalog.cs
namespace WinRestoreKit;

public static class BackupModuleCatalog
{
    public static IReadOnlyList<BackupModuleRegistration> CreateAll()
        => ModuleCatalog.CreateAll()
            .Select(entry => new BackupModuleRegistration(entry.Module, entry.Category))
            .ToArray();
}
```

Add the required `using Conf;`, `System`, `System.Collections.Generic`, and `System.Linq` directives rather than widening Core types or copying the catalog into WPF.

- [ ] **Step 4: Run the focused test and verify the Core and Application catalog rows match**

Run:

```powershell
dotnet test src/WinRestoreKit.Tests/WinRestoreKit.Tests.csproj --filter "FullyQualifiedName~BackupModuleCatalogTests"
```

Expected: PASS; every module count, CLR type, category, and title matches the existing Core catalog in source order.

- [ ] **Step 5: Commit the independently usable catalog facade**

```powershell
git add src/WinRestoreKit.Application/Modules/BackupModuleRegistration.cs src/WinRestoreKit.Application/Modules/BackupModuleCatalog.cs src/WinRestoreKit.Tests/BackupModuleCatalogTests.cs
git commit -m "feat: expose application module catalog"
```

### Task 2: Define immutable evidence records and a manifest-first comparison service

**Files:**
- Create: `src/WinRestoreKit.Application/Comparison/ComparisonState.cs`
- Create: `src/WinRestoreKit.Application/Comparison/ModuleComparison.cs`
- Create: `src/WinRestoreKit.Application/Comparison/SnapshotComparisonService.cs`
- Test: `src/WinRestoreKit.Tests/SnapshotComparisonServiceTests.cs`

**Interfaces:**
- Consumes: `SnapshotEvent` and its Application-internal parsed manifest; `SnapshotPayloadPreparationService`; `BackupBase.HasArtifactIn(string)`; `BackupBase.HasDriftedFrom(string)`; `BackupManifest.StateSucceeded`, `StateSkipped`, and `StateFailed`.
- Produces: immutable, catalog-indexed `ModuleComparison` rows and the exact public `SnapshotComparisonService.CompareAsync(SnapshotEvent, IReadOnlyList<BackupBase>, CancellationToken)` signature.
- Invariant: a row can be restorable only when `HasUsableArtifact` is true. `ComparisonState.Unavailable` may still have a usable artifact when only the live drift probe is indeterminate; it must not be silently classified as `Same`.

- [ ] **Step 1: Write failing state, precedence, and mapping tests**

```csharp
[Theory]
[InlineData(true, ComparisonState.Changed)]
[InlineData(false, ComparisonState.Same)]
public async Task CompareAsync_ManifestSucceeded_MapsDriftWithoutArtifactProbe(
    bool drifted, ComparisonState expected)
{
    ProbeModule module = new("Display", artifact: false, drifted: drifted);
    SnapshotEvent snapshot = Snapshot(Succeeded(module));

    ModuleComparison row = Assert.Single(await new SnapshotComparisonService()
        .CompareAsync(snapshot, new[] { (BackupBase)module }, CancellationToken.None));

    Assert.Equal(expected, row.State);
    Assert.True(row.HasUsableArtifact);
    Assert.Equal(0, module.ArtifactProbeCount);
}

[Theory]
[InlineData(BackupManifest.StateSkipped)]
[InlineData(BackupManifest.StateFailed)]
public async Task CompareAsync_ManifestStatesWithoutArtifact_AreNotCaptured(string state)
{
    ProbeModule module = new("Fonts", artifact: true, drifted: true);

    ModuleComparison row = Assert.Single(await new SnapshotComparisonService()
        .CompareAsync(Snapshot(Entry(module, state)), new[] { (BackupBase)module }, CancellationToken.None));

    Assert.Equal(ComparisonState.NotCaptured, row.State);
    Assert.False(row.HasUsableArtifact);
    Assert.Equal(0, module.ArtifactProbeCount);
    Assert.Equal(0, module.DriftProbeCount);
}

[Fact]
public async Task CompareAsync_ManifestSilentIndeterminateArtifact_IsNotCaptured()
{
    ProbeModule module = new("Terminal", artifact: null, drifted: false);

    ModuleComparison row = Assert.Single(await new SnapshotComparisonService()
        .CompareAsync(Snapshot(manifest: Manifest(EntryForDifferentModule())),
                      new[] { (BackupBase)module }, CancellationToken.None));

    Assert.Equal(ComparisonState.NotCaptured, row.State);
    Assert.False(row.HasUsableArtifact);
    Assert.Equal(0, module.DriftProbeCount);
}

[Fact]
public async Task CompareAsync_NoManifestIndeterminateArtifact_UsesRestoreContentsFallback()
{
    ProbeModule module = new("Legacy", artifact: null, drifted: false);

    ModuleComparison row = Assert.Single(await new SnapshotComparisonService()
        .CompareAsync(Snapshot(manifest: null), new[] { (BackupBase)module }, CancellationToken.None));

    Assert.Equal(ComparisonState.Same, row.State);
    Assert.True(row.HasUsableArtifact);
    Assert.Equal(1, module.DriftProbeCount);
}
```

In this test file, construct `SnapshotEvent` through Timeline's internal constructor under the Application-to-Tests friendship, create a real temporary backup folder for every snapshot, and use private `BackupBase` fakes only to observe the existing virtual probe seams. Do not use fake semantic values or add a semantic-provider interface.

- [ ] **Step 2: Run the tests to verify the comparison types and service are missing**

Run:

```powershell
dotnet test src/WinRestoreKit.Tests/WinRestoreKit.Tests.csproj --filter "FullyQualifiedName~SnapshotComparisonServiceTests"
```

Expected: compilation fails because `ComparisonState`, `ModuleComparison`, and `SnapshotComparisonService` do not exist.

- [ ] **Step 3: Add the state and immutable evidence record**

```csharp
namespace WinRestoreKit;

public enum ComparisonState
{
    Changed,
    Same,
    Unavailable,
    NotCaptured
}

public sealed class ModuleComparison
{
    internal ModuleComparison(BackupBase module, ComparisonState state,
                              bool hasUsableArtifact, string artifactSummary, string reason)
    {
        Module = module ?? throw new ArgumentNullException(nameof(module));
        State = state;
        HasUsableArtifact = hasUsableArtifact;
        ArtifactSummary = artifactSummary ?? string.Empty;
        Reason = reason ?? string.Empty;
    }

    public BackupBase Module { get; }
    public ComparisonState State { get; }
    public bool HasUsableArtifact { get; }
    public string ArtifactSummary { get; }
    public string Reason { get; }
}
```

Keep construction internal so WPF can render evidence but cannot manufacture comparison results. The Tests assembly accesses it through Application's existing `InternalsVisibleTo` attribute.

- [ ] **Step 4: Implement exact manifest-first presence and drift mapping**

```csharp
private static ModuleComparison CompareOne(BackupBase module, string payloadPath,
                                           ManifestData manifest)
{
    ManifestModule entry = FindManifestEntry(manifest, module.GetType().Name);

    if (entry?.State == BackupManifest.StateSkipped || entry?.State == BackupManifest.StateFailed)
    {
        return new ModuleComparison(module, ComparisonState.NotCaptured, false,
            "The snapshot manifest records no usable artifact for this module.",
            string.IsNullOrWhiteSpace(entry.Reason) ? entry.State : entry.Reason);
    }

    bool usableArtifact;
    string artifactSummary;
    if (entry?.State == BackupManifest.StateSucceeded)
    {
        usableArtifact = true;
        artifactSummary = "The snapshot manifest records this module as captured.";
    }
    else
    {
        bool? probe;
        try { probe = module.HasArtifactIn(payloadPath); }
        catch (Exception ex)
        {
            LogHelper.Instance.LogMessage("Comparison artifact probe failed for " + module.Title + ": " + ex.Message);
            return new ModuleComparison(module, ComparisonState.Unavailable, false,
                "Artifact presence could not be determined.", ex.Message);
        }

        if (probe == false)
            return new ModuleComparison(module, ComparisonState.NotCaptured, false,
                "The module proved that this snapshot has no restore artifact.", string.Empty);
        if (!probe.HasValue)
        {
            if (manifest != null)
                return new ModuleComparison(module, ComparisonState.NotCaptured, false,
                    "The manifest does not record this module and the module cannot prove an artifact.",
                    string.Empty);

            // Preserve RestoreContents' no-manifest fallback; drift remains explicit below.
            usableArtifact = true;
            artifactSummary = "No manifest is available and the module cannot disprove a legacy artifact.";
        }
        else
        {
            usableArtifact = true;
            artifactSummary = "The module verified a captured artifact.";
        }

    }

    try
    {
        bool? drifted = module.HasDriftedFrom(payloadPath);
        if (drifted == true)
            return new ModuleComparison(module, ComparisonState.Changed, usableArtifact, artifactSummary,
                "Core confirmed that current state differs from the snapshot.");
        if (drifted == false)
            return new ModuleComparison(module, ComparisonState.Same, usableArtifact, artifactSummary,
                "Core confirmed that current state matches the snapshot.");
        return new ModuleComparison(module, ComparisonState.Unavailable, usableArtifact, artifactSummary,
            "Core could not establish a trustworthy live comparison.");
    }
    catch (Exception ex)
    {
        LogHelper.Instance.LogMessage("Comparison drift probe failed for " + module.Title + ": " + ex.Message);
        return new ModuleComparison(module, ComparisonState.Unavailable, usableArtifact, artifactSummary,
            ex.Message);
    }
}
```

`FindManifestEntry` must match exact CLR type name with `StringComparison.Ordinal`, just as `RestoreContents` does. Treat every manifest state other than the three named states as silent and call `HasArtifactIn`; never claim a future/unknown state means captured.

- [ ] **Step 5: Add the two ownership entry points without duplicating payload extraction**

```csharp
public sealed class SnapshotComparisonService
{
    public async Task<IReadOnlyList<ModuleComparison>> CompareAsync(
        SnapshotEvent snapshot, IReadOnlyList<BackupBase> modules,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!snapshot.IsRestorable)
            throw new ArgumentException("Only a verified or partial snapshot can be compared.", nameof(snapshot));

        using SnapshotPayloadPreparation preparation = await new SnapshotPayloadPreparationService()
            .PrepareAsync(snapshot, cancellationToken).ConfigureAwait(false);
        return await CompareAsync(preparation, modules, cancellationToken, progress: null)
            .ConfigureAwait(false);
    }

    internal Task<IReadOnlyList<ModuleComparison>> CompareAsync(
        SnapshotPayloadPreparation preparation, IReadOnlyList<BackupBase> modules,
        CancellationToken cancellationToken, IProgress<ComparisonProgress> progress)
    {
        ArgumentNullException.ThrowIfNull(preparation);
        // This overload never disposes preparation: the Timeline navigator transferred ownership
        // to the WPF workspace, which disposes it only after this returned task has settled.
        return ComparePreparedAsync(preparation, modules, cancellationToken, progress);
    }
}
```

When `preparation.Error` is non-empty, return `NotCaptured` only for manifest `Skipped`/`Failed` rows; every other module becomes `Unavailable` with that exact preparation error and `HasUsableArtifact == false`. This exposes a missing/corrupt payload as a real disabled comparison/restore condition rather than treating the snapshot as empty.

- [ ] **Step 6: Run the mapping suite and verify every evidence state is honest**

Run:

```powershell
dotnet test src/WinRestoreKit.Tests/WinRestoreKit.Tests.csproj --filter "FullyQualifiedName~SnapshotComparisonServiceTests"
```

Expected: PASS; manifest-success rows bypass `HasArtifactIn`, skipped/failed rows are `NotCaptured`, proven absence is `NotCaptured`, a silent manifest plus indeterminate artifact is `NotCaptured`, no-manifest indeterminacy preserves the `RestoreContents` fallback and remains subject to drift, and throwing artifact or drift probes are `Unavailable` without producing a false `Same` row.

- [ ] **Step 7: Commit the evidence model and basic service behavior**

```powershell
git add src/WinRestoreKit.Application/Comparison/ComparisonState.cs src/WinRestoreKit.Application/Comparison/ModuleComparison.cs src/WinRestoreKit.Application/Comparison/SnapshotComparisonService.cs src/WinRestoreKit.Tests/SnapshotComparisonServiceTests.cs
git commit -m "feat: compare selected snapshot modules"
```

### Task 3: Make comparison bounded, ordered, cancellable, and cleanup-safe

**Files:**
- Modify: `src/WinRestoreKit.Application/Comparison/SnapshotComparisonService.cs`
- Modify: `src/WinRestoreKit.Tests/SnapshotComparisonServiceTests.cs`

**Interfaces:**
- Consumes: Task 2 comparison records and Timeline's disposable `SnapshotPayloadPreparation`.
- Produces: `internal ComparisonProgress(int ordinal, ModuleComparison comparison)` and the existing public comparison method, now with bounded worker scheduling and proven cleanup.
- Invariant: `Task.WhenAll` result position and progress `Ordinal` equal the input catalog position. Cancellation waits for all in-flight synchronous `BackupBase` probes to finish before the caller can dispose the prepared payload.

- [ ] **Step 1: Add failing isolation, order, concurrency, cancellation, and extraction-cleanup tests**

```csharp
[Fact]
public async Task CompareAsync_OneThrowingModule_DoesNotAbortLaterCatalogRows()
{
    ProbeModule broken = new("Broken", artifact: true, drifted: null) { ThrowOnDrift = true };
    ProbeModule same = new("Same", artifact: true, drifted: false);

    IReadOnlyList<ModuleComparison> rows = await Compare(new[] { broken, same });

    Assert.Collection(rows,
        row => Assert.Equal(ComparisonState.Unavailable, row.State),
        row => Assert.Equal(ComparisonState.Same, row.State));
}

[Fact]
public async Task CompareAsync_BoundsProbeConcurrencyAndReturnsCatalogOrder()
{
    ConcurrentProbeModule[] modules = Enumerable.Range(0, 9)
        .Select(index => new ConcurrentProbeModule("Module " + index)).ToArray();

    IReadOnlyList<ModuleComparison> rows = await Compare(modules);

    Assert.True(ConcurrentProbeModule.MaximumObserved <= 4);
    Assert.Equal(modules.Select(module => module.Title), rows.Select(row => row.Module.Title));
}

[Fact]
public async Task CompareAsync_CancellationWaitsForWorkersThenDeletesCompressedExtraction()
{
    string backup = CreateCompressedBackupWithArtifact();
    BlockingProbeModule module = new("Blocking");
    using CancellationTokenSource cancellation = new();
    SnapshotComparisonService service = new();

    Task<IReadOnlyList<ModuleComparison>> task = service.CompareAsync(
        Snapshot(backup, manifest: null), new[] { (BackupBase)module }, cancellation.Token);
    await module.Started.Task;
    cancellation.Cancel();
    module.Release.Set();

    await Assert.ThrowsAsync<OperationCanceledException>(() => task);
    Assert.Empty(Directory.EnumerateDirectories(Path.Combine(Path.GetTempPath(), "WinRestoreKit"),
        "payload-*", SearchOption.TopDirectoryOnly));
}
```

The cleanup test must use a real `payload.zip`, wait until the fake's synchronous `HasDriftedFrom` starts, cancel, release it, and only then assert extraction cleanup. It must not assume cancellation can interrupt module code that does not accept a token.

- [ ] **Step 2: Run the focused suite and verify the missing bounded/cancellation behavior fails**

Run:

```powershell
dotnet test src/WinRestoreKit.Tests/WinRestoreKit.Tests.csproj --filter "FullyQualifiedName~SnapshotComparisonServiceTests"
```

Expected: FAIL because workers are not yet bounded/cancellation-aware and the compressed temporary extraction lifetime is not proved.

- [ ] **Step 3: Implement four-worker scheduling, indexed progress, and cancellation settlement**

```csharp
internal readonly struct ComparisonProgress
{
    internal ComparisonProgress(int ordinal, ModuleComparison comparison)
    {
        Ordinal = ordinal;
        Comparison = comparison;
    }

    internal int Ordinal { get; }
    internal ModuleComparison Comparison { get; }
}

private const int MaximumConcurrentProbes = 4;

private async Task<IReadOnlyList<ModuleComparison>> ComparePreparedAsync(
    SnapshotPayloadPreparation preparation, IReadOnlyList<BackupBase> modules,
    CancellationToken cancellationToken, IProgress<ComparisonProgress> progress)
{
    BackupBase[] catalog = (modules ?? Array.Empty<BackupBase>())
        .Where(module => module != null).ToArray();

    if (!string.IsNullOrWhiteSpace(preparation.Error))
        return PayloadFailureRows(catalog, preparation.Snapshot.Manifest, preparation.Error);

    using SemaphoreSlim gate = new(MaximumConcurrentProbes, MaximumConcurrentProbes);
    Task<ModuleComparison>[] workers = catalog.Select((module, ordinal) =>
        CompareAtOrdinalAsync(module, ordinal, preparation.Path, preparation.Snapshot.Manifest,
                              gate, cancellationToken, progress)).ToArray();

    try
    {
        return await Task.WhenAll(workers).ConfigureAwait(false);
    }
    finally
    {
        // Await active Task.Run probes before allowing the caller's finally/using to remove the
        // prepared payload. Suppress their already-reported exceptions while preserving cancellation.
        await Task.WhenAll(workers.Select(ObserveCompletionAsync)).ConfigureAwait(false);
    }
}

private static async Task<ModuleComparison> CompareAtOrdinalAsync(
    BackupBase module, int ordinal, string payloadPath, ManifestData manifest,
    SemaphoreSlim gate, CancellationToken token, IProgress<ComparisonProgress> progress)
{
    bool enteredGate = false;
    try
    {
        await gate.WaitAsync(token).ConfigureAwait(false);
        enteredGate = true;
        token.ThrowIfCancellationRequested();
        ModuleComparison row = await Task.Run(() => CompareOne(module, payloadPath, manifest), token)
            .ConfigureAwait(false);
        progress?.Report(new ComparisonProgress(ordinal, row));
        return row;
    }
    finally
    {
        if (enteredGate)
            gate.Release();
    }
}

private static async Task ObserveCompletionAsync(Task task)
{
    try { await task.ConfigureAwait(false); }
    catch (OperationCanceledException) { }
    catch (Exception) { }
}
```

Do not use `Parallel.ForEach`, an unbounded `Task.Run` fan-out, `Task.WaitAll`, or a continuation that disposes the scope independently. `Task.WhenAll` preserves the task-array order; `ComparisonProgress.Ordinal` lets WPF update the matching already-created row without sorting completion order.

- [ ] **Step 4: Run the focused suite and verify cancellation and cleanup pass**

Run:

```powershell
dotnet test src/WinRestoreKit.Tests/WinRestoreKit.Tests.csproj --filter "FullyQualifiedName~SnapshotComparisonServiceTests"
```

Expected: PASS; no more than four probes run at once, one broken module leaves later evidence intact, cancellation is observed, and the temporary archive extraction is removed only after active workers settle.

- [ ] **Step 5: Commit the operational comparison guarantees**

```powershell
git add src/WinRestoreKit.Application/Comparison/SnapshotComparisonService.cs src/WinRestoreKit.Tests/SnapshotComparisonServiceTests.cs
git commit -m "feat: make snapshot comparison cancellable"
```

### Task 4: Build the WPF Compare workspace, restore set, and safe snapshot replacement

**Files:**
- Create: `src/WinRestoreKit.Wpf/ViewModels/ComparisonFilter.cs`
- Create: `src/WinRestoreKit.Wpf/ViewModels/ModuleImpactViewModel.cs`
- Create: `src/WinRestoreKit.Wpf/ViewModels/ModuleComparisonRowViewModel.cs`
- Create: `src/WinRestoreKit.Wpf/ViewModels/RestoreSetViewModel.cs`
- Create: `src/WinRestoreKit.Wpf/ViewModels/ComparisonWorkspaceViewModel.cs`
- Create: `src/WinRestoreKit.Wpf/Navigation/CompareWorkflowNavigator.cs`
- Create: `src/WinRestoreKit.Wpf/Services/ICompareDialogService.cs`
- Create: `src/WinRestoreKit.Wpf/Services/CompareDialogService.cs`
- Create: `src/WinRestoreKit.Wpf/Views/ComparisonWorkspaceView.xaml`
- Create: `src/WinRestoreKit.Wpf/Views/ComparisonWorkspaceView.xaml.cs`
- Modify: `src/WinRestoreKit.Wpf/ViewModels/ShellViewModel.cs`
- Modify: `src/WinRestoreKit.Wpf/MainWindow.xaml`
- Modify: `src/WinRestoreKit.Wpf/MainWindow.xaml.cs`
- Test: `src/WinRestoreKit.Tests/RestoreSetViewModelTests.cs`
- Test: `src/WinRestoreKit.Tests/ComparisonWorkspaceViewModelTests.cs`

**Interfaces:**
- Consumes: `ITimelineNavigator.OpenCompare(SnapshotPayloadPreparation)`, `BackupModuleCatalog.CreateAll()`, Task 3's internal prepared-payload comparison overload, and Timeline's `SnapshotEvent` restorable/diagnostic facts.
- Produces: WPF `ComparisonWorkspaceViewModel` with `Rows`, `VisibleRows`, `RestoreSet`, `SelectedRow`, `SelectedFilter`, `StartAsync`, `CancelAsync`, and `ContinueToConfirmCommand`; `CompareWorkflowNavigator` implements the exact Timeline navigation seam and owns the Confirm transition.
- Invariant: `RestoreSet` holds `BackupBase` references only in memory. It neither serializes values nor interprets an `Unavailable` row as `Same`.
- Produces for Task 6: `CompareWorkflowNavigator.CurrentWorkspace` and `Task PendingTransition`, an internal settled-transition observation seam; Timeline continues to call only `void OpenCompare(...)`.
- [ ] **Step 1: Write failing restore-set and workspace behavior tests under STA**

```csharp
[Fact]
public void RestoreSet_OnlyAcceptsRowsWithUsableArtifacts()
{
    WpfTestHost.Run(() =>
    {
        RestoreSetViewModel restoreSet = new();
        ModuleComparison unavailableButUsable = Comparison("Terminal", ComparisonState.Unavailable, true);
        ModuleComparison absent = Comparison("Fonts", ComparisonState.NotCaptured, false);

        restoreSet.Add(unavailableButUsable);
        restoreSet.Add(absent);

        Assert.Single(restoreSet.Modules);
        Assert.Same(unavailableButUsable.Module, restoreSet.Modules[0]);
        Assert.False(restoreSet.Contains(absent.Module));
    });
}

[Fact]
public void Workspace_DefaultsToAllAndChangedOnlyDoesNotChangeRestoreSet()
{
    WpfTestHost.Run(() =>
    {
        ComparisonWorkspaceViewModel workspace = LoadedWorkspace(
            Comparison("Changed", ComparisonState.Changed, true),
            Comparison("Same", ComparisonState.Same, true),
            Comparison("Unknown", ComparisonState.Unavailable, true),
            Comparison("Absent", ComparisonState.NotCaptured, false));

        Assert.Equal(ComparisonFilter.All, workspace.SelectedFilter);
        Assert.Equal(4, workspace.VisibleRows.Count);
        workspace.RestoreSet.Add(workspace.Rows[2].Comparison);

        workspace.SelectedFilter = ComparisonFilter.ChangedOnly;

        Assert.Single(workspace.VisibleRows);
        Assert.Equal("Changed", workspace.VisibleRows[0].Title);
        Assert.True(workspace.RestoreSet.Contains(workspace.Rows[2].Comparison.Module));
    });
}

[Fact]
public void Workspace_SelectedRowExposesOnlyDeclaredImpacts()
{
    WpfTestHost.Run(() =>
    {
        ComparisonWorkspaceViewModel workspace = LoadedWorkspace(ComparisonWithDeclaredImpacts());
        workspace.SelectedRow = workspace.Rows[0];

        Assert.True(workspace.IsDetailTrayOpen);
        Assert.Equal("Settings", workspace.SelectedRow.Category);
        Assert.Contains(workspace.SelectedRow.Impact.Targets,
            item => item.Kind == RestoreTargetKind.RegistryKey);
        Assert.Contains(workspace.SelectedRow.Impact.Processes,
            item => item.NeedsConsent && item.DisplayName == "Visual Studio Code");
        Assert.True(workspace.SelectedRow.Impact.RequiresExplorerRestart);
        Assert.Equal("Existing module warning.", workspace.SelectedRow.Impact.WarningMessage);
    });
}

[Fact]
public void Workspace_ContinueToConfirmUsesTheCurrentWholeModuleRestoreSet()
{
    WpfTestHost.Run(() =>
    {
        SnapshotEvent receivedSnapshot = null;
        IReadOnlyList<BackupBase> receivedModules = null;
        ComparisonWorkspaceViewModel workspace = LoadedWorkspace(
            (snapshot, modules) => { receivedSnapshot = snapshot; receivedModules = modules; },
            Comparison("Changed", ComparisonState.Changed, true));
        workspace.RestoreSet.Add(workspace.Rows[0].Comparison);

        workspace.ContinueToConfirmCommand.Execute(null);

        Assert.Same(workspace.Snapshot, receivedSnapshot);
        Assert.Single(receivedModules);
        Assert.Same(workspace.Rows[0].Comparison.Module, receivedModules[0]);
    });
}
```

Use a small `BackupBase` test module whose overrides return actual `RestoreTarget` and `RestoreCloseRequirement` objects. The assertions must inspect those values directly; do not create guessed values such as “reboot required.” Extend `LoadedWorkspace` to accept the optional `Action<SnapshotEvent, IReadOnlyList<BackupBase>>` callback passed to the workspace constructor; its normal test overload passes a no-op action.

- [ ] **Step 2: Run the tests to verify the ViewModels and WPF host are absent**

Run:

```powershell
dotnet test src/WinRestoreKit.Tests/WinRestoreKit.Tests.csproj --filter "FullyQualifiedName~RestoreSetViewModelTests|FullyQualifiedName~ComparisonWorkspaceViewModelTests"
```

Expected: compilation fails because the Compare ViewModels and `WpfTestHost`-consuming test surface do not yet exist.

- [ ] **Step 3: Implement whole-module restore selection and direct impact projection**

```csharp
internal sealed class RestoreSetViewModel : ObservableObject
{
    private readonly ObservableCollection<BackupBase> modules = new();

    public ReadOnlyObservableCollection<BackupBase> Modules { get; }

    internal RestoreSetViewModel()
        => Modules = new ReadOnlyObservableCollection<BackupBase>(modules);

    internal bool Contains(BackupBase module) => modules.Contains(module);
    public bool HasItems => modules.Count != 0;

    internal void Add(ModuleComparison comparison)
    {
        if (comparison == null || !comparison.HasUsableArtifact || modules.Contains(comparison.Module))
            return;
        modules.Add(comparison.Module);
        OnPropertyChanged(nameof(HasItems));
    }

    internal void Remove(BackupBase module)
    {
        if (modules.Remove(module))
            OnPropertyChanged(nameof(HasItems));
    }

    internal void Clear()
    {
        if (modules.Count == 0)
            return;
        modules.Clear();
        OnPropertyChanged(nameof(HasItems));
    }
}

internal sealed class ModuleImpactViewModel
{
    internal ModuleImpactViewModel(BackupBase module)
    {
        Targets = (module.RestoreTargets ?? Array.Empty<RestoreTarget>()).ToArray();
        Processes = (module.ProcessesToCloseBeforeRestore ?? Array.Empty<RestoreCloseRequirement>())
            .Where(requirement => requirement != null).ToArray();
        RequiresExplorerRestart = module.RequiresExplorerRestart;
        WarningMessage = module.WarningMessage ?? string.Empty;
    }

    public IReadOnlyList<RestoreTarget> Targets { get; }
    public IReadOnlyList<RestoreCloseRequirement> Processes { get; }
    public bool RequiresExplorerRestart { get; }
    public string WarningMessage { get; }
}

internal enum ComparisonFilter { All, ChangedOnly }

internal sealed class ModuleComparisonRowViewModel : ObservableObject
{
    private readonly RestoreSetViewModel restoreSet;

    internal ModuleComparisonRowViewModel(BackupModuleRegistration registration,
                                          RestoreSetViewModel restoreSet)
    {
        Registration = registration ?? throw new ArgumentNullException(nameof(registration));
        this.restoreSet = restoreSet ?? throw new ArgumentNullException(nameof(restoreSet));
        Impact = new ModuleImpactViewModel(registration.Module);
        ToggleRestoreSetCommand = new DelegateCommand(_ => ToggleRestoreSet(), _ => CanChangeRestoreSet);
    }

    internal BackupModuleRegistration Registration { get; }
    internal ModuleComparison Comparison { get; private set; }
    public string Title => Registration.Title;
    public string Category => Registration.Category;
    public ModuleImpactViewModel Impact { get; }
    public bool IsChecking => Comparison == null;
    public bool CanChangeRestoreSet => Comparison?.HasUsableArtifact == true;
    public bool IsInRestoreSet => restoreSet.Contains(Registration.Module);
    public string StateLabel => IsChecking ? "Checking" : Comparison.State.ToString();
    public string ArtifactSummary => IsChecking ? "Comparison has not finished." : Comparison.ArtifactSummary;
    public string Reason => Comparison?.Reason ?? string.Empty;
    public string RestoreActionLabel => IsInRestoreSet ? "Remove from restore" : "Add to restore";
    public DelegateCommand ToggleRestoreSetCommand { get; }

    internal void Apply(ModuleComparison comparison)
    {
        Comparison = comparison ?? throw new ArgumentNullException(nameof(comparison));
        OnPropertyChanged(nameof(IsChecking));
        OnPropertyChanged(nameof(StateLabel));
        OnPropertyChanged(nameof(ArtifactSummary));
        OnPropertyChanged(nameof(Reason));
        OnPropertyChanged(nameof(CanChangeRestoreSet));
        ToggleRestoreSetCommand.RaiseCanExecuteChanged();
    }

    private void ToggleRestoreSet()
    {
        if (!CanChangeRestoreSet)
            return;
        if (IsInRestoreSet)
            restoreSet.Remove(Registration.Module);
        else
            restoreSet.Add(Comparison);
        OnPropertyChanged(nameof(IsInRestoreSet));
        OnPropertyChanged(nameof(RestoreActionLabel));
    }
}
```

Keep `RestoreTarget` values typed in the view model and bind their existing `Kind` and `Path`; do not repeat `RestorePlan` wording or parse `WarningMessage` to manufacture impact categories.

- [ ] **Step 4: Implement workspace ownership, ordered streaming, and filtering**

```csharp
internal sealed class ComparisonWorkspaceViewModel : ObservableObject
{
    private readonly IReadOnlyList<BackupModuleRegistration> registrations;
    private readonly SnapshotComparisonService comparisonService;
    private readonly CancellationTokenSource comparisonCancellation = new();
    private Task comparisonTask;
    private ComparisonFilter selectedFilter = ComparisonFilter.All;
    private ModuleComparisonRowViewModel selectedRow;
    private readonly Action<SnapshotEvent, IReadOnlyList<BackupBase>> showConfirm;

    internal ComparisonWorkspaceViewModel(SnapshotEvent snapshot,
        IReadOnlyList<BackupModuleRegistration> registrations,
        SnapshotComparisonService comparisonService,
        Action<SnapshotEvent, IReadOnlyList<BackupBase>> showConfirm)
    {
        Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        this.registrations = registrations ?? throw new ArgumentNullException(nameof(registrations));
        this.comparisonService = comparisonService ?? throw new ArgumentNullException(nameof(comparisonService));
        this.showConfirm = showConfirm ?? throw new ArgumentNullException(nameof(showConfirm));
        Rows = new ObservableCollection<ModuleComparisonRowViewModel>();
        VisibleRows = new ObservableCollection<ModuleComparisonRowViewModel>();
        RestoreSet = new RestoreSetViewModel();
        RestoreSet.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(RestoreSet.HasItems))
            {
                OnPropertyChanged(nameof(CanContinueToConfirm));
                ContinueToConfirmCommand.RaiseCanExecuteChanged();
            }
        };
        ContinueToConfirmCommand = new DelegateCommand(_ => ContinueToConfirm(), _ => CanContinueToConfirm);
    }

    public SnapshotEvent Snapshot { get; }
    public ObservableCollection<ModuleComparisonRowViewModel> Rows { get; }
    public ObservableCollection<ModuleComparisonRowViewModel> VisibleRows { get; }
    public RestoreSetViewModel RestoreSet { get; }
    public bool IsComparing { get; private set; }
    public string ComparisonStatus { get; private set; } = string.Empty;
    public ComparisonFilter SelectedFilter
    {
        get => selectedFilter;
        set { selectedFilter = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsAllFilter)); OnPropertyChanged(nameof(IsChangedOnlyFilter)); RefreshVisibleRows(); }
    }
    public bool IsAllFilter
    {
        get => SelectedFilter == ComparisonFilter.All;
        set { if (value) SelectedFilter = ComparisonFilter.All; }
    }
    public bool IsChangedOnlyFilter
    {
        get => SelectedFilter == ComparisonFilter.ChangedOnly;
        set { if (value) SelectedFilter = ComparisonFilter.ChangedOnly; }
    }
    public ModuleComparisonRowViewModel SelectedRow
    {
        get => selectedRow;
        set { selectedRow = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsDetailTrayOpen)); }
    }
    public bool IsDetailTrayOpen => SelectedRow != null;
    public bool CanContinueToConfirm => RestoreSet.HasItems && !IsComparing;
    public DelegateCommand ContinueToConfirmCommand { get; }

    internal Task StartAsync(SnapshotPayloadPreparation preparation)
        => comparisonTask ??= StartCoreAsync(preparation);

    private async Task StartCoreAsync(SnapshotPayloadPreparation preparation)
    {
        ArgumentNullException.ThrowIfNull(preparation);
        Rows.Clear();
        foreach (BackupModuleRegistration registration in registrations)
            Rows.Add(new ModuleComparisonRowViewModel(registration, RestoreSet));
        RefreshVisibleRows();

        IsComparing = true;
        OnPropertyChanged(nameof(IsComparing));
        OnPropertyChanged(nameof(CanContinueToConfirm));
        ContinueToConfirmCommand.RaiseCanExecuteChanged();
        try
        {
            IProgress<ComparisonProgress> progress = new Progress<ComparisonProgress>(item =>
            {
                Rows[item.Ordinal].Apply(item.Comparison);
                RefreshVisibleRows();
            });
            await comparisonService.CompareAsync(preparation,
                registrations.Select(registration => registration.Module).ToArray(),
                comparisonCancellation.Token, progress);
        }
        catch (OperationCanceledException) when (comparisonCancellation.IsCancellationRequested)
        {
            ComparisonStatus = "Comparison canceled.";
        }
        finally
        {
            IsComparing = false;
            OnPropertyChanged(nameof(IsComparing));
            OnPropertyChanged(nameof(CanContinueToConfirm));
            ContinueToConfirmCommand.RaiseCanExecuteChanged();
            preparation.Dispose();
            RefreshVisibleRows();
        }
    }

    internal async Task CancelAsync()
    {
        comparisonCancellation.Cancel();
        if (comparisonTask != null)
            await comparisonTask;
    }

    private void ContinueToConfirm()
    {
        if (CanContinueToConfirm)
            showConfirm(Snapshot, RestoreSet.Modules.ToArray());
    }

    private void RefreshVisibleRows()
    {
        VisibleRows.Clear();
        foreach (ModuleComparisonRowViewModel row in Rows)
            if (SelectedFilter == ComparisonFilter.All ||
                row.Comparison?.State == ComparisonState.Changed)
                VisibleRows.Add(row);
    }
}
```

Set `SelectedFilter = ComparisonFilter.All` in the constructor. `Rows` is created once in catalog order before any result is reported; each progress update replaces only the matching ordinal's checking placeholder, so a completion race cannot reorder the list. `CancelAsync` must be awaited by the navigator before it discards a workspace; do not call `Dispose` from a continuation while a probe can still read the payload.

- [ ] **Step 5: Implement the Timeline navigator and snapshot-change confirmation**

```csharp
internal interface ICompareDialogService
{
    bool ConfirmDiscardRestoreSet(Window owner, SnapshotEvent current, SnapshotEvent incoming);
    void ShowSnapshotDiagnostic(Window owner, SnapshotEvent snapshot);
}

internal sealed class CompareDialogService : ICompareDialogService
{
    public bool ConfirmDiscardRestoreSet(Window owner, SnapshotEvent current, SnapshotEvent incoming)
        => MessageBox.Show(owner,
            "Changing from \"" + current.DisplayName + "\" to \"" + incoming.DisplayName +
            "\" clears the selected restore modules. Change snapshot?",
            "Change snapshot", MessageBoxButton.YesNo, MessageBoxImage.Warning,
            MessageBoxResult.No) == MessageBoxResult.Yes;

    public void ShowSnapshotDiagnostic(Window owner, SnapshotEvent snapshot)
        => MessageBox.Show(owner, snapshot.DiagnosticReason, "Snapshot diagnostic",
            MessageBoxButton.OK, MessageBoxImage.Error);
}

internal sealed class CompareWorkflowNavigator : ITimelineNavigator
{
    private readonly ShellViewModel shell;
    private readonly Window owner;
    private readonly ICompareDialogService dialogs;
    private ComparisonWorkspaceViewModel currentWorkspace;

    internal CompareWorkflowNavigator(ShellViewModel shell, Window owner, ICompareDialogService dialogs)
    {
        this.shell = shell ?? throw new ArgumentNullException(nameof(shell));
        this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
        this.dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
    }

    internal Task PendingTransition { get; private set; } = Task.CompletedTask;
    internal ComparisonWorkspaceViewModel CurrentWorkspace => currentWorkspace;

    public void OpenCompare(SnapshotPayloadPreparation incoming)
    {
        if (string.Equals(currentWorkspace?.Snapshot.CanonicalPath, incoming.Snapshot.CanonicalPath,
                          StringComparison.OrdinalIgnoreCase))
        {
            incoming.Dispose();
            return;
        }

        if (currentWorkspace?.RestoreSet.HasItems == true &&
            !dialogs.ConfirmDiscardRestoreSet(owner, currentWorkspace.Snapshot, incoming.Snapshot))
        {
            incoming.Dispose();
            return;
        }

        PendingTransition = ReplaceWorkspaceAsync(incoming);
    }

    public void ShowSnapshotDiagnostic(SnapshotEvent snapshot)
        => dialogs.ShowSnapshotDiagnostic(owner, snapshot);

    private async Task ReplaceWorkspaceAsync(SnapshotPayloadPreparation incoming)
    {
        bool ownershipTransferred = false;
        try
        {
            if (currentWorkspace != null)
                await currentWorkspace.CancelAsync();
            currentWorkspace?.RestoreSet.Clear();

            currentWorkspace = new ComparisonWorkspaceViewModel(incoming.Snapshot,
                BackupModuleCatalog.CreateAll(), new SnapshotComparisonService(), ShowConfirm);
            ownershipTransferred = true;
            shell.ShowCompare(currentWorkspace);
            await currentWorkspace.StartAsync(incoming);
        }
        catch (Exception ex)
        {
            if (!ownershipTransferred)
                incoming.Dispose();
            shell.ShowInlineWorkflowError("Comparison could not start: " + ex.Message);
        }
    }

    private void ShowConfirm(SnapshotEvent snapshot, IReadOnlyList<BackupBase> modules)
    {
        ConfirmViewModel confirm = new(snapshot, modules, () => shell.ShowCompare(currentWorkspace));
        shell.ShowConfirm(confirm);
    }
}
```

`ConfirmDiscardRestoreSet` must be a modal WPF dialog with the main window owner, **Cancel** as its default, and text that names both snapshot display names. Accepting clears the old in-memory set only after the user approves; rejecting leaves the old workspace/set untouched and releases only the incoming scope. The fire-and-forget interface boundary is contained here because Timeline's established `ITimelineNavigator` is `void`; `ReplaceWorkspaceAsync` catches every exception and renders it inline rather than allowing an unobserved task failure.

Add `ShellViewModel.CurrentWorkflow`, `ShowTimeline()`, `ShowCompare(ComparisonWorkspaceViewModel)`, and `ShowConfirm(ConfirmViewModel)`, then bind `MainWindow`'s central `ContentControl` through explicit DataTemplates. `MainWindow` constructs `CompareWorkflowNavigator` with itself as `owner` and passes it to Timeline's view-model composition; the ViewModel itself never discovers or stores a WPF `Window`.

- [ ] **Step 6: Add accessible Compare XAML with All as the default visible filter**

```xml
<UserControl x:Class="WinRestoreKit.Wpf.Views.ComparisonWorkspaceView"
             AutomationProperties.Name="Snapshot comparison workspace"
             AutomationProperties.AutomationId="ComparisonWorkspace">
  <Grid>
    <Grid.RowDefinitions>
      <RowDefinition Height="Auto" />
      <RowDefinition Height="Auto" />
      <RowDefinition Height="*" />
    </Grid.RowDefinitions>
    <StackPanel Orientation="Horizontal" Margin="0,0,0,12">
      <RadioButton Content="All modules" GroupName="ComparisonFilter"
                   IsChecked="{Binding IsAllFilter, Mode=TwoWay}"
                   AutomationProperties.Name="Show all compared modules"
                   AutomationProperties.AutomationId="CompareFilterAll" />
      <RadioButton Content="Changed only" GroupName="ComparisonFilter" Margin="16,0,0,0"
                   IsChecked="{Binding IsChangedOnlyFilter, Mode=TwoWay}"
                   AutomationProperties.Name="Show only changed modules"
                   AutomationProperties.AutomationId="CompareFilterChanged" />
      <TextBlock Text="{Binding ComparisonStatus}" Margin="16,0,0,0" />
    </StackPanel>
    <StackPanel Grid.Row="1" Orientation="Horizontal" Margin="0,0,0,12">
      <ListBox ItemsSource="{Binding RestoreSet.Modules}" MinWidth="220"
               AutomationProperties.Name="Modules selected for restore"
               AutomationProperties.AutomationId="RestoreSetList">
        <ListBox.ItemTemplate><DataTemplate>
          <TextBlock Text="{Binding Title}" />
        </DataTemplate></ListBox.ItemTemplate>
      </ListBox>
      <Button Content="Continue to confirm" Command="{Binding ContinueToConfirmCommand}"
              IsEnabled="{Binding CanContinueToConfirm}" Margin="12,0,0,0"
              AutomationProperties.Name="Review selected modules before restore"
              AutomationProperties.AutomationId="CompareContinueToConfirmButton" />
    </StackPanel>
    <Grid Grid.Row="2">
      <Grid.ColumnDefinitions><ColumnDefinition /><ColumnDefinition Width="360" /></Grid.ColumnDefinitions>
      <ListBox ItemsSource="{Binding VisibleRows}" SelectedItem="{Binding SelectedRow}"
               AutomationProperties.Name="Compared modules"
               AutomationProperties.AutomationId="CompareModuleList">
        <ListBox.ItemTemplate>
          <DataTemplate>
            <Grid Margin="8"><Grid.ColumnDefinitions><ColumnDefinition Width="130" /><ColumnDefinition /><ColumnDefinition Width="Auto" /></Grid.ColumnDefinitions>
              <TextBlock Text="{Binding StateLabel}" />
              <StackPanel Grid.Column="1"><TextBlock Text="{Binding Title}" FontWeight="SemiBold" /><TextBlock Text="{Binding ArtifactSummary}" TextWrapping="Wrap" /></StackPanel>
              <Button Grid.Column="2" Content="{Binding RestoreActionLabel}"
                      Command="{Binding ToggleRestoreSetCommand}" IsEnabled="{Binding CanChangeRestoreSet}"
                      AutomationProperties.Name="{Binding RestoreActionAutomationName}" />
            </Grid>
          </DataTemplate>
        </ListBox.ItemTemplate>
      </ListBox>
      <Border Grid.Column="1" Visibility="{Binding IsDetailTrayOpen, Converter={StaticResource BooleanToVisibilityConverter}}"
              AutomationProperties.Name="Selected module details">
        <ScrollViewer><StackPanel DataContext="{Binding SelectedRow}">
          <TextBlock Text="{Binding Title}" FontWeight="SemiBold" />
          <TextBlock Text="{Binding Category}" />
          <TextBlock Text="{Binding StateLabel}" />
          <TextBlock Text="{Binding ArtifactSummary}" TextWrapping="Wrap" />
          <TextBlock Text="{Binding Reason}" TextWrapping="Wrap" />
          <ItemsControl ItemsSource="{Binding Impact.Targets}" />
          <ItemsControl ItemsSource="{Binding Impact.Processes}" />
          <TextBlock Text="Explorer restart may be offered after successful writes."
                     Visibility="{Binding Impact.RequiresExplorerRestart, Converter={StaticResource BooleanToVisibilityConverter}}" />
          <TextBlock Text="{Binding Impact.WarningMessage}" TextWrapping="Wrap" />
          <Button Content="{Binding RestoreActionLabel}" Command="{Binding ToggleRestoreSetCommand}"
                  IsEnabled="{Binding CanChangeRestoreSet}" />
        </StackPanel></ScrollViewer>
      </Border>
    </Grid>
  </Grid>
</UserControl>
```

Provide item templates that render each existing `RestoreTarget.Kind` and `RestoreTarget.Path`, plus each existing `RestoreCloseRequirement.DisplayName` and `NeedsConsent` value. A row in its checking phase is labelled “Checking”; after comparison it has exactly one of the four required evidence states. Do not show a semantic before/after column, reboot line, or parsed registry/payload text.

- [ ] **Step 7: Run the STA ViewModel suite and verify visible selection behavior**

Run:

```powershell
dotnet test src/WinRestoreKit.Tests/WinRestoreKit.Tests.csproj --filter "FullyQualifiedName~RestoreSetViewModelTests|FullyQualifiedName~ComparisonWorkspaceViewModelTests"
```

Expected: PASS; all four rows are visible by default, Changed only reduces only the view, usable-but-unavailable evidence may be selected as a whole module, NotCaptured cannot be selected, and the detail model contains only declared Core impacts.

- [ ] **Step 8: Commit the Compare workspace and safe selection lifecycle**

```powershell
git add src/WinRestoreKit.Wpf/ViewModels src/WinRestoreKit.Wpf/Navigation/CompareWorkflowNavigator.cs src/WinRestoreKit.Wpf/Services/ICompareDialogService.cs src/WinRestoreKit.Wpf/Services/CompareDialogService.cs src/WinRestoreKit.Wpf/Views/ComparisonWorkspaceView.xaml src/WinRestoreKit.Wpf/Views/ComparisonWorkspaceView.xaml.cs src/WinRestoreKit.Wpf/MainWindow.xaml src/WinRestoreKit.Wpf/MainWindow.xaml.cs src/WinRestoreKit.Tests/RestoreSetViewModelTests.cs src/WinRestoreKit.Tests/ComparisonWorkspaceViewModelTests.cs
git commit -m "feat: add WPF snapshot comparison workspace"
```

### Task 5: Add Confirm, owner-bound consent, and the unchanged restore pipeline

**Files:**
- Create: `src/WinRestoreKit.Wpf/ViewModels/ConfirmViewModel.cs`
- Create: `src/WinRestoreKit.Wpf/Services/RestoreRunDialogService.cs`
- Create: `src/WinRestoreKit.Wpf/Views/ConfirmView.xaml`
- Create: `src/WinRestoreKit.Wpf/Views/ConfirmView.xaml.cs`
- Create: `src/WinRestoreKit.Wpf/Views/Dialogs/RestoreConsentDialog.xaml`
- Create: `src/WinRestoreKit.Wpf/Views/Dialogs/RestoreConsentDialog.xaml.cs`
- Modify: `src/WinRestoreKit.Wpf/Navigation/CompareWorkflowNavigator.cs`
- Modify: `src/WinRestoreKit.Wpf/ViewModels/ShellViewModel.cs`
- Modify: `src/WinRestoreKit.Tests/WpfTestHost.cs`
- Test: `src/WinRestoreKit.Tests/ConfirmViewModelTests.cs`
- Test: `src/WinRestoreKit.Tests/RestoreConsentDialogTests.cs`

**Interfaces:**
- Consumes: `RestoreSetViewModel.Modules`; existing `RestoreTargets`, `ProcessesToCloseBeforeRestore`, `WarningMessage`, `RequiresExplorerRestart`, `RestorePlan.FidelityCaveat`; Foundation's `IRunPresentation`, `IRunDialogService`, `WpfRunUi`, `WpfLogSink`, `RunCoordinator`, `RunControl`, and moved `BackupRestoreOrchestrator`.
- Produces: a Confirm screen that presents selected modules grouped by existing process/Explorer impact and starts the original restore flow; a final WPF consent dialog returned through `IRunUi.ShowConsentDialog`.
- Invariant: Confirm must not independently create a `RestorePlan`, pre-create a snapshot folder, close a process, evaluate a snapshot decision, or call a module's `Restore*` member. Only the orchestrator does those operations, in its existing order.

- [ ] **Step 1: Write failing Confirm and dialog tests**

```csharp
[Fact]
public void Confirm_GroupsOnlyExistingProcessAndExplorerImpacts()
{
    WpfTestHost.Run(() =>
    {
        ConfirmViewModel viewModel = new ConfirmViewModel(
            SnapshotEventForTemporaryFolder(),
            new[] { ModuleWithConsentProcess(), ModuleWithExplorerRestartAndWarning() });

        Assert.Equal(RestorePlan.FidelityCaveat, viewModel.FidelityCaveat);
        Assert.Contains(viewModel.ConsentProcesses, item => item.DisplayName == "Visual Studio Code");
        Assert.Contains(viewModel.ExplorerRestartModules, item => item.Title == "Taskbar");
        Assert.Contains(viewModel.ModuleWarnings, item => item.Text == "Existing sign-out warning.");
    });
}

[Fact]
public void RestoreConsentDialog_IsOwnerBoundAndDefaultsToNoConsentedProcesses()
{
    WpfTestHost.Run(() =>
    {
        Window owner = new Window();
        RestoreConsentDialog dialog = RestoreConsentDialog.Create(owner, PlanWithOneConsentEntry());

        Assert.Same(owner, dialog.Owner);
        Assert.Empty(dialog.ConsentedProcessNames);
        Assert.False(dialog.DialogResult == true);
        dialog.Close();
        owner.Close();
    });
}

[Fact]
public async Task Confirm_StartRestore_InvokesExistingOrchestratorWithSelectedSource()
{
    await WpfTestHost.RunAsync(async () =>
    {
        string source = CreateTemporarySnapshotFolder();
        CancelingRunDialogService dialogs = new();
        ConfirmViewModel viewModel = AttachedConfirm(SnapshotFor(source), new[] { new TestModule() }, dialogs);

        await viewModel.StartRestoreAsync();

        Assert.Equal(Path.GetFullPath(source), dialogs.LastRestorePlan.RestoreSourcePath);
        Assert.Single(dialogs.LastRestorePlan.Modules);
        Assert.Contains("canceled", viewModel.Summary.Headline, StringComparison.OrdinalIgnoreCase);
        Assert.False(RunCoordinator.IsRunning);
    });
}
```

Make the Test dialog service record the supplied plan in `LastRestorePlan` and return `null` from `ShowRestoreConsent`. The real orchestrator then reaches its existing safe cancellation branch after composing a genuine `RestorePlan`, without writing or restoring anything. Add this asynchronous STA helper to the Foundation-created test host; it must pump the owning WPF dispatcher until the delegate finishes and rethrow its original exception:

```csharp
internal static Task RunAsync(Func<Task> action)
{
    TaskCompletionSource<object> completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    Thread thread = new(() =>
    {
        Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
        dispatcher.BeginInvoke(new Action(async () =>
        {
            try
            {
                await action();
                completion.TrySetResult(null);
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
            finally
            {
                dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
            }
        }));
        Dispatcher.Run();
    });

    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    return completion.Task;
}
```

- [ ] **Step 2: Run the tests to verify Confirm and WPF consent do not yet exist**

Run:

```powershell
dotnet test src/WinRestoreKit.Tests/WinRestoreKit.Tests.csproj --filter "FullyQualifiedName~ConfirmViewModelTests|FullyQualifiedName~RestoreConsentDialogTests"
```

Expected: compilation fails because the Confirm view model, WPF dialog, and owner-bound dialog service are undefined.

- [ ] **Step 3: Implement impact grouping without inventing machine-state metadata**

```csharp
internal sealed class Notice
{
    internal Notice(string title, string text) { Title = title; Text = text; }
    public string Title { get; }
    public string Text { get; }
}

internal sealed class ConfirmViewModel : ObservableObject, IRunPresentation
{
    private Dispatcher dispatcher;
    private IRunDialogService dialogService;
    private Func<Window> ownerProvider;
    private RunControl activeControl;
    private bool isRestoring;
    private readonly Action backToCompare;
    private readonly ObservableCollection<string> logLines = new();

    internal ConfirmViewModel(SnapshotEvent snapshot, IReadOnlyList<BackupBase> modules,
                              Action backToCompare = null)
    {
        Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        Modules = modules?.Where(module => module != null).ToArray() ?? Array.Empty<BackupBase>();
        this.backToCompare = backToCompare;
        LogLines = new ReadOnlyObservableCollection<string>(logLines);
        FidelityCaveat = RestorePlan.FidelityCaveat;
        ConsentProcesses = Modules.SelectMany(module => module.ProcessesToCloseBeforeRestore ?? Array.Empty<RestoreCloseRequirement>())
            .Where(requirement => requirement != null && requirement.NeedsConsent)
            .GroupBy(requirement => requirement.ProcessName, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First()).ToArray();
        InformationalProcesses = Modules.SelectMany(module => module.ProcessesToCloseBeforeRestore ?? Array.Empty<RestoreCloseRequirement>())
            .Where(requirement => requirement != null && !requirement.NeedsConsent)
            .GroupBy(requirement => requirement.ProcessName, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First()).ToArray();
        ExplorerRestartModules = Modules.Where(module => module.RequiresExplorerRestart).ToArray();
        ModuleWarnings = Modules.Where(module => !string.IsNullOrWhiteSpace(module.WarningMessage))
            .Select(module => new Notice(module.Title, module.WarningMessage)).ToArray();

        StartRestoreCommand = new DelegateCommand(_ => _ = StartFromCommandAsync(), _ => CanStartRestore);
        CancelRestoreCommand = new DelegateCommand(_ => activeControl?.RequestCancellation(), _ => IsRestoring);
        BackToCompareCommand = new DelegateCommand(_ => this.backToCompare?.Invoke(), _ => CanNavigate);
    }

    public SnapshotEvent Snapshot { get; }
    public IReadOnlyList<BackupBase> Modules { get; }
    public IReadOnlyList<RestoreCloseRequirement> ConsentProcesses { get; }
    public IReadOnlyList<RestoreCloseRequirement> InformationalProcesses { get; }
    public IReadOnlyList<BackupBase> ExplorerRestartModules { get; }
    public IReadOnlyList<Notice> ModuleWarnings { get; }
    public string FidelityCaveat { get; }
    public bool IsSourcePartial => Snapshot.Kind == SnapshotEventKind.Partial;
    public bool IsRestoring
    {
        get => isRestoring;
        private set { isRestoring = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanStartRestore)); OnPropertyChanged(nameof(CanNavigate)); StartRestoreCommand.RaiseCanExecuteChanged(); CancelRestoreCommand.RaiseCanExecuteChanged(); BackToCompareCommand.RaiseCanExecuteChanged(); }
    }
    public bool CanStartRestore => Modules.Count != 0 && !IsRestoring && dialogService != null && ownerProvider != null;
    public bool CanNavigate => !IsRestoring;
    public string ProgressText { get; private set; } = string.Empty;
    public RunSummary Summary { get; private set; }
    public DelegateCommand StartRestoreCommand { get; }
    public DelegateCommand CancelRestoreCommand { get; }
    public DelegateCommand BackToCompareCommand { get; }
    public ReadOnlyObservableCollection<string> LogLines { get; }

    internal void AttachRunSurfaces(Dispatcher dispatcher, Func<Window> ownerProvider,
                                    IRunDialogService dialogs)
    {
        this.dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        this.ownerProvider = ownerProvider ?? throw new ArgumentNullException(nameof(ownerProvider));
        dialogService = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        OnPropertyChanged(nameof(CanStartRestore));
        StartRestoreCommand.RaiseCanExecuteChanged();
    }

    private async Task StartFromCommandAsync()
    {
        try { await StartRestoreAsync(); }
        catch (Exception ex) { ProgressText = "Restore could not start: " + ex.Message; OnPropertyChanged(nameof(ProgressText)); }
    }

    private void AppendLog(string text) => logLines.Add(text ?? string.Empty);
    private void ClearLog() => logLines.Clear();

    public void SetProgressText(string text) { ProgressText = text ?? string.Empty; OnPropertyChanged(nameof(ProgressText)); }
    public void SetProgressPercent(int percent) { }
    public void SetProgressDetail(string groupInfo, string elapsed, string remaining, string throughput, long bytesWritten, int errors, int warnings) { }
    public void ShowSummary(RunSummary summary, string caption, IReadOnlyList<ModuleOutcome> outcomes)
    { Summary = summary; OnPropertyChanged(nameof(Summary)); }
    public void SetExplorerRestartVisible(bool visible) { ExplorerRestartVisible = visible; OnPropertyChanged(nameof(ExplorerRestartVisible)); }
    public bool ExplorerRestartVisible { get; private set; }
    
}
```

Do not define a reboot collection, enum, label, or view-model property. Show a warning text only as the module supplied it, including an existing sign-out warning.

- [ ] **Step 4: Implement owner-bound final dialogs and default-no snapshot override**

```csharp
internal sealed class RestoreRunDialogService : IRunDialogService
{
    private readonly Window owner;

    internal RestoreRunDialogService(Window owner)
        => this.owner = owner ?? throw new ArgumentNullException(nameof(owner));

    public IReadOnlyList<string> ShowRestoreConsent(RestorePlan plan)
    {
        RestoreConsentDialog dialog = RestoreConsentDialog.Create(owner, plan);
        return dialog.ShowDialog() == true ? dialog.ConsentedProcessNames : null;
    }

    public bool ConfirmSnapshotOverride(string text, string caption)
        => MessageBox.Show(owner, text, caption, MessageBoxButton.YesNo,
                           MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;

    public void ShowPlanCompositionError(string text, string caption)
        => MessageBox.Show(owner, text, caption, MessageBoxButton.OK, MessageBoxImage.Error);
}
```

```csharp
internal sealed class ConsentChoice : ObservableObject
{
    internal ConsentChoice(RestoreConsentEntry entry) => Entry = entry;
    internal RestoreConsentEntry Entry { get; }
    public string Label => Entry.Label;
    public bool IsSelected { get; set; }
}

internal sealed partial class RestoreConsentDialog : Window
{
    private readonly RestorePlan plan;

    internal static RestoreConsentDialog Create(Window owner, RestorePlan plan)
        => new RestoreConsentDialog(plan) { Owner = owner ?? throw new ArgumentNullException(nameof(owner)) };

    internal RestoreConsentDialog(RestorePlan plan)
    {
        this.plan = plan ?? throw new ArgumentNullException(nameof(plan));
        ConsentChoices = new ObservableCollection<ConsentChoice>(
            this.plan.ConsentEntries.Select(entry => new ConsentChoice(entry)));
        InitializeComponent();
        DataContext = this;
    }

    public string ConfirmationText => plan.ConfirmationText;
    public string FidelityCaveat => RestorePlan.FidelityCaveat;
    public IReadOnlyList<string> InformationalCloseLines => plan.InformationalCloseLines;
    public ObservableCollection<ConsentChoice> ConsentChoices { get; }
    public IReadOnlyList<string> ConsentedProcessNames
        => ConsentChoices.Where(choice => choice.IsSelected)
            .Select(choice => choice.Entry.ProcessName).ToArray();

    private void Restore_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
```

`RestoreConsentDialog.Create` must set `Owner = owner`; all `RestoreConsentEntry` checkboxes start unchecked; its Cancel button has `IsCancel="True"`; and its Restore button sets `DialogResult = true` only after explicit click. `ShowRestoreConsent` returns `null` for Escape, close, or Cancel, preserving the orchestrator's cancellation behavior. The final dialog renders the actual `RestorePlan.ConfirmationText`, `FidelityCaveat`, `ConsentEntries`, and `InformationalCloseLines` generated after the real pre-restore snapshot destination is chosen.

- [ ] **Step 5: Implement WPF launch using the existing orchestration sequence**

```csharp
internal async Task StartRestoreAsync()
{
    if (Modules.Count == 0 || IsRestoring || dialogService == null || ownerProvider == null || !RunCoordinator.TryStart())
        return;

    activeControl = new RunControl();
    bool logSinkInstalled = false;
    try
    {
        WpfLogSink logSink = new(dispatcher, AppendLog, ClearLog);
        WpfRunUi runUi = new(dispatcher, this, dialogService, ownerProvider);
        LogHelper.Instance.SetSink(logSink);
        logSinkInstalled = true;
        IsRestoring = true;

        await new BackupRestoreOrchestrator(runUi, activeControl)
            .RunRestore(Modules, Snapshot.CanonicalPath);
    }
    catch (Exception ex)
    {
        ShowSummary(RunSummary.For(Array.Empty<ModuleOutcome>(), false, RunVerb.Restore, ex.Message),
                    "Restore", Array.Empty<ModuleOutcome>());
    }
    finally
    {
        if (logSinkInstalled)
            LogHelper.Instance.SetSink(null);
        activeControl?.Dispose();
        activeControl = null;
        IsRestoring = false;
        RunCoordinator.SetRunning(false);
    }
}
```

Wire the visible Cancel action to `activeControl.RequestCancellation()` while `IsRestoring` is true. The method must pass the original selected snapshot directory, not an extracted comparison path, because `RunRestore` owns its own `BackupPayload.TryPrepareForRead` scope. Never call `SnapshotGate`, `RestoreScope.For`, `RestoreDispatch.Decide`, or `ExplorerRestartPrompt.IsNeeded` from WPF: the orchestrator invokes them in the retained safe order and reaches the owner-bound `ConfirmSnapshotOverride` exactly when `SnapshotDecision.RequiresOverride` is true.

- [ ] **Step 6: Add accessible Confirm and consent XAML**

```xml
<!-- ConfirmView.xaml -->
<UserControl x:Class="WinRestoreKit.Wpf.Views.ConfirmView"
             AutomationProperties.Name="Confirm restore">
  <Grid>
    <Grid.RowDefinitions><RowDefinition Height="*"/><RowDefinition Height="Auto"/></Grid.RowDefinitions>
    <ScrollViewer><StackPanel>
      <TextBlock Text="Review restore impact" FontSize="24" FontWeight="SemiBold" />
      <ItemsControl ItemsSource="{Binding Modules}" AutomationProperties.Name="Modules selected for restore" />
      <ItemsControl ItemsSource="{Binding ConsentProcesses}" AutomationProperties.Name="Applications requiring consent to close" />
      <ItemsControl ItemsSource="{Binding InformationalProcesses}" AutomationProperties.Name="Applications that will restart themselves" />
      <ItemsControl ItemsSource="{Binding ExplorerRestartModules}" AutomationProperties.Name="Modules that may offer Explorer restart" />
      <ItemsControl ItemsSource="{Binding ModuleWarnings}" AutomationProperties.Name="Existing module warnings" />
      <TextBlock Text="{Binding FidelityCaveat}" TextWrapping="Wrap" />
      <TextBlock Text="{Binding ProgressText}" TextWrapping="Wrap" />
      <TextBlock Text="{Binding Summary.Headline}" FontWeight="SemiBold" />
      <TextBlock Text="{Binding Summary.Detail}" TextWrapping="Wrap" />
    </StackPanel></ScrollViewer>
    <StackPanel Grid.Row="1" Orientation="Horizontal" HorizontalAlignment="Right">
      <Button Content="Back to compare" Command="{Binding BackToCompareCommand}" IsEnabled="{Binding CanNavigate}" />
      <Button Content="Cancel restore" Command="{Binding CancelRestoreCommand}" IsEnabled="{Binding IsRestoring}" Margin="8,0,0,0" />
      <Button Content="Continue to restore" Command="{Binding StartRestoreCommand}"
              IsEnabled="{Binding CanStartRestore}" Margin="8,0,0,0"
              AutomationProperties.Name="Continue to final restore consent"
              AutomationProperties.AutomationId="ConfirmRestoreButton" />
    </StackPanel>
  </Grid>
</UserControl>

<!-- RestoreConsentDialog.xaml -->
<Window x:Class="WinRestoreKit.Wpf.Views.Dialogs.RestoreConsentDialog"
        Title="Restore" WindowStartupLocation="CenterOwner"
        AutomationProperties.Name="Restore consent">
  <Grid Margin="24">
    <Grid.RowDefinitions><RowDefinition Height="*"/><RowDefinition Height="Auto"/></Grid.RowDefinitions>
    <ScrollViewer><StackPanel>
      <TextBlock Text="{Binding ConfirmationText}" TextWrapping="Wrap" />
      <TextBlock Text="{Binding FidelityCaveat}" TextWrapping="Wrap" Margin="0,12,0,0" />
      <ItemsControl ItemsSource="{Binding ConsentChoices}"
                    AutomationProperties.Name="Applications you agree to close">
        <ItemsControl.ItemTemplate><DataTemplate>
          <CheckBox Content="{Binding Label}" IsChecked="{Binding IsSelected, Mode=TwoWay}" />
        </DataTemplate></ItemsControl.ItemTemplate>
      </ItemsControl>
      <ItemsControl ItemsSource="{Binding InformationalCloseLines}"
                    AutomationProperties.Name="Applications that may close without consent">
        <ItemsControl.ItemTemplate><DataTemplate>
          <TextBlock Text="{Binding}" TextWrapping="Wrap" />
        </DataTemplate></ItemsControl.ItemTemplate>
      </ItemsControl>
    </StackPanel></ScrollViewer>
    <StackPanel Grid.Row="1" Orientation="Horizontal" HorizontalAlignment="Right">
      <Button Content="Cancel" IsCancel="True" />
      <Button Content="Restore" IsDefault="False" Click="Restore_Click" Margin="8,0,0,0"
              AutomationProperties.Name="Confirm restore" />
    </StackPanel>
  </Grid>
</Window>
```

```csharp
// ConfirmView.xaml.cs; invoked from Loaded after the view has a real main-window owner.
private void ConfirmView_Loaded(object sender, RoutedEventArgs e)
{
    if (DataContext is not ConfirmViewModel viewModel)
        return;

    Window owner = Window.GetWindow(this)
        ?? throw new InvalidOperationException("Confirm must be hosted in a Window.");
    viewModel.AttachRunSurfaces(Dispatcher, () => Window.GetWindow(this),
        new RestoreRunDialogService(owner));
}
```

This code-behind performs only WPF owner composition. `RestoreRunDialogService` retains the owner for WPF modal dialogs, while Foundation's `WpfRunUi` invokes the supplied live `Func<Window>` and exposes its opaque result through `IRunUi.DialogOwner` to the retained Core `AppStoreApps.RestoreAsync(path, ui.DialogOwner)` / `RestoreDialog` callback seam; it contains no restore policy or data access.

The Confirm view's primary button is intentionally not the final modal consent. Clicking it lets the orchestrator compose the exact plan and opens `RestoreConsentDialog`, where the user explicitly clicks Restore. This keeps the existing partial pre-restore-snapshot consent after `SnapshotGate` at its mandatory point immediately before writes.

- [ ] **Step 7: Run the focused Confirm/dialog tests and verify the safe orchestrator path**

Run:

```powershell
dotnet test src/WinRestoreKit.Tests/WinRestoreKit.Tests.csproj --filter "FullyQualifiedName~ConfirmViewModelTests|FullyQualifiedName~RestoreConsentDialogTests|FullyQualifiedName~RestoreConsentCancellationTests"
```

Expected: PASS; the final dialog is owned by the active WPF window with no preselected consent, only declared Core impacts are shown, and a final-dialog cancellation produces the existing no-changes restore summary through `BackupRestoreOrchestrator`.

- [ ] **Step 8: Commit Confirm and owner-bound restore consent**

```powershell
git add src/WinRestoreKit.Wpf/ViewModels/ConfirmViewModel.cs src/WinRestoreKit.Wpf/Services/RestoreRunDialogService.cs src/WinRestoreKit.Wpf/Views/ConfirmView.xaml src/WinRestoreKit.Wpf/Views/ConfirmView.xaml.cs src/WinRestoreKit.Wpf/Views/Dialogs/RestoreConsentDialog.xaml src/WinRestoreKit.Wpf/Views/Dialogs/RestoreConsentDialog.xaml.cs src/WinRestoreKit.Wpf/Navigation/CompareWorkflowNavigator.cs src/WinRestoreKit.Wpf/ViewModels/ShellViewModel.cs src/WinRestoreKit.Tests/WpfTestHost.cs src/WinRestoreKit.Tests/ConfirmViewModelTests.cs src/WinRestoreKit.Tests/RestoreConsentDialogTests.cs
git commit -m "feat: confirm WPF restores safely"
```

### Task 6: Verify the complete Compare → Confirm path without changing backup or cutover scope

**Files:**
- Modify: `src/WinRestoreKit.Tests/SnapshotComparisonServiceTests.cs`
- Modify: `src/WinRestoreKit.Tests/ComparisonWorkspaceViewModelTests.cs`
- Modify: `src/WinRestoreKit.Tests/ConfirmViewModelTests.cs`
- Modify: `src/WinRestoreKit.Tests/RestoreConsentDialogTests.cs`

**Interfaces:**
- Consumes: all contracts from Tasks 1–5.
- Produces: a tested Stage-3 workflow boundary. No project identity, publish configuration, backup creation flow, WinForms deletion, or semantic-provider surface is produced by this task.

- [ ] **Step 1: Add failing cross-workflow regression tests for snapshot replacement and partial source labeling**

```csharp
[Fact]
public async Task Navigator_ChangingSnapshotWithRestoreSet_CancelKeepsOriginalSetAndDisposesIncomingScope()
{
    await WpfTestHost.RunAsync(async () =>
    {
        TestDiscardDialog dialogs = new(answer: false);
        CompareWorkflowNavigator navigator = Navigator(dialogs);
        navigator.OpenCompare(Prepared("first"));
        await navigator.PendingTransition;
        navigator.CurrentWorkspace.RestoreSet.Add(navigator.CurrentWorkspace.Rows[0].Comparison);

        string incomingOwnedPath = Path.Combine(Path.GetTempPath(), "WinRestoreKit.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(incomingOwnedPath);
        SnapshotPayloadPreparation incoming = new SnapshotPayloadPreparation(
            SnapshotFor(incomingOwnedPath, "second"),
            new BackupPayload.ReadScope(incomingOwnedPath, incomingOwnedPath), error: null);

        navigator.OpenCompare(incoming);
        await navigator.PendingTransition;

        Assert.Equal("first", navigator.CurrentWorkspace.Snapshot.DisplayName);
        Assert.True(navigator.CurrentWorkspace.RestoreSet.HasItems);
        Assert.False(Directory.Exists(incomingOwnedPath));
    });
}

[Fact]
public void Confirm_PartialSourceStaysExplicitWithoutBypassingSnapshotGate()
{
    WpfTestHost.Run(() =>
    {
        ConfirmViewModel viewModel = new ConfirmViewModel(PartialSnapshot(), new[] { ModuleWithArtifact() });

        Assert.Equal(SnapshotEventKind.Partial, viewModel.Snapshot.Kind);
        Assert.True(viewModel.IsSourcePartial);
    });
}
```

The second test pins the separation between a selected partial source and the later, orchestrator-owned pre-restore `SnapshotGate` result: WPF can label the source honestly but cannot pre-authorize an override.

- [ ] **Step 2: Run the Stage-3 focused test set and verify the new integration tests fail**

Run:

```powershell
dotnet test src/WinRestoreKit.Tests/WinRestoreKit.Tests.csproj --filter "FullyQualifiedName~SnapshotComparisonServiceTests|FullyQualifiedName~RestoreSetViewModelTests|FullyQualifiedName~ComparisonWorkspaceViewModelTests|FullyQualifiedName~ConfirmViewModelTests|FullyQualifiedName~RestoreConsentDialogTests"
```

Expected: FAIL until snapshot replacement retains/cancels state exactly as specified and Confirm exposes the partial-source label while leaving the snapshot override decision to the orchestrator.

- [ ] **Step 3: Implement the remaining workflow facts and preserve existing tests**

```csharp
internal bool IsSourcePartial => Snapshot.Kind == SnapshotEventKind.Partial;
```

Complete `CompareWorkflowNavigator` so it disposes every rejected incoming preparation, waits for `CancelAsync()` before replacing an active comparison, and clears the old restore set only after accepted replacement. Keep the existing WinForms-only regression tests unchanged while the side-by-side shell remains runnable; later cutover removes them only after explicit WPF/Application replacements. Do not delete them in this plan.

- [ ] **Step 4: Run all Stage-3 focused tests and verify success**

Run:

```powershell
dotnet test src/WinRestoreKit.Tests/WinRestoreKit.Tests.csproj --filter "FullyQualifiedName~SnapshotComparisonServiceTests|FullyQualifiedName~RestoreSetViewModelTests|FullyQualifiedName~ComparisonWorkspaceViewModelTests|FullyQualifiedName~ConfirmViewModelTests|FullyQualifiedName~RestoreConsentDialogTests|FullyQualifiedName~RestoreContentsTests|FullyQualifiedName~RestorePlanTests|FullyQualifiedName~RestoreScopeTests|FullyQualifiedName~RestoreDispatchTests|FullyQualifiedName~SnapshotGateConsentTests|FullyQualifiedName~ExplorerRestartPromptTests"
```

Expected: PASS; comparison behavior is deterministic and isolated, WPF selection/confirmation is safe, and the retained Core restore gates still pass their original contracts.

- [ ] **Step 5: Build and run the WPF Compare/Confirm smoke path**

Run:

```powershell
dotnet build src/WinRestoreKit.sln -c Debug
dotnet run --project src/WinRestoreKit.Wpf/WinRestoreKit.Wpf.csproj -c Debug
```

Expected: build succeeds with zero errors; the WPF app opens while the WinForms project remains runnable.

In the launched WPF application, exercise this exact read-only path with a local verified or partial Timeline snapshot:

1. Select the snapshot with keyboard Enter; confirm its Compare view names the selected snapshot and starts with **All modules** selected.
2. Wait for results; verify Changed, Same, Unavailable, and Not captured labels remain textually distinct, and a known unavailable row remains visible under All.
3. Switch to Changed only, then back to All; verify row evidence and any already-added usable module remain unchanged.
4. Open a row's detail tray; verify it displays only target/process declarations, existing warning text, and an Explorer note only for modules declaring `RequiresExplorerRestart`; verify no reboot claim is displayed.
5. Add a Changed row and, if available, an Unavailable row with a proven artifact; verify a Not captured row cannot be added. Select another Timeline snapshot, first choose Cancel and confirm the original restore set remains, then choose the explicit discard action and confirm the set clears.
6. Continue to Confirm; verify selected modules and process/Explorer groups, then press Continue to restore. Verify the modal consent dialog is centered on and blocked by the main WPF window, has no prechecked consent boxes, and Cancel returns the existing no-changes result without a restore write.
7. For a controlled test where the pre-restore snapshot is incomplete, verify the owner-bound `Pre-restore snapshot` Yes/No prompt appears only after the original pipeline reaches `SnapshotGate`, defaults to No, and No prevents restore writes.

- [ ] **Step 6: Commit the verified Stage-3 boundary**

```powershell
git add src/WinRestoreKit.Tests/SnapshotComparisonServiceTests.cs src/WinRestoreKit.Tests/ComparisonWorkspaceViewModelTests.cs src/WinRestoreKit.Tests/ConfirmViewModelTests.cs src/WinRestoreKit.Tests/RestoreConsentDialogTests.cs
git commit -m "test: cover compare confirm restore workflow"
```

## Plan Self-Review

- **Spec coverage:** Tasks 2–3 cover arbitrary selected snapshot comparison, manifest precedence, the four states, per-module isolation, bounded cancellation, and read-scope cleanup. Task 4 covers All by default, Changed only, ordered rows, detail tray, whole-module selection, and snapshot-change clearing. Task 5 preserves restore impacts, creates owner-bound consent, and launches only the existing orchestration/gates. Task 6 exercises the combined selection/confirmation and WPF smoke path. Backup creation, progress/results migration, app-restoration UX expansion, project identity, publishing, and cutover are intentionally delegated to their respective later plans.
- **No fabricated evidence:** No XAML or ViewModel parses registry exports/payload contents, constructs before/after values, adds semantic providers, or creates reboot metadata. All visible impacts come from existing typed Core declarations or verbatim module warnings.
- **Type consistency:** Every later WPF component consumes `BackupModuleRegistration`, `SnapshotEvent`, `SnapshotPayloadPreparation`, `ModuleComparison`, `RestoreSetViewModel`, and the Foundation `WpfRunUi` interfaces defined in the interface map. The required public comparison signature remains exactly `CompareAsync(SnapshotEvent, IReadOnlyList<BackupBase>, CancellationToken)`.
- **Placeholder scan:** This plan contains no TODO/TBD/future implementation steps. Deferred scope is named only where the approved migration sequence assigns it to a different complete plan.

