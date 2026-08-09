# Timeline Event Model Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver one framework-neutral snapshot-event source and an accessible WPF Timeline plus advanced History that can select verified or partial snapshots safely, while making failures and unreadable entries diagnostic-only.

**Architecture:** Move filesystem discovery out of the WinForms view layer into `WinRestoreKit.Application`, preserving the existing root-recognition and manifest/payload semantics. `SnapshotEventCatalog` classifies and orders immutable events once; both WPF representations project the same event object through the same status projection. A selected event is prepared by a disposable Application-layer payload service and ownership transfers to the future Compare stage without exposing payload or manifest text to XAML.

**Tech Stack:** .NET 8 (`net8.0-windows` framework-neutral Application layer and `net8.0-windows` WPF/test projects), WPF/MVVM, xUnit 2.9.3, Core `BackupManifest`, `BackupPayload`, `BackupLog`, `BackupRootRegistry`, and `DataHelper.Data`.

## Global Constraints

- Execute this plan after the Foundation plan has created `src/WinRestoreKit.Application/WinRestoreKit.Application.csproj`, moved `Settings/BackupRootRegistry.cs`, and granted Application access to Core internals.
- `WinRestoreKit.Application` MUST reference neither WinForms nor WPF; its namespace remains `WinRestoreKit`.
- Preserve the existing Core snapshot format, backup/restore behavior, custom-root recognition, and `BackupPayload.TryPrepareForRead` cleanup semantics.
- The shared public event contract is exactly `SnapshotEventKind { Verified, Partial, Failed, Unreadable }`, immutable `SnapshotEvent`, and `SnapshotEventCatalog.Read(...)` (implemented here as the public instance method `Read()`).
- The sort is deterministic: descending event timestamp, then `StringComparer.Ordinal` on the canonical full path. Never rely on directory enumeration order or localized string comparison.
- `Verified` and `Partial` are selectable; `Failed` and `Unreadable` are diagnostic-only and must never enter Compare or Restore.
- Durable failures are limited to retained attempts: a folder which remains on disk and has retained folder/manifest/log evidence survives restart and is rediscovered. Folder-creation failures and cancelled runs whose owned folder was deleted by cleanup are session-only events; they MUST NOT be persisted and MUST NOT affect retention or cleanup safety.
- A failed custom root or manifest/payload read must show the real exception message or the truthful validation failure; no error may be rendered as “No backups yet”, an empty snapshot, or a guessed success.
- Compressed selection uses a private `BackupPayload.ReadScope`; every unsuccessful path disposes it immediately, and the receiver that accepts a successful preparation owns disposal.
- WPF uses MVVM. Views never parse registry exports, manifests, logs, or payload files. This plan intentionally does not create comparison rows or restore confirmation.
- Keep the existing WinForms shell runnable during migration. Do not add a compatibility overload or a second status-classification path.
- Keep tests in `src/WinRestoreKit.Tests`; update their project references for Application/WPF and add an STA helper for WPF construction.

## Produced Interfaces

These are the only Timeline interfaces later plans may consume.

```csharp
namespace WinRestoreKit;

public enum SnapshotEventKind { Verified, Partial, Failed, Unreadable }

public interface ISnapshotEventReader
{
    IReadOnlyList<SnapshotEvent> Read();
}

public sealed class SnapshotEvent
{
    internal SnapshotEvent(
        SnapshotEventKind kind, DateTime created, string displayName,
        string canonicalPath, string diagnosticReason, string machineName,
        long sizeBytes, bool isSizeComplete, ManifestData manifest);

    public SnapshotEventKind Kind { get; }
    public DateTime Created { get; }
    public string DisplayName { get; }
    public string CanonicalPath { get; }
    public string DiagnosticReason { get; }
    public string MachineName { get; }
    public long SizeBytes { get; }
    public bool IsSizeComplete { get; }
    public bool IsRestorable { get; } // true only for Verified and Partial

    // Internal so WPF never sees Core persistence text. Application comparison code may read it.
    internal ManifestData Manifest { get; }
}

public sealed class SnapshotEventCatalog : ISnapshotEventReader
{
    public SnapshotEventCatalog();
    public IReadOnlyList<SnapshotEvent> Read();
    public void RecordSessionFailure(DateTime created, string displayName, string diagnosticReason);
}

public interface ISnapshotPayloadPreparationService
{
    Task<SnapshotPayloadPreparation> PrepareAsync(
        SnapshotEvent snapshot, CancellationToken cancellationToken);
}

public sealed class SnapshotPayloadPreparation : IDisposable
{
    internal SnapshotPayloadPreparation(SnapshotEvent snapshot, BackupPayload.ReadScope scope, string error);
    public SnapshotEvent Snapshot { get; }
    public string Path { get; }
    public string Error { get; }
    public bool IsPrepared { get; }
    public void Dispose();
}

public sealed class SnapshotPayloadPreparationService : ISnapshotPayloadPreparationService
{
    public Task<SnapshotPayloadPreparation> PrepareAsync(
        SnapshotEvent snapshot, CancellationToken cancellationToken);
}
```

```csharp
// src/WinRestoreKit.Wpf/Navigation/ITimelineNavigator.cs
namespace WinRestoreKit.Wpf.Navigation;

internal interface ITimelineNavigator
{
    void OpenCompare(SnapshotPayloadPreparation preparation); // ownership transfers to receiver
    void ShowSnapshotDiagnostic(SnapshotEvent snapshot);
}
```

```csharp
// Bound WPF types and every property XAML binds MUST be public: WPF binding reflects public
// properties and silently drops inaccessible bindings.
public sealed class SnapshotEventStatus
{
    public string Label { get; }
    public string Glyph { get; }
    public bool IsDiagnosticOnly { get; }
}

public sealed class SnapshotEventViewModel
{
    public SnapshotEvent Event { get; }
    public SnapshotEventStatus Status { get; }
    public string DisplayName { get; }
    public string CreatedDisplay { get; }
    public string DiagnosticReason { get; }
    public bool HasDiagnostic { get; }
    public string AutomationName { get; }
}

public sealed class TimelineViewModel : INotifyPropertyChanged
{
    internal TimelineViewModel(
        ISnapshotEventReader catalog,
        ISnapshotPayloadPreparationService preparationService,
        ITimelineNavigator navigator);

    public ReadOnlyObservableCollection<SnapshotEventViewModel> Events { get; }
    public SnapshotEventViewModel SelectedEvent { get; set; }
    public string SelectionError { get; }
    public bool HasSelectionError { get; }
    public event PropertyChangedEventHandler PropertyChanged;
    internal Task RefreshAsync(CancellationToken cancellationToken = default);
    internal Task OpenSelectedAsync(CancellationToken cancellationToken = default);
}

public sealed class AdvancedHistoryViewModel : INotifyPropertyChanged
{
    internal AdvancedHistoryViewModel(ISnapshotEventReader catalog);

    public ICollectionView Events { get; }
    public string SearchText { get; set; }
    public SnapshotEventViewModel SelectedEvent { get; set; }
    public event PropertyChangedEventHandler PropertyChanged;
    internal Task RefreshAsync(CancellationToken cancellationToken = default);
}
```

---

### Task 1: Move lossless backup-folder discovery into Application

**Files:**
- Create: `src/WinRestoreKit.Application/Snapshots/BackupFolders.cs`
- Delete: `src/WinRestoreKit/Views/BackupFolders.cs`
- Modify: `src/WinRestoreKit/WinRestoreKit.csproj`
- Modify: `src/WinRestoreKit.Tests/WinRestoreKit.Tests.csproj`
- Modify: `src/WinRestoreKit.Tests/BackupFoldersReadTests.cs`
- Modify: `src/WinRestoreKit/MainForm.cs`
- Modify: `src/WinRestoreKit/Views/HomePageView.cs`
- Modify: `src/WinRestoreKit/Views/HistoryPageView.cs`
- Modify: `src/WinRestoreKit/Views/ProgressPageView.cs`
- Modify: `src/WinRestoreKit/Views/RestoreWizardStep1View.cs`
- Modify: `src/WinRestoreKit/Views/RestoreWizardStep2View.cs`
- Test: `src/WinRestoreKit.Tests/BackupFoldersReadTests.cs`

**Interfaces:**
- Consumes: Foundation’s framework-neutral Application project, `Settings/BackupRootRegistry.cs`, Core `Data.DataRootDir`, `BackupManifest`, `BackupPayload`, and `BackupLog`.
- Produces: internal `WinRestoreKit.BackupFolders` and `BackupFolder` with the existing `Read()`, `Backups`, `Snapshots`, `UnreadableReason`, `Path`, `Name`, `DisplayName`, `Created`, and `ReadManifest()` members, plus internal `ManifestError` that records an invalid/unreadable manifest reason. Existing WinForms callers consume these through an Application friend assembly, not a copied View implementation.

- [ ] **Step 1: Write failing discovery-preservation tests**

Move the existing `BackupFoldersReadTests` import from `Views` to `WinRestoreKit`, retaining its custom-root coverage. Add a malformed-manifest test and an unreadable-root assertion that tests the actual message rather than a synthetic empty-state label:

```csharp
[Fact]
public void Read_MalformedManifestKeepsValidationReason()
{
    RunWithRoots((defaultRoot, customRoot) =>
    {
        string folder = Directory.CreateDirectory(Path.Combine(defaultRoot, "bad-manifest")).FullName;
        File.WriteAllText(Path.Combine(folder, BackupManifest.FileName), "{ not json");

        BackupFolder found = Assert.Single(BackupFolders.Read().Backups);

        Assert.Null(found.ReadManifest());
        Assert.Equal("The backup manifest is invalid or uses an unsupported schema.", found.ManifestError);
    });
}
```

- [ ] **Step 2: Run the focused test and verify it fails**

Run: `dotnet test src/WinRestoreKit.Tests/WinRestoreKit.Tests.csproj --filter FullyQualifiedName~BackupFoldersReadTests`

Expected: FAIL to compile because `WinRestoreKit.BackupFolders` and `BackupFolder.ManifestError` do not yet exist in Application.

- [ ] **Step 3: Move the filesystem-only types and preserve root behavior**

Move the old implementation without UI references to `src/WinRestoreKit.Application/Snapshots/BackupFolders.cs`, change its namespace to `WinRestoreKit`, and retain the existing default-root/custom-root rules verbatim: direct `Directory.GetDirectories`, recognizability only for custom children, nested-custom-root exclusion, snapshot-name handling, manifest timestamp preference, and legacy timestamp-name recognition. Do not retain a forwarding class in `Views`.

Add explicit manifest evidence while retaining `ReadManifest()` compatibility for legacy WinForms consumers:

```csharp
internal sealed class BackupFolder
{
    private readonly ManifestData manifest;

    internal BackupFolder(string path)
    {
        Path = path;
        Name = System.IO.Path.GetFileName(path);
        manifest = ReadManifest(path, out string manifestError);
        ManifestError = manifestError;
        Created = ReadCreated(path, manifest);
    }

    internal string ManifestError { get; }
    internal ManifestData ReadManifest() => manifest;

    private static ManifestData ReadManifest(string path, out string error)
    {
        error = null;
        string file = System.IO.Path.Combine(path, BackupManifest.FileName);
        try
        {
            if (!File.Exists(file))
                return null;

            ManifestData parsed = BackupManifest.TryParse(File.ReadAllText(file));
            if (parsed == null)
                error = "The backup manifest is invalid or uses an unsupported schema.";
            return parsed;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return null;
        }
    }
}
```

Reference Application from the still-runnable WinForms project and tests; remove the compiled View source rather than leaving a duplicate filesystem reader:

```xml
<ItemGroup>
  <ProjectReference Include="..\WinRestoreKit.Application\WinRestoreKit.Application.csproj" />
  <ProjectReference Include="..\WinRestoreKit.Core\WinRestoreKit.Core.csproj" />
</ItemGroup>
```

Foundation owns the necessary Application friend declaration; before moving the types, confirm `src/WinRestoreKit.Application/Properties/AssemblyInfo.cs` contains `[assembly: InternalsVisibleTo("WinRestoreKit")]`. Do not add a second declaration in this plan.

Add `using WinRestoreKit;` to each of `MainForm.cs`, `HomePageView.cs`, `HistoryPageView.cs`, `ProgressPageView.cs`, `RestoreWizardStep1View.cs`, and `RestoreWizardStep2View.cs` when it is not already present. Preserve each existing call to `BackupFolders.Read()`, `BackupFolder`, `ReadManifest()`, and `IsSnapshot`; only its assembly owner changes. This is required to keep every WinForms backup/restore picker, progress surface, and existing Home/History view compiling and runnable while WPF is introduced.

- [ ] **Step 4: Run the focused discovery tests and compile the side-by-side shell**

Run: `dotnet test src/WinRestoreKit.Tests/WinRestoreKit.Tests.csproj --filter FullyQualifiedName~BackupFoldersReadTests`

Expected: PASS; existing default/custom/nested root cases still pass, and malformed JSON produces the validation reason while an inaccessible root retains its OS-supplied error message.

Run: `dotnet build src/WinRestoreKit.sln`

Expected: Build succeeds. The WinForms app still resolves `BackupFolders` and `BackupFolder` from its Application friend assembly; no `Views.BackupFolders` copy remains.

- [ ] **Step 5: Commit the discovery extraction**

```bash
git add src/WinRestoreKit.Application/Snapshots/BackupFolders.cs src/WinRestoreKit/WinRestoreKit.csproj src/WinRestoreKit.Tests/WinRestoreKit.Tests.csproj src/WinRestoreKit/MainForm.cs src/WinRestoreKit/Views/HomePageView.cs src/WinRestoreKit/Views/HistoryPageView.cs src/WinRestoreKit/Views/ProgressPageView.cs src/WinRestoreKit/Views/RestoreWizardStep1View.cs src/WinRestoreKit/Views/RestoreWizardStep2View.cs src/WinRestoreKit.Tests/BackupFoldersReadTests.cs
git rm src/WinRestoreKit/Views/BackupFolders.cs
git commit -m "refactor: move backup folder discovery to application"
```

### Task 2: Add the immutable event DTO, status classifier, and deterministic catalog

**Files:**
- Create: `src/WinRestoreKit.Application/Snapshots/SnapshotEventKind.cs`
- Create: `src/WinRestoreKit.Application/Snapshots/SnapshotEvent.cs`
- Create: `src/WinRestoreKit.Application/Snapshots/SnapshotEventCatalog.cs`
- Create: `src/WinRestoreKit.Tests/SnapshotEventCatalogTests.cs`
- Test: `src/WinRestoreKit.Tests/SnapshotEventCatalogTests.cs`

**Interfaces:**
- Consumes: Task 1’s lossless `BackupFolders`/`BackupFolder` discovery, `ManifestData`, manifest state literals, and moved `BackupRootRegistry` root configuration.
- Produces: the exact public `SnapshotEventKind`, `SnapshotEvent`, and `SnapshotEventCatalog` interfaces in **Produced Interfaces**. Later comparison code may read `SnapshotEvent.Manifest` only inside Application; WPF consumes only public DTO fields.

- [ ] **Step 1: Write failing catalog contract tests**

Create a catalog test fixture that temporarily sets `Data.DataRootDir`, isolates `BackupRootRegistry`, and constructs one app-lifetime `SnapshotEventCatalog`. Write valid v1 manifests using the real schema fields. Add these focused facts:

```csharp
[Fact]
public void Read_OrdersSameTimestampByOrdinalCanonicalPath()
{
    using TempDirectory root = TempDirectory.Create();
    WriteManifest(root.Create("zulu"), "2026-08-09T12:00:00.0000000Z", "succeeded");
    WriteManifest(root.Create("alpha"), "2026-08-09T12:00:00.0000000Z", "succeeded");
    UseBackupRoots(root.Path, () =>
    {
        IReadOnlyList<SnapshotEvent> events = new SnapshotEventCatalog().Read();

        Assert.Equal(new[] { "alpha", "zulu" }, events.Select(e => e.DisplayName));
    });
}

[Fact]
public void Read_ClassifiesFailedPartialAndUnreadableWithoutMakingThemRestorable()
{
    using TempDirectory root = TempDirectory.Create();
    WriteManifest(root.Create("verified"), "2026-08-09T11:00:00.0000000Z", "succeeded");
    WriteManifest(root.Create("partial"), "2026-08-09T10:00:00.0000000Z", "skipped");
    WriteManifest(root.Create("failed"), "2026-08-09T09:00:00.0000000Z", "failed");
    File.WriteAllText(Path.Combine(root.Create("broken"), BackupManifest.FileName), "not-json");

    UseBackupRoots(root.Path, () =>
    {
        SnapshotEvent[] events = new SnapshotEventCatalog().Read().ToArray();

        Assert.Equal(SnapshotEventKind.Verified, events.Single(e => e.DisplayName == "verified").Kind);
        Assert.Equal(SnapshotEventKind.Partial, events.Single(e => e.DisplayName == "partial").Kind);
        Assert.Equal(SnapshotEventKind.Failed, events.Single(e => e.DisplayName == "failed").Kind);
        SnapshotEvent unreadable = events.Single(e => e.DisplayName == "broken");
        Assert.Equal(SnapshotEventKind.Unreadable, unreadable.Kind);
        Assert.False(events.Single(e => e.DisplayName == "failed").IsRestorable);
        Assert.False(unreadable.IsRestorable);
        Assert.NotEmpty(unreadable.DiagnosticReason);
    });
}
```

Also add facts for: an empty manifest module list is `Failed`; a manifest with any succeeded plus skipped/failed/unknown module is `Partial`; a missing manifest in a recognized legacy folder is `Partial`; an unreadable root produces one `Unreadable` event containing `ex.Message` while readable roots remain represented; and a `RecordSessionFailure` entry disappears when a fresh catalog instance simulates restart.

- [ ] **Step 2: Run the catalog tests and verify they fail**

Run: `dotnet test src/WinRestoreKit.Tests/WinRestoreKit.Tests.csproj --filter FullyQualifiedName~SnapshotEventCatalogTests`

Expected: FAIL to compile because `SnapshotEventKind`, `SnapshotEvent`, and `SnapshotEventCatalog` are absent.

- [ ] **Step 3: Implement the one canonical status path**

Keep all classification in `SnapshotEventCatalog`; do not reproduce `HistoryPageView.ReadResult`, `HomePageView.IsEntirelyFailed`, or text-log parsing. Construct a normal folder event with its canonical full path and actual size evidence. Classify only from trusted manifest evidence:

```csharp
private static SnapshotEventKind Classify(BackupFolder folder, ManifestData manifest)
{
    if (folder.ManifestError != null)
        return SnapshotEventKind.Unreadable;

    if (manifest == null)
        return SnapshotEventKind.Partial; // legacy/manifest-silent evidence, never inferred verified

    if (manifest.Modules.Count == 0 || manifest.Modules.All(m => m.State == BackupManifest.StateFailed))
        return SnapshotEventKind.Failed;

    if (manifest.Modules.Any(m => m.State != BackupManifest.StateSucceeded))
        return SnapshotEventKind.Partial;

    return SnapshotEventKind.Verified;
}

private static int CompareEvents(SnapshotEvent left, SnapshotEvent right)
{
    int timestamp = right.Created.CompareTo(left.Created);
    return timestamp != 0
        ? timestamp
        : StringComparer.Ordinal.Compare(left.CanonicalPath, right.CanonicalPath);
}
```

For each root enumeration failure, make an `Unreadable` event with `DiagnosticReason = ex.Message` and a canonicalized root path when possible. For invalid manifest content use Task 1’s validation reason; for an invalid directory timestamp use `DateTime.MinValue` and the real read error. Compute `SizeBytes` by enumerating recursively; set `IsSizeComplete = false` and retain the exception message as diagnostic evidence if an individual entry cannot be measured.

Keep a private, in-memory `List<SnapshotEvent>` for `RecordSessionFailure`. Reject null/whitespace reasons with `ArgumentException`, generate a process-local `session://failure/<ordinal>` canonical path, classify it as `Failed`, and merge it only in that catalog instance’s `Read()`. It writes no folder, manifest, log, registry value, or retention marker. A retained directory with manifest/log/payload evidence is never recorded this way; it is rediscovered from disk so normal cleanup continues to be based on the existing durable storage rules.

- [ ] **Step 4: Run the catalog tests and verify they pass**

Run: `dotnet test src/WinRestoreKit.Tests/WinRestoreKit.Tests.csproj --filter FullyQualifiedName~SnapshotEventCatalogTests`

Expected: PASS; status, non-restorability, actual error diagnostics, stable ordinal canonical-path tie-breaking, and session-only failure lifetime all pass.

- [ ] **Step 5: Commit the event source**

```bash
git add src/WinRestoreKit.Application/Snapshots/SnapshotEventKind.cs src/WinRestoreKit.Application/Snapshots/SnapshotEvent.cs src/WinRestoreKit.Application/Snapshots/SnapshotEventCatalog.cs src/WinRestoreKit.Tests/SnapshotEventCatalogTests.cs
git commit -m "feat: add deterministic snapshot event catalog"
```

### Task 3: Prepare selected payloads with explicit ownership and cleanup

**Files:**
- Create: `src/WinRestoreKit.Application/Snapshots/SnapshotPayloadPreparation.cs`
- Create: `src/WinRestoreKit.Application/Snapshots/SnapshotPayloadPreparationService.cs`
- Create: `src/WinRestoreKit.Tests/SnapshotPayloadPreparationServiceTests.cs`
- Test: `src/WinRestoreKit.Tests/SnapshotPayloadPreparationServiceTests.cs`

**Interfaces:**
- Consumes: Task 2 `SnapshotEvent`; Core `BackupPayload.TryPrepareForRead`, `BackupPayload.ReadScope`, and its archive validation/error behavior.
- Produces: `SnapshotPayloadPreparation` and `SnapshotPayloadPreparationService.PrepareAsync(SnapshotEvent, CancellationToken)` exactly as declared above. A later Compare stage receives the successful preparation and owns its `Dispose()` call.

- [ ] **Step 1: Write failing preparation tests against real loose and compressed folders**

Use the existing `SnapshotCompressionTests`/`BackupPayloadTests` archive helpers rather than a fake zip reader. Cover successful compressed extraction, corrupted archive evidence, cancellation before extraction, failed-event rejection, and cleanup:

```csharp
[Fact]
public async Task PrepareAsync_CompressedSnapshotDeletesPrivateExtractionWhenDisposed()
{
    using TempDirectory backup = TempDirectory.Create();
    CreateCompressedPayload(backup.Path, "registry/mouse.reg", "Windows Registry Editor Version 5.00");
    SnapshotEvent snapshot = NewEvent(SnapshotEventKind.Verified, backup.Path);
    SnapshotPayloadPreparationService service = new SnapshotPayloadPreparationService();

    SnapshotPayloadPreparation prepared = await service.PrepareAsync(snapshot, CancellationToken.None);
    string extractedPath = prepared.Path;

    Assert.True(prepared.IsPrepared);
    Assert.True(File.Exists(Path.Combine(extractedPath, "registry", "mouse.reg")));
    prepared.Dispose();
    Assert.False(Directory.Exists(extractedPath));
}

[Fact]
public async Task PrepareAsync_FailedSnapshotReturnsDiagnosticWithoutOpeningPayload()
{
    SnapshotPayloadPreparation prepared = await new SnapshotPayloadPreparationService().PrepareAsync(
        NewEvent(SnapshotEventKind.Failed, @"C:\retained-failure"), CancellationToken.None);

    Assert.False(prepared.IsPrepared);
    Assert.Contains("cannot be selected", prepared.Error, StringComparison.OrdinalIgnoreCase);
}
```

- [ ] **Step 2: Run the focused preparation tests and verify they fail**

Run: `dotnet test src/WinRestoreKit.Tests/WinRestoreKit.Tests.csproj --filter FullyQualifiedName~SnapshotPayloadPreparationServiceTests`

Expected: FAIL to compile because the preparation DTO and service do not exist.

- [ ] **Step 3: Implement asynchronous preparation without changing Core payload semantics**

Make the result own the Core read scope and expose its path only when preparation succeeds. Do not filter archive entries here: comparison/restore needs the selected snapshot’s complete prepared payload and Core remains the archive validator.

```csharp
public async Task<SnapshotPayloadPreparation> PrepareAsync(
    SnapshotEvent snapshot, CancellationToken cancellationToken)
{
    if (snapshot == null)
        throw new ArgumentNullException(nameof(snapshot));

    if (!snapshot.IsRestorable)
        return new SnapshotPayloadPreparation(snapshot, null,
            "This backup attempt cannot be selected because it is failed or unreadable.");

    cancellationToken.ThrowIfCancellationRequested();
    return await Task.Run(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!BackupPayload.TryPrepareForRead(snapshot.CanonicalPath, out BackupPayload.ReadScope scope,
                                             out string error))
        {
            return new SnapshotPayloadPreparation(snapshot, null,
                "The selected backup payload could not be prepared: " + error);
        }

        if (cancellationToken.IsCancellationRequested)
        {
            scope.Dispose();
            cancellationToken.ThrowIfCancellationRequested();
        }

        return new SnapshotPayloadPreparation(snapshot, scope, null);
    }, cancellationToken).ConfigureAwait(false);
}
```

`SnapshotPayloadPreparation.Dispose()` must be idempotent and call `scope?.Dispose()`. On every failure it holds no scope; its `Error` is the Core error text with context, never an invented “empty snapshot” state.

Pin the DTO invariant in both implementation and tests so a successful loose-folder scope (whose owned extraction path is null) is not misreported as failed:

```csharp
public string Path => scope?.Path;
public string Error { get; }
public bool IsPrepared => Error == null;
```

- [ ] **Step 4: Run the preparation tests and verify they pass**

Run: `dotnet test src/WinRestoreKit.Tests/WinRestoreKit.Tests.csproj --filter FullyQualifiedName~SnapshotPayloadPreparationServiceTests`

Expected: PASS; loose folders keep their original path, compressed folders are removed after disposal, and corrupt/missing payload errors are surfaced verbatim enough to diagnose the actual failure.

- [ ] **Step 5: Commit safe selection preparation**

```bash
git add src/WinRestoreKit.Application/Snapshots/SnapshotPayloadPreparation.cs src/WinRestoreKit.Application/Snapshots/SnapshotPayloadPreparationService.cs src/WinRestoreKit.Tests/SnapshotPayloadPreparationServiceTests.cs
git commit -m "feat: prepare selected snapshot payloads safely"
```

### Task 4: Project the shared events into Timeline and advanced-history view models

**Files:**
- Create: `src/WinRestoreKit.Wpf/Navigation/ITimelineNavigator.cs`
- Create: `src/WinRestoreKit.Wpf/ViewModels/Snapshots/SnapshotEventStatus.cs`
- Create: `src/WinRestoreKit.Wpf/ViewModels/Snapshots/SnapshotEventViewModel.cs`
- Create: `src/WinRestoreKit.Wpf/ViewModels/Timeline/TimelineViewModel.cs`
- Create: `src/WinRestoreKit.Wpf/ViewModels/History/AdvancedHistoryViewModel.cs`
- Modify: `src/WinRestoreKit.Wpf/Properties/AssemblyInfo.cs`
- Create: `src/WinRestoreKit.Tests/TimelineViewModelTests.cs`
- Create: `src/WinRestoreKit.Tests/AdvancedHistoryViewModelTests.cs`
- Test: `src/WinRestoreKit.Tests/TimelineViewModelTests.cs`

**Interfaces:**
- Consumes: Task 2 event catalog, Task 3 preparation service, and `ITimelineNavigator` defined in this task.
- Produces: `SnapshotEventViewModel` as the sole WPF projection of kind, label, icon, diagnostic-only state, and selection eligibility; `TimelineViewModel` exposes the default timeline list and `AdvancedHistoryViewModel` exposes the same source filtered by query. `ITimelineNavigator.OpenCompare` owns a successful preparation; `ShowSnapshotDiagnostic` never receives a preparation.

- [ ] **Step 1: Write failing selection, status, and history-source tests**

Test a constructed catalog/fixture event list, not XAML. Assert one status mapper determines labels for both screens and that rejected statuses never call payload preparation:

```csharp
[Fact]
public async Task OpenSelectedAsync_PreparesPartialAndTransfersOwnershipToNavigator()
{
    SnapshotEvent partial = NewEvent(SnapshotEventKind.Partial, @"C:\snapshot");
    RecordingNavigator navigator = new RecordingNavigator();
    TimelineViewModel viewModel = new TimelineViewModel(
        new FakeCatalog(partial), new FakePreparationService(partial), navigator);

    await viewModel.RefreshAsync();
    viewModel.SelectedEvent = Assert.Single(viewModel.Events);
    await viewModel.OpenSelectedAsync();

    Assert.Same(partial, navigator.Prepared.Snapshot);
    Assert.Null(navigator.Diagnostic);
}

[Fact]
public async Task OpenSelectedAsync_ShowsFailedEvidenceWithoutPreparingPayload()
{
    SnapshotEvent failed = NewEvent(SnapshotEventKind.Failed, @"C:\failed", "disk full");
    FakePreparationService service = new FakePreparationService();
    RecordingNavigator navigator = new RecordingNavigator();
    TimelineViewModel viewModel = new TimelineViewModel(new FakeCatalog(failed), service, navigator);

    await viewModel.RefreshAsync();
    viewModel.SelectedEvent = Assert.Single(viewModel.Events);
    await viewModel.OpenSelectedAsync();

    Assert.Same(failed, navigator.Diagnostic);
    Assert.Equal(0, service.Calls);
}


private static SnapshotEvent NewEvent(SnapshotEventKind kind, string path, string reason = null)
    => new SnapshotEvent(kind, new DateTime(2026, 8, 9, 12, 0, 0, DateTimeKind.Local),
        Path.GetFileName(path), Path.GetFullPath(path), reason, "TEST-PC", 0, true, null);
private sealed class FakeCatalog : ISnapshotEventReader
{
    private readonly IReadOnlyList<SnapshotEvent> events;
    internal FakeCatalog(params SnapshotEvent[] events) => this.events = events;
    public IReadOnlyList<SnapshotEvent> Read() => events;
}

private sealed class FakePreparationService : ISnapshotPayloadPreparationService
{
    private readonly SnapshotEvent preparedEvent;
    internal FakePreparationService(SnapshotEvent preparedEvent = null) => this.preparedEvent = preparedEvent;
    internal int Calls { get; private set; }

    public Task<SnapshotPayloadPreparation> PrepareAsync(SnapshotEvent snapshot, CancellationToken cancellationToken)
    {
        Calls++;
        return Task.FromResult(new SnapshotPayloadPreparation(
            preparedEvent ?? snapshot, null, preparedEvent == null ? "unexpected preparation" : null));
    }
}

private sealed class RecordingNavigator : ITimelineNavigator
{
    internal SnapshotPayloadPreparation Prepared { get; private set; }
    internal SnapshotEvent Diagnostic { get; private set; }
    public void OpenCompare(SnapshotPayloadPreparation preparation) => Prepared = preparation;
    public void ShowSnapshotDiagnostic(SnapshotEvent snapshot) => Diagnostic = snapshot;
}
```

Add an advanced-history test that filters by display name, machine, canonical path, and kind label while retaining the same `SnapshotEventViewModel.Status` instance/values used by Timeline. The history test must not create a second classifier.

- [ ] **Step 2: Run the focused view-model tests and verify they fail**

Run: `dotnet test src/WinRestoreKit.Tests/WinRestoreKit.Tests.csproj --filter "FullyQualifiedName~TimelineViewModelTests|FullyQualifiedName~AdvancedHistoryViewModelTests"`

Expected: FAIL to compile because the WPF view-model and navigation types are absent.

- [ ] **Step 3: Implement one presentation projection and explicit selection transfer**

Make `SnapshotEventStatus.For` the only WPF kind-to-UI projection. It supplies words and a Fluent glyph key but does not recalculate whether a snapshot is restorable:

```csharp
public sealed class SnapshotEventStatus
{
    public SnapshotEventStatus(string label, string glyph, bool isDiagnosticOnly)
    {
        Label = label;
        Glyph = glyph;
        IsDiagnosticOnly = isDiagnosticOnly;
    }

    public string Label { get; }
    public string Glyph { get; }
    public bool IsDiagnosticOnly { get; }
}

internal static class SnapshotEventStatusProjection
{
    internal static SnapshotEventStatus For(SnapshotEventKind kind) => kind switch
    {
        SnapshotEventKind.Verified => new("Verified", "CheckmarkCircle", false),
        SnapshotEventKind.Partial => new("Partial snapshot", "Warning", false),
        SnapshotEventKind.Failed => new("Backup failed", "ErrorCircle", true),
        SnapshotEventKind.Unreadable => new("Details unavailable", "ErrorCircle", true),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
}
```

`SnapshotEventViewModel` holds the immutable event and this status. `TimelineViewModel.RefreshAsync` replaces its observable collection from one `catalog.Read()` result; its `OpenSelectedAsync` follows this ownership-safe branch:

```csharp
if (SelectedEvent == null)
    return;

if (!SelectedEvent.Event.IsRestorable)
{
    navigator.ShowSnapshotDiagnostic(SelectedEvent.Event);
    return;
}

SnapshotPayloadPreparation prepared = await preparationService
    .PrepareAsync(SelectedEvent.Event, cancellationToken);
if (!prepared.IsPrepared)
{
    SelectionError = prepared.Error;
    prepared.Dispose();
    return;
}


try
{
    navigator.OpenCompare(prepared);
    prepared = null; // navigator now owns it
}
finally
{
    prepared?.Dispose();
}
```

Add `<InternalsVisibleTo Include="WinRestoreKit.Tests" />` to the WPF project’s existing assembly-friend declarations so test-only `RecordingNavigator` can implement the internal navigation seam. Do not make `ITimelineNavigator` public; make only types/properties bound from XAML public, as specified in **Produced Interfaces**.

`AdvancedHistoryViewModel` receives the same catalog instance and uses `ICollectionView` filtering over `SnapshotEventViewModel`; it exposes exact timestamp, machine, canonical path, manifest state label, byte count/unknown size, and diagnostic reason. It never reads files or parses manifest/log/payload content.

- [ ] **Step 4: Run the view-model tests and verify they pass**

Run: `dotnet test src/WinRestoreKit.Tests/WinRestoreKit.Tests.csproj --filter "FullyQualifiedName~TimelineViewModelTests|FullyQualifiedName~AdvancedHistoryViewModelTests"`

Expected: PASS; verified/partial transfer a prepared scope, failed/unreadable are diagnostic-only, failed preparation is inline evidence, and both screens use the one status projection.

- [ ] **Step 5: Commit the WPF event projection**

```bash
git add src/WinRestoreKit.Wpf/Properties/AssemblyInfo.cs src/WinRestoreKit.Wpf/Navigation/ITimelineNavigator.cs src/WinRestoreKit.Wpf/ViewModels/Snapshots/SnapshotEventStatus.cs src/WinRestoreKit.Wpf/ViewModels/Snapshots/SnapshotEventViewModel.cs src/WinRestoreKit.Wpf/ViewModels/Timeline/TimelineViewModel.cs src/WinRestoreKit.Wpf/ViewModels/History/AdvancedHistoryViewModel.cs src/WinRestoreKit.Tests/TimelineViewModelTests.cs src/WinRestoreKit.Tests/AdvancedHistoryViewModelTests.cs
git commit -m "feat: add timeline and advanced history view models"
```

### Task 5: Render accessible Timeline and advanced History from the shared projection

**Files:**
- Create: `src/WinRestoreKit.Wpf/Views/Controls/SnapshotEventList.xaml`
- Create: `src/WinRestoreKit.Wpf/Views/Controls/SnapshotEventList.xaml.cs`
- Create: `src/WinRestoreKit.Wpf/Views/TimelineView.xaml`
- Create: `src/WinRestoreKit.Wpf/Views/TimelineView.xaml.cs`
- Create: `src/WinRestoreKit.Wpf/Views/AdvancedHistoryView.xaml`
- Create: `src/WinRestoreKit.Wpf/Views/AdvancedHistoryView.xaml.cs`
- Create: `src/WinRestoreKit.Wpf/Resources/SnapshotEventTemplates.xaml`
- Create: `src/WinRestoreKit.Tests/TimelineAccessibilityTests.cs`
- Test: `src/WinRestoreKit.Tests/TimelineAccessibilityTests.cs`

**Interfaces:**
- Consumes: Task 4’s `TimelineViewModel`, `AdvancedHistoryViewModel`, `SnapshotEventViewModel`, and one status projection.
- Produces: keyboard-operable views that bind only to view-model properties. `SnapshotEventList` is the common visual-timeline/list-fallback control used by both screens; it has a standard `ListBox` UIA surface and never contains filesystem or status-classification code.

- [ ] **Step 1: Write failing STA accessibility and construction tests**

Use Foundation’s sole `src/WinRestoreKit.Tests/WpfTestHost.cs` helper, `WpfTestHost.Run(Action)`, to construct the actual view:

```csharp
[Fact]
public void TimelineView_ExposesEquivalentNamedListAndKeyboardSelection()
{
    WpfTestHost.Run(() =>
    {
        TimelineView view = new TimelineView { DataContext = NewTimelineViewModel() };
        view.ApplyTemplate();

        ListBox list = Assert.IsType<ListBox>(view.FindName("TimelineEventList"));
        Assert.Equal("Snapshots", AutomationProperties.GetName(list));
        Assert.Equal(SelectionMode.Single, list.SelectionMode);
        Assert.True(KeyboardNavigation.GetDirectionalNavigation(list) == KeyboardNavigationMode.Continue);
        Assert.Contains("Enter", AutomationProperties.GetHelpText(list));
        list.SelectedIndex = 0;
        RaiseKey(list, Key.Right);
        Assert.Equal(1, list.SelectedIndex);
        RaiseKey(list, Key.Left);
        Assert.Equal(0, list.SelectedIndex);
    });
}
```

Add this test-local key helper; `NewTimelineViewModel()` creates two events through the `ISnapshotEventReader`/preparation-service test doubles defined in Task 4:

```csharp
private static void RaiseKey(UIElement target, Key key)
{
    target.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, null, 0, key)
    {
        RoutedEvent = Keyboard.PreviewKeyDownEvent
    });
}
```

Also assert each row’s `AutomationProperties.Name` includes its title, formatted timestamp, text status, and `DiagnosticReason` when diagnostic-only; assert the inline selection-error `TextBlock` has `AutomationProperties.LiveSetting="Polite"`.

- [ ] **Step 2: Run the focused WPF tests and verify they fail**

Run: `dotnet test src/WinRestoreKit.Tests/WinRestoreKit.Tests.csproj --filter FullyQualifiedName~TimelineAccessibilityTests`

Expected: FAIL to compile because the Timeline WPF views do not exist.

- [ ] **Step 3: Implement reusable list/list-fallback XAML and input semantics**

Use an ordinary `ListBox` for the UIA-equivalent narrow representation; it must remain the accessible control even when the wide visual timeline applies an item template with a restrained connecting line. Bind the common collection and avoid duplicate row templates.

```xml
<!-- Views/Controls/SnapshotEventList.xaml -->
<ListBox x:Name="TimelineEventList"
         ItemsSource="{Binding Events}"
         SelectedItem="{Binding SelectedEvent, Mode=TwoWay}"
         SelectionMode="Single"
         AutomationProperties.Name="Snapshots"
         AutomationProperties.HelpText="Left and Right Arrow move snapshots. Enter opens a verified or partial snapshot; failed and unreadable entries open details."
         KeyboardNavigation.DirectionalNavigation="Continue"
         PreviewKeyDown="OnPreviewKeyDown">
  <ListBox.ItemTemplate>
    <DataTemplate DataType="{x:Type vm:SnapshotEventViewModel}">
      <Grid AutomationProperties.Name="{Binding AutomationName}">
        <Grid.ColumnDefinitions>
          <ColumnDefinition Width="Auto" />
          <ColumnDefinition Width="*" />
        </Grid.ColumnDefinitions>
        <TextBlock Grid.Column="0" Text="{Binding Status.Glyph}" />
        <StackPanel Grid.Column="1">
          <TextBlock Text="{Binding DisplayName}" />
          <TextBlock Text="{Binding CreatedDisplay}" />
          <TextBlock Text="{Binding Status.Label}" />
          <TextBlock Text="{Binding DiagnosticReason}"
                     Visibility="{Binding HasDiagnostic, Converter={StaticResource BooleanToVisibilityConverter}}" />
        </StackPanel>
      </Grid>
    </DataTemplate>
  </ListBox.ItemTemplate>
</ListBox>
```

In `OnPreviewKeyDown`, change `SelectedIndex` by `-1` for `Key.Left` and `+1` for `Key.Right`, constrain it to `[0, Items.Count - 1]`, set `e.Handled = true`, and call `ScrollIntoView`; let Up/Down, Tab, Shift+Tab, and standard `ListBox` selection behavior remain native. On Enter execute the view model’s async open command. In a width trigger at 1024px, change only layout and connector visibility; retain the same `ListBox`, bindings, item text, commands, names, and focus behavior rather than maintaining a second data source.

Use `SnapshotEventTemplates.xaml` from both Timeline and History to bind `Status.Label`, not local `DataTrigger` status text. `TimelineView` contains the error text shown after failed payload preparation:

```xml
<TextBlock Text="{Binding SelectionError}"
           Visibility="{Binding HasSelectionError, Converter={StaticResource BooleanToVisibilityConverter}}"
           AutomationProperties.LiveSetting="Polite"
           Foreground="{DynamicResource WarningTextBrush}" />
```

`AdvancedHistoryView` binds the same event rows in a searchable `ListView`/`GridView` (timestamp, machine, path, manifest status, size, diagnostics), preserving the shared status template and no Restore button.

Use Foundation’s existing `WpfTestHost.Run(Action)`; do not create another STA helper.

- [ ] **Step 4: Run the accessibility tests and verify they pass**

Run: `dotnet test src/WinRestoreKit.Tests/WinRestoreKit.Tests.csproj --filter FullyQualifiedName~TimelineAccessibilityTests`

Expected: PASS; WPF views construct on an STA thread, the list exposes a stable UIA name/help text, all state is textually distinguishable, and narrow/wide layouts share the same accessible item collection.

- [ ] **Step 5: Commit the WPF Timeline and advanced History views**

```bash
git add src/WinRestoreKit.Wpf/Views/Controls/SnapshotEventList.xaml src/WinRestoreKit.Wpf/Views/Controls/SnapshotEventList.xaml.cs src/WinRestoreKit.Wpf/Views/TimelineView.xaml src/WinRestoreKit.Wpf/Views/TimelineView.xaml.cs src/WinRestoreKit.Wpf/Views/AdvancedHistoryView.xaml src/WinRestoreKit.Wpf/Views/AdvancedHistoryView.xaml.cs src/WinRestoreKit.Wpf/Resources/SnapshotEventTemplates.xaml src/WinRestoreKit.Tests/TimelineAccessibilityTests.cs
git commit -m "feat: render accessible snapshot timeline"
```

### Task 6: Verify the Timeline WPF runtime path without comparison scope

**Files:**
- Create: `src/WinRestoreKit.Tests/TimelineWpfSmokeTests.cs`
- Test: `src/WinRestoreKit.Tests/TimelineWpfSmokeTests.cs`

**Interfaces:**
- Consumes: Tasks 2–5. The test host supplies a real `TimelineViewModel`, catalog, preparation service, and recording `ITimelineNavigator`.
- Produces: runtime evidence that the real Timeline WPF control can load event data, render an accessible list, and transfer a verified/partial preparation to its navigation boundary. Task 5 supplies the separate explicit keyboard-movement test. The Compare/Confirm plan owns production shell-stage composition and the long-lived preparation after this boundary.

- [ ] **Step 1: Write the failing WPF runtime smoke test**

```csharp
[Fact]
public void TimelineView_LoadsSelectionAndTransfersPreparedSnapshot()
{
    WpfTestHost.Run(() =>
    {
        RecordingNavigator navigator = new RecordingNavigator();
        SnapshotEvent snapshot = NewEvent(SnapshotEventKind.Verified, @"C:\timeline-smoke");
        TimelineViewModel viewModel = new TimelineViewModel(
            new FakeCatalog(snapshot), new FakePreparationService(snapshot), navigator);
        TimelineView view = new TimelineView { DataContext = viewModel };
        Window host = new Window { Content = view, Width = 1024, Height = 720 };

        host.Show();
        try
        {
            viewModel.RefreshAsync().GetAwaiter().GetResult();
            ListBox list = FindDescendant<ListBox>(view);
            list.SelectedIndex = 0;
            viewModel.OpenSelectedAsync().GetAwaiter().GetResult();

            Assert.NotNull(navigator.Prepared);
            Assert.Equal(SnapshotEventKind.Verified, navigator.Prepared.Snapshot.Kind);
        }
        finally
        {
            navigator.Prepared?.Dispose();
            host.Close();
        }
    });
}
```

Add this test-local visual-tree helper in `TimelineWpfSmokeTests.cs`:

```csharp
private static T FindDescendant<T>(DependencyObject root) where T : DependencyObject
{
    if (root is T matched)
        return matched;

    for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
    {
        T found = FindDescendant<T>(VisualTreeHelper.GetChild(root, index));
        if (found != null)
            return found;
    }

    return null;
}
```

- [ ] **Step 2: Run the runtime smoke test and verify it fails**

Run: `dotnet test src/WinRestoreKit.Tests/WinRestoreKit.Tests.csproj --filter FullyQualifiedName~TimelineWpfSmokeTests`

Expected: FAIL because the Timeline control cannot yet be hosted with a populated view model and transfer a selected preparation.

- [ ] **Step 3: Correct real-control binding, layout, and command wiring**

Make only the Task 4/Task 5 controls work under a shown WPF `Window`: use `Loaded`/dispatcher-safe collection refresh, ensure the `ListBox` receives keyboard focus after the test selects it, and bind the Enter handler to `OpenSelectedAsync`. The recording navigator must be a test double only; production ownership and navigation are deliberately implemented by the next Compare/Confirm plan. Do not add a comparison surface, a fake “coming soon” state, a restore-set collection, or a confirmation dialog.

- [ ] **Step 4: Run all focused Timeline tests and the WPF runtime smoke test**

Run: `dotnet test src/WinRestoreKit.Tests/WinRestoreKit.Tests.csproj --filter "FullyQualifiedName~SnapshotEventCatalogTests|FullyQualifiedName~SnapshotPayloadPreparationServiceTests|FullyQualifiedName~TimelineViewModelTests|FullyQualifiedName~AdvancedHistoryViewModelTests|FullyQualifiedName~TimelineAccessibilityTests|FullyQualifiedName~TimelineWpfSmokeTests"`

Expected: PASS; event classification, selection cleanup, shared status projection, keyboard/UIA construction, and the shown-window Timeline runtime path all pass.

- [ ] **Step 5: Commit Timeline runtime verification coverage**

```bash
git add src/WinRestoreKit.Tests/TimelineWpfSmokeTests.cs
git commit -m "test: smoke timeline WPF runtime path"
```
## Final Verification

- [ ] Run: `dotnet test src/WinRestoreKit.Tests/WinRestoreKit.Tests.csproj --filter "FullyQualifiedName~BackupFoldersReadTests|FullyQualifiedName~SnapshotEventCatalogTests|FullyQualifiedName~SnapshotPayloadPreparationServiceTests|FullyQualifiedName~TimelineViewModelTests|FullyQualifiedName~AdvancedHistoryViewModelTests|FullyQualifiedName~TimelineAccessibilityTests|FullyQualifiedName~TimelineWpfSmokeTests"`

Expected: PASS with no failing selected Timeline/event tests.

- [ ] Run: `dotnet build src/WinRestoreKit.Wpf/WinRestoreKit.Wpf.csproj`

Expected: Build succeeds; `WinRestoreKit.Application` compiles without `UseWindowsForms` or `UseWPF`, and WPF references the event DTO rather than a `Views` discovery type.

- [ ] Run: `dotnet build src/WinRestoreKit.sln`

Expected: Build succeeds with both the legacy WinForms shell and side-by-side WPF project resolving one Application-owned backup-folder discovery implementation.

- [ ] On a real Windows desktop, perform Task 5’s keyboard/UIA smoke and Task 6’s shown-window preparation smoke with a compressed snapshot, a failed retained folder, a malformed manifest, and a session-only folder-creation failure. Expected: only verified/partial events can start payload preparation; Left/Right changes the selected accessible list item; all diagnostic text reflects actual evidence; closing/canceling a successful compressed selection removes its private extraction directory; restarting the app removes only the session-only failure from Timeline and does not alter any backup retention state.
