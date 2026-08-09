# WPF Cutover, Release, and Verification Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` (recommended) or `superpowers:executing-plans` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Verify the completed WPF workflows on a real Windows desktop, make WPF the sole shipping `WinRestoreKit` application without compatibility shims, and prove that the released artifact is one self-contained, version-coherent `WinRestoreKit.exe`.

**Architecture:** This is the fifth and final migration stage. It consumes the framework-neutral Application layer and the WPF Timeline, Compare/Confirm, and Backup/Progress outputs; it does not redesign their behavior. First make the existing WPF shell observable and testable, then complete the real-desktop parity gate, switch the sole shipping identity and test references to WPF, remove every obsolete WinForms artifact, and finally run the release procedure against the published executable.

**Tech Stack:** .NET 8, C#, WPF/MVVM, xUnit 2.9.3, WPF Dispatcher and UI Automation peers, Windows accessibility tooling, PowerShell, Windows SDK `mt.exe`, GitHub Releases, self-contained `win-x64` single-file publishing.

## Global Constraints

- Preserve `WinRestoreKit.Core` backup/restore semantics, the on-disk snapshot and manifest format, `RestorePlan`, `SnapshotGate`, `RestoreScope`, `RestoreDispatch`, `ExplorerRestartPrompt`, backup-root behavior, and all existing outcome meanings.
- Execute this plan only after the Foundation, Timeline, Compare/Confirm, and Backup/Progress plans below have completed their focused test cycles and their WPF equivalents are runnable alongside WinForms.
- `WinRestoreKit.Application` remains framework-neutral: it references neither WinForms nor WPF, keeps namespace `WinRestoreKit`, and retains its own `InternalsVisibleTo("WinRestoreKit.Tests")` access boundary.
- `RunSummary` uses `RunSeverity { Information, Warning, Error }`. `IRunUi` has no `MessageBoxIcon`, `IWin32Window`, or typed `Owner`; it exposes only `object DialogOwner { get; }` so the existing Core app-restore seam stays framework-neutral and opaque.
- Timeline events retain `SnapshotEventKind { Verified, Partial, Failed, Unreadable }`; sort deterministically by descending timestamp and then ordinal canonical path; failed and unreadable events remain non-restorable; session-only failures never change cleanup retention.
- Compare continues to be manifest-first with the exact `RestoreContents` artifact-precedence rules, maps `HasDriftedFrom` `true`/`false`/`null` to `Changed`/`Same`/`Unavailable`, marks only the throwing module unavailable, and always disposes its `BackupPayload.ReadScope`.
- WPF never parses registry exports or payload text and never invents semantic comparison values. Restore remains whole-module and its confirmation reports only process closures, Explorer restart, and sign-out impacts that existing contracts declare; do not introduce reboot metadata.
- Do not delete WinForms until the real Windows desktop smoke matrix in Task 2 has passed and its evidence is committed. A build, unit test, or screenshot alone is not a substitute for this gate.
- The final WPF app has assembly and executable identity `WinRestoreKit`, preserves `highestAvailable`, `longPathAware`, `WinRestoreKit.ico`, and the physical raw fallback source `src/WinRestoreKit/Properties/AssemblyInfo.cs`.
- The raw fallback source retains the exact three-part `[assembly: AssemblyFileVersion("x.y.z")]` line. `GenerateAssemblyInfo=false` appears only on the final shipping WPF project; `Core` and `Application` keep SDK-generated assembly metadata.
- The only shipping release is self-contained `win-x64`, single-file, native-self-extracting, compressed, and untrimmed. The publish directory contains exactly one `WinRestoreKit.exe`; do not ship a framework-dependent `bin\Release` executable.
- Preserve relevant Core and Application tests. Replace tests that only construct or inspect WinForms controls with observable WPF contract tests; do not mechanically port control-tree assertions.
- Do not commit `bin/`, `obj/`, `publish/`, temporary snapshot roots, extracted manifests, screenshots outside the committed baseline directory, or release-verification scratch output.

---

## Upstream Interfaces and Required Completed Outputs

This plan may consume the preceding plans only through this block. Do not reimplement these services in Stage 5.

| Upstream plan | Consumed files and interfaces | Cutover responsibility |
| --- | --- | --- |
| `docs/superpowers/plans/2026-08-09-wpf-foundation-application-shell.md` | `src/WinRestoreKit.Application/`; `RunSeverity`; neutral `RunSummary`; neutral `IRunUi`; moved `RunCoordinator`, `RunControl`, `BackupRestoreOrchestrator`, `ProgressMetrics`; `Application/Updates/VersionInfo.cs`, `UpdateVerdict.cs`, `UpdateCheckService.cs`; `Application/Settings/BackupRootRegistry.cs`; `WinRestoreKit.Wpf/App.xaml`, `App.xaml.cs`, `MainWindow.xaml`, `ViewModels/ShellViewModel.cs`, WPF dispatcher/dialog/log/theme/update adapters | Keep Application framework-neutral; retain WPF MVVM composition and update/version behavior while changing WPF from its temporary identity to the shipping identity. |
| `docs/superpowers/plans/2026-08-09-timeline-event-model.md` | `SnapshotEventKind`; immutable `SnapshotEvent`; `SnapshotEventCatalog.Read()`; `SnapshotEventCatalog.RecordSessionFailure(DateTime created, string displayName, string diagnosticReason)`; `SnapshotPayloadPreparationService.PrepareAsync(SnapshotEvent snapshot, CancellationToken cancellationToken)`; `TimelineViewModel`; `AdvancedHistoryViewModel`; `TimelineView.xaml`; `AdvancedHistoryView.xaml`; shared `WpfTestHost.cs`; Timeline accessibility and smoke tests | Reuse the same event catalog for Timeline and Advanced History; retain the session-failure rule and the Timeline `ListBox` automation name `Snapshots`; extend the sole STA helper and runtime coverage rather than introducing another one. |
| `docs/superpowers/plans/2026-08-09-compare-confirm-restore.md` | `ComparisonState { Changed, Same, Unavailable, NotCaptured }`; immutable `ModuleComparison`; `Task<IReadOnlyList<ModuleComparison>> SnapshotComparisonService.CompareAsync(SnapshotEvent snapshot, IReadOnlyList<BackupBase> modules, CancellationToken cancellationToken)`; `ComparisonWorkspaceViewModel`; `ModuleComparisonRowViewModel`; `RestoreSetViewModel`; `ConfirmViewModel`; `ComparisonWorkspaceView`; `ConfirmView`; `RestoreConsentDialog`; `WpfRunUi` dialog wiring | Verify all comparison evidence states and cancellation at runtime, preserve whole-module restore selection, and verify WPF-owned modal ownership without reintroducing a typed WinForms owner seam. |
| `docs/superpowers/plans/2026-08-09-backup-progress-results.md` | `BackupWorkspaceViewModel`; `ProgressWorkspaceViewModel`; `ResultWorkspaceViewModel`; `BackupWorkspaceView`; `ProgressWorkspaceView`; `ResultWorkspaceView`; `WpfRunUi`; `WpfLogSink`; `WpfAppRestoreDialog`; Application-side `ScopeGroups`, `BackupPresets`, `BackupFolders` | Verify Create snapshot → Progress → Results → Timeline end to end, preserve app-restore behavior, and remove the corresponding WinForms implementations only after their WPF contracts pass. |

### Stage-5 invariants to pin before removal

- `SnapshotEventCatalog.Read()` is the sole persisted Timeline/Advanced History source. A failed session event comes only from `RecordSessionFailure`, is diagnostic-only, and does not create or preserve a cleanup-retained folder.
- `SnapshotComparisonService.CompareAsync(SnapshotEvent snapshot, IReadOnlyList<BackupBase> modules, CancellationToken cancellationToken)` remains read-only. Every temporary payload from `SnapshotPayloadPreparationService.PrepareAsync(SnapshotEvent snapshot, CancellationToken cancellationToken)` is released when the Compare workspace cancels, changes snapshot, or closes.
- `IRunUi` carries progress, summary, consent, snapshot-override, plan-error, Explorer-restart, and opaque `object DialogOwner { get; }` without a WinForms type. The Application orchestrator preserves the Core call `await appStoreApps.RestoreAsync(currentRestorePath, ui.DialogOwner)`; WPF supplies its shell `Window` as the opaque owner and registers `WpfAppRestoreDialog` through Core's existing `RestoreDialog` delegate.
- `src/WinRestoreKit.Tests/WpfTestHost.cs` is the sole STA smoke helper. It constructs a named STA thread, exposes `Run(Action)` and `Run<T>(Func<T>)`, captures and rethrows failures, shuts down the thread's `Dispatcher`, and joins the thread. Tests construct `ShellViewModel` with Foundation test fakes and pass it directly to `new MainWindow(shell)`; they do not use reflection or add a second application startup path.
- WPF automation identifiers are stable contracts, not layout details: `TimelineEventList`, `ComparisonWorkspace`, `CompareModuleList`, `CompareFilterAll`, `CompareFilterChanged`, `RestoreSetList`, `CompareContinueToConfirmButton`, `ConfirmRestoreButton`, `CreateSnapshotButton`, `SettingsThemeFollowSystem`, `SettingsThemeLight`, and `SettingsThemeDark`.

---

### Task 1: Make the completed WPF shell testable on one STA host and pin its observable runtime contract

**Files:**
- Create: `src/WinRestoreKit.Tests/WpfCutoverRuntimeTests.cs`
- Modify: `src/WinRestoreKit.Tests/WpfTestHost.cs`
- Modify: `src/WinRestoreKit.Tests/TimelineWpfSmokeTests.cs`
- Modify: `src/WinRestoreKit.Tests/WinRestoreKit.Tests.csproj`
- Modify: `src/WinRestoreKit.Wpf/MainWindow.xaml`
- Modify: `src/WinRestoreKit.Wpf/Views/TimelineView.xaml`
- Modify: `src/WinRestoreKit.Wpf/Views/ComparisonWorkspaceView.xaml`
- Modify: `src/WinRestoreKit.Wpf/Views/ConfirmView.xaml`
- Modify: `src/WinRestoreKit.Wpf/Views/SettingsView.xaml`
- Test: `src/WinRestoreKit.Tests/TimelineWpfSmokeTests.cs`
- Test: `src/WinRestoreKit.Tests/TimelineAccessibilityTests.cs`
- Test: `src/WinRestoreKit.Tests/WpfCutoverRuntimeTests.cs`

**Interfaces:**
- Consumes: Foundation `internal static WpfTestHost.Run(Action action)` and `WpfTestHost.Run<T>(Func<T> action)`; `ShellViewModel(IThemeService themes, WpfUpdatePresenter updates, string currentVersion)`, internal `NavigateTo(object workspace, string workflowLabel)`, `ShowTimeline()`, `ShowSettingsCommand`, and `CreateSnapshotCommand`; `MainWindow(ShellViewModel shell)`; WPF test friendship; direct `TimelineView`, `ComparisonWorkspaceView`, and `ConfirmView` hosts; and the Foundation `IThemeService`, `IUpdateCheckService`, `IWpfDialogService`, and `IExternalLinkService` contracts.
- Produces: exactly one reusable STA helper, stable UI Automation identifiers and names, and runtime tests that construct the real WPF shell, navigate actual WPF workspace controls through it, assert focusability and automation roles, and never touch a registry, snapshot, network endpoint, backup run, or dialog.

- [ ] **Step 1: Write the failing WPF shell automation-contract test**

  Add `WpfCutoverRuntimeTests.cs`. Construct `ShellViewModel` through the Foundation test fakes—not a reflection factory and not `App.OnStartup`—then host the real `MainWindow` in `WpfTestHost`. The initial test must fail until Compare, Confirm, and Settings expose the required automation identifiers.

  ```csharp
  using System;
  using System.Threading;
  using System.Runtime.ExceptionServices;
  using System.Threading.Tasks;
  using System.Windows;
  using System.Windows.Automation;
  using System.Windows.Threading;
  using System.Windows.Automation.Peers;
  using System.Windows.Controls;
  using System.Windows.Media;
  using WinRestoreKit;
  using WinRestoreKit.Wpf;
  using WinRestoreKit.Wpf.Services;
  using WinRestoreKit.Wpf.ViewModels;
  using WinRestoreKit.Wpf.Views;
  using Xunit;

  namespace WinRestoreKit.Tests
  {
      public sealed class WpfCutoverRuntimeTests
      {
          [Fact]
          public void MainWindow_ConstructsOnStaAndExposesThePrimaryAutomationSurface()
          {
              WpfTestHost.Run(() =>
              {
                  ShellViewModel shell = CreateShell();
                  MainWindow window = new MainWindow(shell);
                  window.Show();

                  try
                  {
                      Navigate(shell, window, new TimelineView(), "Timeline");
                      ListBox timeline = Require<ListBox>(window, "TimelineEventList");
                      Assert.Equal("Snapshots", AutomationProperties.GetName(timeline));
                      Assert.True(timeline.Focusable);
                      AssertControlType(timeline, AutomationControlType.List);

                      Navigate(shell, window, new ComparisonWorkspaceView(), "Compare");
                      ComparisonWorkspaceView comparison = Require<ComparisonWorkspaceView>(
                          window, "ComparisonWorkspace");
                      Assert.Equal("Snapshot comparison workspace",
                          AutomationProperties.GetName(comparison));
                      ListBox modules = Require<ListBox>(window, "CompareModuleList");
                      Assert.Equal("Modules in the selected snapshot",
                          AutomationProperties.GetName(modules));
                      AssertControlType(modules, AutomationControlType.List);
                      RadioButton all = Require<RadioButton>(window, "CompareFilterAll");
                      Assert.Equal("Show all compared modules", AutomationProperties.GetName(all));
                      Assert.True(all.Focusable);
                      RadioButton changed = Require<RadioButton>(window, "CompareFilterChanged");
                      Assert.Equal("Show only changed modules",
                          AutomationProperties.GetName(changed));
                      Assert.True(changed.Focusable);
                      Assert.True(Require<ListBox>(window, "RestoreSetList").Focusable);
                      Assert.True(Require<Button>(window,
                          "CompareContinueToConfirmButton").Focusable);

                      Navigate(shell, window, new ConfirmView(), "Confirm");
                      Button confirm = Require<Button>(window, "ConfirmRestoreButton");
                      Assert.Equal("Continue to final restore consent",
                          AutomationProperties.GetName(confirm));
                      Assert.True(confirm.Focusable);
                      AssertControlType(confirm, AutomationControlType.Button);

                      shell.ShowSettingsCommand.Execute(null);
                      window.UpdateLayout();
                      Assert.True(Require<RadioButton>(window,
                          "SettingsThemeFollowSystem").Focusable);
                      Assert.True(Require<RadioButton>(window, "SettingsThemeLight").Focusable);
                      Assert.True(Require<RadioButton>(window, "SettingsThemeDark").Focusable);

                      shell.ShowTimeline();
                      shell.CreateSnapshotCommand.Execute(null);
                      window.UpdateLayout();
                      Button createSnapshot = Require<Button>(window, "CreateSnapshotButton");
                      Assert.Equal("Create snapshot",
                          AutomationProperties.GetName(createSnapshot));
                      Assert.True(createSnapshot.Focusable);
                      AssertControlType(createSnapshot, AutomationControlType.Button);
                  }
                  finally
                  {
                      window.Close();
                  }
              });
          }

          private static ShellViewModel CreateShell()
          {
              WpfUpdatePresenter updates = new WpfUpdatePresenter(
                  new FakeUpdates(),
                  new FakeDialogs(),
                  new FakeLinks());
              return new ShellViewModel(new FakeThemeService(), updates, "0.0.1");
          }

          private static void Navigate(ShellViewModel shell, MainWindow window,
              FrameworkElement workspace, string workflowLabel)
          {
              shell.NavigateTo(workspace, workflowLabel);
              window.UpdateLayout();
          }

          private static T Require<T>(DependencyObject root, string automationId)
              where T : FrameworkElement
          {
              T found = Find<T>(root, automationId);
              Assert.NotNull(found);
              return found;
          }

          private static void AssertControlType(FrameworkElement element,
              AutomationControlType expected)
          {
              AutomationPeer peer = UIElementAutomationPeer.CreatePeerForElement(
                  (UIElement)element);
              Assert.NotNull(peer);
              Assert.Equal(expected, peer.GetAutomationControlType());
          }

          private static T Find<T>(DependencyObject root, string automationId)
              where T : FrameworkElement
          {
              if (root is T current
                  && AutomationProperties.GetAutomationId(current) == automationId)
                  return current;

              for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
              {
                  T found = Find<T>(VisualTreeHelper.GetChild(root, index), automationId);
                  if (found != null)
                      return found;
              }

              return null;
          }

          private sealed class FakeThemeService : IThemeService
          {
              public ThemeMode Mode { get; private set; } = ThemeMode.FollowSystem;
              public ThemeMode EffectiveMode { get; private set; } = ThemeMode.Light;
              public event EventHandler ThemeChanged;

              public void SetMode(ThemeMode mode)
              {
                  Mode = mode;
                  EffectiveMode = mode == ThemeMode.FollowSystem ? ThemeMode.Light : mode;
                  ThemeChanged?.Invoke(this, EventArgs.Empty);
              }

              public void Dispose() { }
          }

          private sealed class FakeUpdates : IUpdateCheckService
          {
              public Task<UpdateCheckResult> CheckAsync(string currentVersion,
                  CancellationToken cancellationToken)
                  => Task.FromResult(new UpdateCheckResult(
                      UpdateVerdict.UpToDate, currentVersion, currentVersion));
          }

          private sealed class FakeDialogs : IWpfDialogService
          {
              public void ShowInformation(string text, string caption) { }
              public void ShowWarning(string text, string caption) { }
              public void ShowError(string text, string caption) { }
              public bool Confirm(string text, string caption) => false;
          }

          private sealed class FakeLinks : IExternalLinkService
          {
              public void Open(string url) { }
          }
      }
  }
  ```
  The test uses the internal navigation seam only to host each concrete, production `UserControl` in the real `MainWindow`; it does not manufacture a replacement window or execute a workflow. Timeline, Compare, Confirm, backup, and dialog behavior remains covered by their upstream view-model tests and the Task 2 desktop session.


- [ ] **Step 2: Run the new test to verify it fails before all WPF automation surfaces exist**


  Run:

  ```powershell
  dotnet test src\WinRestoreKit.Tests\WinRestoreKit.Tests.csproj -c Debug --filter FullyQualifiedName~WpfCutoverRuntimeTests
  ```

  Expected: the test fails because one or more required Compare, Confirm, or Settings automation identifiers are absent. Do not accept a hand-built fake window as a substitute for the real `ShellViewModel` and `MainWindow`.

- [ ] **Step 3: Give the shared test assembly temporary dual-framework support while WinForms remains runnable**

  During Tasks 1–2, WinForms tests and WPF tests must both compile. Keep the existing WinForms reference and add WPF/Application support; do not remove the WinForms property or project reference until Task 3 deletes every test that depends on it.

  ```xml
  <PropertyGroup>
    <TargetFramework>net8.0-windows</TargetFramework>
    <UseWindowsForms>true</UseWindowsForms>
    <UseWPF>true</UseWPF>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
    <Nullable>disable</Nullable>
    <ImplicitUsings>disable</ImplicitUsings>
    <Platforms>AnyCPU</Platforms>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\WinRestoreKit\WinRestoreKit.csproj" />
    <ProjectReference Include="..\WinRestoreKit.Application\WinRestoreKit.Application.csproj" />
    <ProjectReference Include="..\WinRestoreKit.Wpf\WinRestoreKit.Wpf.csproj" />
  </ItemGroup>

  <ItemGroup>
    <None Include="..\WinRestoreKit\Properties\AssemblyInfo.cs" Link="TestData\AssemblyInfo.cs">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </None>
  </ItemGroup>
  ```

  This dual-reference state is migration-only. It keeps the tree green while real-desktop parity is being proven; it is removed atomically with the obsolete tests and project in Task 3.

- [ ] **Step 4: Keep `WpfTestHost` as the sole STA helper**

  `WpfTestHost` is the sole STA helper from Foundation through cutover. Update Timeline WPF smoke tests to call `WpfTestHost.Run(Action)`; do not create, delete, or reference a second STA helper.

  ```csharp
  using System;
  using System.Runtime.ExceptionServices;
  using System.Threading;
  using System.Windows.Threading;

  internal static class WpfTestHost
  {
      internal static T Run<T>(Func<T> action)
      {
          T result = default;
          Exception failure = null;
          Thread thread = new Thread(() =>
          {
              try
              {
                  result = action();
              }
              catch (Exception ex)
              {
                  failure = ex;
              }
              finally
              {
                  Dispatcher.CurrentDispatcher.InvokeShutdown();
              }
          });

          thread.Name = "WinRestoreKit WPF test STA";
          thread.IsBackground = true;
          thread.SetApartmentState(ApartmentState.STA);
          thread.Start();
          thread.Join();

          if (failure != null)
              ExceptionDispatchInfo.Capture(failure).Throw();
          return result;
      }
  }
  ```

  Retain the existing `Run(Action action)` overload as a thin wrapper around `Run<object>(() => { action(); return null; })`. Do not use `Application.Run`, create a second `Application`, or leave a Dispatcher alive after a test.

- [ ] **Step 5: Pin automation names, roles, focusability, and live status in the actual WPF views**

  Add the stable UIA surface to the existing controls; do not create hidden duplicate controls merely to satisfy a test. The values below are intentionally user-facing and testable.

  ```xml
  <!-- TimelineView.xaml -->
  <ListBox x:Name="TimelineEventList"
           Focusable="True"
           AutomationProperties.AutomationId="TimelineEventList"
           AutomationProperties.Name="Snapshots"
           AutomationProperties.HelpText="Use Left and Right Arrow to move through snapshots. Press Enter to compare a restorable snapshot." />

  <!-- ComparisonWorkspaceView.xaml: merge these attributes into the existing elements;
       the root and controls keep their existing parents and bindings. -->
  <UserControl AutomationProperties.AutomationId="ComparisonWorkspace"
               AutomationProperties.Name="Snapshot comparison workspace" />
  <ListBox x:Name="CompareModuleList"
           Focusable="True"
           AutomationProperties.AutomationId="CompareModuleList"
           AutomationProperties.Name="Modules in the selected snapshot" />
  <RadioButton x:Name="CompareFilterAll"
               AutomationProperties.AutomationId="CompareFilterAll"
               AutomationProperties.Name="Show all compared modules" />
  <RadioButton x:Name="CompareFilterChanged"
               AutomationProperties.AutomationId="CompareFilterChanged"
               AutomationProperties.Name="Show only changed modules" />
  <ListBox x:Name="RestoreSetList"
           AutomationProperties.AutomationId="RestoreSetList"
           AutomationProperties.Name="Modules selected for restore" />
  <Button x:Name="CompareContinueToConfirmButton"
          AutomationProperties.AutomationId="CompareContinueToConfirmButton" />

  <!-- ConfirmView.xaml -->
  <Button x:Name="ConfirmRestoreButton"
          AutomationProperties.AutomationId="ConfirmRestoreButton"
          AutomationProperties.Name="Continue to final restore consent" />

  <!-- SettingsView.xaml -->
  <RadioButton x:Name="SettingsThemeFollowSystem"
               AutomationProperties.AutomationId="SettingsThemeFollowSystem"
               Content="Follow system" />
  <RadioButton x:Name="SettingsThemeLight"
               AutomationProperties.AutomationId="SettingsThemeLight"
               Content="Light" />
  <RadioButton x:Name="SettingsThemeDark"
               AutomationProperties.AutomationId="SettingsThemeDark"
               Content="Dark" />
  ```

  For each event row, compose its automation name from title, timestamp, event state, and diagnostic reason when one exists. Mark the real inline error/status element with `AutomationProperties.LiveSetting="Polite"`; failed and unreadable entries remain disabled for restoration rather than disappearing.

- [ ] **Step 6: Run the focused STA, Timeline, and accessibility tests to verify they pass**

  Run:

  ```powershell
  dotnet test src\WinRestoreKit.Tests\WinRestoreKit.Tests.csproj -c Debug --filter "FullyQualifiedName~WpfCutoverRuntimeTests|FullyQualifiedName~TimelineWpfSmokeTests|FullyQualifiedName~TimelineAccessibilityTests"
  ```

  Expected: all selected tests pass, no test opens a visible window indefinitely, and output reports `Failed: 0`. At this point the WinForms tests remain present and runnable under the temporary dual-reference project.

- [ ] **Step 7: Commit the reusable WPF runtime-test surface**

  ```powershell
  git add src\WinRestoreKit.Tests\WpfTestHost.cs src\WinRestoreKit.Tests\WpfCutoverRuntimeTests.cs src\WinRestoreKit.Tests\TimelineWpfSmokeTests.cs src\WinRestoreKit.Tests\WinRestoreKit.Tests.csproj src\WinRestoreKit.Wpf\MainWindow.xaml src\WinRestoreKit.Wpf\Views\TimelineView.xaml src\WinRestoreKit.Wpf\Views\ComparisonWorkspaceView.xaml src\WinRestoreKit.Wpf\Views\ConfirmView.xaml src\WinRestoreKit.Wpf\Views\SettingsView.xaml
  git commit -m "test: add WPF cutover runtime coverage"
  ```
### Task 2: Execute and record the real Windows parity, accessibility, responsive, theme, and visual-baseline gate

**Files:**
- Create: `docs/superpowers/verification/2026-08-09-wpf-cutover.md`
- Create: `docs/superpowers/verification/2026-08-09-wpf-cutover/screenshots/timeline-normal-light-100.png`
- Create: `docs/superpowers/verification/2026-08-09-wpf-cutover/screenshots/timeline-normal-dark-100.png`
- Create: `docs/superpowers/verification/2026-08-09-wpf-cutover/screenshots/timeline-partial-light-100.png`
- Create: `docs/superpowers/verification/2026-08-09-wpf-cutover/screenshots/timeline-partial-dark-100.png`
- Create: `docs/superpowers/verification/2026-08-09-wpf-cutover/screenshots/timeline-failed-light-100.png`
- Create: `docs/superpowers/verification/2026-08-09-wpf-cutover/screenshots/timeline-failed-dark-100.png`
- Create: `docs/superpowers/verification/2026-08-09-wpf-cutover/screenshots/timeline-unreadable-light-100.png`
- Create: `docs/superpowers/verification/2026-08-09-wpf-cutover/screenshots/timeline-unreadable-dark-100.png`
- Create: `docs/superpowers/verification/2026-08-09-wpf-cutover/screenshots/compare-all-light-100.png`
- Create: `docs/superpowers/verification/2026-08-09-wpf-cutover/screenshots/compare-all-dark-100.png`
- Create: `docs/superpowers/verification/2026-08-09-wpf-cutover/screenshots/confirm-light-100.png`
- Create: `docs/superpowers/verification/2026-08-09-wpf-cutover/screenshots/confirm-dark-100.png`
- Create: `docs/superpowers/verification/2026-08-09-wpf-cutover/screenshots/timeline-normal-light-150.png`
- Create: `docs/superpowers/verification/2026-08-09-wpf-cutover/screenshots/compare-all-light-150.png`
- Create: `docs/superpowers/verification/2026-08-09-wpf-cutover/screenshots/confirm-light-150.png`
- Create: `docs/superpowers/verification/2026-08-09-wpf-cutover/screenshots/timeline-normal-light-200.png`
- Create: `docs/superpowers/verification/2026-08-09-wpf-cutover/screenshots/compare-all-light-200.png`
- Create: `docs/superpowers/verification/2026-08-09-wpf-cutover/screenshots/confirm-light-200.png`
- Create: `docs/superpowers/verification/2026-08-09-wpf-cutover/screenshots/compare-all-light-1024w.png`
- Modify: `src/WinRestoreKit.Wpf/Views/TimelineView.xaml`
- Modify: `src/WinRestoreKit.Wpf/Views/ComparisonWorkspaceView.xaml`
- Modify: `src/WinRestoreKit.Wpf/Views/ConfirmView.xaml`
- Modify: `src/WinRestoreKit.Wpf/Views/SettingsView.xaml`
- Test: `src/WinRestoreKit.Tests/WpfCutoverRuntimeTests.cs`
- Test: `src/WinRestoreKit.Tests/TimelineAccessibilityTests.cs`
- Test: `src/WinRestoreKit.Tests/ComparisonWorkspaceViewModelTests.cs`
- Test: `src/WinRestoreKit.Tests/ConfirmViewModelTests.cs`
- Test: `src/WinRestoreKit.Tests/BackupWorkspaceViewModelTests.cs`
- Test: `src/WinRestoreKit.Tests/ProgressWorkspaceViewModelTests.cs`
- Test: `src/WinRestoreKit.Tests/ResultWorkspaceViewModelTests.cs`

**Interfaces:**
- Consumes: one application-lifetime `SnapshotEventCatalog`, `TimelineViewModel.RefreshAsync()`, `SnapshotPayloadPreparationService.PrepareAsync(SnapshotEvent snapshot, CancellationToken cancellationToken)`, `SnapshotComparisonService.CompareAsync(SnapshotEvent snapshot, IReadOnlyList<BackupBase> modules, CancellationToken cancellationToken)`, comparison and restore-set view models, `WpfRunUi`, `WpfDialogService`, `WpfAppRestoreDialog`, `IThemeService`, and the Task 1 automation identifiers.
- Produces: a committed real-desktop verification record and deterministic screenshot baselines proving the WPF shell is usable before destructive source removal. It produces no new product workflow, snapshot format, comparison evidence, restore rule, or compatibility shell.

- [ ] **Step 1: Write the verification record before running the desktop session**

  Create `docs/superpowers/verification/2026-08-09-wpf-cutover.md` with these immutable headings and a results table under each heading: `Environment`, `Build under test`, `Snapshot fixtures`, `Workflow parity`, `Keyboard and UI Automation`, `Themes and reduced motion`, `DPI and responsive layout`, `Screenshot baselines`, `Publish smoke prerequisites`, and `Result`.

  Record all of the following before taking screenshots:

  | Legacy workflow that must be absent after cutover | WPF workflow that must be observed now | Required result |
  | --- | --- | --- |
  | `MainForm` rail and `HomePageView` dashboard | `MainWindow` top bar and default Timeline workspace | No persistent sidebar or dashboard metric-strip home is present. |
  | `RestoreWizardStep1View` | Timeline event selection and narrow list fallback | Verified and Partial entries enter Compare; Failed and Unreadable entries expose diagnostics only. |
  | `RestoreWizardStep2View` | Comparison workspace and whole-module restore set | All/Changed-only filtering retains Unavailable and Not captured evidence under All; selection never descends below a module. |
  | `RestoreConfirmForm` | Confirm workspace plus `RestoreConsentDialog` | Existing plan, snapshot gate, process/Explorer/sign-out impacts, and incomplete-snapshot consent are presented before a write. |
  | `BackupPageView` and `ScopeGroups` | Create snapshot and WPF backup selection | Existing preset, scope, containment, naming, and compression choices reach the unchanged Application orchestration. |
  | `ProgressPageView`, `ProgressLogSink`, `RichTextBoxLogSink` | WPF progress/result views and WPF dispatcher/log adapters | Progress, pause/cancel, logs, late-cancel wording, and final summary remain observable. |
  | `HistoryPageView` | Timeline plus Advanced History | Both projections show the same events and source paths. |
  | `AboutPageView`, `Theme`, `UpdateCheck` | WPF About, settings, theme and update adapters | Light, Dark, Follow system, version display, update status, and safe external link behavior are present. |
  | `RestAppsForm` | `WpfAppRestoreDialog` | Existing app-export source and outcome behavior is reachable from a WPF-owned dialog. |

- [ ] **Step 2: Build the side-by-side WPF application and run its focused deterministic tests**

  Run:

  ```powershell
  dotnet build src\WinRestoreKit.sln -c Debug
  dotnet test src\WinRestoreKit.sln -c Debug --no-build --filter "FullyQualifiedName~Timeline|FullyQualifiedName~SnapshotComparison|FullyQualifiedName~ComparisonWorkspace|FullyQualifiedName~RestoreSet|FullyQualifiedName~Confirm|FullyQualifiedName~BackupSelection|FullyQualifiedName~Progress|FullyQualifiedName~Wpf"
  ```

  Expected: the solution builds, all selected tests pass with `Failed: 0`, and both the old WinForms app and temporary WPF app remain buildable at this point. Record the SDK version, Windows build, monitor resolution, current DPI scale, and test summary in the verification record.

- [ ] **Step 3: Run the WPF workflow smoke on a real Windows desktop before any WinForms deletion**

  Launch the temporary WPF shell from an elevated Windows test account or a disposable Windows VM:

  ```powershell
  dotnet run --project src\WinRestoreKit.Wpf\WinRestoreKit.Wpf.csproj -c Debug
  ```

  Use a disposable backup root and reversible test data. Do not initiate a real registry restore on a personal workstation. When a restore write is required for the smoke, use the disposable VM, select one reversible whole module from a snapshot created in that VM, and obtain the VM operator's confirmation immediately before clicking **Start restore**.

  Record each result in the verification document:

  | Scenario | Exact interaction | Expected observable result |
  | --- | --- | --- |
  | Startup | Start the command above and wait for the window to settle. | The WPF `MainWindow` opens on Timeline with compact top bar, wordmark, Settings, and Create snapshot; no WinForms rail, dashboard home, ownerless dialog, or startup exception appears. |
  | Timeline source states | Load one verified snapshot, one partial snapshot, a catalog session failure created through `RecordSessionFailure`, and one malformed/unreadable fixture. | All four states show honest labels and diagnostics. Only Verified/Partial can enter Compare; Failed/Unreadable have no restore action. |
  | Timeline selection | Select a verified compressed snapshot, enter Compare, return, then select another snapshot. | Preparation is read-only; changing source clears a non-empty restore set only after its explicit confirmation; temporary payload scope is released when leaving Compare. |
  | Compare | Let comparison load, use All and Changed-only filters, select a row, open its detail tray, cancel an in-flight comparison, and load it again. | Rows remain catalog-ordered; Changed, Same, Unavailable, and Not captured are distinguishable with text/icons; a one-module error does not erase other rows; cancel is awaited and leaves no leaked temporary payload. |
  | Confirm | Add restorable modules, enter Confirm, inspect impacts, cancel, then repeat in the disposable VM and accept the existing consent/snapshot gates. | Selection is whole-module; exact existing process closures, Explorer restart, and sign-out impacts are shown; no reboot impact is invented; cancel writes nothing; accepted execution follows existing `RestorePlan`, `SnapshotGate`, `RestoreScope`, and `RestoreDispatch`. |
  | Create snapshot | Choose an existing scope/preset and compression option, start one disposable backup, observe progress, then return to Timeline. | Run admission, progress, logs, cancellation controls, compression, manifest/log output, and late-cancel wording match Application contracts. A verified completion becomes selectable; untrusted/failed outcomes are truthful session diagnostics only. |
  | App restore | Enter the app-restore module from the disposable snapshot and close its WPF dialog without installing software. | The WPF-owned dialog is modal to the main window and its source/list/error text matches the Application service; no WinForms form opens. |
  | Advanced destinations | Open Advanced History, Settings, and About, then return to Timeline. | Advanced History is the same event source; theme and version settings persist correctly; About update/link flows are WPF-owned. |

- [ ] **Step 4: Verify keyboard-only and UI Automation behavior on the live desktop**

  Start the Windows SDK inspection utility while the WPF shell is visible:

  ```powershell
  $inspect = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin" -Recurse -Filter Inspect.exe | Select-Object -First 1 -ExpandProperty FullName
  if ([string]::IsNullOrWhiteSpace($inspect)) { throw "Install the Windows SDK Inspect.exe tool before recording UI Automation evidence." }
  & $inspect
  ```

  Then execute and record this matrix. For each row record the UIA Name, ControlType, IsEnabled state, and keyboard result observed with Inspect.exe.

  | Keyboard path | Expected result |
  | --- | --- |
  | Tab from the top bar through Create snapshot, Timeline, filters, row actions, restore-set actions, Confirm, Settings, and About | Visible focus never disappears, no focus trap occurs, and labels describe each action. |
  | Left/Right while `TimelineEventList` has focus | Moves among timeline events without a mouse. |
  | Enter on Verified or Partial event | Selects the event and opens Compare. |
  | Enter on Failed or Unreadable event | Opens diagnostic detail only; no restore set or restore command becomes available. |
  | Tab/Shift+Tab and Space/Enter in Compare and Confirm | Reaches filters, row selection, detail tray, add/remove actions, cancel, and confirm in a predictable order. |
  | Narrow Timeline list fallback | Exposes the same title, timestamp, state, diagnostic, enabled/disabled status, and selection action as the visual timeline. |
  | WPF dialog open | Dialog has the main WPF window as owner, remains in the foreground, and its controls are independently discoverable by UIA. |

  Use the Task 1 automation identifiers as the stable evidence points. For a row with an error, verify the live status is announced politely rather than converted to “nothing found.”

- [ ] **Step 5: Verify Light, Dark, Follow system, contrast, and reduced-motion behavior**

  In the WPF Settings view, select **Light**, **Dark**, and **Follow system** in separate runs. For Follow system, change the Windows app color preference and return focus to the application. Test reduced motion on the disposable test profile only:

  ```powershell
  Start-Process "ms-settings:easeofaccess-display"
  ```

  Capture the previous animation setting, turn off **Animation effects**, restart the WPF app, select a Timeline event, filter Compare, and open/close the detail tray. Restore the prior Windows setting after the test.

  Expected:

  - Light and Dark use neutral Windows surfaces, mineral-blue primary action, restrained coral warnings, Segoe UI Variable for normal UI text, and the packaged monospace face only for logs/technical values.
  - Status is never encoded only by color; focus, selected, warning, unavailable, and changed states remain distinguishable in grayscale.
  - Inspect foreground/background pairs with a contrast checker and record a contrast ratio of at least `4.5:1` for every normal-text token in Light and Dark.
  - Follow system changes only while Follow system is selected; explicit Light and Dark do not react to the OS setting.
  - With Animation effects disabled, nonessential selection/filter/tray motion is absent and state changes remain immediate and usable.

- [ ] **Step 6: Verify all required DPI scales and the minimum-width layout on the live desktop**

  Open Windows Display Settings on the disposable test profile:

  ```powershell
  Start-Process "ms-settings:display"
  ```

  Restart the WPF app at 100%, 125%, 150%, 175%, and 200% scaling. At each scale inspect Timeline, Compare, Confirm, backup selection, progress, result, settings, About, and a dialog. At 100%, resize the window to its 1024 px minimum usable width and test the Compare workspace.

  Expected:

  - No clipped labels, overlap, unusable hit targets, hidden keyboard focus, or horizontal content loss occurs at any required scale.
  - The window cannot be reduced below its usable 1024 px minimum.
  - At the minimum width, comparison evidence and restore-set panes stack vertically; both remain reachable through keyboard and UIA.
  - At wider widths the normal two-pane comparison presentation returns without duplicate content or a stale selection.

- [ ] **Step 7: Capture, compare, and commit deterministic screenshots**

  Use the same test account, 100% scaling unless the filename names another scale, a fixed application window size, the same disposable fixture root, and no unrelated windows. Capture the exact filenames listed in **Files** for this task. For each baseline, record theme, DPI, logical window size, fixture state, and SHA-256 in the verification document.

  Compare every new capture side by side with its committed predecessor when a predecessor exists. A change is accepted only when it matches the approved Timeline + Compare visual direction: no permanent sidebar, generic dashboard card grid, giant failure headline, glow, purple gradient, decorative chart, or color-only state. Record `new baseline` for the first approved set and `matched` or a concise visual difference for later runs.

  Expected screenshot coverage:

  - Light and Dark Timeline screenshots cover normal, Partial, Failed, and Unreadable source states.
  - Light and Dark Compare screenshots show All modules, including Changed, Same, Unavailable, and Not captured evidence.
  - Light and Dark Confirm screenshots show grouped existing restore impacts and the explicit start action.
  - Timeline, Compare, and Confirm each have 100%, 150%, and 200% baseline coverage.
  - `compare-all-light-1024w.png` demonstrates the required stacked narrow layout.

  Before setting the gate result, verify the baseline directory has exactly the required files and emit their hashes for the record:

  ```powershell
  $baselineRoot = 'docs\superpowers\verification\2026-08-09-wpf-cutover\screenshots'
  $expectedBaselineNames = @(
    'timeline-normal-light-100.png', 'timeline-normal-dark-100.png',
    'timeline-partial-light-100.png', 'timeline-partial-dark-100.png',
    'timeline-failed-light-100.png', 'timeline-failed-dark-100.png',
    'timeline-unreadable-light-100.png', 'timeline-unreadable-dark-100.png',
    'compare-all-light-100.png', 'compare-all-dark-100.png',
    'confirm-light-100.png', 'confirm-dark-100.png',
    'timeline-normal-light-150.png', 'compare-all-light-150.png',
    'confirm-light-150.png', 'timeline-normal-light-200.png',
    'compare-all-light-200.png', 'confirm-light-200.png',
    'compare-all-light-1024w.png'
  ) | Sort-Object
  $actualBaselineNames = @(Get-ChildItem $baselineRoot -File -Filter '*.png' |
      Select-Object -ExpandProperty Name | Sort-Object)
  $baselineDelta = Compare-Object $expectedBaselineNames $actualBaselineNames
  if ($baselineDelta) {
      throw "Screenshot baseline set differs from the required 19 files:`n$($baselineDelta | Out-String)"
  }
  Get-ChildItem $baselineRoot -File -Filter '*.png' |
      Sort-Object Name | Get-FileHash -Algorithm SHA256
  ```

  Expected: the command prints exactly 19 named SHA-256 rows and throws if any capture is absent, extra, or misspelled.

- [ ] **Step 8: Make the deletion decision explicit**

  In the verification record, set `Result: PASS — real Windows desktop gate complete` only when every Task 2 row passed, every listed screenshot exists, and the desktop exercise observed an actual WPF startup, Timeline, Compare, Confirm, backup/progress/results, dialogs, theme modes, keyboard/UIA, reduced motion, DPI, and minimum-width layout.

  If any item fails, set `Result: BLOCKED —` followed by the exact scenario name and observed failure, repair the owning WPF view/view-model/service, then repeat the affected focused test and live desktop row. Do not begin Task 3 or remove a WinForms file while the result is blocked.

- [ ] **Step 9: Commit the real-desktop evidence and baselines**

  ```powershell
  git add docs\superpowers\verification\2026-08-09-wpf-cutover.md docs\superpowers\verification\2026-08-09-wpf-cutover\screenshots src\WinRestoreKit.Wpf\Views\TimelineView.xaml src\WinRestoreKit.Wpf\Views\ComparisonWorkspaceView.xaml src\WinRestoreKit.Wpf\Views\ConfirmView.xaml src\WinRestoreKit.Wpf\Views\SettingsView.xaml
  git commit -m "docs: record WPF desktop verification baselines"
  ```

### Task 3: Atomically transfer the `WinRestoreKit` shipping identity to WPF and remove every obsolete WinForms artifact

**Files:**
- Modify: `src/WinRestoreKit.Wpf/WinRestoreKit.Wpf.csproj`
- Move: `src/WinRestoreKit/app.manifest` to `src/WinRestoreKit.Wpf/app.manifest`
- Move: `src/WinRestoreKit/WinRestoreKit.ico` to `src/WinRestoreKit.Wpf/WinRestoreKit.ico`
- Move: `src/WinRestoreKit/Fonts/IBMPlexMono-Regular.ttf` to `src/WinRestoreKit.Wpf/Assets/Fonts/IBMPlexMono-Regular.ttf`
- Move: `src/WinRestoreKit/Fonts/IBMPlexMono-Medium.ttf` to `src/WinRestoreKit.Wpf/Assets/Fonts/IBMPlexMono-Medium.ttf`
- Delete: `src/WinRestoreKit.Wpf/Properties/AssemblyInfo.cs`
- Modify: `src/WinRestoreKit.Core/Conf/AppStoreApps.cs`
- Modify: `src/WinRestoreKit/Properties/AssemblyInfo.cs`
- Modify: `src/WinRestoreKit.Tests/WinRestoreKit.Tests.csproj`
- Create: `src/WinRestoreKit.Tests/ShippingIdentityTests.cs`
- Modify: `src/WinRestoreKit.Tests/VersionParsingTests.cs`
- Modify: `src/WinRestoreKit.Tests/RebrandIdentityTests.cs`
- Modify: `src/WinRestoreKit.Tests/RestoreDeclarationTests.cs`
- Modify: `src/WinRestoreKit.Tests/RunSummaryTests.cs`
- Modify: `src/WinRestoreKit.Tests/AssemblyInfo.cs`
- Modify: `src/WinRestoreKit.Tests/UpdateCheckVerdictTests.cs`
- Modify: `src/WinRestoreKit.Tests/OsVersionTests.cs`
- Modify: `src/WinRestoreKit.Tests/AppRestoreDialogTests.cs`
- Modify: `src/WinRestoreKit.Tests/BackupFoldersReadTests.cs`
- Modify: `src/WinRestoreKit.Tests/LogHelperTests.cs`
- Modify: `src/WinRestoreKit.Tests/ScopeGroupsPrivacyTests.cs`
- Create: `src/WinRestoreKit.Tests/AppRestoreOwnerTests.cs` (rewrite/rename of `RestoreDialogOwnerTests.cs`)
- Modify: `src/WinRestoreKit.Tests/ArchiveProgressTests.cs`
- Modify: `src/WinRestoreKit.Tests/BackupDestinationContainmentTests.cs`
- Modify: `src/WinRestoreKit.Tests/BackupDestinationLifecycleTests.cs`
- Modify: `src/WinRestoreKit.Tests/BackupPresetsTests.cs`
- Modify: `src/WinRestoreKit.Tests/LockedPayloadBackupTests.cs`
- Modify: `src/WinRestoreKit.Tests/ModuleShapeTests.cs`
- Modify: `src/WinRestoreKit.Tests/RestoreConsentCancellationTests.cs`
- Modify: `src/WinRestoreKit.Tests/SnapshotFolderPathTests.cs`
- Modify: `src/WinRestoreKit.Tests/ViewDataHelperTests.cs`
- Modify: `src/WinRestoreKit.Tests/SnapshotComparisonServiceTests.cs`
- Modify: `src/WinRestoreKit.sln`
- Delete: `src/WinRestoreKit/WinRestoreKit.csproj`
- Delete: `src/WinRestoreKit/Program.cs`
- Delete: `src/WinRestoreKit/MainForm.cs`
- Delete: `src/WinRestoreKit/MainForm.Designer.cs`
- Delete: `src/WinRestoreKit/MainForm.resx`
- Delete: `src/WinRestoreKit/GitHub.cs`
- Delete: `src/WinRestoreKit/GitHubIcon.png`
- Delete: `src/WinRestoreKit/Views/`
- Delete: `src/WinRestoreKit/Forms/`
- Delete: `src/WinRestoreKit/Controls/`
- Delete: `src/WinRestoreKit/Helpers/`
- Delete: `src/WinRestoreKit/Orchestration/`
- Delete: `src/WinRestoreKit/Results/`
- Delete: `src/WinRestoreKit/Fonts/Barlow-Regular.otf`
- Delete: `src/WinRestoreKit/Fonts/Barlow-Medium.otf`
- Delete: `src/WinRestoreKit/Fonts/Barlow-SemiBold.otf`
- Delete: `src/WinRestoreKit/Fonts/BarlowCondensed-Regular.otf`
- Delete: `src/WinRestoreKit/Fonts/BarlowCondensed-SemiBold.otf`
- Delete: `src/WinRestoreKit/Fonts/BarlowCondensed-Bold.otf`
- Delete: `src/WinRestoreKit/Properties/Resources.resx`
- Delete: `src/WinRestoreKit/Properties/Resources.Designer.cs`
- Delete: `src/WinRestoreKit.Tests/BackupPageViewTests.cs`
- Delete: `src/WinRestoreKit.Tests/HomePageViewBaselineTests.cs`
- Delete: `src/WinRestoreKit.Tests/HomePageViewDriftTests.cs`
- Delete: `src/WinRestoreKit.Tests/MainFormRunAdmissionTests.cs`
- Delete: `src/WinRestoreKit.Tests/NavigationServiceTests.cs`
- Delete: `src/WinRestoreKit.Tests/ProgressPageViewTests.cs`
- Delete: `src/WinRestoreKit.Tests/HistoryPageViewTests.cs`
- Delete: `src/WinRestoreKit.Tests/RestoreDialogOwnerTests.cs`
- Delete: `src/WinRestoreKit.Tests/ShellLayoutTests.cs`
- Delete: `src/WinRestoreKit.Tests/FontLoaderTests.cs`
- Delete: `src/WinRestoreKit.Tests/CompressedDriftPayloadTests.cs`
- Test: `src/WinRestoreKit.Tests/ShippingIdentityTests.cs`
- Test: `src/WinRestoreKit.Tests/SnapshotComparisonServiceTests.cs`
- Test: `src/WinRestoreKit.Tests/AdvancedHistoryViewModelTests.cs`
- Test: `src/WinRestoreKit.Tests/BackupWorkspaceViewModelTests.cs`
- Test: `src/WinRestoreKit.Tests/ProgressWorkspaceViewModelTests.cs`
- Test: `src/WinRestoreKit.Tests/ResultWorkspaceViewModelTests.cs`
- Test: `src/WinRestoreKit.Tests/RestoreConsentDialogTests.cs`
- Test: `src/WinRestoreKit.Tests/AppRestoreDialogTests.cs`
- Test: `src/WinRestoreKit.Tests/AppRestoreOwnerTests.cs`

**Interfaces:**
- Consumes: Application implementations of orchestration, run state, run summary, backup root, scope groups, presets, folders, update checking, and app-restore parsing; WPF adapters and view models from the four upstream plans; Core's existing `BackupBase`, payload, manifest, restore, and logging contracts; the Task 2 desktop gate.
- Produces: the only `WinRestoreKit` executable/assembly is WPF; the solution contains `Core`, `Application`, `Wpf`, and `Tests`; the physical raw version source survives at its exact old path; no old WinForms project, source, resource, helper, control, form, test, or compatibility path remains.

- [ ] **Step 1: Re-run the real-desktop deletion gate and stop on missing evidence**

  Before writing an identity test or issuing any removal command, read `docs/superpowers/verification/2026-08-09-wpf-cutover.md` and verify that `Result` is exactly `PASS — real Windows desktop gate complete` and every screenshot listed in Task 2 exists.

  Run:

  ```powershell
  Test-Path docs\superpowers\verification\2026-08-09-wpf-cutover.md
  Get-ChildItem docs\superpowers\verification\2026-08-09-wpf-cutover\screenshots -File | Measure-Object
  ```

  Expected: the report exists and the screenshot file count is `19`. If either condition is false, stop. WinForms deletion is not authorized until the real desktop evidence is complete.

- [ ] **Step 2: Write and observe the failing final-identity regression test while temporary side-by-side references still compile**

  Add `ShippingIdentityTests.cs`. During this one red test cycle, WPF still has its temporary assembly name and the test project still has both application references, so the test can compile and fail only on the intended shipping-identity assertion.

  ```csharp
  using System.IO;
  using System.Linq;
  using System.Reflection;
  using System.Runtime.Versioning;
  using DataHelper;
  using WinRestoreKit.Wpf;
  using Xunit;

  namespace WinRestoreKit.Tests
  {
      public sealed class ShippingIdentityTests
      {
          [Fact]
          public void ShippingWpfAssembly_UsesTheRawFallbackVersionSource()
          {
              string source = File.ReadAllText(Path.Combine(
                  AppContext.BaseDirectory, "TestData", "AssemblyInfo.cs"));
              Assembly assembly = typeof(App).Assembly;
              string compiled = assembly.GetCustomAttribute<AssemblyFileVersionAttribute>().Version;

              Assert.Equal("WinRestoreKit", assembly.GetName().Name);
              Assert.Equal(Data.ParseLatestVersion(source), compiled);
              Assert.Equal(3, compiled.Split('.').Length);
          }

          [Fact]
          public void ShippingWpfAssembly_HasNoWinFormsAssemblyReference()
          {
              string[] references = typeof(App).Assembly
                  .GetReferencedAssemblies()
                  .Select(reference => reference.Name)
                  .ToArray();

              Assert.DoesNotContain("System.Windows.Forms", references);
              Assert.Contains(typeof(App).Assembly.GetCustomAttributes<SupportedOSPlatformAttribute>(),
                  attribute => attribute.PlatformName == "windows7.0");
          }
      }
  }
  ```

- [ ] **Step 3: Run the identity test to verify it fails for the temporary WPF identity**

  Run:

  ```powershell
  dotnet test src\WinRestoreKit.Tests\WinRestoreKit.Tests.csproj -c Debug --filter FullyQualifiedName~ShippingIdentityTests
  ```

  Expected: `ShippingWpfAssembly_UsesTheRawFallbackVersionSource` fails because the temporary WPF assembly name is `WinRestoreKit.Wpf`. Do not commit this red intermediate state.

- [ ] **Step 4: Preserve every behavioral test before removing its WinForms host**

  Verify this ownership map and make the stated test migration before deleting the old source:

  | Legacy path or test | Required surviving owner and assertion | Cutover action |
  | --- | --- | --- |
  | `Orchestration/BackupRestoreOrchestrator.cs`, `RunControl.cs`, `RunCoordinator.cs`, `IRunUi.cs`, `Results/RunSummary.cs` | `src/WinRestoreKit.Application/`; `RunCoordinatorTests`, `RunControlTests`, `RunSummaryTests`, backup/restore lifecycle tests compile without WinForms imports. | Remove old files after tests target Application. |
  | `Helpers/BackupRootRegistry.cs` | `Application/Settings/BackupRootRegistry.cs`; registry format and destination tests retain exact behavior. | Remove old file. |
  | `Helpers/UpdateCheck.cs` | `Application/Updates/VersionInfo.cs`, `UpdateVerdict.cs`, `UpdateCheckService.cs`, plus WPF presenters. | Retarget version tests; keep raw URL/path invariant. |
  | `Program` version/update helpers and their tests | Application `VersionInfo`, `UpdateVerdict`, and `UpdateCheckService`; final `App` assembly. | Retarget `VersionParsingTests`, `UpdateCheckVerdictTests`, `RebrandIdentityTests`, `RestoreDeclarationTests`, and `OsVersionTests` from `Program`/`MainForm`; preserve unknown-version, normalization, raw-fallback, startup-failure wording, and product-identity assertions. Remove every stale WinForms/startup comment. |
  | `Helpers/RichTextBoxLogSink.cs`, `LogHelperTargetExtensions`, and `LogHelperTests.cs` | Core `ILogSink` and `LogHelper.SetSink(ILogSink)` with WPF `WpfLogSink`. | Replace the `RichTextBox` construction with a recording `ILogSink` fake; retain braces, format, clear, and no-sink behavior, then remove every `SetTarget` call and extension. |
  | `Views/ScopeGroups.cs`, `BackupPresets.cs`, `BackupFolders.cs`, `WatchedGroups.cs` | Application data contracts plus WPF backup/history view models. | Retarget `BackupPresetsTests`, `BackupFoldersReadTests`, `ScopeGroupsPrivacyTests`, and `ViewDataHelperTests`; retain exact membership literals, privacy-safe exclusions, and folder behavior. |
  | `Forms/RestAppsForm.cs` and its former parser/list/install helpers | `Application/AppRestore/AppRestoreService.cs` and `WpfAppRestoreDialog`. | Rewrite `AppRestoreDialogTests.cs` to use `AppRestoreService.BuildSources`, `ReadFromSource`, `ComposeListState`, `RouteProblem`, and `InstallAsync`; preserve exact export-source precedence, parser states, package IDs, winget outcome, and failure wording without a `Views` or WinForms import. |
  | `HomePageView` drift checks and restore wizard artifact discovery | `SnapshotComparisonService`, `SnapshotPayloadPreparationService`, Timeline/Compare view models. | Move the old drift and compressed-payload assertions into `SnapshotComparisonServiceTests.cs` and `SnapshotPayloadPreparationServiceTests.cs`; no reflection over `HomePageView` remains. |

  Write the `AppRestoreDialogTests.cs` red assertions against the Application service before removing the Form helpers, then make the test green only after the helper migration:

  ```csharp
  AppExport export = AppRestoreService.ReadFromSource(sourcePath);

  Assert.Equal(AppExportState.Ok, export.State);
  Assert.Equal(new[] { "Microsoft.PowerToys" }, export.PackageIdentifiers);
  ```

  `ReadFromSource` must use `AppStoreApps.ExportPathIn` and dispose its private payload `ReadScope`; do not recreate JSON parsing in WPF or in the test.

  Rewrite `LogHelperTests.cs` before deleting `Helpers/RichTextBoxLogSink.cs`:

  ```csharp
  private sealed class RecordingLogSink : ILogSink
  {
      internal List<string> Entries { get; } = new List<string>();

      public void Append(string text) => Entries.Add(text);
      public void Clear() => Entries.Clear();
  }

  [Fact]
  public void LogMessage_UnmatchedBrace_DoesNotThrowAndStillLogs()
  {
      RecordingLogSink sink = new RecordingLogSink();
      LogHelper.Instance.SetSink(sink);
      try
      {
          LogHelper.Instance.LogMessage("failed on {0 unbalanced");
          Assert.Contains("unbalanced", sink.Entries[0]);
      }
      finally
      {
          LogHelper.Instance.SetSink(null);
      }
  }
  ```

  Preserve the existing `Log`, `LogMessage`, `ClearLog`, and no-sink assertions; no test constructs a UI control after this conversion.

  Update the test doubles in `ArchiveProgressTests.cs`, `BackupDestinationContainmentTests.cs`, `BackupDestinationLifecycleTests.cs`, `LockedPayloadBackupTests.cs`, `RestoreConsentCancellationTests.cs`, and `SnapshotFolderPathTests.cs` to remove `using System.Windows.Forms;` and a typed `IWin32Window Owner`. They implement `IRunUi.DialogOwner` with an opaque sentinel; tests for app restore live in `AppRestoreOwnerTests.cs`, not generic run doubles.

  ```csharp
  private sealed class TestRunUi : IRunUi
  {
      private static readonly object DialogOwnerSentinel = new object();

      internal List<string> ProgressTexts { get; } = new List<string>();

      public object DialogOwner => DialogOwnerSentinel;
      public void SetProgressText(string text) => ProgressTexts.Add(text ?? string.Empty);
      public void SetProgressPercent(int percent) { }
      public void SetProgressDetail(string groupInfo, string elapsed, string remaining,
          string throughput, long bytesWritten, int errors, int warnings) { }
      public void ShowSummary(RunSummary summary, string caption,
          IReadOnlyList<ModuleOutcome> outcomes) { }
      public IReadOnlyList<string> ShowConsentDialog(RestorePlan plan) => Array.Empty<string>();
      public bool ConfirmSnapshotOverride(string text, string caption) => false;
      public void ShowPlanCompositionError(string text, string caption) { }
      public void SetExplorerRestartVisible(bool visible) { }
  }
  ```

  Preserve the Core API and outcome semantics in `src/WinRestoreKit.Core/Conf/AppStoreApps.cs`: `RestoreAsync(string path, object owner)`, `Restore(string path, object owner)`, and `Action<string, object> RestoreDialog` remain unchanged. Rewrite only stale `RestAppsForm`, WinForms, `IWin32Window`, and `Program.Main` XML remarks so they describe `WpfAppRestoreDialog`, an opaque shell-owned STA `Window`, and `App.OnStartup`; do not change visibility, failure messages, skipped result, or callback invocation. `AppRestoreOwnerTests.cs` must prove all three paths:

  ```csharp
  using Conf;
  using Xunit;

  [Fact]
  public async Task RestoreAsync_ForwardsTheOpaqueDialogOwner()
  {
      object expectedOwner = new object();
      object actualOwner = null;
      AppStoreApps.RestoreDialog = (_, owner) => actualOwner = owner;

      ModuleResult result = await new AppStoreApps().RestoreAsync("apps.json", expectedOwner);

      Assert.Same(expectedOwner, actualOwner);
      Assert.Contains(result.Steps, step => step.State == ResultState.Skipped);
  }
  ```

  Keep distinct facts named `Restore_WithNoDialogRegistered_FailsRatherThanClaimingSkipped`, `Restore_WithADialogButNoOwner_FailsRatherThanClaimingSkipped`, `Restore_WithADialogRegistered_OpensItForTheSelectedSourceAndReportsSkipped`, and `RestoreAsync_RunsTheDialogOnTheCallersThread`; all use an `object` sentinel rather than a WinForms type. Move the old static `DialogHook` into this renamed file and use it to restore the prior delegate after every test.

  Reset the mutable static delegate in `finally`/`IDisposable` cleanup. Keep the module's existing backup/result behavior unchanged: the moved Application orchestrator invokes exactly `await appStoreApps.RestoreAsync(currentRestorePath, ui.DialogOwner)`, and the WPF composition root supplies the current shell `Window` as that opaque owner.

- [ ] **Step 5: Perform the identity transfer, test-project migration, solution removal, asset moves, and source deletion as one atomic green change**

  Do not create two app assemblies named `WinRestoreKit` in a green tree. First remove the old project from the solution, then make the WPF assembly the shipping identity while removing the old app source and all tests that require it.

  ```powershell
  dotnet sln src\WinRestoreKit.sln remove src\WinRestoreKit\WinRestoreKit.csproj
  New-Item -ItemType Directory -Force src\WinRestoreKit.Wpf\Assets\Fonts | Out-Null
  git mv src\WinRestoreKit\app.manifest src\WinRestoreKit.Wpf\app.manifest
  git mv src\WinRestoreKit\WinRestoreKit.ico src\WinRestoreKit.Wpf\WinRestoreKit.ico
  git mv src\WinRestoreKit\Fonts\IBMPlexMono-Regular.ttf src\WinRestoreKit.Wpf\Assets\Fonts\IBMPlexMono-Regular.ttf
  git mv src\WinRestoreKit\Fonts\IBMPlexMono-Medium.ttf src\WinRestoreKit.Wpf\Assets\Fonts\IBMPlexMono-Medium.ttf
  ```

  Change `src/WinRestoreKit.Wpf/WinRestoreKit.Wpf.csproj` exactly as follows. This is the only app project with `GenerateAssemblyInfo=false`.

  ```xml
  <Project Sdk="Microsoft.NET.Sdk">
    <PropertyGroup>
      <OutputType>WinExe</OutputType>
      <TargetFramework>net8.0-windows</TargetFramework>
      <UseWPF>true</UseWPF>
      <RootNamespace>WinRestoreKit.Wpf</RootNamespace>
      <AssemblyName>WinRestoreKit</AssemblyName>
      <ApplicationManifest>app.manifest</ApplicationManifest>
      <ApplicationIcon>WinRestoreKit.ico</ApplicationIcon>
      <GenerateAssemblyInfo>false</GenerateAssemblyInfo>
      <Nullable>disable</Nullable>
      <ImplicitUsings>disable</ImplicitUsings>
      <Platforms>AnyCPU</Platforms>
      <Deterministic>true</Deterministic>
    </PropertyGroup>

    <ItemGroup>
      <Compile Include="..\WinRestoreKit\Properties\AssemblyInfo.cs"
               Link="Properties\AssemblyInfo.cs" />
      <Resource Include="Assets\Fonts\IBMPlexMono-Regular.ttf" />
      <Resource Include="Assets\Fonts\IBMPlexMono-Medium.ttf" />
    </ItemGroup>

    <ItemGroup>
      <ProjectReference Include="..\WinRestoreKit.Application\WinRestoreKit.Application.csproj" />
      <ProjectReference Include="..\WinRestoreKit.Core\WinRestoreKit.Core.csproj" />
    </ItemGroup>
  </Project>
  ```

  Do not set `Version`, `AssemblyVersion`, `FileVersion`, `InformationalVersion`, `PublishTrimmed`, or `UseWindowsForms` in this project. Delete `src/WinRestoreKit.Wpf/Properties/AssemblyInfo.cs`, because the linked physical file becomes the one source of WPF assembly attributes and test friendship.

  Retain these manifest elements exactly after the move; remove only WinForms-specific explanatory comments:

  ```xml
  <requestedExecutionLevel level="highestAvailable" uiAccess="false" />
  <longPathAware xmlns="http://schemas.microsoft.com/SMI/2016/WindowsSettings">true</longPathAware>
  ```

  Do not add a `dpiAware` element. WPF handles DPI without `Application.SetHighDpiMode`.

  In `WinRestoreKit.Tests.csproj`, remove the old WinForms project reference and replace the temporary dual-framework settings with final WPF-only support:

  ```xml
  <PropertyGroup>
    <TargetFramework>net8.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
    <Nullable>disable</Nullable>
    <ImplicitUsings>disable</ImplicitUsings>
    <Platforms>AnyCPU</Platforms>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\WinRestoreKit.Application\WinRestoreKit.Application.csproj" />
    <ProjectReference Include="..\WinRestoreKit.Wpf\WinRestoreKit.Wpf.csproj" />
  </ItemGroup>
  ```

  Keep the existing `None Include="..\WinRestoreKit\Properties\AssemblyInfo.cs"` test-data link unchanged.

  Update the comments in the physical `src/WinRestoreKit/Properties/AssemblyInfo.cs` from WinForms to WPF but keep the physical path, attribute order, and exact version source format unchanged:

  ```csharp
  [assembly: SupportedOSPlatform("windows7.0")]
  [assembly: AssemblyTitle("WinRestoreKit")]
  [assembly: AssemblyVersion("0.0.1")]
  [assembly: AssemblyFileVersion("0.0.1")]
  [assembly: InternalsVisibleTo("WinRestoreKit.Tests")]
  ```

  Keep `Data.Uri.URL_ASSEMBLY` exactly `https://raw.githubusercontent.com/nicolasestrem/WinRestoreKit/main/src/WinRestoreKit/Properties/AssemblyInfo.cs`.

- [ ] **Step 6: Delete every obsolete production and construction-test path without a shim**

  Remove the old shell in the same atomic change. Retain only `src/WinRestoreKit/Properties/AssemblyInfo.cs` under the old directory.

  ```powershell
  git rm src\WinRestoreKit\WinRestoreKit.csproj src\WinRestoreKit\Program.cs src\WinRestoreKit\MainForm.cs src\WinRestoreKit\MainForm.Designer.cs src\WinRestoreKit\MainForm.resx src\WinRestoreKit\GitHub.cs src\WinRestoreKit\GitHubIcon.png
  git rm -r src\WinRestoreKit\Views src\WinRestoreKit\Forms src\WinRestoreKit\Controls src\WinRestoreKit\Helpers src\WinRestoreKit\Orchestration src\WinRestoreKit\Results
  git rm src\WinRestoreKit\Fonts\Barlow-Regular.otf src\WinRestoreKit\Fonts\Barlow-Medium.otf src\WinRestoreKit\Fonts\Barlow-SemiBold.otf src\WinRestoreKit\Fonts\BarlowCondensed-Regular.otf src\WinRestoreKit\Fonts\BarlowCondensed-SemiBold.otf src\WinRestoreKit\Fonts\BarlowCondensed-Bold.otf
  git rm src\WinRestoreKit\Properties\Resources.resx src\WinRestoreKit\Properties\Resources.Designer.cs
  git rm src\WinRestoreKit.Tests\BackupPageViewTests.cs src\WinRestoreKit.Tests\HomePageViewBaselineTests.cs src\WinRestoreKit.Tests\HomePageViewDriftTests.cs src\WinRestoreKit.Tests\MainFormRunAdmissionTests.cs src\WinRestoreKit.Tests\NavigationServiceTests.cs src\WinRestoreKit.Tests\ProgressPageViewTests.cs src\WinRestoreKit.Tests\HistoryPageViewTests.cs src\WinRestoreKit.Tests\RestoreDialogOwnerTests.cs src\WinRestoreKit.Tests\ShellLayoutTests.cs src\WinRestoreKit.Tests\FontLoaderTests.cs src\WinRestoreKit.Tests\CompressedDriftPayloadTests.cs
  ```

  Apply this exact test disposition before the delete command:

  | Legacy test | Required replacement or retained assertion |
  | --- | --- |
  | `BackupPageViewTests.cs` | `BackupWorkspaceViewModelTests.cs` verifies selected scopes/modules, validation, compression, and request emission. |
  | `ProgressPageViewTests.cs` | `ProgressWorkspaceViewModelTests.cs` and `ResultWorkspaceViewModelTests.cs` verify cancellation state, progress text, late-cancel summary, and outcomes. |
  | `MainFormRunAdmissionTests.cs` | `RunCoordinatorTests.cs` plus WPF progress navigation/runtime test verify one active run and replacement only after completion. |
  | `NavigationServiceTests.cs` | `ShellViewModel`/runtime tests verify explicit workspace navigation. |
  | `ShellLayoutTests.cs` | Task 1 automation and Task 2 DPI/screenshot evidence verify actual WPF layout. |
  | `HomePageViewBaselineTests.cs` | `TimelineViewModelTests.cs`, `TimelineAccessibilityTests.cs`, and Task 2 state baselines verify empty/verified/failed visuals. |
  | `HomePageViewDriftTests.cs` | `SnapshotComparisonServiceTests.cs` verifies changed/same/unavailable evidence. |
  | `CompressedDriftPayloadTests.cs` | `SnapshotComparisonServiceTests.cs` and `SnapshotPayloadPreparationServiceTests.cs` retain compressed-payload evidence. |
  | `HistoryPageViewTests.cs` | `AdvancedHistoryViewModelTests.cs` and `SnapshotEventCatalogTests.cs` cover event reading, filtering, pruning rules, and source parity. |
  | `RestoreDialogOwnerTests.cs` | Rename and rewrite as `AppRestoreOwnerTests.cs`; Core unit tests prove opaque-owner forwarding plus missing-owner/missing-dialog outcomes, while WPF STA runtime tests verify a current WPF `Window` reaches the dialog. |
  | `FontLoaderTests.cs` | Task 1 runtime tests and Task 2 baselines verify packaged IBM Plex Mono; normal UI uses Segoe UI Variable. |
  | `AppRestoreDialogTests.cs` / `ModuleShapeTests.cs` form calls | `AppRestoreService` tests retain parser, source, winget outcome, and failure wording; replace `RestAppsForm.Describe` assertions with `AppRestoreService.RouteProblem`; WPF STA dialog tests retain ownership. |
  | `BackupPresetsTests.cs`, `BackupFoldersReadTests.cs`, `ScopeGroupsPrivacyTests.cs`, and `ViewDataHelperTests.cs` | Retarget Application scope/preset/folder types and retain exact membership literals, source ordering, and privacy exclusions. |

  `GitHub.cs`/`Stargazers` has no consumers; remove it without replacement. Do not preserve a `MainForm`, `RestAppsForm`, `RestoreConfirmForm`, `NavigationService`, `RichTextBoxLogSink`, `ProgressLogSink`, `MessageBoxIcon`, or `IWin32Window` compatibility alias. The pre-existing Core `object DialogOwner`/`RestoreDialog` seam is not a compatibility alias and must remain opaque to Application.

- [ ] **Step 7: Retarget the surviving version, summary, declaration, and test-assembly assertions**

  Replace direct `MainForm`/`Program` references in version tests with the final WPF assembly and the Foundation `VersionInfo` normalizer. Preserve all three-part and malformed-input assertions.

  ```csharp
  Assembly shipping = typeof(global::WinRestoreKit.Wpf.App).Assembly;
  string compiled = shipping
      .GetCustomAttribute<AssemblyFileVersionAttribute>()
      .Version;
  string parsed = global::DataHelper.Data.ParseLatestVersion(RealAssemblyInfoText());

  Assert.Equal(parsed, compiled);
  Assert.Equal("1.2.3", VersionInfo.Normalize("  1.2.3+build  "));
  ```

  In `RunSummaryTests.cs`, replace each `MessageBoxIcon` expectation with neutral severity:

  ```csharp
  Assert.Equal(RunSeverity.Warning, summary.Severity);
  ```

  In `RestoreDeclarationTests.cs`, use `typeof(global::WinRestoreKit.Wpf.App).Assembly` as the app assembly to prove no concrete `BackupBase` module leaked into the shell. In `AssemblyInfo.cs`, remove mutable WinForms `Theme` state from the parallelism rationale and retain the disabled-parallelization attribute for real process-wide data/registry tests.

- [ ] **Step 8: Run the post-removal build, complete test suite, and no-legacy-path checks**

  Run:

  ```powershell
  dotnet build src\WinRestoreKit.sln -c Debug
  dotnet test src\WinRestoreKit.sln -c Debug --no-build

  $removed = @(
    'src\WinRestoreKit\WinRestoreKit.csproj',
    'src\WinRestoreKit\Program.cs',
    'src\WinRestoreKit\MainForm.cs',
    'src\WinRestoreKit\Views',
    'src\WinRestoreKit\Forms',
    'src\WinRestoreKit\Controls',
    'src\WinRestoreKit\Helpers',
    'src\WinRestoreKit\Orchestration',
    'src\WinRestoreKit\Results'
  ) | Where-Object { Test-Path $_ }
  if ($removed) { throw "Obsolete WinForms paths remain: $($removed -join ', ')" }

  $forms = git grep -n 'System\.Windows\.Forms' -- 'src/**/*.cs' 'src/**/*.csproj'
  if ($forms) { throw "WinForms source references remain:`n$forms" }

  $obsoleteShell = git grep -nE 'MainForm|RestoreConfirmForm|RestAppsForm|NavigationService|RichTextBoxLogSink|ProgressLogSink|MessageBoxIcon|LogHelperTargetExtensions|SetTarget\(' -- 'src/**/*.cs' 'src/**/*.csproj'
  if ($obsoleteShell) { throw "Obsolete shell references remain:`n$obsoleteShell" }

  $legacyViews = git grep -nE '^[[:space:]]*(using[[:space:]]+Views;|namespace[[:space:]]+Views([[:space:]]|\{))' -- 'src/**/*.cs'
  if ($legacyViews) { throw "Removed legacy Views namespace references remain:`n$legacyViews" }

  $typedOwners = git grep -n 'IWin32Window' -- 'src/WinRestoreKit.Application/**/*.cs' 'src/WinRestoreKit.Wpf/**/*.cs' 'src/WinRestoreKit.Tests/**/*.cs'
  if ($typedOwners) { throw "Typed WinForms dialog owners remain outside historical Core comments:`n$typedOwners" }
  ```

  Expected: build succeeds, all Core/Application/WPF tests pass with `Failed: 0`, every removed path is absent, and the searches return no source hits. Historical docs may mention WinForms, but shipping source, project files, and tests may not.

- [ ] **Step 9: Commit the single green atomic cutover**

  ```powershell
  git add -A src\WinRestoreKit src\WinRestoreKit.Application src\WinRestoreKit.Wpf src\WinRestoreKit.Tests src\WinRestoreKit.sln
  git commit -m "refactor: make WPF the WinRestoreKit app"
  ```
### Task 4: Update active release documentation and prove the final self-contained WPF executable

**Files:**
- Modify: `CLAUDE.md`
- Modify: `README.md`
- Modify: `.claude/skills/release/SKILL.md`
- Modify: `.claude/agents/windows-safety-reviewer.md`
- Modify: `CHANGELOG.md`
- Create: `docs/superpowers/verification/2026-08-09-wpf-release.md`
- Test: `src/WinRestoreKit.Tests/ShippingIdentityTests.cs`
- Test: `src/WinRestoreKit.Tests/VersionParsingTests.cs`
- Test: `src/WinRestoreKit.Tests/RebrandIdentityTests.cs`

**Interfaces:**
- Consumes: final WPF project `src/WinRestoreKit.Wpf/WinRestoreKit.Wpf.csproj`, the preserved raw fallback source, Foundation `VersionInfo`/`UpdateCheckService`, Core `Data.DataRootDir`, the final `App` assembly, manifest/icon/font resources, and the project-local `/release` procedure.
- Produces: active build/release guidance that names WPF, a verified self-contained one-file executable, recorded manifest/resource/data-root/version evidence, and a release procedure that cannot target the removed project.

- [ ] **Step 1: Write the failing documentation and artifact target check**

  Before editing active docs, search them for the removed publish target:

  ```powershell
  git grep -nE 'src[\\/]WinRestoreKit[\\/]WinRestoreKit\.csproj|UseWindowsForms|Windows Forms desktop app' -- README.md CLAUDE.md .claude\skills\release\SKILL.md .claude\agents\windows-safety-reviewer.md
  ```

  Expected: this command returns active legacy references. Preserve historical changelog/design records as history; update only current instructions and the new changelog entry for this release.

  Create `docs/superpowers/verification/2026-08-09-wpf-release.md` now with these fixed headings: `Build and tests`, `Publish directory`, `Embedded manifest`, `Clean-desktop executable smoke`, `Version coherence`, `GitHub Release`, and `Result`. Under each heading, record only the command, actual output, artifact SHA-256, screenshot-independent desktop observation, or remote value that proves its result. Leave the file uncommitted until Step 10 because it must describe the real released artifact, not a planned one.

- [ ] **Step 2: Update the active project and release documentation to target WPF**

  Make these exact documentation changes:

  - `CLAUDE.md`: describe `WinRestoreKit.Wpf` as the only shipping app, `WinRestoreKit.Application` as framework-neutral shared orchestration, `WinRestoreKit.Core` as engine, and `WinRestoreKit.Tests` as xUnit. State that the raw fallback source physically remains `src/WinRestoreKit/Properties/AssemblyInfo.cs` and is linked by WPF. Replace WinForms output paths with `src\WinRestoreKit.Wpf\bin\Debug\net8.0-windows\` and `src\WinRestoreKit.Wpf\bin\Release\net8.0-windows\`. Explain WPF dispatcher/dialog/log adapters and retain the no-trimming, manifest, and one-file requirements.
  - `README.md`: retain the build/test commands but replace the publish project path with the WPF project path shown below.
  - `.claude/skills/release/SKILL.md`: retain the three-way version invariant, raw fallback URL, three-part format, PR/approval/tag/release order, and one-file flags; replace every old project publish path and WinForms-only trimming rationale with the final WPF project and its XAML/resource/runtime loading rationale.
  - `.claude/agents/windows-safety-reviewer.md`: describe the WPF shell and Application layer while retaining the elevation, registry, process-kill, overwrite, and safety-review scope.
  - `CHANGELOG.md`: add the approved WPF cutover release entry only when the release version is selected; describe the clean removal of WinForms and preserved backup compatibility without claiming a reboot requirement.

  The publish command must be byte-for-byte identical in README and the release skill:

  ```bat
  dotnet publish src\WinRestoreKit.Wpf\WinRestoreKit.Wpf.csproj -c Release -r win-x64 --self-contained true ^
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true ^
    -p:EnableCompressionInSingleFile=true -p:DebugType=none -o publish
  ```

  Commit these active, version-independent instructions before opening a release branch. Do not stage `CHANGELOG.md` or the post-publication verification record in this commit.

  ```powershell
  git add CLAUDE.md README.md .claude\skills\release\SKILL.md .claude\agents\windows-safety-reviewer.md
  git commit -m "docs: target release workflow at WPF shell"
  ```

- [ ] **Step 3: Run the final Release build, full test suite, and exact single-file publish**

  Run:

  ```powershell
  dotnet build src\WinRestoreKit.sln -c Release
  dotnet test src\WinRestoreKit.sln -c Release --no-build
  Remove-Item -Recurse -Force publish -ErrorAction SilentlyContinue
  dotnet publish src\WinRestoreKit.Wpf\WinRestoreKit.Wpf.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -o publish
  ```

  Expected: Release build succeeds, tests report `Failed: 0`, and publish succeeds without setting `PublishTrimmed`.

- [ ] **Step 4: Prove that publish contains exactly one realistic-size executable**

  Run:

  ```powershell
  $files = @(Get-ChildItem .\publish -File)
  if ($files.Count -ne 1 -or $files[0].Name -ne 'WinRestoreKit.exe') {
      throw "Expected exactly one publish artifact named WinRestoreKit.exe; found: $($files.Name -join ', ')"
  }
  $sizeMiB = [math]::Round($files[0].Length / 1MB, 1)
  if ($sizeMiB -lt 60 -or $sizeMiB -gt 90) {
      throw "Expected a compressed self-contained WPF executable near 69 MiB; got $sizeMiB MiB."
  }
  Get-FileHash .\publish\WinRestoreKit.exe -Algorithm SHA256
  ```

  Expected: one `WinRestoreKit.exe`, approximately 69 MiB, no loose WPF native DLLs, and a recorded SHA-256. Do not put an extracted manifest or any verification file in `publish`; that would invalidate the one-file assertion.

- [ ] **Step 5: Extract the embedded manifest and verify elevated/long-path behavior**

  Run:

  ```powershell
  New-Item -ItemType Directory -Force release-verification | Out-Null
  cmd /c "mt.exe -inputresource:publish\WinRestoreKit.exe;#1 -out:release-verification\WinRestoreKit.manifest.xml"
  Select-String -Path release-verification\WinRestoreKit.manifest.xml -Pattern 'requestedExecutionLevel level="highestAvailable" uiAccess="false"','<longPathAware[^>]*>true</longPathAware>'
  ```

  Expected: `mt.exe` writes the extracted manifest outside `publish`, and both requested strings are found. The executable remains the only file in `publish`.

- [ ] **Step 6: Verify real published-executable behavior on a clean Windows desktop or disposable equivalent**

  Copy only `publish\WinRestoreKit.exe` to a clean disposable folder and launch it from a different current working directory:

  ```powershell
  $smokeRoot = Join-Path $env:TEMP 'WinRestoreKit-WpfReleaseSmoke'
  Remove-Item -Recurse -Force $smokeRoot -ErrorAction SilentlyContinue
  New-Item -ItemType Directory -Force $smokeRoot, "$smokeRoot\working" | Out-Null
  Copy-Item .\publish\WinRestoreKit.exe "$smokeRoot\WinRestoreKit.exe"
  $process = Start-Process "$smokeRoot\WinRestoreKit.exe" -WorkingDirectory "$smokeRoot\working" -PassThru
  ```

  On the clean desktop, verify and record all of these before closing the process:

  | Check | Required evidence |
  | --- | --- |
  | Single-file startup | The copied exe opens without a .NET Desktop Runtime installation and without adjacent DLLs. |
  | Elevated manifest behavior | UAC/highest-available behavior is observed on the disposable account and Task Manager shows the expected elevated state when the account can elevate. |
  | Icon and title bar | Explorer, taskbar, and title bar display `WinRestoreKit.ico` and the WPF theme applies correctly. |
  | Fonts | Normal UI uses Segoe UI Variable; a technical/log surface uses the packaged IBM Plex Mono face; no legacy Barlow face is required. |
  | Data-root resolution | Create one disposable snapshot through the published UI. The new `app` data is beside `$smokeRoot\WinRestoreKit.exe`, never under `$smokeRoot\working`; Timeline can read it after restart. |
  | WPF workflow | Startup, Timeline, Compare, Confirm cancellation, Settings theme switch, About, and an owner-bound dialog work from the published artifact. |

  Stop the disposable process after recording the evidence:

  ```powershell
  Stop-Process -Id $process.Id
  ```

  Expected: no crash, no missing native resource, no framework-runtime prompt, and no data root created in the unrelated working directory.

- [ ] **Step 7: Verify update-version coherence against the actual artifact and release inputs**

  Read the raw source, normalize the Windows file-version resource to three parts, and compare it to the update fallback parser:

  ```powershell
  $source = Get-Content src\WinRestoreKit\Properties\AssemblyInfo.cs -Raw
  $match = [regex]::Match($source, '\[assembly: AssemblyFileVersion\("(?<v>\d+\.\d+\.\d+)"\)\]')
  if (-not $match.Success) { throw 'The raw AssemblyFileVersion line is missing or malformed.' }
  $sourceVersion = $match.Groups['v'].Value
  $resourceVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo((Resolve-Path .\publish\WinRestoreKit.exe)).FileVersion
  $parsedResource = [version]$resourceVersion
  $artifactVersion = "$($parsedResource.Major).$($parsedResource.Minor).$($parsedResource.Build)"
  if ($sourceVersion -ne $artifactVersion) {
      throw "Artifact version $artifactVersion does not match raw fallback version $sourceVersion."
  }
  Write-Host "Version coherence preflight passed: $sourceVersion"
  ```

  Expected: the script prints the same three-part version embedded in the artifact and in the raw fallback source. `ShippingIdentityTests`, `VersionParsingTests`, and `RebrandIdentityTests` must already have passed before this release-only check.

- [ ] **Step 8: Execute the tag and GitHub Release only after explicit release approval**

  This step has external effects. First verify the working tree is clean and the release starts from current `main`; then obtain the approver's explicit release version and confirmation:

  ```powershell
  git checkout main
  git pull
  if (git status --porcelain) { throw "Release work requires a clean working tree." }
  $version = Read-Host "Enter the explicitly approved three-part release version"
  if ($version -notmatch '^\d+\.\d+\.\d+$') {
      throw "Release version must have exactly three numeric parts."
  }
  $approval = Read-Host "Type RELEASE $version to authorize the release branch, tag, and GitHub Release"
  if ($approval -cne "RELEASE $version") {
      throw "Release approval was not supplied."
  }
  ```

  Create the release branch, change only the two hand-maintained version attributes to `$version`, add the dated changelog heading, and rerun Steps 3 through 7 against the bumped artifact:

  ```powershell
  git checkout -b ("release/" + $version)
  $assemblyInfo = 'src\WinRestoreKit\Properties\AssemblyInfo.cs'
  $text = Get-Content $assemblyInfo -Raw
  $text = [regex]::Replace($text,
      '\[assembly: AssemblyVersion\("\d+\.\d+\.\d+"\)\]',
      ('[assembly: AssemblyVersion("{0}")]' -f $version))
  $text = [regex]::Replace($text,
      '\[assembly: AssemblyFileVersion\("\d+\.\d+\.\d+"\)\]',
      ('[assembly: AssemblyFileVersion("{0}")]' -f $version))
  Set-Content -Path $assemblyInfo -Value $text -NoNewline -Encoding utf8
  git diff -- src\WinRestoreKit\Properties\AssemblyInfo.cs
  ```

  Expected diff: only `AssemblyVersion` and `AssemblyFileVersion` change to the same three-part `$version`, and the raw `AssemblyFileVersion` line keeps its exact bracket/quote format. Add the dated `$version` release heading to `CHANGELOG.md`, then commit and open a PR:

  ```powershell
  git add src\WinRestoreKit\Properties\AssemblyInfo.cs CHANGELOG.md
  git commit -m ("release: " + $version)
  git push -u origin ("release/" + $version)
  ```

  Stop for PR review. Only after the PR is approved and merged to `main`, recreate and re-verify the final publish artifact from merged `main` before making a tag:

  ```powershell
  git checkout main
  git pull
  Remove-Item -Recurse -Force publish -ErrorAction SilentlyContinue
  dotnet build src\WinRestoreKit.sln -c Release
  dotnet test src\WinRestoreKit.sln -c Release --no-build
  dotnet publish src\WinRestoreKit.Wpf\WinRestoreKit.Wpf.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -o publish
  ```

  Rerun the exact single-file (Step 4), extracted-manifest (Step 5), clean-desktop (Step 6), and artifact-version (Step 7) checks against this merged-main output. Do not create the tag if any of those checks fails. Then validate `HEAD` before tagging and validate the tag after creation:

  ```powershell
  $headAssemblyInfo = git show 'HEAD:src/WinRestoreKit/Properties/AssemblyInfo.cs'
  $headMatch = [regex]::Match($headAssemblyInfo,
      '\[assembly: AssemblyFileVersion\("(?<v>\d+\.\d+\.\d+)"\)\]')
  if (-not $headMatch.Success -or $headMatch.Groups['v'].Value -ne $version) {
      throw "Merged main does not contain the approved AssemblyFileVersion $version."
  }
  $headCommit = (git rev-parse HEAD).Trim()
  git tag $version
  $tagCommit = (git rev-parse ($version + '^{commit}')).Trim()
  if ($tagCommit -ne $headCommit) {
      throw "The new tag does not point at the verified merged-main commit."
  }
  git push origin $version
  gh release create $version publish\WinRestoreKit.exe --title ("WinRestoreKit " + $version) --generate-notes
  ```

  Expected: the tagged file contains the exact same three-part `AssemblyFileVersion`, and the GitHub Release attaches only `WinRestoreKit.exe`. Never attach `bin\Release\net8.0-windows\WinRestoreKit.exe`.
- [ ] **Step 9: Perform the post-publication remote checks and record release evidence**

  Run after the release is published and `main` contains the version bump:
  ```powershell

  $latest = Invoke-RestMethod 'https://api.github.com/repos/nicolasestrem/WinRestoreKit/releases/latest' -Headers @{ 'User-Agent' = 'WinRestoreKit-release-verification' }
  $raw = Invoke-WebRequest 'https://raw.githubusercontent.com/nicolasestrem/WinRestoreKit/main/src/WinRestoreKit/Properties/AssemblyInfo.cs' -UseBasicParsing
  $rawMatch = [regex]::Match($raw.Content,
      '\[assembly: AssemblyFileVersion\("(?<v>\d+\.\d+\.\d+)"\)\]')
  if (-not $rawMatch.Success) { throw 'Remote raw AssemblyFileVersion is missing or malformed.' }
  $rawVersion = $rawMatch.Groups['v'].Value
  if ($latest.tag_name -ne $rawVersion) {
      throw "Latest release tag $($latest.tag_name) differs from raw source version $rawVersion."
  }
  $assets = @($latest.assets | Where-Object { $_.name -eq 'WinRestoreKit.exe' })
  if ($assets.Count -ne 1 -or $latest.assets.Count -ne 1) {
      throw "Latest release must contain exactly one WinRestoreKit.exe asset."
  }
  $downloaded = 'release-verification\WinRestoreKit.downloaded.exe'
  Invoke-WebRequest $assets[0].browser_download_url -OutFile $downloaded -UseBasicParsing
  $localHash = (Get-FileHash .\publish\WinRestoreKit.exe -Algorithm SHA256).Hash
  $downloadedHash = (Get-FileHash $downloaded -Algorithm SHA256).Hash
  if ($localHash -ne $downloadedHash) {
      throw "Downloaded release hash $downloadedHash differs from verified local publish hash $localHash."
  }
  [pscustomobject]@{
      ReleaseUrl = $latest.html_url
      Tag = $latest.tag_name
      RawVersion = $rawVersion
      AssetSize = $assets[0].size
      Sha256 = $downloadedHash
  }
  ```

- [ ] **Step 10: Commit post-publication release evidence**

  ```powershell
  git add docs\superpowers\verification\2026-08-09-wpf-release.md
  git commit -m "docs: record WPF release verification"
  ```

### Task 5: Perform the final regression and safety review before declaring the migration complete

**Files:**
- Modify: `docs/superpowers/verification/2026-08-09-wpf-cutover.md`
- Modify: `docs/superpowers/verification/2026-08-09-wpf-release.md`
- Test: `src/WinRestoreKit.Tests/ShippingIdentityTests.cs`
- Test: `src/WinRestoreKit.Tests/SnapshotEventCatalogTests.cs`
- Test: `src/WinRestoreKit.Tests/SnapshotComparisonServiceTests.cs`
- Test: `src/WinRestoreKit.Tests/RestoreSetViewModelTests.cs`
- Test: `src/WinRestoreKit.Tests/ConfirmViewModelTests.cs`
- Test: `src/WinRestoreKit.Tests/BackupDestinationLifecycleTests.cs`
- Test: `src/WinRestoreKit.Tests/BackupManifestTests.cs`
- Test: `src/WinRestoreKit.Tests/RestorePlanTests.cs`
- Test: `src/WinRestoreKit.Tests/SnapshotGateConsentTests.cs`
- Test: `src/WinRestoreKit.Tests/ExplorerRestartPromptTests.cs`

**Interfaces:**
- Consumes: all final Application/Core/WPF contracts and Task 2/Task 4 evidence.
- Produces: a signed-off acceptance record with evidence that the WPF app is the sole shipping app, no Core safety rule regressed, no legacy path remains, and the release executable is runnable.

- [ ] **Step 1: Run the final focused safety and migration regression suite**

  Run:

  ```powershell
  dotnet build src\WinRestoreKit.sln -c Release
  dotnet test src\WinRestoreKit.sln -c Release --no-build --filter "FullyQualifiedName~ShippingIdentityTests|FullyQualifiedName~SnapshotEventCatalogTests|FullyQualifiedName~SnapshotComparisonServiceTests|FullyQualifiedName~RestoreSetViewModelTests|FullyQualifiedName~ConfirmViewModelTests|FullyQualifiedName~BackupDestinationLifecycleTests|FullyQualifiedName~BackupManifestTests|FullyQualifiedName~RestorePlanTests|FullyQualifiedName~SnapshotGateConsentTests|FullyQualifiedName~ExplorerRestartPromptTests"
  dotnet test src\WinRestoreKit.sln -c Release --no-build
  ```

  Expected: both selected and full suites report `Failed: 0`. Treat any failure as a regression until it is reproduced, fixed in its owning layer, and covered by the test that failed.

- [ ] **Step 2: Review the final diff for prohibited layering and safety regressions**

  Review the completed diff against these concrete questions:

  | Review question | Required answer |
  | --- | --- |
  | Does any `WinRestoreKit.Application` source reference WPF or WinForms? | No. Application remains framework-neutral. |
  | Does any WPF view/view model parse a `.reg`, manifest, or payload body? | No. It renders Application/Core models only. |
  | Can Compare write to a snapshot or live system? | No. It uses read scopes and `HasDriftedFrom` evidence only. |
  | Can Failed/Unreadable Timeline events restore or influence retention? | No. They remain diagnostic-only and session failures do not retain cleanup data. |
  | Can a WPF restore bypass `RestorePlan`, `SnapshotGate`, `RestoreScope`, `RestoreDispatch`, incomplete-snapshot consent, or `ExplorerRestartPrompt`? | No. Confirm delegates to the existing Application orchestration path. |
  | Does a user see an invented reboot requirement? | No. Only existing process, Explorer restart, and sign-out impacts render. |
  | Is there an ownerless dialog or a WinForms owner seam? | No. WPF dialog services own modal windows. |
  | Does any app identity/version path diverge? | No. Linked raw source, compiled WPF assembly, release tag, and GitHub Release use one normalized three-part version. |

  Run the repository checks from Task 3 Step 8 again. In addition, verify Application does not import either UI framework:

  ```powershell
  $applicationUi = git grep -nE 'System\.Windows\.Forms|System\.Windows\.(Controls|Window|Application)' -- 'src/WinRestoreKit.Application/**/*.cs' 'src/WinRestoreKit.Application/**/*.csproj'
  if ($applicationUi) { throw "Application layer references a UI framework:`n$applicationUi" }
  ```

  Expected: no output from the check.

- [ ] **Step 3: Request the repository's Windows safety review for the final WPF restore/dialog diff**

  Give `.claude/agents/windows-safety-reviewer.md` the final diff touching `src/WinRestoreKit.Application/`, `src/WinRestoreKit.Wpf/Services/WpfRunUi.cs`, WPF dialog views, restore view models, and release settings. Require evidence-backed findings only.

  Expected: no unresolved finding that permits a registry import, process closure, profile overwrite, restore prompt bypass, ownerless dialog, silent failure, or unintended elevated browser launch. Record reviewer identity, reviewed commit, and disposition in `docs/superpowers/verification/2026-08-09-wpf-release.md`.

- [ ] **Step 4: Record final acceptance evidence and commit it**

  Add this checklist to both verification records and mark an item only with the command/output or desktop observation that proved it:

  ```markdown
  - [ ] WPF Timeline + Compare is the shipping home and restore workflow.
  - [ ] All current workflows have a verified WPF equivalent.
  - [ ] Existing snapshot gates and restore safety behavior remain enforced.
  - [ ] Failed attempts are visible and non-restorable.
  - [ ] Keyboard, UIA, reduced motion, themes, DPI, and narrow layout passed real-desktop verification.
  - [ ] All required screenshot baselines were reviewed.
  - [ ] Core, Application, WPF, and full solution tests passed.
  - [ ] No obsolete WinForms project, source, resource, control, helper, test, or compatibility path remains.
  - [ ] Publish contains exactly one self-contained `WinRestoreKit.exe`.
  - [ ] The executable passed the clean Windows desktop smoke and update-version coherence checks.
  ```

  Commit the evidence-only final review:

  ```powershell
  git add docs\superpowers\verification\2026-08-09-wpf-cutover.md docs\superpowers\verification\2026-08-09-wpf-release.md
  git commit -m "docs: complete WPF cutover regression review"
  ```

## Completion Criteria

The migration is complete only when the real-desktop gate preceded WinForms deletion, the WPF project is the sole shipping `WinRestoreKit` app, `src/WinRestoreKit/Properties/AssemblyInfo.cs` remains the linked raw version source, all relevant Core/Application/WPF tests are green, the code tree contains no WinForms shell or compatibility artifacts, and the final publish directory contains exactly one verified self-contained `WinRestoreKit.exe` that ran successfully on a real Windows desktop.
