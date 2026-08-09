# Backup, Progress, Results, and App Restore WPF Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver the WPF Create Snapshot workflow from module selection through safe execution, honest results, and return to an updated Timeline, including the owner-bound app-reinstall dialog, while the WinForms application remains runnable.

**Architecture:** Reuse the Foundation plan's framework-neutral `WinRestoreKit.Application` runner, run-control, and summary contracts plus the Compare/Confirm plan's module-catalog contract without modifying Core backup/restore semantics or snapshot files. WPF view models own selection, navigation, presentation, and commands; Foundation's `WpfRunUi` and `WpfLogSink` are extended and composed with the dispatcher and owner-bound dialogs. A small Application completion publisher asks the Timeline event catalog to discover retained output first and creates an in-memory failure event only when no retained event exists.

**Tech Stack:** .NET 8 for Windows, WPF/XAML, MVVM (`INotifyPropertyChanged` and `ICommand`), xUnit 2.9.3, existing Core `BackupBase`/payload/manifest/logging APIs, and the existing Windows registry-backed destination registry.

## Global Constraints

- Execute after `2026-08-09-wpf-foundation-application-shell.md`, `2026-08-09-timeline-event-model.md`, and `2026-08-09-compare-confirm-restore.md`; this plan does not duplicate their contracts.
- Keep `WinRestoreKit.Core` backup, restore, manifest, payload, cleanup, containment, ownership, archive, rollback, Explorer, and shutdown semantics unchanged. Do not change the snapshot format.
- `src/WinRestoreKit.Application` references Core only; it MUST NOT reference WPF or WinForms. WPF views and view models MUST NOT parse registry exports, JSON payload text, or archive contents.
- Preserve the six ordered scopes, their current module membership, default-selection behavior, environment-variable explicit opt-in, warning text, `BackupPresets` literal membership, destination containment protection, name validation, and `SnapshotCompression.None/Fast/Max` behavior. The default compression remains `Fast`.
- The only admission point is `RunCoordinator.TryStart()`. A rejected second attempt must not construct a runner, replace the active workspace, install a log sink, or mutate snapshot state. Release admission with `RunCoordinator.SetRunning(false)` in the run owner's `finally` path.
- `RunControl.Pause()`, `Resume()`, and `RequestCancellation()` retain their existing boundary-only behavior: an active module finishes; no later module starts; cancellation wakes a paused run. Never claim rollback for work already performed.
- Disable cancellation when the runner reports the exact `BackupRestoreOrchestrator.ArchiveProgressText` value. Preserve real metric values from `ProgressMetrics`; use `N/A` where Core reports no byte measurement, never an invented estimate.
- `RunSummary` is the sole authority for completion wording. Render `RunSeverity { Information, Warning, Error }`; do not inspect `RunControl.IsCancellationRequested` to relabel a summary. In particular, a completed `RunSummary.For(...)` remains completed after a late cancel click, while `RunSummary.Incomplete(...)` remains visibly incomplete.
- The Foundation `IRunUi` has no `IWin32Window Owner` and no Windows Forms type. `DialogOwner` is an opaque `object` supplied by the shell solely for Core's existing app-reinstall dialog seam; Application neither casts it nor passes it into `AppRestoreService`. Its exact surface is:

  ```csharp
  internal interface IRunUi
  {
      object DialogOwner { get; }
      void SetProgressText(string text);
      void SetProgressPercent(int percent);
      void SetProgressDetail(string groupInfo, string elapsed, string remaining,
          string throughput, long bytesWritten, int errors, int warnings);
      void ShowSummary(RunSummary summary, string caption,
          IReadOnlyList<ModuleOutcome> outcomes);
      IReadOnlyList<string> ShowConsentDialog(RestorePlan plan);
      bool ConfirmSnapshotOverride(string text, string caption);
      void ShowPlanCompositionError(string text, string caption);
      void SetExplorerRestartVisible(bool visible);
  }
  ```

- Use the Timeline plan's app-lifetime `SnapshotEventCatalog`: `IReadOnlyList<SnapshotEvent> Read()` and `void RecordSessionFailure(DateTime created, string displayName, string diagnosticReason)`. Its immutable events use `SnapshotEventKind { Verified, Partial, Failed, Unreadable }`; only `Verified` and `Partial` are restorable.
- First re-read the catalog after a backup. Publish a session failure only when no retained recognized event corresponds to the expected backup path (for example, folder creation failed or cancellation removed the new folder). Retained partial, failed, or unreadable folders are discovered from disk and MUST NOT receive a duplicate session event. Never keep an incomplete folder merely to manufacture history.
- The WPF shell registers Core's existing `AppStoreApps.RestoreDialog` with a callback that accepts only a live WPF `Window` owner; `AppRestoreService` has no owner argument. Source-selection/read or winget-install failures appear in the owner-bound WPF dialog or inline dialog state; never use an ownerless `MessageBox`.
- Keep the original `src/WinRestoreKit` WinForms shell and `RestAppsForm` runnable during this phase. Port shared, non-UI app-restore logic out of the form and update the form to consume it; do not delete the form, designer, or legacy construction tests until Cutover.
- Keep tests in `src/WinRestoreKit.Tests`. Preserve pure tests, update all `IRunUi` fakes for the Foundation interface, and use Foundation's `WpfTestHost.Run(...)` for WPF window/control construction. Do not run formatters, linters, builds, or tests while drafting this plan.

## Prerequisite Interfaces

Foundation supplies the Application runner/run-control/summary contracts, WPF shell/adapters, and `WpfTestHost`; Compare/Confirm supplies `BackupModuleCatalog`. Do not recreate any of them:

```csharp
// src/WinRestoreKit.Application/Modules/BackupModuleCatalog.cs (Compare/Confirm)
public static IReadOnlyList<BackupModuleRegistration> BackupModuleCatalog.CreateAll();
// BackupModuleRegistration exposes public BackupBase Module, string Category, string Title.

// src/WinRestoreKit.Application/Orchestration/BackupRestoreOrchestrator.cs
internal BackupRestoreOrchestrator(IRunUi ui, RunControl runControl = null);
internal Task RunBackup(IReadOnlyList<BackupBase> modules, string destination,
    string snapshotName, SnapshotCompression compression);
internal Task RunRestore(IReadOnlyList<BackupBase> modules, string backupPath);
internal string BackupOutputPath { get; private set; }

// src/WinRestoreKit.Application/Results/RunSummary.cs
internal enum RunState { Problems, Done, NothingDone, Canceled, DidNotRun }
internal enum RunSeverity { Information, Warning, Error }
// RunSummary retains For(IReadOnlyList<ModuleOutcome>, bool, RunVerb, string),
// Incomplete(IReadOnlyList<ModuleOutcome>, RunVerb, string), and Canceled(RunVerb).
// It exposes State, Severity, Headline, and Detail, maps Problems (including Incomplete)
// and DidNotRun to Warning, and all remaining current states to Information; it contains no MessageBoxIcon.

// src/WinRestoreKit.Wpf/ViewModels/ShellViewModel.cs
public object CurrentWorkspace { get; private set; }
public string WorkflowLabel { get; private set; }
public ICommand ShowTimelineCommand { get; }
internal void ShowTimeline();
internal void NavigateTo(object workspace, string workflowLabel);

// src/WinRestoreKit.Wpf/Navigation/ITimelineNavigator.cs
void OpenCompare(SnapshotPayloadPreparation preparation);
void ShowSnapshotDiagnostic(SnapshotEvent snapshot);
```

## File Map

| Path | Responsibility |
| --- | --- |
| `src/WinRestoreKit.Application/Backup/ScopeGroups.cs` | Framework-neutral six-scope catalog and immutable scope rows. |
| `src/WinRestoreKit.Application/Backup/BackupPresets.cs` | Literal Developer machine and Minimal privacy-safe preset memberships. |
| `src/WinRestoreKit.Application/Backup/BackupCompletionPublisher.cs` | Discover retained post-run event first; add session-only failed event only when discovery cannot represent the attempt. |
| `src/WinRestoreKit.Application/AppRestore/AppRestoreService.cs` | Shared app-export source discovery from current payload plus public Timeline events, payload preparation/disposal, parsing, list state, winget outcome wording, and install loop. |
| `src/WinRestoreKit.Wpf/ViewModels/BackupWorkspaceViewModel.cs` | Scope/preset/metadata selection and a validated, admitted backup request. |
| `src/WinRestoreKit.Wpf/Infrastructure/AsyncDelegateCommand.cs` | Reusable non-reentrant `ICommand` that awaits view-model work and re-enables on completion. |
| `src/WinRestoreKit.Wpf/Views/BackupWorkspaceView.xaml` | Accessible Create Snapshot selection UI; no backup policy or payload parsing. |
| `src/WinRestoreKit.Wpf/ViewModels/ProgressWorkspaceViewModel.cs` | One admitted run's live metrics, logs, pause/cancel commands, and terminal summary. |
| `src/WinRestoreKit.Wpf/ViewModels/ResultWorkspaceViewModel.cs` | Neutral summary/outcome rendering and return-to-Timeline command. |
| `src/WinRestoreKit.Wpf/Services/WpfRunUi.cs` | Dispatcher-safe implementation of the neutral Application callback interface and owner-bound dialog bridge. |
| `src/WinRestoreKit.Wpf/Services/WpfLogSink.cs` | Dispatcher-safe `ILogSink` that stops posting after disposal. |
| `src/WinRestoreKit.Wpf/Services/WpfAppRestoreDialog.cs` | Core `AppStoreApps.RestoreDialog` registration and owner-bound WPF package-picker construction. |
| `src/WinRestoreKit.Wpf/Views/AppRestoreDialog.xaml` and `.xaml.cs` | WPF package-picker dialog. |
| `src/WinRestoreKit.Wpf/ViewModels/AppRestoreDialogViewModel.cs` | App-export/list/install/stop state without WPF payload or JSON parsing. |
| `src/WinRestoreKit.Wpf/ViewModels/ShellViewModel.cs`, `src/WinRestoreKit.Wpf/MainWindow.xaml` | Create Snapshot command, workspace data template, run availability, and return navigation. |
| `src/WinRestoreKit/Views/BackupPageView.cs`, `src/WinRestoreKit/Forms/RestAppsForm.cs` | Still-runnable WinForms clients updated to consume moved shared selection/app-restore services; no duplicate logic. |
| `src/WinRestoreKit.Tests/WpfTestHost.cs` | Foundation-owned deterministic STA/dispatcher helper reused by all WPF runtime tests. |
| `src/WinRestoreKit.Tests/*Tests.cs` listed in each task | Focused regression, MVVM, dispatcher, dialog, and completion-publication coverage. |

---

### Task 1: Move shared backup-selection metadata and add completion publication

**Files:**
- Create: `src/WinRestoreKit.Application/Backup/ScopeGroups.cs`
- Create: `src/WinRestoreKit.Application/Backup/BackupPresets.cs`
- Create: `src/WinRestoreKit.Application/Backup/BackupCompletionPublisher.cs`
- Modify: `src/WinRestoreKit/Views/BackupPageView.cs`
- Modify: `src/WinRestoreKit.Tests/ScopeGroupsPrivacyTests.cs`
- Modify: `src/WinRestoreKit.Tests/ViewDataHelperTests.cs`
- Modify: `src/WinRestoreKit.Tests/BackupPresetsTests.cs`
- Create: `src/WinRestoreKit.Tests/BackupCompletionPublisherTests.cs`

**Interfaces:**
- Consumes: `BackupModuleCatalog.CreateAll()`, `SnapshotEventCatalog.Read()`, `SnapshotEvent.CanonicalPath`, and `RunSummary.Detail`; neither `BackupFolders` nor a WPF-local root parser is used.
- Produces: `public sealed class ScopeGroupRow`; `public static class ScopeGroups` with `public static IReadOnlyList<ScopeGroupRow> Build()`; `public static class BackupPresets` with `DeveloperMachine` and `MinimalPrivacySafeExclusions`; and `internal sealed class BackupCompletionPublisher` with `void Publish(string attemptedBackupPath, string displayName, RunSummary summary, DateTime created)`.

- [ ] **Step 1: Write the failing scope, preset, and no-duplicate-event tests**

  Move the existing pure scope/preset assertions away from `Views` and add completion-publication tests that pass the runner's exact attempted output path: create a recognized retained folder whose name deliberately differs from `Data.NowShort`, then use a fresh nonexistent attempted path for the session-only case.

  ```csharp
  [Fact]
  public void Publish_RetainedPartialFolder_DoesNotAddSessionFailure()
  {
      using var isolation = new BackupRunIsolation();
      string path = CreateRecognizedPartialFolder(isolation.DestinationRoot);
      BackupRootRegistry.Remember(isolation.DestinationRoot);
      var catalog = new SnapshotEventCatalog();
      var publisher = new BackupCompletionPublisher(catalog);

      publisher.Publish(path, "nightly", RunSummary.Incomplete(
          Array.Empty<ModuleOutcome>(), RunVerb.Backup,
          "Cancellation was requested. No further group was started."),
          new DateTime(2026, 8, 9, 9, 0, 0));

      SnapshotEvent discovered = Assert.Single(catalog.Read());
      Assert.Equal(SnapshotEventKind.Partial, discovered.Kind);
      Assert.Equal(Path.GetFullPath(path), discovered.CanonicalPath,
          StringComparer.OrdinalIgnoreCase);
  }

  private static string CreateRecognizedPartialFolder(string root)
  {
      string path = Directory.CreateDirectory(Path.Combine(root, "snapshot-started-before-rollover")).FullName;
      string manifest = BackupManifest.Compose(
          new BackupBase[] { new DMouse() }, Array.Empty<ModuleResult>(),
          new DateTime(2026, 8, 9, 9, 0, 0), "test-machine", "test-user", "test-build", "0.0.0");
      File.WriteAllText(Path.Combine(path, BackupManifest.FileName), manifest);
      return path;
  }

  [Fact]
  public void Publish_NoRetainedRecognizedFolder_RecordsSessionFailureOnly()
  {
      var catalog = new SnapshotEventCatalog();
      var publisher = new BackupCompletionPublisher(catalog);
      var summary = RunSummary.For(Array.Empty<ModuleOutcome>(), false, RunVerb.Backup,
          "the backup folder could not be created: access denied");

      string attemptedPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"),
          "snapshot-attempted-before-rollover");
      publisher.Publish(attemptedPath, "nightly", summary, new DateTime(2026, 8, 9, 9, 0, 0));

      SnapshotEvent failure = Assert.Single(catalog.Read().Where(e => e.Kind == SnapshotEventKind.Failed));
      Assert.Equal("nightly", failure.DisplayName);
      Assert.Contains("could not be created", failure.DiagnosticReason, StringComparison.OrdinalIgnoreCase);
      Assert.False(failure.IsRestorable);
  }
  ```

  Keep the current literal expected scopes and type lists in `ViewDataHelperTests`; add assertions that `ScopeGroups.Build()` is directly importable from `WinRestoreKit`, not `Views`.

- [ ] **Step 2: Run the focused tests to verify they fail before the move**

  Run:

  ```powershell
  dotnet test src/WinRestoreKit.Tests/WinRestoreKit.Tests.csproj --filter "FullyQualifiedName~ScopeGroupsPrivacyTests|FullyQualifiedName~ViewDataHelperTests|FullyQualifiedName~BackupPresetsTests|FullyQualifiedName~BackupCompletionPublisherTests"
  ```

  Expected: FAIL because Application `ScopeGroups`, `BackupPresets`, and `BackupCompletionPublisher` do not exist yet. Do not accept a failure caused by a stale `Views` import as evidence that the new behavior is covered.

- [ ] **Step 3: Move the selection data without changing its membership or defaults**

  Move the implementation from `Views/ScopeGroups.cs` into the Application file, change its namespace to `WinRestoreKit`, make the row/catalog callable by WPF, and construct registrations through the Compare/Confirm `BackupModuleCatalog` facade. Preserve exactly the current six definitions, test-all-modules-once guard, detail truncation, and current default/opt-in calculation:

  ```csharp
  public sealed class ScopeGroupRow
  {
      internal ScopeGroupRow(string name, string detail, bool defaultChecked,
          bool requiresExplicitOptIn, string cautionNote, IReadOnlyList<BackupBase> modules)
      {
          Name = name;
          Detail = detail;
          DefaultChecked = defaultChecked;
          RequiresExplicitOptIn = requiresExplicitOptIn;
          CautionNote = cautionNote ?? string.Empty;
          Modules = modules;
      }

      public string Name { get; }
      public string Detail { get; }
      public bool DefaultChecked { get; }
      public bool RequiresExplicitOptIn { get; }
      public string CautionNote { get; }
      public IReadOnlyList<BackupBase> Modules { get; }
  }

  public static class ScopeGroups
  {
      private const int DetailLimit = 96;
      private static readonly ScopeDefinition[] Definitions =
      {
          new("Explorer & shell", IsExplorerAndShell),
          new("Power & devices", IsPowerAndDevices),
          new("Fonts", static module => module is WFonts),
          new("Network profiles", IsNetworkProfile),
          new("Environment variables (unfiltered)", static module => module is EEnvironment,
              true, static modules => modules.Single().WarningMessage),
          new("App settings (AppData)", IsAppSetting)
      };

      public static IReadOnlyList<ScopeGroupRow> Build()
      {
          IReadOnlyList<BackupModuleRegistration> registrations = BackupModuleCatalog.CreateAll();
          var modulesByScope = Definitions.ToDictionary(
              definition => definition, _ => new List<BackupBase>());
          foreach (BackupModuleRegistration registration in registrations)
          {
              ScopeDefinition match = null;
              foreach (ScopeDefinition definition in Definitions)
              {
                  if (!definition.Includes(registration.Module))
                      continue;
                  if (match != null)
                      throw new InvalidOperationException(
                          $"Module '{registration.Module.GetType().FullName}' belongs to multiple backup scopes.");
                  match = definition;
              }
              if (match == null)
                  throw new InvalidOperationException(
                      $"Module '{registration.Module.GetType().FullName}' has no backup scope.");
              modulesByScope[match].Add(registration.Module);
          }
          return Definitions.Select(definition =>
          {
              IReadOnlyList<BackupBase> modules = modulesByScope[definition];
              return new ScopeGroupRow(definition.Name,
                  modules.Count == 0 ? "No supported items detected" : Truncate(modules[0].Info),
                  !definition.RequiresExplicitOptIn && modules.Any(module => module.IsInstalled()),
                  definition.RequiresExplicitOptIn,
                  definition.CautionNoteFactory?.Invoke(modules) ?? string.Empty, modules);
          }).ToList();
      }

      private static bool IsExplorerAndShell(BackupBase module) =>
          module is WPersonalization or WVisualEffects or WTaskbar or WThemes or APinnedApps;
      private static bool IsPowerAndDevices(BackupBase module) =>
          module is WPowerPlans or DPrinters or DMouse or DKeyboard or DTouchpad;
      private static bool IsNetworkProfile(BackupBase module) =>
          module is WNetworkConf or WMappedDrives or CWiFiConf or EHosts;
      private static bool IsAppSetting(BackupBase module) =>
          module is WPrivacy or WAPrivacy or WTelemetry or WUpdates or WAccessibility or WRegional
              or WOther or AppStoreApps or GGaming or ETerminal or EVSCode or ESsh
              or EEnvironmentFiltered;
      private static string Truncate(string value)
      {
          string singleLine = string.Join(" ", (value ?? string.Empty).Split(
              (char[])null, StringSplitOptions.RemoveEmptyEntries));
          return singleLine.Length <= DetailLimit ? singleLine :
              singleLine.Substring(0, DetailLimit - 3).TrimEnd() + "...";
      }

      private sealed class ScopeDefinition
      {
          internal ScopeDefinition(string name, Func<BackupBase, bool> includes,
              bool requiresExplicitOptIn = false,
              Func<IReadOnlyList<BackupBase>, string> cautionNoteFactory = null)
          {
              Name = name; Includes = includes; RequiresExplicitOptIn = requiresExplicitOptIn;
              CautionNoteFactory = cautionNoteFactory;
          }
          internal string Name { get; }
          internal Func<BackupBase, bool> Includes { get; }
          internal bool RequiresExplicitOptIn { get; }
          internal Func<IReadOnlyList<BackupBase>, string> CautionNoteFactory { get; }
      }
  }
  ```

  `BackupPresets.cs` must keep these exact values and no UI dependency:

  ```csharp
  public static readonly IReadOnlyList<string> DeveloperMachine =
      new[] { "ETerminal", "EVSCode", "ESsh", "EEnvironment", "EHosts" };
  public static readonly IReadOnlyList<string> MinimalPrivacySafeExclusions =
      new[] { "WUpdates", "EEnvironment", "EEnvironmentFiltered", "CWiFiConf" };
  ```

  Update `BackupPageView` to import these Application types and remove the old `Views` definitions; retain its form behavior so WinForms remains runnable. Do not leave an Application-to-WinForms forwarding shim.

- [ ] **Step 4: Implement discovery-first completion publication**

  Create a small Application-only publisher. It does not create files, write manifests, prune folders, or decide cleanup; those choices remain in `BackupRestoreOrchestrator`.

  ```csharp
  internal sealed class BackupCompletionPublisher
  {
      private readonly SnapshotEventCatalog catalog;

      internal BackupCompletionPublisher(SnapshotEventCatalog catalog)
          => this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));

      internal void Publish(string attemptedBackupPath, string displayName,
          RunSummary summary, DateTime created)
      {
          string canonicalPath = TryCanonicalize(attemptedBackupPath);
          bool retained = canonicalPath != null && catalog.Read().Any(snapshot =>
              string.Equals(snapshot.CanonicalPath, canonicalPath,
                  StringComparison.OrdinalIgnoreCase));

          if (retained)
              return;

          string diagnostic = summary?.Detail;
          if (string.IsNullOrWhiteSpace(diagnostic))
              diagnostic = "The snapshot run ended without a retained recognizable result.";

          catalog.RecordSessionFailure(created, displayName ?? string.Empty, diagnostic);
      }

      private static string TryCanonicalize(string path)
      {
          try { return Path.GetFullPath(path); }
          catch (Exception) { return null; }
      }
  }
  ```

  The publisher intentionally permits a session event even if a completed summary cannot be reconciled to a recognizable folder: the UI reports the observable discrepancy rather than inventing a verified snapshot. It intentionally does *not* publish where `Read()` finds the exact attempted canonical path as `Verified`, `Partial`, `Failed`, or `Unreadable`. It never computes a folder name or calls `Data.NowShort`; cleanup remains solely the runner's existing responsibility.

- [ ] **Step 5: Run the focused tests to verify the shared contracts pass**

  Run the Step 2 command again.

  Expected: PASS. The six scopes retain their exact order and module assignment; unfiltered environment variables remain unchecked with their source warning; preset names still resolve; retained partial output yields exactly its discovered event; missing output yields one non-restorable current-session failure without filesystem retention.

- [ ] **Step 6: Commit the shared selection and completion layer**

  ```powershell
  git add src/WinRestoreKit.Application/Backup/ScopeGroups.cs src/WinRestoreKit.Application/Backup/BackupPresets.cs src/WinRestoreKit.Application/Backup/BackupCompletionPublisher.cs src/WinRestoreKit/Views/BackupPageView.cs src/WinRestoreKit.Tests/ScopeGroupsPrivacyTests.cs src/WinRestoreKit.Tests/ViewDataHelperTests.cs src/WinRestoreKit.Tests/BackupPresetsTests.cs src/WinRestoreKit.Tests/BackupCompletionPublisherTests.cs
  git commit -m "feat: share backup selection and completion events"
  ```

### Task 2: Build the accessible WPF Create Snapshot selection workspace

**Files:**
- Create: `src/WinRestoreKit.Wpf/ViewModels/BackupRunRequest.cs`
- Create: `src/WinRestoreKit.Wpf/ViewModels/BackupScopeItemViewModel.cs`
- Create: `src/WinRestoreKit.Wpf/ViewModels/BackupWorkspaceViewModel.cs`
- Create: `src/WinRestoreKit.Wpf/Infrastructure/AsyncDelegateCommand.cs`
- Create: `src/WinRestoreKit.Wpf/Views/BackupWorkspaceView.xaml`
- Create: `src/WinRestoreKit.Wpf/Views/BackupWorkspaceView.xaml.cs`
- Modify: `src/WinRestoreKit.Wpf/WinRestoreKit.Wpf.csproj`
- Modify: `src/WinRestoreKit.Wpf/MainWindow.xaml`
- Create: `src/WinRestoreKit.Tests/BackupWorkspaceViewModelTests.cs`
- Create: `src/WinRestoreKit.Tests/BackupWorkspaceViewTests.cs`

**Interfaces:**
- Consumes: Task 1's `ScopeGroups.Build()` and `BackupPresets`; Foundation `ObservableObject`, `DelegateCommand`, and `RunCoordinator`; Compare/Confirm `BackupModuleCatalog`; `SnapshotCompression` from Core; and Foundation `WpfTestHost`.
- Produces: `internal sealed class BackupRunRequest` (`IReadOnlyList<BackupBase> Modules`, `string SnapshotName`, `SnapshotCompression Compression`, `string Destination`); `internal sealed class BackupWorkspaceViewModel`; and `internal Task StartAsync()` that invokes its supplied `Func<BackupRunRequest, Task>` only after local selection/destination validation.

- [ ] **Step 1: Write failing VM and STA view tests**

  Reuse Foundation's `WpfTestHost.Run(Action)` for each test that constructs a WPF control or window. It supplies the STA thread, pumps its dispatcher, propagates action failures, and shuts the dispatcher down; do not create a second helper.

  Test observable selection behavior, not XAML implementation details:

  ```csharp
  [Fact]
  public async Task StartAsync_SelectedScope_RequestsItsConcreteModulesAndFastCompression()
  {
      BackupRunRequest request = null;
      var vm = new BackupWorkspaceViewModel(r => { request = r; return Task.CompletedTask; }, @"C:\snapshots");
      foreach (BackupScopeItemViewModel scope in vm.Scopes) scope.IsSelected = false;
      vm.Scopes.Single(s => s.Name == "Explorer & shell").IsSelected = true;

      await vm.StartAsync();

      Assert.Equal(SnapshotCompression.Fast, request.Compression);
      Assert.Equal(new[] { typeof(WPersonalization), typeof(WVisualEffects), typeof(WTaskbar),
          typeof(WThemes), typeof(APinnedApps) }, request.Modules.Select(m => m.GetType()));
  }

  [Fact]
  public async Task StartAsync_EmptySelectionOrDestination_ShowsValidationAndDoesNotRequestRun()
  {
      int starts = 0;
      var vm = new BackupWorkspaceViewModel(_ => { starts++; return Task.CompletedTask; }, "");
      foreach (BackupScopeItemViewModel scope in vm.Scopes) scope.IsSelected = false;

      await vm.StartAsync();

      Assert.Equal(0, starts);
      Assert.Equal("Select at least one scope with supported items.", vm.ValidationMessage);
  }

  [Fact]
  public void View_ExposesScopeWarningsAndLabeledCompressionChoices()
  {
      WpfTestHost.Run(() =>
      {
          var view = new BackupWorkspaceView { DataContext = new BackupWorkspaceViewModel(_ => Task.CompletedTask, @"C:\snapshots") };
          Assert.NotNull(view.FindName("CreateSnapshotButton"));
          Assert.NotNull(view.FindName("CompressionComboBox"));
      });
  }
  ```

  Add cases that pin: the unfiltered environment scope begins unselected and exposes `CautionNote`; blank destination produces the exact destination message once there is a scope; presets use the Task 1 literal lists; `None`, `Fast`, and `Max` map directly to their enum values; module flattening is `Distinct()` while preserving catalog order.

- [ ] **Step 2: Run the new selection tests to verify they fail**

  Run:

  ```powershell
  dotnet test src/WinRestoreKit.Tests/WinRestoreKit.Tests.csproj --filter "FullyQualifiedName~BackupWorkspaceViewModelTests|FullyQualifiedName~BackupWorkspaceViewTests"
  ```

  Expected: FAIL at compile time because the WPF workspace and request do not exist. `WpfTestHost` is already supplied by Foundation and must not be recreated.

- [ ] **Step 3: Implement the selection VM with no backup execution policy**

  Keep request composition in the VM, but leave name safety, containment, destination creation, ownership, archiving, manifest writing, and custom-root persistence exclusively in `BackupRestoreOrchestrator`.

  ```csharp
  internal sealed class BackupWorkspaceViewModel : ObservableObject
  {
      private readonly Func<BackupRunRequest, Task> startRunAsync;

      internal BackupWorkspaceViewModel(Func<BackupRunRequest, Task> startRunAsync, string defaultDestination)
      {
          this.startRunAsync = startRunAsync ?? throw new ArgumentNullException(nameof(startRunAsync));
          Destination = defaultDestination ?? string.Empty;
          Compression = SnapshotCompression.Fast;
          Scopes = new ObservableCollection<BackupScopeItemViewModel>(
              ScopeGroups.Build().Select(scope => new BackupScopeItemViewModel(scope)));
          StartCommand = new AsyncDelegateCommand(StartAsync, ex =>
          {
              ValidationMessage = ex.Message;
              OnPropertyChanged(nameof(ValidationMessage));
          });
      }

      public ObservableCollection<BackupScopeItemViewModel> Scopes { get; }
      public IReadOnlyList<SnapshotCompression> CompressionOptions { get; } =
          new[] { SnapshotCompression.None, SnapshotCompression.Fast, SnapshotCompression.Max };
      public string SnapshotName { get; set; } = string.Empty;
      public string Destination { get; set; }
      public SnapshotCompression Compression { get; set; }
      public string ValidationMessage { get; private set; }
      public ICommand StartCommand { get; }

      internal async Task StartAsync()
      {
          IReadOnlyList<BackupBase> modules = Scopes.Where(scope => scope.IsSelected)
              .SelectMany(scope => scope.Modules).Distinct().ToArray();
          if (modules.Count == 0)
          {
              ValidationMessage = "Select at least one scope with supported items.";
              OnPropertyChanged(nameof(ValidationMessage));
              return;
          }
          if (string.IsNullOrWhiteSpace(Destination))
          {
              ValidationMessage = "Choose a destination folder before capturing.";
              OnPropertyChanged(nameof(ValidationMessage));
              return;
          }

          ValidationMessage = null;
          OnPropertyChanged(nameof(ValidationMessage));
          await startRunAsync(new BackupRunRequest(modules, SnapshotName?.Trim() ?? string.Empty,
              Compression, Destination.Trim()));
      }
  }
  ```
  If Foundation's `DelegateCommand` cannot await tasks, implement the following WPF-infrastructure command. Its `async void` method is the required `ICommand.Execute` boundary; it catches every task fault and is not a view click handler:

  ```csharp
  internal sealed class AsyncDelegateCommand : ICommand
  {
      private readonly Func<Task> executeAsync;
      private readonly Action<Exception> reportFailure;
      private bool executing;

      internal AsyncDelegateCommand(Func<Task> executeAsync, Action<Exception> reportFailure = null)
      {
          this.executeAsync = executeAsync ?? throw new ArgumentNullException(nameof(executeAsync));
          this.reportFailure = reportFailure;
      }

      public event EventHandler CanExecuteChanged;
      public bool CanExecute(object parameter) => !executing;

      public async void Execute(object parameter)
      {
          if (executing) return;
          executing = true;
          CanExecuteChanged?.Invoke(this, EventArgs.Empty);
          try { await executeAsync(); }
          catch (Exception ex) { reportFailure?.Invoke(ex); }
          finally
          {
              executing = false;
              CanExecuteChanged?.Invoke(this, EventArgs.Empty);
          }
      }
  }
  ```

  Keep this command internal to WPF infrastructure; do not use an `async void` view click handler.
  `BackupScopeItemViewModel` carries `Name`, `Detail`, `CautionNote`, computed `HasCaution`, `RequiresExplicitOptIn`, `IReadOnlyList<BackupBase> Modules`, and mutable `IsSelected` initialized from `DefaultChecked`. Implement commands for Select all, Clear, Developer machine, and Minimal privacy-safe by transforming the existing type-name lists through one tested `SelectModulesByTypeName`/exclusion routine; do not hard-code module type names in WPF.


- [ ] **Step 4: Implement XAML for keyboard and automation parity**

  Create a content view with the Foundation resource dictionaries and a named, labeled destination browser. Use `Microsoft.Win32.OpenFolderDialog` in code-behind only to set `Destination`; the binding and view model retain all selection state.

  ```xml
  <UserControl x:Class="WinRestoreKit.Wpf.Views.BackupWorkspaceView"
               xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
               xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
               MinWidth="1024" AutomationProperties.Name="Create snapshot">
    <Grid Margin="32">
      <Grid.RowDefinitions>
        <RowDefinition Height="Auto" />
        <RowDefinition Height="*" />
        <RowDefinition Height="Auto" />
      </Grid.RowDefinitions>
      <TextBlock Text="Create snapshot" Style="{StaticResource PageTitleText}" />
      <ListBox Grid.Row="1" ItemsSource="{Binding Scopes}"
               AutomationProperties.Name="Backup scopes">
        <ListBox.ItemTemplate>
          <DataTemplate>
            <CheckBox IsChecked="{Binding IsSelected, Mode=TwoWay}"
                      AutomationProperties.Name="{Binding Name}">
              <StackPanel>
                <TextBlock Text="{Binding Name}" />
                <TextBlock Text="{Binding Detail}" />
                <TextBlock Text="{Binding CautionNote}" Visibility="{Binding HasCaution, Converter={StaticResource BooleanToVisibilityConverter}}" />
              </StackPanel>
            </CheckBox>
          </DataTemplate>
        </ListBox.ItemTemplate>
      </ListBox>
      <StackPanel Grid.Row="2">
        <TextBox Text="{Binding SnapshotName, UpdateSourceTrigger=PropertyChanged}"
                 AutomationProperties.Name="Snapshot name" />
        <DockPanel>
          <TextBox Text="{Binding Destination, UpdateSourceTrigger=PropertyChanged}"
                   AutomationProperties.Name="Destination folder" />
          <Button Content="Browse" Click="BrowseDestination_Click" AutomationProperties.Name="Browse destination" />
        </DockPanel>
        <ComboBox x:Name="CompressionComboBox" ItemsSource="{Binding CompressionOptions}"
                  SelectedItem="{Binding Compression}" AutomationProperties.Name="Compression" />
        <TextBlock Text="{Binding ValidationMessage}" Foreground="{DynamicResource WarningBrush}" />
        <Button x:Name="CreateSnapshotButton" Content="Create snapshot" Command="{Binding StartCommand}"
                AutomationProperties.Name="Create snapshot" />
      </StackPanel>
    </Grid>
  </UserControl>
  ```

  Ensure every preset action has a text label, focus order follows scope list then metadata then action, and the caution is textual (not color-only). Add a `DataTemplate` for `BackupWorkspaceViewModel` to `MainWindow.xaml`; do not add a permanent navigation rail.

- [ ] **Step 5: Run the selection tests to verify they pass**

  Re-run the Step 2 command.

  Expected: PASS. The view constructs on an STA thread, controls have automation names, scope choices match the existing module catalog, validation blocks an empty request, and no test invokes backup I/O.

- [ ] **Step 6: Commit the WPF selection workspace**

  ```powershell
  git add src/WinRestoreKit.Wpf/Infrastructure/AsyncDelegateCommand.cs src/WinRestoreKit.Wpf/ViewModels/BackupRunRequest.cs src/WinRestoreKit.Wpf/ViewModels/BackupScopeItemViewModel.cs src/WinRestoreKit.Wpf/ViewModels/BackupWorkspaceViewModel.cs src/WinRestoreKit.Wpf/Views/BackupWorkspaceView.xaml src/WinRestoreKit.Wpf/Views/BackupWorkspaceView.xaml.cs src/WinRestoreKit.Wpf/MainWindow.xaml src/WinRestoreKit.Tests/BackupWorkspaceViewModelTests.cs src/WinRestoreKit.Tests/BackupWorkspaceViewTests.cs
  git commit -m "feat: add WPF snapshot selection"
  ```

### Task 3: Add dispatcher-safe run progress, logging, and neutral result rendering

**Files:**
- Modify: `src/WinRestoreKit.Wpf/Services/WpfRunUi.cs`
- Modify: `src/WinRestoreKit.Wpf/Services/WpfLogSink.cs`
- Create: `src/WinRestoreKit.Wpf/ViewModels/ProgressWorkspaceViewModel.cs`
- Create: `src/WinRestoreKit.Wpf/ViewModels/ResultWorkspaceViewModel.cs`
- Create: `src/WinRestoreKit.Wpf/Views/ProgressWorkspaceView.xaml`
- Create: `src/WinRestoreKit.Wpf/Views/ProgressWorkspaceView.xaml.cs`
- Create: `src/WinRestoreKit.Wpf/Views/ResultWorkspaceView.xaml`
- Create: `src/WinRestoreKit.Wpf/Views/ResultWorkspaceView.xaml.cs`
- Modify: `src/WinRestoreKit.Application/Orchestration/BackupRestoreOrchestrator.cs`
- Modify: `src/WinRestoreKit.Tests/ArchiveProgressTests.cs`
- Modify: `src/WinRestoreKit.Tests/BackupDestinationContainmentTests.cs`
- Modify: `src/WinRestoreKit.Tests/BackupDestinationLifecycleTests.cs`
- Modify: `src/WinRestoreKit.Tests/LockedPayloadBackupTests.cs`
- Modify: `src/WinRestoreKit.Tests/RestoreConsentCancellationTests.cs`
- Modify: `src/WinRestoreKit.Tests/SnapshotFolderPathTests.cs`
- Create: `src/WinRestoreKit.Tests/WpfRunUiTests.cs`
- Create: `src/WinRestoreKit.Tests/WpfLogSinkTests.cs`
- Create: `src/WinRestoreKit.Tests/ProgressWorkspaceViewModelTests.cs`
- Create: `src/WinRestoreKit.Tests/ResultWorkspaceViewModelTests.cs`
- Create: `src/WinRestoreKit.Tests/BackupOutputPathTests.cs`

**Interfaces:**

- Consumes: Foundation `IRunUi`, `IRunPresentation`, `IRunDialogService`, `IWpfDialogService`, `WpfRunUi`, `WpfLogSink`, `BackupRestoreOrchestrator`, `RunControl`, `RunCoordinator`, `ProgressMetrics`, `RunSummary`, `RunSeverity`, Core `ILogSink`/`LogHelper`; Task 2 `BackupRunRequest`; and Compare/Confirm's owner-bound `RestoreRunDialogService`.
- Produces: `BackupRestoreOrchestrator.BackupOutputPath` as the exact attempted output path; `internal sealed class ProgressWorkspaceViewModel : IRunPresentation` with `Task<RunSummary> RunBackupAsync(BackupRunRequest request)`, `Task<RunSummary> RunRestoreAsync(IReadOnlyList<BackupBase> modules, string backupPath)`, `IReadOnlyList<ModuleOutcome> Outcomes`, and `string AttemptedBackupPath`; `internal sealed class ResultWorkspaceViewModel`; and extended dispatcher-bound Foundation `WpfRunUi`/`WpfLogSink`.

- [ ] **Step 1: Write failing tests for admission-independent presentation, dispatch, cancellation, and results**

  Update every existing orchestration fake to remove `IWin32Window Owner` and supply the opaque shell owner explicitly:

  ```csharp
  public object DialogOwner => null;
  ```

  Add focused WPF tests. The late-cancel test must render a completed summary *after* requesting cancellation and prove that the result remains complete:

  ```csharp
  [Fact]
  public void Result_CompletedSummaryAfterLateCancel_IsNotRelabeledIncomplete()
  {
      var control = new RunControl();
      control.RequestCancellation();
      RunSummary completed = RunSummary.For(new[] { SucceededOutcome() }, true, RunVerb.Backup);

      ResultWorkspaceViewModel vm = ResultWorkspaceViewModel.From(completed,
          new[] { SucceededOutcome() }, () => Task.CompletedTask);

      Assert.Equal("Run complete", vm.StatusLabel);
      Assert.DoesNotContain("canceled", vm.Headline, StringComparison.OrdinalIgnoreCase);
      Assert.Equal(RunSeverity.Information, vm.Severity);
  }

  [Fact]
  public void Result_IncompleteSummary_UsesItsActualCancellationWording()
  {
      RunSummary incomplete = RunSummary.Incomplete(Array.Empty<ModuleOutcome>(), RunVerb.Backup,
          "Cancellation was requested. No further group was started.");

      ResultWorkspaceViewModel vm = ResultWorkspaceViewModel.From(incomplete,
          Array.Empty<ModuleOutcome>(), () => Task.CompletedTask);

      Assert.Equal("Run canceled, incomplete", vm.StatusLabel);
      Assert.Contains("canceled, run incomplete", vm.Headline, StringComparison.OrdinalIgnoreCase);
      Assert.Equal(RunSeverity.Warning, vm.Severity);
  }

  [Fact]
  public void LogSink_AfterDispose_DoesNotPostAnotherLogLine()
  {
      WpfTestHost.Run(() =>
      {
          var lines = new List<string>();
          using var sink = new WpfLogSink(Dispatcher.CurrentDispatcher, lines.Add, () => lines.Clear());
          sink.Append("before");
          Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
          sink.Dispose();
          sink.Append("after");
          Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
          Assert.Equal(new[] { "before" }, lines);
      });
  }
  ```

  Add tests that `PauseCommand` changes only the requested boundary state and logs the exact pause/resume messages; confirmed cancellation disables pause/cancel and logs the exact active-group warning; archive progress disables cancel; a runner that returns without `ShowSummary` produces the existing did-not-run fallback; and `WpfRunUi.DialogOwner` returns the live `Window` when available and `null` when unavailable. The existing Compare/Confirm dialog-service tests remain the proof that consent and override calls fail closed without an owner.

  Add `BackupOutputPathTests` for both existing backup overloads. The direct-path overload must set `BackupOutputPath` to the caller's `backupPath` before `RunBackupCore` validates/creates it. The destination overload must compute `backupPath = Path.Combine(destinationPath, Data.NowShort)`, assign that exact value to `BackupOutputPath` before `DestinationInsideSelectedSource`, and retain it even when validation produces a did-not-run summary. Use a deliberately pre-rollover-looking direct folder name in the test; assert equality to the supplied string, never a later `Data.NowShort` value.

- [ ] **Step 2: Run the focused progress and adapter tests to verify they fail**

  Run:

  ```powershell
  dotnet test src/WinRestoreKit.Tests/WinRestoreKit.Tests.csproj --filter "FullyQualifiedName~WpfRunUiTests|FullyQualifiedName~WpfLogSinkTests|FullyQualifiedName~ProgressWorkspaceViewModelTests|FullyQualifiedName~ResultWorkspaceViewModelTests|FullyQualifiedName~ArchiveProgressTests|FullyQualifiedName~BackupDestinationContainmentTests|FullyQualifiedName~BackupDestinationLifecycleTests|FullyQualifiedName~BackupOutputPathTests|FullyQualifiedName~LockedPayloadBackupTests|FullyQualifiedName~RestoreConsentCancellationTests|FullyQualifiedName~SnapshotFolderPathTests"
  ```

  Expected: FAIL because `BackupOutputPath`, WPF progress/result workspaces, and the post-disposal log-sink behavior do not exist yet. The Foundation move has already replaced `Owner` with `DialogOwner`; a stale WinForms owner fake is a setup error, not evidence of this behavior.
- [ ] **Step 3: Implement the dispatcher and dialog adapters**

  Extend Foundation's existing `WpfLogSink` rather than replacing it: preserve its dispatcher-safe append/clear implementation and add the following disposed-state gate so no queued or later engine log line mutates the view after a run has ended:

  ```csharp
  internal sealed class WpfLogSink : ILogSink, IDisposable
  {
      private readonly Dispatcher dispatcher;
      private readonly Action<string> append;
      private readonly Action clear;
      private int disposed;

      internal WpfLogSink(Dispatcher dispatcher, Action<string> append, Action clear)
      {
          this.dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
          this.append = append ?? throw new ArgumentNullException(nameof(append));
          this.clear = clear ?? throw new ArgumentNullException(nameof(clear));
      }

      public void Append(string text) => Post(() => append(text ?? string.Empty));
      public void Clear() => Post(clear);
      public void Dispose() => Interlocked.Exchange(ref disposed, 1);

      private void Post(Action action)
      {
          if (Volatile.Read(ref disposed) != 0 || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
              return;
          try { dispatcher.BeginInvoke(() => { if (Volatile.Read(ref disposed) == 0) action(); }); }
          catch (InvalidOperationException) { }
      }
  }
  ```

  `WpfRunUi` is extended with its Foundation constructor shape—`Dispatcher`, `IRunPresentation`, `IRunDialogService`, and `Func<Window> ownerProvider`—not a second adapter API. `ProgressWorkspaceViewModel` is the concrete `IRunPresentation`: its setters update bound values and `ShowSummary` stores the exact supplied `RunSummary`/outcomes before returning, so navigation cannot race a blank result page. `WpfRunUi.DialogOwner` returns the visible, non-closing `Window` or `null`; it does not call the app-reinstall UI itself. Fire-and-forget presentation setters use `BeginInvoke`; callbacks requiring a result and `ShowSummary` use `Dispatcher.Invoke`. It forwards dialog calls only through the supplied `IRunDialogService`, whose concrete shell implementation owns fail-closed modal behavior.
  The owner-bound `IRunDialogService` is Compare/Confirm's `RestoreRunDialogService`; do not create another dialog interface, owner protocol, consent dialog, or snapshot-override implementation here. This task only consumes the already composed service when `RunRestoreAsync` is invoked; Compare/Confirm owns its owner validation and fail-closed behavior.

  Render `RunSeverity` in the WPF service/resource layer; no `MessageBoxIcon` appears in Application, WPF VM, or XAML.

- [ ] **Step 4: Implement the run VM and XAML views**

  Create exactly one `RunControl` and `BackupRestoreOrchestrator` per progress VM. The caller owns admission: this VM never calls `TryStart`, `SetRunning(true)`, or `SetRunning(false)`. It always copies `runner.BackupOutputPath` to `AttemptedBackupPath`, clears the global log sink in `finally`, and returns the terminal summary to its caller.


  ```csharp
  internal string BackupOutputPath { get; private set; }

  internal Task RunBackup(IReadOnlyList<BackupBase> selection, string backupPath)
  {
      BackupOutputPath = backupPath;
      return RunBackupCore(selection, backupPath, null, SnapshotCompression, null);
  }

  internal Task RunBackup(IReadOnlyList<BackupBase> selection, string destinationPath,
      string snapshotName, SnapshotCompression compression)
  {
      if (!BackupNaming.TryValidateCustomName(snapshotName, out string safeSnapshotName))
      {
          ui.ShowSummary(RunSummary.For(new List<ModuleOutcome>(), false, RunVerb.Backup,
              "the snapshot name is not a safe single folder name"), "Backup",
              new List<ModuleOutcome>());
          return Task.CompletedTask;
      }
      if (!IsKnownCompression(compression))
      {
          ui.ShowSummary(RunSummary.For(new List<ModuleOutcome>(), false, RunVerb.Backup,
              "the selected compression mode is not supported"), "Backup",
              new List<ModuleOutcome>());
          return Task.CompletedTask;
      }
      if (string.IsNullOrWhiteSpace(destinationPath))
      {
          ui.ShowSummary(RunSummary.For(new List<ModuleOutcome>(), false, RunVerb.Backup,
              "the backup destination is empty"), "Backup", new List<ModuleOutcome>());
          return Task.CompletedTask;
      }

      SnapshotCompression = compression;
      string backupPath = Path.Combine(destinationPath, Data.NowShort);
      BackupOutputPath = backupPath;
      if (DestinationInsideSelectedSource(backupPath, selection, out string containingSource))
      {
          ui.ShowSummary(RunSummary.For(new List<ModuleOutcome>(), false, RunVerb.Backup,
              "the chosen destination is inside a folder this backup would copy (" + containingSource
              + "), which would copy the backup into itself; choose a destination outside the "
              + "folders being backed up"), "Backup", new List<ModuleOutcome>());
          return Task.CompletedTask;
      }
      return RunBackupCore(selection, backupPath, safeSnapshotName, compression, destinationPath);
  }
  ```

  Construct each `ProgressWorkspaceViewModel` with the live WPF `Dispatcher`, `Func<Window> ownerProvider`, Compare/Confirm's `IRunDialogService`, and Foundation `IWpfDialogService`. When it starts an already admitted run, it creates exactly one `RunControl`, `WpfRunUi(dispatcher, this, runDialogService, ownerProvider)`, `BackupRestoreOrchestrator`, and `WpfLogSink`; it never calls `RunCoordinator.TryStart`, `SetRunning`, or navigates the shell. Its owner passes that factory/dispatcher in production and a deterministic test factory in VM tests.

  ```csharp
  internal async Task<RunSummary> RunBackupAsync(BackupRunRequest request)
  {
      LogHelper.Instance.SetSink(logSink);
      sinkInstalled = true;
      SetProgressText("Started snapshot " + request.SnapshotName + ".");
      try
      {
          await runner.RunBackup(request.Modules, request.Destination,
              request.SnapshotName, request.Compression);
          if (summary == null)
              SetSummary(RunSummary.For(Array.Empty<ModuleOutcome>(), false, RunVerb.Backup,
                  "the backup runner returned without a result"), Array.Empty<ModuleOutcome>());
          return summary;
      }
      catch (Exception ex)
      {
          SetSummary(RunSummary.For(Array.Empty<ModuleOutcome>(), false, RunVerb.Backup, ex.Message),
              Array.Empty<ModuleOutcome>());
          return summary;
      }
      finally
      {
          AttemptedBackupPath = runner.BackupOutputPath;
          if (sinkInstalled) LogHelper.Instance.SetSink(null);
          sinkInstalled = false;
          logSink.Dispose();
      }
  }
  ```

  The restore overload follows the same lifecycle with `runner.RunRestore`; it reuses `WpfRunUi` for the Compare/Confirm plan and must not create its own restore gates. `PauseCommand` toggles only `RunControl.Pause/Resume`; its log text is exactly `Run paused. The active group will finish before pausing.` or `Run resumed. The next group may start.`. `CancelCommand` asks through Foundation's already owner-bound `IWpfDialogService.Confirm` with the existing accurate warning, then calls `RequestCancellation`, disables pause/cancel, and appends `Cancellation requested. The active group will finish before cancellation.`.

  ```xml
  <!-- ProgressWorkspaceView.xaml -->
  <UserControl x:Class="WinRestoreKit.Wpf.Views.ProgressWorkspaceView"
               xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
               xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <Grid Margin="32">
      <Grid.RowDefinitions><RowDefinition Height="Auto"/><RowDefinition Height="Auto"/><RowDefinition Height="*"/><RowDefinition Height="Auto"/></Grid.RowDefinitions>
      <TextBlock Text="{Binding ProgressText}" AutomationProperties.Name="Snapshot progress status" />
      <ProgressBar Grid.Row="1" Minimum="0" Maximum="100" Value="{Binding Percent}"
                   AutomationProperties.Name="Snapshot progress percentage" />
      <ListBox Grid.Row="2" ItemsSource="{Binding LogLines}" AutomationProperties.Name="Snapshot run log" />
      <StackPanel Grid.Row="3" Orientation="Horizontal" HorizontalAlignment="Right">
        <Button Content="{Binding PauseCaption}" Command="{Binding PauseCommand}" AutomationProperties.Name="Pause or resume snapshot" />
        <Button Content="Cancel" Command="{Binding CancelCommand}" AutomationProperties.Name="Cancel snapshot" />
      </StackPanel>
    </Grid>
  </UserControl>

  <!-- ResultWorkspaceView.xaml -->
  <UserControl x:Class="WinRestoreKit.Wpf.Views.ResultWorkspaceView"
               xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
               xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <StackPanel Margin="32">
      <TextBlock Text="{Binding SeverityText}" AutomationProperties.Name="Run severity" />
      <TextBlock Text="{Binding Headline}" Style="{StaticResource PageTitleText}" />
      <TextBlock Text="{Binding Detail}" TextWrapping="Wrap" />
      <ListBox ItemsSource="{Binding Outcomes}" AutomationProperties.Name="Module outcomes" />
      <Button Content="Back to Timeline" Command="{Binding ReturnToTimelineCommand}"
              AutomationProperties.Name="Back to Timeline" />
    </StackPanel>
  </UserControl>
  ```
  `ResultWorkspaceViewModel.From` derives `StatusLabel`, `Headline`, `Detail`, `Severity`, and outcome rows solely from the passed summary/outcomes. It accepts a `Func<Task>` return action and exposes it through an `AsyncDelegateCommand`. Its only special labels are exact facts already encoded in the summary: `Run canceled, no changes` for `RunState.Canceled`; `Run canceled, incomplete` for the `RunSummary.Incomplete` headline; otherwise `Run complete`. It never reads `RunControl`.

  Bind progress to text, percent, elapsed/remaining, throughput, bytes, error/warning counts, an `ObservableCollection<LogLineViewModel>`, and Pause/Cancel commands. Bind results to the neutral severity icon/text, headline/detail, module outcome list, and a text-labeled `Back to Timeline` command. Give every actionable control and live log panel an AutomationProperties name. Do not replace the workspace with a giant failure banner.

- [ ] **Step 5: Run the focused tests to verify the adapters and result behavior pass**

  Re-run the Step 2 command.

  Expected: PASS. Orchestrator regression tests compile against the `IRunUi` opaque `DialogOwner` contract with no Windows Forms type; dispatcher callbacks do not update after disposal; pause/cancel retain boundary-only wording; archive disables cancellation; summary severity/writing is neutral; a late cancellation cannot rewrite a completed outcome.

- [ ] **Step 6: Commit progress, logging, and result rendering**

  ```powershell
  git add src/WinRestoreKit.Application/Orchestration/BackupRestoreOrchestrator.cs src/WinRestoreKit.Wpf/Services/WpfRunUi.cs src/WinRestoreKit.Wpf/Services/WpfLogSink.cs src/WinRestoreKit.Wpf/ViewModels/ProgressWorkspaceViewModel.cs src/WinRestoreKit.Wpf/ViewModels/ResultWorkspaceViewModel.cs src/WinRestoreKit.Wpf/Views/ProgressWorkspaceView.xaml src/WinRestoreKit.Wpf/Views/ProgressWorkspaceView.xaml.cs src/WinRestoreKit.Wpf/Views/ResultWorkspaceView.xaml src/WinRestoreKit.Wpf/Views/ResultWorkspaceView.xaml.cs src/WinRestoreKit.Tests/ArchiveProgressTests.cs src/WinRestoreKit.Tests/BackupDestinationContainmentTests.cs src/WinRestoreKit.Tests/BackupDestinationLifecycleTests.cs src/WinRestoreKit.Tests/BackupOutputPathTests.cs src/WinRestoreKit.Tests/LockedPayloadBackupTests.cs src/WinRestoreKit.Tests/RestoreConsentCancellationTests.cs src/WinRestoreKit.Tests/SnapshotFolderPathTests.cs src/WinRestoreKit.Tests/WpfRunUiTests.cs src/WinRestoreKit.Tests/WpfLogSinkTests.cs src/WinRestoreKit.Tests/ProgressWorkspaceViewModelTests.cs src/WinRestoreKit.Tests/ResultWorkspaceViewModelTests.cs
  git commit -m "feat: add WPF run progress and results"
  ```

### Task 4: Port the app-reinstall dialog behind the WPF owner boundary

**Files:**
- Create: `src/WinRestoreKit.Application/AppRestore/AppRestoreService.cs`
- Create: `src/WinRestoreKit.Wpf/Services/WpfAppRestoreDialog.cs`
- Create: `src/WinRestoreKit.Wpf/ViewModels/AppRestoreDialogViewModel.cs`
- Create: `src/WinRestoreKit.Wpf/Views/AppRestoreDialog.xaml`
- Create: `src/WinRestoreKit.Wpf/Views/AppRestoreDialog.xaml.cs`
- Modify: `src/WinRestoreKit/Forms/RestAppsForm.cs`
- Modify: `src/WinRestoreKit.Tests/AppRestoreDialogTests.cs`
- Modify: `src/WinRestoreKit.Tests/RestoreDialogOwnerTests.cs`
- Modify: `src/WinRestoreKit.Tests/ModuleShapeTests.cs`
- Create: `src/WinRestoreKit.Tests/WpfAppRestoreDialogTests.cs`

**Interfaces:**
- Consumes: Foundation's opaque `IRunUi.DialogOwner`; Core `AppStoreApps.RestoreDialog`, `AppStoreApps.ExportPathIn`, `BackupPayload.TryPrepareForRead`, `BackupPayload.ReadScope`, `Utils.RunWingetAsync`, and `ProcessOutcome`; Timeline `SnapshotEventCatalog.Read()` and public `SnapshotEvent`.
- Produces: `internal enum AppExportState { Ok, Absent, Unreadable }`; immutable internal `AppRestoreSource`, `AppExport`, `AppRestoreListState`, and `AppRestoreOutcome`; `internal static class AppRestoreService` with `BuildSources(string selectedRestorePath, IReadOnlyList<SnapshotEvent> snapshots)`, `ReadFromSource(string sourcePath)`, `ComposeListState(AppExport export)`, and `InstallAsync(IReadOnlyList<string> packageIdentifiers, Func<bool> stopRequested)`; and an internal WPF registration that assigns Core's existing `AppStoreApps.RestoreDialog`.

- [ ] **Step 1: Write failing tests for the shared app-restore contract and WPF ownership**

  Move the current pure tests for source order/deduplication, absent versus unreadable export, package parsing, list enablement, winget outcome description, and stopped/failed wording from `RestAppsForm` nested types to `AppRestoreService`. Keep the current assertions that an empty package list is `Ok`, a missing `Packages` array is unreadable, blank package identifiers are omitted, and a `ProcessOutcome.OutcomeUnknown` is never described as never started. Add a catalog-source test: the selected payload remains first, a later `Verified`/`Partial` event is added in catalog order, and `Failed`/`Unreadable` events or duplicate canonical paths do not become app-restore sources.

  Test the WPF Core callback without a modal window by injecting its show action:

  ```csharp
  [Fact]
  public void RestoreDialogCallback_PassesTheWpfWindowAndPreparedPath()
  {
      WpfTestHost.Run(() =>
      {
          var owner = new Window();
          owner.Show();
          Window receivedOwner = null;
          string receivedPath = null;
          Action<string, object> callback = WpfAppRestoreDialog.CreateCallback(
              (actualOwner, path) => { receivedOwner = actualOwner; receivedPath = path; });

          try
          {
              callback(@"C:\prepared", owner);
              Assert.Same(owner, receivedOwner);
              Assert.Equal(@"C:\prepared", receivedPath);
          }
          finally
          {
              owner.Close();
          }
      });
  }

  [Fact]
  public void RestoreDialogCallback_WithoutWpfOwner_DoesNotOpenDialog()
  {
      Action<string, object> callback = WpfAppRestoreDialog.CreateCallback((_, _) =>
          throw new Xunit.Sdk.XunitException("must not open"));
      callback(@"C:\prepared", new object());
  }
  ```
  Also assert that a newly constructed but hidden WPF `Window` does not invoke the show action; only a loaded, visible WPF owner may open the modal dialog.

  Add a compressed-source test proving `ReadFromSource` disposes its private `BackupPayload.ReadScope` after copying package identifiers into the immutable result. Add an internal-service test whose fake installer requests stop after one package and expects `Stopped after 1 of 2 app(s). The remaining 1 were not started.`; do not kill an in-flight winget process.

- [ ] **Step 2: Run the app-restore tests to verify they fail**

  Run:

  ```powershell
  dotnet test src/WinRestoreKit.Tests/WinRestoreKit.Tests.csproj --filter "FullyQualifiedName~AppRestoreDialogTests|FullyQualifiedName~RestoreDialogOwnerTests|FullyQualifiedName~ModuleShapeTests|FullyQualifiedName~WpfAppRestoreDialogTests"
  ```

  Expected: FAIL because the Application app-restore service and WPF owner port do not exist; existing tests still name the WinForms nested helper types.

- [ ] **Step 3: Move all non-UI app-restore behavior into Application**

  Create immutable WPF-safe data contracts and move the current `RestAppsForm` parsing, source-deduplication, list-enable, problem-routing, winget-description, and outcome-composition logic into this one Application service. Its internal entry points expose no `ProcessOutcome` or payload scope; the one installer overload that accepts a process delegate remains internal solely for the Application friend test assembly to cover all outcome branches deterministically:

  ```csharp
  internal enum AppExportState { Ok, Absent, Unreadable }

  internal enum AppRestoreProblemRouting { None, ShowNow, Defer }

  internal sealed class AppRestoreSource
  {
      internal AppRestoreSource(string path, string displayName, bool isSelectedRestoreSource)
      {
          Path = path ?? string.Empty;
          DisplayName = displayName ?? string.Empty;
          IsSelectedRestoreSource = isSelectedRestoreSource;
      }
      public string Path { get; }
      public string DisplayName { get; }
      public bool IsSelectedRestoreSource { get; }
  }

  internal sealed class AppExport
  {
      private AppExport(AppExportState state, IReadOnlyList<string> packageIdentifiers, string message)
      {
          State = state;
          PackageIdentifiers = packageIdentifiers ?? Array.Empty<string>();
          Message = message ?? string.Empty;
      }
      internal AppExportState State { get; }
      internal IReadOnlyList<string> PackageIdentifiers { get; }
      internal string Message { get; }
      internal bool IsProblem => State == AppExportState.Unreadable;
      internal static AppExport Ok(IReadOnlyList<string> ids, string message) => new(AppExportState.Ok, ids, message);
      internal static AppExport Absent(string message) => new(AppExportState.Absent, null, message);
      internal static AppExport Unreadable(string message) => new(AppExportState.Unreadable, null, message);
  }

  internal sealed class AppRestoreListState
  {
      internal AppRestoreListState(IReadOnlyList<string> items, bool installEnabled)
      {
          Items = items ?? Array.Empty<string>();
          InstallEnabled = installEnabled;
      }
      internal IReadOnlyList<string> Items { get; }
      internal bool InstallEnabled { get; }
  }

  internal sealed class AppRestoreOutcome
  {
      internal AppRestoreOutcome(string caption, string text, RunSeverity severity)
          => (Caption, Text, Severity) = (caption ?? string.Empty, text ?? string.Empty, severity);
      internal string Caption { get; }
      internal string Text { get; }
      internal RunSeverity Severity { get; }
  }

  internal static class AppRestoreService
  {
      internal static IReadOnlyList<AppRestoreSource> BuildSources(
          string selectedRestorePath, IReadOnlyList<SnapshotEvent> snapshots)
      {
          var sources = new List<AppRestoreSource>();
          AddDistinct(sources, selectedRestorePath, "Selected restore source", true);
          foreach (SnapshotEvent snapshot in snapshots ?? Array.Empty<SnapshotEvent>())
          {
              if (!snapshot.IsRestorable)
                  continue;
              AddDistinct(sources, snapshot.CanonicalPath, snapshot.DisplayName, false);
          }
          return sources;
      }

      private static void AddDistinct(List<AppRestoreSource> sources, string path,
          string displayName, bool isSelectedRestoreSource)
      {
          if (string.IsNullOrWhiteSpace(path))
              return;
          string canonicalPath;
          try { canonicalPath = Path.GetFullPath(path); }
          catch (Exception) { return; }
          if (sources.Any(source => string.Equals(source.Path, canonicalPath,
              StringComparison.OrdinalIgnoreCase)))
              return;
          sources.Add(new AppRestoreSource(canonicalPath, displayName, isSelectedRestoreSource));
      }

      internal static AppRestoreListState ComposeListState(AppExport export) =>
          export == null
              ? new AppRestoreListState(null, false)
              : new AppRestoreListState(export.PackageIdentifiers,
                  export.PackageIdentifiers.Count > 0);

      internal static AppRestoreProblemRouting RouteProblem(AppExport export, bool windowShown)
      {
          if (export == null || !export.IsProblem)
              return AppRestoreProblemRouting.None;
          return windowShown ? AppRestoreProblemRouting.ShowNow : AppRestoreProblemRouting.Defer;
      }

      internal static AppExport ReadFromSource(string sourcePath)
      {
          if (!BackupPayload.TryPrepareForRead(sourcePath, out BackupPayload.ReadScope scope, out string error))
              return AppExport.Unreadable("Could not prepare the app export source: " + error);
          using (scope)
              return ReadExport(AppStoreApps.ExportPathIn(scope.Path));
      }

      internal static Task<AppRestoreOutcome> InstallAsync(
          IReadOnlyList<string> packageIdentifiers, Func<bool> stopRequested)
          => InstallAsync(packageIdentifiers, id => Utils.RunWingetAsync(true, "install",
              "--id", id, "--accept-source-agreements", "--accept-package-agreements"), stopRequested);

      internal static async Task<AppRestoreOutcome> InstallAsync(
          IReadOnlyList<string> packageIdentifiers,
          Func<string, Task<ProcessOutcome>> installOneAsync, Func<bool> stopRequested)
      {
          string[] requested = (packageIdentifiers ?? Array.Empty<string>())
              .Where(id => !string.IsNullOrWhiteSpace(id)).ToArray();
          var failures = new List<string>();
          int attempted = 0;

          foreach (string id in requested)
          {
              if (stopRequested?.Invoke() == true)
                  break;
              attempted++;
              string reason = Describe(await installOneAsync(id));
              if (reason != null)
                  failures.Add(id + ": " + reason);
          }

          return ComposeOutcome(requested.Length, attempted, failures);
      }
  }
  ```
  Move `RestAppsForm.AppExport.Read` and `Parse` verbatim in behavior into `ReadFromSource`: only `FileNotFoundException` maps to `Absent`; every other read failure maps to `Unreadable`; empty JSON, malformed JSON, or a missing `Sources[0].Packages` array maps to `Unreadable`; nonblank `PackageIdentifier` values stay in file order. `ComposeListState` enables installation only when there is at least one identifier. `Describe` and `ComposeOutcome` retain the existing exact wording, but `AppRestoreOutcome` carries `Caption`, `Text`, and `RunSeverity` instead of `MessageBoxIcon`: no selected apps, stopped-without-failures, and complete installs are `Information`; stopped-with-failures and completed failures are `Warning`.

  Update `RestAppsForm` to consume this service for source list (passing `new SnapshotEventCatalog().Read()`), parser/list state, descriptions, and final outcome. Retain its WinForms presentation and user-close behavior; leave `Program.RegisterUiSeams()` as the WinForms-specific `AppStoreApps.RestoreDialog` registration that opens the form. Delete the form's nested business types and helper copies. Application orchestration remains UI-agnostic and forwards only `IRunUi.DialogOwner` to Core's existing `AppStoreApps.RestoreAsync`.

- [ ] **Step 4: Implement the owner-bound WPF dialog**

  Register the WPF shell callback once while composing the app-lifetime `SnapshotEventCatalog`, before any restore can be admitted. It retains Core's established `Action<string, object>` seam, rejects non-WPF owners, and invokes its modal dialog on the owner dispatcher:

  ```csharp
  internal sealed class WpfAppRestoreDialog
  {
      private readonly SnapshotEventCatalog catalog;

      internal WpfAppRestoreDialog(SnapshotEventCatalog catalog)
          => this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));

      internal static void Register(SnapshotEventCatalog catalog)
      {
          var dialog = new WpfAppRestoreDialog(catalog);
          AppStoreApps.RestoreDialog = CreateCallback(dialog.Show);
      }

      internal static Action<string, object> CreateCallback(Action<Window, string> show)
      {
          return (path, owner) =>
          {
              if (owner is not Window window || !window.IsLoaded || !window.IsVisible ||
                  window.Dispatcher.HasShutdownStarted || window.Dispatcher.HasShutdownFinished)
                  return;
              Action open = () => show(window, path);
              if (window.Dispatcher.CheckAccess()) open();
              else window.Dispatcher.Invoke(open);
          };
      }

      internal void Show(Window owner, string payloadPath)
      {
          var dialog = new AppRestoreDialog(payloadPath, catalog.Read()) { Owner = owner };
          dialog.ShowDialog();
      }
  }
  ```

  `AppRestoreDialog` has `ShowInTaskbar="False"` and `WindowStartupLocation="CenterOwner"`. Its VM calls `AppRestoreService.BuildSources(payloadPath, snapshots)` and `ReadFromSource`; it receives only `AppRestoreSource`/immutable parsed values and never reads JSON, archives, registry keys, or `BackupFolders`. The dialog shows selected restore source first, a labeled source chooser, checkbox package rows, `Install selected apps`, and a text-labeled cancel/stop action. During install it disables source/package/install controls. Stop sets the VM flag, changes the action to the exact `Stopping after the current app (or its timeout)`, and lets the Application service finish the active package before the next boundary. A user-requested close while installing requests the same stop and defers close until the loop completes; dispatcher shutdown or owner teardown is never vetoed. Owner-bound unreadable-export/final outcome dialogs use the visible app-restore window; if it is no longer visible, append the message to `LogHelper` instead.

  ```xml
  <Window x:Class="WinRestoreKit.Wpf.Views.AppRestoreDialog"
          ShowInTaskbar="False" WindowStartupLocation="CenterOwner"
          Title="Reinstall apps" AutomationProperties.Name="Reinstall apps">
    <Grid Margin="24">
      <Grid.RowDefinitions><RowDefinition Height="Auto"/><RowDefinition Height="*"/><RowDefinition Height="Auto"/></Grid.RowDefinitions>
      <ComboBox ItemsSource="{Binding Sources}" SelectedItem="{Binding SelectedSource}"
                DisplayMemberPath="DisplayName" AutomationProperties.Name="App backup source" />
      <ListBox Grid.Row="1" ItemsSource="{Binding Packages}" AutomationProperties.Name="Apps to install">
        <ListBox.ItemTemplate>
          <DataTemplate><CheckBox Content="{Binding Identifier}" IsChecked="{Binding IsSelected, Mode=TwoWay}" /></DataTemplate>
        </ListBox.ItemTemplate>
      </ListBox>
      <StackPanel Grid.Row="2" Orientation="Horizontal" HorizontalAlignment="Right">
        <Button Content="Install selected apps" Command="{Binding InstallCommand}" />
        <Button Content="{Binding StopCaption}" Command="{Binding StopOrCloseCommand}" />
      </StackPanel>
    </Grid>
  </Window>
  ```

- [ ] **Step 5: Run the app-restore tests to verify they pass**

  Re-run the Step 2 command.

  Expected: PASS. Existing parser/winget facts are preserved in Application, compressed source preparation disposes its temporary scope, only a WPF `Window` owner invokes the registered Core callback, the callback preserves that owner and prepared path, and stop wording remains honest about work not started.

- [ ] **Step 6: Commit the app-restore dialog port**

  ```powershell
  git add src/WinRestoreKit.Application/AppRestore/AppRestoreService.cs src/WinRestoreKit.Wpf/Services/WpfAppRestoreDialog.cs src/WinRestoreKit.Wpf/ViewModels/AppRestoreDialogViewModel.cs src/WinRestoreKit.Wpf/Views/AppRestoreDialog.xaml src/WinRestoreKit.Wpf/Views/AppRestoreDialog.xaml.cs src/WinRestoreKit/Forms/RestAppsForm.cs src/WinRestoreKit.Tests/AppRestoreDialogTests.cs src/WinRestoreKit.Tests/RestoreDialogOwnerTests.cs src/WinRestoreKit.Tests/ModuleShapeTests.cs src/WinRestoreKit.Tests/WpfAppRestoreDialogTests.cs
  git commit -m "feat: port app restore dialog to WPF"
  ```

### Task 5: Wire Create Snapshot through admission, results, event publication, and Timeline return

**Files:**
- Modify: `src/WinRestoreKit.Wpf/ViewModels/ShellViewModel.cs`
- Modify: `src/WinRestoreKit.Wpf/MainWindow.xaml`
- Modify: `src/WinRestoreKit.Wpf/ViewModels/BackupWorkspaceViewModel.cs`
- Create: `src/WinRestoreKit.Wpf/ViewModels/BackupRunCompletion.cs`
- Create: `src/WinRestoreKit.Tests/ShellBackupFlowTests.cs`
- Create: `src/WinRestoreKit.Tests/BackupResultTimelinePublicationTests.cs`
- Modify: `src/WinRestoreKit.Tests/RunCoordinatorTests.cs`
- Modify: `src/WinRestoreKit.Tests/ProgressPageViewTests.cs`

**Interfaces:**
- Consumes: Tasks 1–4; Foundation `ShellViewModel.NavigateTo`, `ShowTimeline`, `RunCoordinator`; Timeline `SnapshotEventCatalog` and `TimelineViewModel.RefreshAsync(CancellationToken)`; no new competing global navigation service.
- Produces: `internal sealed class BackupRunCompletion` (`RunSummary Summary`, `IReadOnlyList<ModuleOutcome> Outcomes`, `string AttemptedBackupPath`); a testable `Func<BackupRunRequest, Task<BackupRunCompletion>>` shell seam; and the end-to-end `Create snapshot → BackupWorkspaceViewModel → ProgressWorkspaceViewModel → ResultWorkspaceViewModel → refreshed Timeline` sequence.

- [ ] **Step 1: Write failing host-flow and publication tests**

  Test atomic admission and navigation without executing a real backup by supplying the shell's explicit run delegate:

  ```csharp
  [Fact]
  public async Task CreateSnapshot_AdmitsOneRun_ShowsResult_ThenRefreshesTimeline()
  {
      using var isolation = new BackupRunIsolation();
      BackupRootRegistry.Remember(isolation.DestinationRoot);
      RunCoordinator.SetRunning(false);
      int refreshes = 0;
      RunSummary summary = RunSummary.For(new[] { SucceededOutcome() }, true, RunVerb.Backup);
      var shell = ShellViewModel.ForTest(
          _ => Task.FromResult(new BackupRunCompletion(summary, new[] { SucceededOutcome() },
              @"C:\snapshots\snapshot-started-before-rollover")),
          new SnapshotEventCatalog(),
          () => { refreshes++; return Task.CompletedTask; });

      shell.CreateSnapshotCommand.Execute(null);
      var selection = Assert.IsType<BackupWorkspaceViewModel>(shell.CurrentWorkspace);
      await selection.StartAsync();

      Assert.IsType<ResultWorkspaceViewModel>(shell.CurrentWorkspace);
      Assert.False(RunCoordinator.IsRunning);
      Assert.Equal(1, refreshes);
      ((ResultWorkspaceViewModel)shell.CurrentWorkspace).ReturnToTimelineCommand.Execute(null);
      Assert.Equal(1, refreshes);
  }
  ```

  Add a second concurrent-start test that pre-sets `RunCoordinator` true, invokes `StartAsync`, and proves the workspace remains selection, no log sink/run factory is used, and its visible message is `Another backup or restore is already running.`. Add a completion-publication test for a canceled-new-folder cleanup: after the runner's `Incomplete` summary and no retained recognized path, the Timeline read contains one current-session non-restorable `Failed` event; when the retained path is discovered as Partial, only the persisted Partial event exists.

  Retain the old `ProgressPageViewTests` while WinForms is still present, but migrate their `IRunUi` fakes to Foundation's opaque `DialogOwner` contract. The equivalent new tests are the WPF result/view-model tests from Task 3; do not delete the WinForms construction test in this phase.

- [ ] **Step 2: Run the host-flow tests to verify they fail**

  Run:

  ```powershell
  dotnet test src/WinRestoreKit.Tests/WinRestoreKit.Tests.csproj --filter "FullyQualifiedName~ShellBackupFlowTests|FullyQualifiedName~BackupResultTimelinePublicationTests|FullyQualifiedName~RunCoordinatorTests|FullyQualifiedName~ProgressPageViewTests"
  ```

  Expected: FAIL because `CreateSnapshotCommand` has not been wired, no injected run seam is available for the test, and Timeline is not refreshed/published after result completion.

- [ ] **Step 3: Add the one WPF backup flow to ShellViewModel**

  Add a real command to the compact top bar; it is unavailable while a run is active. Opening the selection workspace is not run admission. The start callback performs the only admission attempt and owns release in one `finally`:

  ```csharp
  private async Task StartBackupAsync(BackupRunRequest request)
  {
      if (!RunCoordinator.TryStart())
      {
          backupWorkspace?.ReportAdmissionRejected("Another backup or restore is already running.");
          return;
      }

      BackupRunCompletion completion;
      try
      {
          completion = await runBackupAsync(request);
      }
      finally
      {
          RunCoordinator.SetRunning(false);
      }

      completionPublisher.Publish(completion.AttemptedBackupPath, request.SnapshotName,
          completion.Summary, DateTime.Now);
      await timelineWorkspace.RefreshAsync();
      NavigateTo(new ResultWorkspaceViewModel(completion.Summary, completion.Outcomes,
          ReturnToTimelineAsync), "Snapshot result");
  }

  private async Task<BackupRunCompletion> RunWpfBackupAsync(BackupRunRequest request)
  {
      var progress = new ProgressWorkspaceViewModel(dispatcher, ownerProvider,
          runDialogService, dialogs);
      NavigateTo(progress, "Creating snapshot");
      RunSummary summary = await progress.RunBackupAsync(request);
      return new BackupRunCompletion(summary, progress.Outcomes, progress.AttemptedBackupPath);
  }

  private Task ReturnToTimelineAsync()
  {
      ShowTimeline();
      return Task.CompletedTask;
  }
  ```

  In the production `ShellViewModel` composition constructor, create the one app-lifetime `SnapshotEventCatalog`, call `WpfAppRestoreDialog.Register(snapshotEventCatalog)` before accepting restore navigation, then initialize `runBackupAsync` to `RunWpfBackupAsync`. Its internal `ForTest` constructor accepts the run delegate and async Timeline-refresh delegate without registering the Core static callback, so host-flow tests have no disk or WPF-window dependency. `RunCoordinator.RunningChanged` marshals through the WPF dispatcher before it calls `CreateSnapshotCommand.RaiseCanExecuteChanged()`; inactive presentation is a convenience, never race prevention.

  Add `CreateSnapshotCommand` to `MainWindow.xaml`'s top command bar with text and an automation name. It must open only the selection page—not start writes—and must not introduce a permanent sidebar.

- [ ] **Step 4: Publish and return without retaining output or inventing snapshot state**

  Call Task 1's publisher once per terminal backup before showing results, passing `completion.AttemptedBackupPath` copied from the runner's `BackupOutputPath`. The publisher canonicalizes that exact attempted path and calls `SnapshotEventCatalog.Read()`; when it sees an event there, regardless of `Verified`, `Partial`, `Failed`, or `Unreadable`, it leaves discovery as the sole event source. When no recognized retained event exists, it calls `RecordSessionFailure` with the terminal summary's real detail. It must never construct a path or call `Data.NowShort` after the run ends.

  After publication, await the Timeline plan's `TimelineViewModel.RefreshAsync(CancellationToken cancellationToken = default)` once before showing the result. That method rereads the app-lifetime catalog and replaces its observable event collection; returning from the result only calls `ShowTimeline()` and does not open Compare automatically.

- [ ] **Step 5: Run the host-flow tests to verify they pass**

  Re-run the Step 2 command.


  Expected: PASS. Exactly one run gains admission, a rejected second request leaves the active UI untouched, the completed/partial/failed display comes from the runner summary, Timeline receives a session failure only when disk cannot represent the attempt, and Back to Timeline shows the already refreshed one shared event model.

- [ ] **Step 6: Perform the required real Windows smoke path while WinForms remains runnable**

  Run the WPF application from a Windows desktop:

  ```powershell
  dotnet run --project src/WinRestoreKit.Wpf/WinRestoreKit.Wpf.csproj
  ```

  Expected: the Timeline home opens with the compact top bar and enabled **Create snapshot** when idle. Select **Create snapshot**; verify the six scopes, environment warning, preset buttons, destination browse, and None/Fast/Max choices are keyboard-reachable. Choose a safe empty destination outside every selected folder, capture one small supported scope, and observe Progress metrics/logs, pause/resume at a module boundary, and the archive phase disabling Cancel. Complete the capture; verify Result wording matches the actual summary; choose **Back to Timeline**; the new verified snapshot appears as selectable. Repeat with a deliberately canceled run only when a safe test destination is used: verify the result says incomplete only if the engine gave `RunSummary.Incomplete`, and Timeline shows either the discovered retained state or one session-only diagnostic failure—never a fabricated verified snapshot.

  Then start the legacy application without deleting or modifying it:

  ```powershell
  dotnet run --project src/WinRestoreKit/WinRestoreKit.csproj
  ```

  Expected: WinForms still constructs and exposes its existing backup/progress flow. Do not run a concurrent backup from both shells; close it after confirming startup. This is side-by-side migration verification, not final cutover or publish verification.

- [ ] **Step 7: Commit the integrated WPF flow**

  ```powershell
  git add src/WinRestoreKit.Wpf/ViewModels/ShellViewModel.cs src/WinRestoreKit.Wpf/MainWindow.xaml src/WinRestoreKit.Wpf/ViewModels/BackupWorkspaceViewModel.cs src/WinRestoreKit.Wpf/ViewModels/BackupRunCompletion.cs src/WinRestoreKit.Tests/ShellBackupFlowTests.cs src/WinRestoreKit.Tests/BackupResultTimelinePublicationTests.cs src/WinRestoreKit.Tests/RunCoordinatorTests.cs src/WinRestoreKit.Tests/ProgressPageViewTests.cs
  git commit -m "feat: complete WPF snapshot workflow"
  ```

## Final Verification Checklist

- [ ] Run the focused commands from Tasks 1–5 and confirm each expected outcome, including Core-backed destination containment, archive, ownership, run-control, and summary regressions.
- [ ] Run the full existing test suite only after all focused tests and the WPF smoke path succeed:

  ```powershell
  dotnet test src/WinRestoreKit.Tests/WinRestoreKit.Tests.csproj
  ```

  Expected: PASS. Existing Core/module/payload/manifest/orchestration behavior remains covered; no test depends on `IWin32Window Owner` or `MessageBoxIcon` in Application contracts.

- [ ] On a real Windows desktop, perform the Task 5 WPF smoke path and confirm the owner-bound app-reinstall dialog opens over the WPF owner, supports a selected source plus alternate discovered source, reports unreadable exports honestly, and asks to stop only after the current app.
- [ ] Confirm WPF has no registry/payload parsing code: `AppRestoreService` is the sole app-export/payload preparation implementation; selection, progress, results, and XAML only bind structured values.
- [ ] Confirm both WPF and WinForms remain runnable, there is no publish command, no removal of WinForms files, and no shipping-identity change in this plan. Those are Cutover work.
