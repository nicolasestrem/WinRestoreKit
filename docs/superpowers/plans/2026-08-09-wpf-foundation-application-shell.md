# WPF Foundation Application Shell Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Introduce a framework-neutral shared application layer and a runnable side-by-side WPF shell whose only workspace is an honest, empty Timeline state, while preserving the runnable WinForms application and all Core backup/restore semantics.

**Architecture:** `WinRestoreKit.Application` is a `net8.0-windows` class library because it consumes the Windows-targeted Core project, but it has neither `UseWindowsForms` nor `UseWPF` and contains no framework types. It owns shared orchestration, neutral result/UI contracts, registry-backed application settings, and update/version logic; framework projects use friend access instead of widening those migration-only types. `WinRestoreKit.Wpf` is an MVVM host that composes WPF dispatcher, dialog, log, theme, settings, About, and update adapters, then renders a real empty Timeline view without manufacturing snapshot data.

**Tech Stack:** .NET 8 (`net8.0-windows`), C# 12-compatible SDK project settings, WPF/XAML, existing WinRestoreKit.Core, xUnit 2.9.3, Microsoft.Win32 registry APIs, `HttpClient`, `Microsoft.Win32.SystemEvents`.

## Global Constraints

- Preserve `WinRestoreKit.Core` backup/restore semantics, existing snapshot format, `RestorePlan`, `SnapshotGate`, `RestoreScope`, `RestoreDispatch`, `ExplorerRestartPrompt`, and every existing Core result/safety decision.
- `WinRestoreKit.Application` targets `net8.0-windows` solely because Core does; it MUST contain no `System.Windows.Forms`, `System.Windows`, WPF XAML, WinForms/WPF project property, or UI-framework reference.
- Keep namespace `WinRestoreKit` for all Application contracts moved from the app; Application exposes internals to the still-runnable `WinRestoreKit` WinForms assembly, `WinRestoreKit.Wpf`, and `WinRestoreKit.Tests` with its own assembly-level `InternalsVisibleTo` attributes.
- Keep the WinForms project runnable throughout this plan. It continues to reference Core directly for its existing internal Core consumers and additionally references Application for moved app-layer types.
- Replace `RunSummary.Icon : MessageBoxIcon` with `RunSummary.Severity : RunSeverity`; `RunSeverity` values are exactly `Information`, `Warning`, and `Error`.
- Replace the WinForms-typed `IRunUi.Owner` with framework-neutral `object DialogOwner { get; }`. No WinForms or WPF type crosses Application; each shell supplies its current native dialog owner as an opaque object.
- Preserve Core `AppStoreApps.RestoreDialog : Action<string, object>` and the exact existing AppStore restore outcome semantics. The moved orchestrator calls `await appStoreApps.RestoreAsync(currentRestorePath, ui.DialogOwner)`; do not introduce a new restore-dialog callback or fallback result path.
- The WPF shell has no permanent rail, dashboard cards, fake timeline entries, comparison data, restore selection, backup-selection view, progress view, or cutover deletion in this plan.
- Use exactly the new user-facing theme labels **Follow system**, **Light**, and **Dark**. Store the WPF preference as a DWORD `ThemeMode` under `HKCU\Software\WinRestoreKit`; do not reuse the WinForms-only `PaletteMode` setting.
- WPF visual resources use neutral Windows surfaces, mineral-blue actions, restrained coral warnings, Segoe UI Variable interface text, and the linked packaged `IBMPlexMono-Regular.ttf` only for technical/log styles. State is conveyed by text/icon as well as color.
- The side-by-side WPF project has assembly/executable identity `WinRestoreKit.Wpf`. It links the existing `app.manifest` and `WinRestoreKit.ico`, preserving `highestAvailable` and `longPathAware`, but does **not** compile the existing `Properties/AssemblyInfo.cs` and does **not** set `GenerateAssemblyInfo=false`.
- Preserve the final publish contract in WPF project properties: self-contained `win-x64`, single-file, native-library self-extract, compression, and `PublishTrimmed=false`. The later cutover plan changes the WPF identity to `WinRestoreKit` and links the existing AssemblyInfo source at its exact physical path.
- `src/WinRestoreKit/Properties/AssemblyInfo.cs` remains at that exact path and retains the exact three-part `[assembly: AssemblyFileVersion("x.y.z")]` source consumed by the GitHub raw fallback. Do not set competing version properties or attributes in any project.
- Tests remain in `src/WinRestoreKit.Tests`. Preserve pure existing tests; retain WinForms construction tests while WinForms exists; add an STA helper before constructing WPF runtime objects.
- Every code change below follows a red → green cycle. Run only the named focused test/build command at each step; do not run formatters, linters, or broad test suites as part of this foundation plan.

---

## File Structure and Ownership

| Path | Responsibility |
| --- | --- |
| `src/WinRestoreKit.Application/WinRestoreKit.Application.csproj` | Framework-neutral shared application library; references Core and grants friends access to WPF/tests. |
| `src/WinRestoreKit.Application/Properties/AssemblyInfo.cs` | Application-only friend declarations for WPF and tests. |
| `src/WinRestoreKit.Application/Orchestration/{IRunUi,RunCoordinator,RunControl,BackupRestoreOrchestrator}.cs` | Moved orchestration and neutral interaction surface. `BackupRestoreOrchestrator.cs` also retains `ProgressMetricValues` and `ProgressMetrics`. |
| `src/WinRestoreKit.Application/Results/RunSummary.cs` | Moved run grammar/state/summary and neutral severity. |
| `src/WinRestoreKit.Application/Settings/{BackupRootRegistry,ThemeMode,IThemeSettings,RegistryThemeSettings}.cs` | Registry-backed custom-root and theme-preference contracts, without UI framework types. |
| `src/WinRestoreKit.Application/Updates/{VersionInfo,UpdateVerdict,UpdateCheckResult,IUpdateCheckService,UpdateCheckService}.cs` | Version normalization and GitHub-release/raw-AssemblyInfo update decision/fetch service without dialog code. |
| `src/WinRestoreKit.Wpf/WinRestoreKit.Wpf.csproj` | Side-by-side WPF app configuration, linked manifest/icon/font, final publish properties, Application/Core references. |
| `src/WinRestoreKit.Wpf/{App.xaml,App.xaml.cs,MainWindow.xaml,MainWindow.xaml.cs}` | WPF resources, startup composition, and top-level content host. |
| `src/WinRestoreKit.Wpf/Infrastructure/{ObservableObject,DelegateCommand}.cs` | Minimal dependency-free MVVM notifications and commands. |
| `src/WinRestoreKit.Wpf/ViewModels/{ShellViewModel,TimelineWorkspaceViewModel,SettingsViewModel,AboutViewModel}.cs` | Shell navigation and state; the Timeline VM models only the actual empty state. |
| `src/WinRestoreKit.Wpf/Views/{TimelineWorkspaceView,SettingsView,AboutView}.xaml` and code-behind | Declarative, framework-owned presentation with no registry/payload parsing. |
| `src/WinRestoreKit.Wpf/Themes/{Controls,Light,Dark}.xaml` | Dynamic resource keys and the approved Light/Dark token dictionaries. |
| `src/WinRestoreKit.Wpf/Services/{ISystemThemeDetector,WindowsThemeDetector,IThemeService,WpfThemeService,IWpfDialogService,WpfDialogService,IExternalLinkService,ExternalLinkService,WpfUpdatePresenter,IRunPresentation,IRunDialogService,WpfDispatcher,WpfRunUi,WpfLogSink}.cs` | Shell-owned dispatcher/dialog/log/theme/update adapters. The run interfaces are ready for later workflow views but no workflow is rendered in this plan. |
| `src/WinRestoreKit/Orchestration/*`, `src/WinRestoreKit/Results/RunSummary.cs`, `src/WinRestoreKit/Helpers/{BackupRootRegistry,UpdateCheck}.cs` | Deleted after their contents move to Application; no forwarding copies remain. |
| `src/WinRestoreKit/{WinRestoreKit.csproj,Program.cs,Views/ProgressPageView.cs,Views/AboutPageView.cs,Helpers/WinFormsUpdatePresenter.cs}` | Existing shell rebuilt against Application, preserving modal WinForms ownership and startup behavior. |
| `src/WinRestoreKit.Core/WinRestoreKit.Core.csproj` | Adds Application and WPF as internal friends without changing Core type access levels. |
| `src/WinRestoreKit.Tests/{WinRestoreKit.Tests.csproj,AssemblyInfo.cs,WpfTestHost.cs,ApplicationBoundaryTests.cs,RunSummaryTests.cs,RunCoordinatorTests.cs,RunControlTests.cs,ThemeSettingsTests.cs,ThemeServiceTests.cs,VersionParsingTests.cs,UpdateCheckVerdictTests.cs,WpfShellTests.cs}` | Existing tests migrated to moved symbols plus focused new Application/WPF tests and STA construction helper. |
| `src/WinRestoreKit.sln` | Adds Application and WPF projects without removing WinForms, Core, or Tests. |

## Definitive Interfaces Produced by This Plan

These signatures are the integration boundary for the Timeline, Compare/Confirm, Backup/Progress, and Cutover plans.

```csharp
// src/WinRestoreKit.Application/Orchestration/IRunUi.cs
namespace WinRestoreKit;

internal interface IRunUi
{
    void SetProgressText(string text);
    void SetProgressPercent(int percent);
    void SetProgressDetail(string groupInfo, string elapsed, string remaining, string throughput,
                           long bytesWritten, int errors, int warnings);
    void ShowSummary(RunSummary summary, string caption, IReadOnlyList<ModuleOutcome> outcomes);
    IReadOnlyList<string> ShowConsentDialog(RestorePlan plan);
    object DialogOwner { get; }
    bool ConfirmSnapshotOverride(string text, string caption);
    void ShowPlanCompositionError(string text, string caption);
    void SetExplorerRestartVisible(bool visible);
}

// src/WinRestoreKit.Application/Orchestration/BackupRestoreOrchestrator.cs
namespace WinRestoreKit;

internal sealed class BackupRestoreOrchestrator
{
    internal BackupRestoreOrchestrator(IRunUi ui, RunControl runControl = null);
    internal Task RunBackup(IReadOnlyList<BackupBase> modules, string destination,
                            string snapshotName, SnapshotCompression compression);
    internal Task RunRestore(IReadOnlyList<BackupBase> modules, string backupPath);
}

// src/WinRestoreKit.Application/Results/RunSummary.cs
namespace WinRestoreKit;

internal enum RunSeverity { Information, Warning, Error }

internal sealed class RunSummary
{
    public RunState State { get; private set; }
    public string Headline { get; private set; }
    public string Detail { get; private set; }
    public RunSeverity Severity { get; }
    internal static RunSummary For(IReadOnlyList<ModuleOutcome> outcomes, bool ran, RunVerb verb,
                                   string because = null);
    internal static RunSummary Incomplete(IReadOnlyList<ModuleOutcome> outcomes, RunVerb verb,
                                          string detail);
    internal static RunSummary Canceled(RunVerb verb);
}

// src/WinRestoreKit.Application/Settings/ThemeMode.cs
namespace WinRestoreKit;

internal enum ThemeMode { FollowSystem = 0, Light = 1, Dark = 2 }

internal interface IThemeSettings
{
    ThemeMode ReadThemeMode();
    void WriteThemeMode(ThemeMode mode);
}

// src/WinRestoreKit.Application/Updates/IUpdateCheckService.cs
namespace WinRestoreKit;

internal interface IUpdateCheckService
{
    Task<UpdateCheckResult> CheckAsync(string currentVersion, CancellationToken cancellationToken);
}
```

```csharp
// src/WinRestoreKit.Wpf/Services/WpfRunUi.cs
namespace WinRestoreKit.Wpf.Services;

internal sealed class WpfRunUi : IRunUi
{
    internal WpfRunUi(Dispatcher dispatcher, IRunPresentation presentation,
                      IRunDialogService dialogs, Func<Window> ownerProvider);
    object IRunUi.DialogOwner { get; }
}

// src/WinRestoreKit.Wpf/Services/IRunPresentation.cs
internal interface IRunPresentation
{
    void SetProgressText(string text);
    void SetProgressPercent(int percent);
    void SetProgressDetail(string groupInfo, string elapsed, string remaining, string throughput,
                           long bytesWritten, int errors, int warnings);
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

// src/WinRestoreKit.Wpf/ViewModels/ShellViewModel.cs
internal sealed class ShellViewModel : ObservableObject
{
    internal ShellViewModel(IThemeService themes, WpfUpdatePresenter updates,
                            string currentVersion);
    public object CurrentWorkspace { get; private set; }
    public string WorkflowLabel { get; private set; }
    public ICommand ShowTimelineCommand { get; }
    public ICommand ShowSettingsCommand { get; }
    public ICommand ShowAboutCommand { get; }
    internal void ShowTimeline();
    internal void NavigateTo(object workspace, string workflowLabel);
}
```

`WpfRunUi` is not composed by the empty-shell startup; later real Backup/Progress and Compare/Confirm workspaces provide concrete `IRunPresentation` and `IRunDialogService` implementations. This is an adapter boundary, not a visible unfinished workflow.

### Task 1: Add the application/WPF build topology and test access boundaries

**Files:**
- Create: `src/WinRestoreKit.Application/WinRestoreKit.Application.csproj`
- Create: `src/WinRestoreKit.Application/Properties/AssemblyInfo.cs`
- Create: `src/WinRestoreKit.Wpf/WinRestoreKit.Wpf.csproj`
- Create: `src/WinRestoreKit.Wpf/App.xaml`
- Create: `src/WinRestoreKit.Wpf/App.xaml.cs`
- Create: `src/WinRestoreKit.Wpf/Properties/AssemblyInfo.cs`
- Create: `src/WinRestoreKit.Tests/ApplicationBoundaryTests.cs`
- Modify: `src/WinRestoreKit.Core/WinRestoreKit.Core.csproj:36-46`
- Modify: `src/WinRestoreKit/WinRestoreKit.csproj:38-49`
- Modify: `src/WinRestoreKit.Tests/WinRestoreKit.Tests.csproj:3-41`
- Modify: `src/WinRestoreKit.sln`

**Interfaces:**
- Consumes: Existing `WinRestoreKit.Core` net8.0-windows target, its `WinRestoreKit`/`WinRestoreKit.Tests` internal friends, the existing manifest/icon, and the existing hand-maintained AssemblyInfo source.
- Produces: Application and WPF projects in the solution; Core grants internals to `WinRestoreKit.Application` and `WinRestoreKit.Wpf`; Application grants internals to WinForms, WPF, and tests; Tests can reference Application/WPF and will receive the STA helper in Task 3.

- [ ] **Step 1: Add empty project files and solution entries before adding behavior.**

Create the two project files and use the SDK to add both to the existing solution. The Application project must target Windows only to match Core; it must not set either UI framework property. The WPF app has a temporary distinct assembly identity, links the existing manifest/icon/font, and includes the release properties without taking ownership of the version source.

```xml
<!-- src/WinRestoreKit.Application/WinRestoreKit.Application.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0-windows</TargetFramework>
    <RootNamespace>WinRestoreKit</RootNamespace>
    <AssemblyName>WinRestoreKit.Application</AssemblyName>
    <Nullable>disable</Nullable>
    <ImplicitUsings>disable</ImplicitUsings>
    <Platforms>AnyCPU</Platforms>
    <Deterministic>true</Deterministic>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\WinRestoreKit.Core\WinRestoreKit.Core.csproj" />
  </ItemGroup>
</Project>

<!-- src/WinRestoreKit.Wpf/WinRestoreKit.Wpf.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net8.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <RootNamespace>WinRestoreKit.Wpf</RootNamespace>
    <AssemblyName>WinRestoreKit.Wpf</AssemblyName>
    <ApplicationManifest>..\WinRestoreKit\app.manifest</ApplicationManifest>
    <ApplicationIcon>..\WinRestoreKit\WinRestoreKit.ico</ApplicationIcon>
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
    <SelfContained>true</SelfContained>
    <PublishSingleFile>true</PublishSingleFile>
    <IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>
    <EnableCompressionInSingleFile>true</EnableCompressionInSingleFile>
    <PublishTrimmed>false</PublishTrimmed>
    <Nullable>disable</Nullable>
    <ImplicitUsings>disable</ImplicitUsings>
    <Platforms>AnyCPU</Platforms>
    <Deterministic>true</Deterministic>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\WinRestoreKit.Application\WinRestoreKit.Application.csproj" />
    <ProjectReference Include="..\WinRestoreKit.Core\WinRestoreKit.Core.csproj" />
    <Resource Include="..\WinRestoreKit\Fonts\IBMPlexMono-Regular.ttf" Link="Fonts\IBMPlexMono-Regular.ttf" />
  </ItemGroup>
</Project>
```

Create the minimal WPF application definition now so the side-by-side project has a generated STA entry point and can build before Task 6 adds composition:

```xml
<!-- src/WinRestoreKit.Wpf/App.xaml -->
<Application x:Class="WinRestoreKit.Wpf.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" />
```

```csharp
// src/WinRestoreKit.Wpf/App.xaml.cs
using System.Windows;

namespace WinRestoreKit.Wpf;

public partial class App : Application
{
}
```

Run:

```powershell
dotnet sln src\WinRestoreKit.sln add src\WinRestoreKit.Application\WinRestoreKit.Application.csproj src\WinRestoreKit.Wpf\WinRestoreKit.Wpf.csproj
```

Expected: both projects appear in `src\WinRestoreKit.sln`; neither project removes or retargets WinForms, Core, or Tests.

- [ ] **Step 2: Write the failing project-boundary test.**

Add a direct use of the Application `RunControl` type. Do not create that type until Task 2; this establishes that the test project references the new assembly before the moved code exists.

```csharp
// src/WinRestoreKit.Tests/ApplicationBoundaryTests.cs
extern alias Application;

using System.Linq;
using ApplicationWinRestoreKit = Application::WinRestoreKit;
using Xunit;

namespace WinRestoreKit.Tests
{
    public class ApplicationBoundaryTests
    {
        [Fact]
        public void ApplicationRunControl_IsAvailableWithoutConstructingAUiFrameworkObject()
        {
            using (ApplicationWinRestoreKit.RunControl control =
                   new ApplicationWinRestoreKit.RunControl())
            {
                Assert.False(control.IsPaused);
                Assert.False(control.IsCancellationRequested);
            }
        }
    }
}
```

Extend the test project now so this fails for the missing moved type rather than silently exercising the old app assembly:

```xml
<!-- Add to src/WinRestoreKit.Tests/WinRestoreKit.Tests.csproj -->
<PropertyGroup>
  <UseWindowsForms>true</UseWindowsForms>
  <UseWPF>true</UseWPF>
</PropertyGroup>
<ItemGroup>
  <ProjectReference Include="..\WinRestoreKit.Application\WinRestoreKit.Application.csproj">
    <Aliases>global;Application</Aliases>
  </ProjectReference>
  <ProjectReference Include="..\WinRestoreKit.Wpf\WinRestoreKit.Wpf.csproj" />
</ItemGroup>
```

Run:

```powershell
dotnet test src\WinRestoreKit.Tests\WinRestoreKit.Tests.csproj --filter FullyQualifiedName~ApplicationBoundaryTests
```

Expected: FAIL at compile time because `RunControl` has not yet been created in `WinRestoreKit.Application`.

- [ ] **Step 3: Establish all internal-friend and project-reference boundaries.**

Add the Application and WPF friends to Core without changing Core type modifiers, add WinForms/WPF/tests as Application friends, and add Application as an additional reference from the still-runnable WinForms project. The test project retains its existing WinForms project reference because existing tests still construct the old shell during migration.

```xml
<!-- Add inside the existing ItemGroup in src/WinRestoreKit.Core/WinRestoreKit.Core.csproj -->
<InternalsVisibleTo Include="WinRestoreKit.Application" />
<InternalsVisibleTo Include="WinRestoreKit.Wpf" />

<!-- Add inside src/WinRestoreKit/WinRestoreKit.csproj ItemGroup containing project references -->
<ProjectReference Include="..\WinRestoreKit.Application\WinRestoreKit.Application.csproj" />
```

```csharp
// src/WinRestoreKit.Application/Properties/AssemblyInfo.cs
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("WinRestoreKit")]
[assembly: InternalsVisibleTo("WinRestoreKit.Wpf")]
[assembly: InternalsVisibleTo("WinRestoreKit.Tests")]

// src/WinRestoreKit.Wpf/Properties/AssemblyInfo.cs
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("WinRestoreKit.Tests")]
```

Do not add `GenerateAssemblyInfo=false` to either new project. Do not copy or link `src/WinRestoreKit/Properties/AssemblyInfo.cs` into WPF.

- [ ] **Step 4: Run focused project topology verification.**

Run:

```powershell
dotnet build src\WinRestoreKit.Application\WinRestoreKit.Application.csproj
dotnet build src\WinRestoreKit.Wpf\WinRestoreKit.Wpf.csproj
dotnet build src\WinRestoreKit\WinRestoreKit.csproj
```

Expected: the first command succeeds with an Application DLL that has no UI-framework project dependency; the second succeeds as `WinRestoreKit.Wpf`; the third succeeds as the existing `WinRestoreKit` WinForms executable. The focused test still fails only because Task 2 has not moved `RunControl`.

- [ ] **Step 5: Commit the topology only.**

```powershell
git add src\WinRestoreKit.sln src\WinRestoreKit.Core\WinRestoreKit.Core.csproj src\WinRestoreKit\WinRestoreKit.csproj src\WinRestoreKit.Tests\WinRestoreKit.Tests.csproj src\WinRestoreKit.Tests\ApplicationBoundaryTests.cs src\WinRestoreKit.Application\WinRestoreKit.Application.csproj src\WinRestoreKit.Application\Properties\AssemblyInfo.cs src\WinRestoreKit.Wpf\WinRestoreKit.Wpf.csproj src\WinRestoreKit.Wpf\App.xaml src\WinRestoreKit.Wpf\App.xaml.cs src\WinRestoreKit.Wpf\Properties\AssemblyInfo.cs
git commit -m "build: add application and WPF migration projects"
```

### Task 2: Extract shared run state/orchestration and neutralize its UI contract

**Files:**
- Create: `src/WinRestoreKit.Application/Orchestration/IRunUi.cs`
- Create: `src/WinRestoreKit.Application/Orchestration/RunCoordinator.cs`
- Create: `src/WinRestoreKit.Application/Orchestration/RunControl.cs`
- Create: `src/WinRestoreKit.Application/Orchestration/BackupRestoreOrchestrator.cs`
- Create: `src/WinRestoreKit.Application/Results/RunSummary.cs`
- Create: `src/WinRestoreKit.Application/Settings/BackupRootRegistry.cs`
- Verify unchanged: `src/WinRestoreKit/MainForm.cs:17-303` — `StartBackup`, `StartRestore`, and `OnRunningChanged` continue to resolve the moved `RunCoordinator` by the same `WinRestoreKit` namespace; do not redesign its rail/navigation in this foundation plan.
- Modify: `src/WinRestoreKit/Views/ProgressPageView.cs:15-25,399-445,685-790`
- Modify: `src/WinRestoreKit/Orchestration/BackupRestoreOrchestrator.cs:1-1325` then delete it
- Modify: `src/WinRestoreKit/Orchestration/IRunUi.cs:1-52` then delete it
- Modify: `src/WinRestoreKit/Orchestration/RunCoordinator.cs:1-47` then delete it
- Modify: `src/WinRestoreKit/Orchestration/RunControl.cs:1-106` then delete it
- Modify: `src/WinRestoreKit/Results/RunSummary.cs:1-169` then delete it
- Modify: `src/WinRestoreKit/Helpers/BackupRootRegistry.cs:1-104` then delete it
- Modify: `src/WinRestoreKit.Tests/RunSummaryTests.cs`
- Modify: `src/WinRestoreKit.Tests/ApplicationBoundaryTests.cs`
- Modify: `src/WinRestoreKit.Tests/RunCoordinatorTests.cs`
- Modify: `src/WinRestoreKit.Tests/RunControlTests.cs`
- Modify: `src/WinRestoreKit.Tests/BackupDestinationLifecycleTests.cs`
- Verify unchanged: `src/WinRestoreKit.Tests/RestoreDialogOwnerTests.cs:1-64` — its existing `AppStoreApps.RestoreDialog` hook must continue to prove that the opaque owner passed to the Core overload reaches the registered dialog and returns the current `Skipped` outcome.

**Interfaces:**
- Consumes: Task 1 project/friend topology; Core `BackupBase`, `ModuleOutcome`, `RestorePlan`, `LogHelper`, `SnapshotGate`, `RestoreScope`, `RestoreDispatch`, `ExplorerRestartPrompt`, and `Conf.AppStoreApps`.
- Produces: The exact Application `IRunUi`, `RunControl`, `RunCoordinator`, `BackupRestoreOrchestrator`, `RunSummary`, `RunSeverity`, and `BackupRootRegistry` contracts declared above. WinForms remains an `IRunUi` implementation and supplies its dialog owner only as the opaque `object DialogOwner`.

- [ ] **Step 1: Write failing severity and neutral-owner-contract tests.**

Replace the obsolete WinForms icon assertions in `RunSummaryTests` with neutral severity assertions, and add a contract check that pins `IRunUi.DialogOwner` to `object` while proving the WinForms-typed `Owner` property has gone. Do not modify `RestoreDialogOwnerTests`; it is the existing focused regression test for the preserved Core `Action<string, object>` dialog seam and is re-run with this task’s focused command.

```csharp
[Fact]
public void DidNotRun_IsWarningWithoutAWinFormsMessageBoxIcon()
{
    RunSummary summary = RunSummary.For(new List<ModuleOutcome>(), false, RunVerb.Backup,
                                        "the destination is empty");

    Assert.Equal(RunSeverity.Warning, summary.Severity);
    var dialogOwner = typeof(IRunUi).GetProperty("DialogOwner");
    Assert.NotNull(dialogOwner);
    Assert.Equal(typeof(object), dialogOwner.PropertyType);
    Assert.Null(typeof(IRunUi).GetProperty("Owner"));
}

[Fact]
public void IncompleteRun_IsWarning()
{
    RunSummary summary = RunSummary.Incomplete(new List<ModuleOutcome>(), RunVerb.Restore,
                                                "The pre-restore snapshot was incomplete.");

    Assert.Equal(RunSeverity.Warning, summary.Severity);
}

```

Add this separate test to `ApplicationBoundaryTests.cs`, which already imports `ApplicationWinRestoreKit` through the direct Application assembly alias:

```csharp
[Fact]
public void ApplicationAssembly_HasNoWinFormsOrWpfAssemblyReference()
{
    string[] references = typeof(ApplicationWinRestoreKit.RunControl).Assembly.GetReferencedAssemblies()
        .Select(reference => reference.Name)
        .ToArray();

    Assert.DoesNotContain("System.Windows.Forms", references);
    Assert.DoesNotContain("PresentationFramework", references);
    Assert.DoesNotContain("WindowsBase", references);
}
```

Run:

```powershell
dotnet test src\WinRestoreKit.Tests\WinRestoreKit.Tests.csproj --filter "FullyQualifiedName~RunSummaryTests|FullyQualifiedName~ApplicationBoundaryTests"
```

Expected: FAIL because the Application project does not yet contain `RunSummary`, `IRunUi`, or `RunControl`.

- [ ] **Step 2: Move run types verbatim first, then make the two intentional contract changes.**

Move the existing bodies from the old `Orchestration`, `Results`, and `BackupRootRegistry` paths to the exact Application paths. Preserve all existing namespace, type names, method bodies, cancellation behavior, manifest writes, `SnapshotGate` sequencing, cleanup/retention rules, and `ProgressMetrics` formulas. Delete the old source files after the move; do not retain forwarding files.

Replace the WinForms-only `RunSummary.Icon` property with this mapping:

```csharp
internal enum RunSeverity
{
    Information,
    Warning,
    Error
}

public RunSeverity Severity
    => State == RunState.Problems || State == RunState.DidNotRun
        ? RunSeverity.Warning
        : RunSeverity.Information;
```

Replace the former WinForms-typed `Owner` property in the moved interface with this exact neutral abstraction:

```csharp
/// <summary>
/// Opaque owner for a shell-native modal dialog. The Application layer never casts it.
/// </summary>
object DialogOwner { get; }
```

At the existing AppStore branch in `BackupRestoreOrchestrator.RunRestore`, preserve the Core seam and all current result semantics; change only the property name and type at the Application boundary:

```csharp
ModuleResult outcome = config is Conf.AppStoreApps appStoreApps
    ? await appStoreApps.RestoreAsync(currentRestorePath, ui.DialogOwner)
    : await config.RestoreAsync(currentRestorePath);
```

Do not modify `Conf.AppStoreApps`, its `RestoreDialog : Action<string, object>` seam, Core artifact handling, restore order, or the AppStore module’s existing `Skipped`/failure outcome semantics in this task.

- [ ] **Step 3: Adapt the still-shipping WinForms run view to the neutral contract.**

Replace the explicit `IRunUi.Owner` implementation in `ProgressPageView` with this exact opaque owner property. It returns the same current Form-or-control object currently passed to `AppStoreApps.RestoreAsync`; do not construct `RestAppsForm` here or change its dialog lifecycle.

```csharp
object IRunUi.DialogOwner => (object)FindForm() ?? this;
```

Keep every existing `ShowConsentDialog`, snapshot-override confirmation, plan-composition error, Explorer restart, progress, summary, log-sink, cancellation, and dispatcher behavior. Preserve the current title accent behavior by using the neutral severity:

```csharp
titleLabel.ForeColor = summary.Severity == RunSeverity.Warning
    ? Theme.Current.Accent2_600
    : Theme.Current.Text;
```

- [ ] **Step 4: Run the extracted pure-contract tests to verify they pass.**

Run:

```powershell
dotnet test src\WinRestoreKit.Tests\WinRestoreKit.Tests.csproj --filter "FullyQualifiedName~ApplicationBoundaryTests|FullyQualifiedName~RunSummaryTests|FullyQualifiedName~RunCoordinatorTests|FullyQualifiedName~RunControlTests|FullyQualifiedName~BackupDestinationLifecycleTests|FullyQualifiedName~RestoreDialogOwnerTests"
```

Expected: PASS. The existing coordinator test still admits exactly one concurrent run; control tests still release paused waiters on cancellation; backup-root lifecycle behavior still records valid custom roots; new summary tests prove severity plus an `object`-typed `IRunUi.DialogOwner` without `IRunUi.Owner`; `RestoreDialogOwnerTests` proves the unchanged Core AppStore dialog seam receives the supplied opaque owner.

- [ ] **Step 5: Verify the Application boundary and WinForms compatibility compile independently.**

Run:

```powershell
dotnet build src\WinRestoreKit.Application\WinRestoreKit.Application.csproj
dotnet build src\WinRestoreKit\WinRestoreKit.csproj
```

Expected: both PASS. Application compiles against Core with no WinForms/WPF reference; WinForms compiles its existing `ProgressPageView` against the moved Application types.

- [ ] **Step 6: Commit the complete orchestration extraction.**

```powershell
git add -A src\WinRestoreKit.Application src\WinRestoreKit\Orchestration src\WinRestoreKit\Results src\WinRestoreKit\Helpers\BackupRootRegistry.cs src\WinRestoreKit\Views\ProgressPageView.cs src\WinRestoreKit.Tests\ApplicationBoundaryTests.cs src\WinRestoreKit.Tests\RunSummaryTests.cs src\WinRestoreKit.Tests\RunCoordinatorTests.cs src\WinRestoreKit.Tests\RunControlTests.cs src\WinRestoreKit.Tests\BackupDestinationLifecycleTests.cs
git commit -m "refactor: move shared run orchestration to application"
```

### Task 3: Add registry-backed WPF theme settings and dynamic Light/Dark/Follow-system resources

**Files:**
- Create: `src/WinRestoreKit.Application/Settings/ThemeMode.cs`
- Create: `src/WinRestoreKit.Application/Settings/IThemeSettings.cs`
- Create: `src/WinRestoreKit.Application/Settings/RegistryThemeSettings.cs`
- Create: `src/WinRestoreKit.Wpf/Themes/Controls.xaml`
- Create: `src/WinRestoreKit.Wpf/Themes/Light.xaml`
- Create: `src/WinRestoreKit.Wpf/Themes/Dark.xaml`
- Create: `src/WinRestoreKit.Wpf/Services/ISystemThemeDetector.cs`
- Create: `src/WinRestoreKit.Wpf/Services/WindowsThemeDetector.cs`
- Create: `src/WinRestoreKit.Wpf/Services/IThemeService.cs`
- Create: `src/WinRestoreKit.Wpf/Services/WpfThemeService.cs`
- Create: `src/WinRestoreKit.Tests/WpfTestHost.cs`
- Create: `src/WinRestoreKit.Tests/ThemeSettingsTests.cs`
- Create: `src/WinRestoreKit.Tests/ThemeServiceTests.cs`

**Interfaces:**
- Consumes: Application’s no-UI project boundary from Task 1; `Microsoft.Win32.Registry`; WPF resource dictionaries; and `SystemEvents.UserPreferenceChanged`.
- Produces: `ThemeMode`, `IThemeSettings`, `RegistryThemeSettings`, `ISystemThemeDetector`, `IThemeService`, and `WpfThemeService`. The WPF shell reads only these neutral settings; it does not reuse WinForms `Theme`, `PaletteMode`, `Voltage`, or `Flux`.

- [ ] **Step 1: Write the failing settings and effective-theme tests.**

Use a unique HKCU subkey in the persistence test, delete it in `finally`, and use fakes for the system detector so the visual decision is deterministic.

Create the STA helper as test infrastructure before writing the WPF-specific test. It creates no `Application` object and never displays a window:

```csharp
// src/WinRestoreKit.Tests/WpfTestHost.cs
using System;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows.Threading;

namespace WinRestoreKit.Tests
{
    internal static class WpfTestHost
    {
        internal static void Run(Action action)
            => Run<object>(() =>
            {
                action();
                return null;
            });

        internal static T Run<T>(Func<T> action)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));

            T result = default;
            ExceptionDispatchInfo failure = null;
            Thread thread = new Thread(() =>
            {
                try
                {
                    result = action();
                }
                catch (Exception ex)
                {
                    failure = ExceptionDispatchInfo.Capture(ex);
                }
                finally
                {
                    Dispatcher.CurrentDispatcher.InvokeShutdown();
                }
            })
            {
                IsBackground = true,
                Name = "WinRestoreKit WPF test"
            };

            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            failure?.Throw();
            return result;
        }
    }
}
```

```csharp
[Fact]
public void RegistryThemeSettings_RoundTripsLightDarkAndFollowSystem()
{
    string keyPath = @"Software\WinRestoreKit.Tests\" + Guid.NewGuid().ToString("N");
    try
    {
        IThemeSettings settings = new RegistryThemeSettings(keyPath);

        settings.WriteThemeMode(ThemeMode.Dark);
        Assert.Equal(ThemeMode.Dark, settings.ReadThemeMode());
        settings.WriteThemeMode(ThemeMode.Light);
        Assert.Equal(ThemeMode.Light, settings.ReadThemeMode());
        settings.WriteThemeMode(ThemeMode.FollowSystem);
        Assert.Equal(ThemeMode.FollowSystem, settings.ReadThemeMode());
    }
    finally
    {
        Registry.CurrentUser.DeleteSubKeyTree(keyPath, false);
    }
}

[Fact]
public void WpfThemeService_UsesSystemDarkOnlyWhenModeFollowsSystem()
{
    WpfTestHost.Run(() =>
    {
        ResourceDictionary resources = new ResourceDictionary();
        FakeThemeSettings settings = new FakeThemeSettings(ThemeMode.FollowSystem);
        using (WpfThemeService service = new WpfThemeService(resources, settings,
                   new FakeSystemThemeDetector(isDark: true)))
        {
            Assert.Equal(ThemeMode.Dark, service.EffectiveMode);
            service.SetMode(ThemeMode.Light);
            Assert.Equal(ThemeMode.Light, service.EffectiveMode);
            Assert.Equal(ThemeMode.Light, settings.ReadThemeMode());
        }
    });
}

private sealed class FakeThemeSettings : IThemeSettings
{
    private ThemeMode mode;

    internal FakeThemeSettings(ThemeMode mode) => this.mode = mode;

    public ThemeMode ReadThemeMode() => mode;
    public void WriteThemeMode(ThemeMode mode) => this.mode = mode;
}

private sealed class FakeSystemThemeDetector : ISystemThemeDetector
{
    private readonly bool isDark;

    internal FakeSystemThemeDetector(bool isDark) => this.isDark = isDark;

    public bool IsDarkAppsTheme() => isDark;
}
```

Run:

```powershell
dotnet test src\WinRestoreKit.Tests\WinRestoreKit.Tests.csproj --filter "FullyQualifiedName~ThemeSettingsTests|FullyQualifiedName~ThemeServiceTests"
```

Expected: FAIL to compile because `ThemeMode`, `RegistryThemeSettings`, and `WpfThemeService` do not exist yet.

- [ ] **Step 2: Implement neutral settings with a deterministic invalid-value default.**

Use the existing product registry root and a new `ThemeMode` DWORD. Allow an optional key path only for isolated tests; production construction uses the default path. Invalid/missing data and registry failures default to Follow system.

```csharp
internal enum ThemeMode
{
    FollowSystem = 0,
    Light = 1,
    Dark = 2
}

internal sealed class RegistryThemeSettings : IThemeSettings
{
    private const string DefaultKeyPath = @"Software\WinRestoreKit";
    private const string ValueName = "ThemeMode";
    private readonly string keyPath;

    internal RegistryThemeSettings(string keyPath = DefaultKeyPath)
    {
        this.keyPath = string.IsNullOrWhiteSpace(keyPath) ? DefaultKeyPath : keyPath;
    }

    public ThemeMode ReadThemeMode()
    {
        try
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(keyPath))
            {
                return key?.GetValue(ValueName) is int value && Enum.IsDefined(typeof(ThemeMode), value)
                    ? (ThemeMode)value
                    : ThemeMode.FollowSystem;
            }
        }
        catch (Exception)
        {
            return ThemeMode.FollowSystem;
        }
    }

    public void WriteThemeMode(ThemeMode mode)
    {
        try
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(keyPath))
                key?.SetValue(ValueName, (int)mode, RegistryValueKind.DWord);
        }
        catch (Exception)
        {
        }
    }
}
```

Do not modify `Helpers/Theme.cs` or delete the WinForms `PaletteMode` in this staged task. The two shells intentionally maintain their own settings during side-by-side migration.

- [ ] **Step 3: Implement WPF theme resolution, preference-change subscription, and token dictionaries.**

`WindowsThemeDetector` reads `HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize\AppsUseLightTheme`, returns `false` on any failure, and has this exact interface:

```csharp
internal interface ISystemThemeDetector
{
    bool IsDarkAppsTheme();
}
```

`WpfThemeService` owns the `SystemEvents.UserPreferenceChanged` subscription, updates only while `Mode == ThemeMode.FollowSystem`, replaces exactly one Light/Dark dictionary in `resources.MergedDictionaries`, and unsubscribes in `Dispose`.

```csharp
internal interface IThemeService : IDisposable
{
    ThemeMode Mode { get; }
    ThemeMode EffectiveMode { get; }
    event EventHandler ThemeChanged;
    void SetMode(ThemeMode mode);
}

internal sealed class WpfThemeService : IThemeService
{
    internal WpfThemeService(ResourceDictionary resources, IThemeSettings settings,
                             ISystemThemeDetector systemTheme);

    public ThemeMode Mode { get; private set; }
    public ThemeMode EffectiveMode { get; private set; }

    public void SetMode(ThemeMode mode)
    {
        Mode = mode;
        settings.WriteThemeMode(mode);
        ApplyEffectiveMode();
    }
}
```

Create `Themes/Controls.xaml` with `FontFamily` resources and common focusable-control styles, including an Automation-friendly visible focus trigger. Use `DynamicResource` keys rather than hardcoded colors in views. Link the packaged monospace font only through the technical text style.

```xml
<!-- Representative resource keys in Themes/Light.xaml; Dark.xaml defines the same keys. -->
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
  <Color x:Key="WindowBackgroundColor">#FFF7F8FA</Color>
  <Color x:Key="SurfaceColor">#FFFFFFFF</Color>
  <Color x:Key="TextColor">#FF1A1D21</Color>
  <Color x:Key="MutedTextColor">#FF5F6875</Color>
  <Color x:Key="AccentColor">#FF1769AA</Color>
  <Color x:Key="WarningColor">#FFB44E3C</Color>
  <Color x:Key="DividerColor">#FFD7DCE2</Color>
  <SolidColorBrush x:Key="WindowBackgroundBrush" Color="{StaticResource WindowBackgroundColor}" />
  <SolidColorBrush x:Key="SurfaceBrush" Color="{StaticResource SurfaceColor}" />
  <SolidColorBrush x:Key="TextBrush" Color="{StaticResource TextColor}" />
  <SolidColorBrush x:Key="MutedTextBrush" Color="{StaticResource MutedTextColor}" />
  <SolidColorBrush x:Key="AccentBrush" Color="{StaticResource AccentColor}" />
  <SolidColorBrush x:Key="WarningBrush" Color="{StaticResource WarningColor}" />
  <SolidColorBrush x:Key="DividerBrush" Color="{StaticResource DividerColor}" />
</ResourceDictionary>
```

In `Controls.xaml`, use `Segoe UI Variable` for `TextBlock`, `Button`, `ComboBox`, and `Label`, and reserve the linked package for the key `TechnicalTextStyle`:

```xml
<Style x:Key="TechnicalTextStyle" TargetType="TextBlock">
  <Setter Property="FontFamily" Value="pack://application:,,,/Fonts/#IBM Plex Mono" />
  <Setter Property="FontSize" Value="12" />
</Style>
```

- [ ] **Step 4: Re-run the focused theme tests.**

Run:

```powershell
dotnet test src\WinRestoreKit.Tests\WinRestoreKit.Tests.csproj --filter "FullyQualifiedName~ThemeSettingsTests|FullyQualifiedName~ThemeServiceTests"
```

Expected: PASS. The test proves all three persisted choices round-trip and Follow system maps to detector output while an explicit Light choice overrides a dark detector.

- [ ] **Step 5: Commit settings and theme infrastructure.**

```powershell
git add src\WinRestoreKit.Application\Settings\ThemeMode.cs src\WinRestoreKit.Application\Settings\IThemeSettings.cs src\WinRestoreKit.Application\Settings\RegistryThemeSettings.cs src\WinRestoreKit.Wpf\Themes src\WinRestoreKit.Wpf\Services\ISystemThemeDetector.cs src\WinRestoreKit.Wpf\Services\WindowsThemeDetector.cs src\WinRestoreKit.Wpf\Services\IThemeService.cs src\WinRestoreKit.Wpf\Services\WpfThemeService.cs src\WinRestoreKit.Tests\WpfTestHost.cs src\WinRestoreKit.Tests\ThemeSettingsTests.cs src\WinRestoreKit.Tests\ThemeServiceTests.cs
git commit -m "feat: add WPF theme settings and resources"
```

### Task 4: Move version/update decisions into Application and keep dialog ownership in each shell

**Files:**
- Create: `src/WinRestoreKit.Application/Updates/VersionInfo.cs`
- Create: `src/WinRestoreKit.Application/Updates/UpdateVerdict.cs`
- Create: `src/WinRestoreKit.Application/Updates/UpdateCheckResult.cs`
- Create: `src/WinRestoreKit.Application/Updates/IUpdateCheckService.cs`
- Create: `src/WinRestoreKit.Application/Updates/UpdateCheckService.cs`
- Create: `src/WinRestoreKit/Helpers/WinFormsUpdatePresenter.cs`
- Create: `src/WinRestoreKit.Wpf/Services/IWpfDialogService.cs`
- Create: `src/WinRestoreKit.Wpf/Services/WpfDialogService.cs`
- Create: `src/WinRestoreKit.Wpf/Services/IExternalLinkService.cs`
- Create: `src/WinRestoreKit.Wpf/Services/ExternalLinkService.cs`
- Create: `src/WinRestoreKit.Wpf/Services/WpfUpdatePresenter.cs`
- Modify: `src/WinRestoreKit/Program.cs:13-133`
- Modify: `src/WinRestoreKit/Views/AboutPageView.cs:137-141,346-401`
- Modify: `src/WinRestoreKit/Helpers/UpdateCheck.cs:1-189` then delete it
- Modify: `src/WinRestoreKit.Tests/VersionParsingTests.cs`
- Modify: `src/WinRestoreKit.Tests/UpdateCheckVerdictTests.cs`

**Interfaces:**
- Consumes: Current assembly file-version behavior, Core `Data.UserAgent`, `Data.IsInet`, release API URL, raw AssemblyInfo URL, `ParseLatestReleaseTag`, `ParseLatestVersion`, and existing `Utils.OpenUrl` error seam.
- Produces: Framework-neutral `VersionInfo` and update service/result contracts; WinForms and WPF presenters map results to shell-owned owner-bound dialogs. The raw `Properties/AssemblyInfo.cs` location and parser contract stay unchanged.

- [ ] **Step 1: Write the failing tests against Application version/update types.**

Migrate existing tests from `Program`/`UpdateCheck` to the destination types and add coverage for the neutral error result. Keep the real AssemblyInfo test-data link exactly as it is in the test project.

```csharp
[Fact]
public void GetCurrentVersion_UsesTheAssemblyFileVersionAttribute()
{
    string version = VersionInfo.GetCurrentVersion(typeof(VersionParsingTests).Assembly);

    Assert.NotEqual(VersionInfo.UnknownVersion, version);
    Assert.Matches("^\\d+\\.\\d+\\.\\d+$", version);
}

[Fact]
public void Decide_ReturnsWarningVerdictWhenInstalledVersionIsUnknown()
{
    UpdateVerdict verdict = UpdateCheckService.Decide(VersionInfo.UnknownVersion, "1.2.3");

    Assert.Equal(UpdateVerdict.CannotDetermineCurrentVersion, verdict);
}
```

Run:

```powershell
dotnet test src\WinRestoreKit.Tests\WinRestoreKit.Tests.csproj --filter "FullyQualifiedName~VersionParsingTests|FullyQualifiedName~UpdateCheckVerdictTests"
```

Expected: FAIL because Application version/update types do not exist and existing tests still refer to the WinForms types.

- [ ] **Step 2: Implement the framework-neutral version and update service.**

Move the exact normalization behavior out of `Program`: trim whitespace; drop `+`/`-` suffixes before parsing; require a third version component; preserve malformed non-empty values verbatim; and return the literal `"unknown"` only for null/empty input. The assembly accessor must read `AssemblyFileVersionAttribute` before `Assembly.GetName().Version`.

```csharp
internal static class VersionInfo
{
    internal const string UnknownVersion = "unknown";

    internal static string GetCurrentVersion(Assembly assembly)
        => Normalize(assembly?.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version
                     ?? assembly?.GetName().Version?.ToString());

    internal static string Normalize(string rawVersion)
    {
        if (string.IsNullOrWhiteSpace(rawVersion))
            return UnknownVersion;

        string raw = rawVersion.Trim();
        int suffix = raw.IndexOfAny(new[] { '+', '-' });
        string candidate = suffix >= 0 ? raw.Substring(0, suffix) : raw;
        return Version.TryParse(candidate, out Version parsed) && parsed.Build >= 0
            ? parsed.ToString(3)
            : raw;
    }
}
```

`UpdateCheckService` owns one process-wide `HttpClient` with the existing `Data.UserAgent`, checks `Data.IsInet` off the calling thread, reads the GitHub Releases tag first, and on any primary read failure falls back to `Data.ParseLatestVersion(await Client.GetStringAsync(Data.Uri.URL_ASSEMBLY))`. It returns data, never displays a dialog and never launches a URL.

```csharp
internal sealed class UpdateCheckResult
{
    internal UpdateCheckResult(UpdateVerdict verdict, string currentVersion, string latestVersion,
                               string errorMessage = null);
    public UpdateVerdict Verdict { get; }
    public string CurrentVersion { get; }
    public string LatestVersion { get; }
    public string ErrorMessage { get; }
}

internal sealed class UpdateCheckService : IUpdateCheckService
{
    public Task<UpdateCheckResult> CheckAsync(string currentVersion,
                                               CancellationToken cancellationToken);

    internal static UpdateVerdict Decide(string currentVersion, string latestTag);
}
```

Preserve current verdict semantics exactly: unknown installed version is `CannotDetermineCurrentVersion`; blank/unreadable latest version is `LatestVersionUnreadable`; equal/newer installed versions are `UpToDate`; a higher parsable tag or incomparable nonblank tag is `UpdateAvailable`. Translate transport/parse exceptions into an `UpdateCheckResult` with the original exception message in `ErrorMessage`, not a fabricated latest version.

- [ ] **Step 3: Replace each shell’s old direct MessageBox update code with owner-bound presenters.**

`WinFormsUpdatePresenter` receives an `IUpdateCheckService`, an `IWin32Window` owner, and the current version. It reuses the current exact wording and default buttons, but chooses `MessageBoxIcon` only inside the WinForms project. `AboutPageView` calls it instead of `UpdateCheck.CheckForUpdatesAsync`, and reads its display version from `VersionInfo.GetCurrentVersion(typeof(AboutPageView).Assembly)`.

WPF keeps native ownership in WPF. Its dialog service accepts an owner provider and invokes the supplied action on the dispatcher; no Application type receives a `Window`.

```csharp
internal interface IWpfDialogService
{
    void ShowInformation(string text, string caption);
    void ShowWarning(string text, string caption);
    void ShowError(string text, string caption);
    bool Confirm(string text, string caption);
}

internal interface IExternalLinkService
{
    void Open(string url);
}

internal sealed class WpfUpdatePresenter
{
    internal WpfUpdatePresenter(IUpdateCheckService updates, IWpfDialogService dialogs,
                                IExternalLinkService links);

    internal async Task CheckAsync(string currentVersion, CancellationToken cancellationToken)
    {
        UpdateCheckResult result = await updates.CheckAsync(currentVersion, cancellationToken);
        switch (result.Verdict)
        {
            case UpdateVerdict.CannotDetermineCurrentVersion:
                dialogs.ShowWarning(
                    "The installed WinRestoreKit version could not be determined, so no update download can be offered.",
                    "WinRestoreKit Update");
                return;
            case UpdateVerdict.LatestVersionUnreadable:
                if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
                    dialogs.ShowError("Checking for WinRestoreKit updates failed.\n" + result.ErrorMessage,
                                      "WinRestoreKit Update");
                else
                    dialogs.ShowWarning(
                        "Could not read the latest WinRestoreKit version number from the update file.",
                        "WinRestoreKit Update");
                return;
            case UpdateVerdict.UpToDate:
                dialogs.ShowInformation("No new WinRestoreKit updates are available.", "WinRestoreKit Update");
                return;
            case UpdateVerdict.UpdateAvailable:
                if (dialogs.Confirm(
                    "WinRestoreKit version " + result.LatestVersion +
                    " is available.\nDo you want to open the download page?",
                    "WinRestoreKit Update Available"))
                {
                    links.Open(Data.Uri.URL_GITLATEST);
                }
                return;
            default:
                throw new InvalidOperationException("The update result has an unknown verdict.");
        }
    }
}
```

`WpfDialogService` calls `MessageBox.Show(ownerProvider(), ...)` only after checking the owner is non-null and loaded; it returns `false` from `Confirm` if no owner is available. `ExternalLinkService.Open(string url)` calls existing `Utils.OpenUrl(url)`. During WPF startup in Task 6, register `Utils.UrlFailureUi` to dispatcher-marshal an owner-bound `ShowWarning`; retain the WinForms registration in `Program.RegisterUiSeams()`.

Remove `Helpers/UpdateCheck.cs` once both presenters use `UpdateCheckService`. Remove the moved version methods from `Program`, leaving only startup-failure description and WinForms Core seam registration there.

- [ ] **Step 4: Run update/version tests and WinForms compilation.**

Run:

```powershell
dotnet test src\WinRestoreKit.Tests\WinRestoreKit.Tests.csproj --filter "FullyQualifiedName~VersionParsingTests|FullyQualifiedName~UpdateCheckVerdictTests"
dotnet build src\WinRestoreKit\WinRestoreKit.csproj
```

Expected: PASS. The tests prove the raw AssemblyInfo fallback and normalized comparison behavior remain coherent; the WinForms application still builds with owner-bound update dialogs despite `UpdateCheck.cs` no longer existing in that project.

- [ ] **Step 5: Commit the neutral update seam.**

```powershell
git add -A src\WinRestoreKit.Application\Updates src\WinRestoreKit\Program.cs src\WinRestoreKit\Views\AboutPageView.cs src\WinRestoreKit\Helpers\WinFormsUpdatePresenter.cs src\WinRestoreKit\Helpers\UpdateCheck.cs src\WinRestoreKit.Wpf\Services\IWpfDialogService.cs src\WinRestoreKit.Wpf\Services\WpfDialogService.cs src\WinRestoreKit.Wpf\Services\IExternalLinkService.cs src\WinRestoreKit.Wpf\Services\ExternalLinkService.cs src\WinRestoreKit.Wpf\Services\WpfUpdatePresenter.cs src\WinRestoreKit.Tests\VersionParsingTests.cs src\WinRestoreKit.Tests\UpdateCheckVerdictTests.cs
git commit -m "refactor: separate update decisions from shell dialogs"
```

### Task 5: Build the MVVM WPF shell with an honest empty Timeline, settings, and About views

**Files:**
- Create: `src/WinRestoreKit.Wpf/Infrastructure/ObservableObject.cs`
- Create: `src/WinRestoreKit.Wpf/Infrastructure/DelegateCommand.cs`
- Create: `src/WinRestoreKit.Wpf/ViewModels/ShellViewModel.cs`
- Create: `src/WinRestoreKit.Wpf/ViewModels/TimelineWorkspaceViewModel.cs`
- Create: `src/WinRestoreKit.Wpf/ViewModels/SettingsViewModel.cs`
- Create: `src/WinRestoreKit.Wpf/ViewModels/AboutViewModel.cs`
- Create: `src/WinRestoreKit.Wpf/Views/TimelineWorkspaceView.xaml`
- Create: `src/WinRestoreKit.Wpf/Views/TimelineWorkspaceView.xaml.cs`
- Create: `src/WinRestoreKit.Wpf/Views/SettingsView.xaml`
- Create: `src/WinRestoreKit.Wpf/Views/SettingsView.xaml.cs`
- Create: `src/WinRestoreKit.Wpf/Views/AboutView.xaml`
- Create: `src/WinRestoreKit.Wpf/Views/AboutView.xaml.cs`
- Create: `src/WinRestoreKit.Tests/WpfShellTests.cs`

**Interfaces:**
- Consumes: Task 3 `IThemeService`/`ThemeMode`, Task 4 `VersionInfo`/`WpfUpdatePresenter`, WPF theme resource keys, and the shell type names declared in the definitive interface section.
- Produces: Constructible `ShellViewModel`, `TimelineWorkspaceViewModel`, Settings and About view models, and the three matching views. The shell deliberately exposes only Timeline, Settings, and About at this phase; subsequent real workspaces use `NavigateTo` rather than a duplicate navigation implementation.

- [ ] **Step 1: Write the failing STA construction and empty-state tests.**

Use the `WpfTestHost` added in Task 3. Write the tests before adding the MVVM shell types:


```csharp
[Fact]
public void Shell_StartsOnTimelineWithTheOnlyRealEmptyState()
{
    WpfTestHost.Run(() =>
    {
        ShellViewModel shell = CreateShell();

        Assert.Equal("Timeline", shell.WorkflowLabel);
        TimelineWorkspaceViewModel timeline = Assert.IsType<TimelineWorkspaceViewModel>(shell.CurrentWorkspace);
        Assert.Equal("No local snapshots are available.", timeline.EmptyStateTitle);
        Assert.Equal("Create a snapshot to begin protecting this PC.", timeline.EmptyStateDescription);
    });
}

[Fact]
public void Shell_SettingsAndAboutCommandsNavigateWithoutAVisualRail()
{
    WpfTestHost.Run(() =>
    {
        ShellViewModel shell = CreateShell();
        shell.ShowSettingsCommand.Execute(null);
        Assert.Equal("Settings", shell.WorkflowLabel);
        shell.ShowAboutCommand.Execute(null);
        Assert.Equal("About", shell.WorkflowLabel);
        shell.ShowTimeline();
        Assert.IsType<TimelineWorkspaceViewModel>(shell.CurrentWorkspace);
    });
}
```

Use this exact test fixture code; it supplies all dependencies without reading a registry or making a network call:

```csharp
private static ShellViewModel CreateShell()
{
    return new ShellViewModel(
        new FakeThemeService(),
        new WpfUpdatePresenter(new FakeUpdates(), new FakeDialogs(), new FakeLinks()),
        "0.0.1");
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
    public Task<UpdateCheckResult> CheckAsync(string currentVersion, CancellationToken cancellationToken)
        => Task.FromResult(new UpdateCheckResult(UpdateVerdict.UpToDate, currentVersion, currentVersion));
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
```

Run:

```powershell
dotnet test src\WinRestoreKit.Tests\WinRestoreKit.Tests.csproj --filter FullyQualifiedName~WpfShellTests
```

Expected: FAIL to compile because the MVVM shell types do not exist.

- [ ] **Step 2: Implement the dependency-free MVVM base and explicit workspace state.**

`ObservableObject` implements `INotifyPropertyChanged` and only raises when a field changes. `DelegateCommand` implements `ICommand`, accepts execute/can-execute delegates, and exposes `RaiseCanExecuteChanged()`; do not add a third-party MVVM package.

```csharp
internal sealed class DelegateCommand : ICommand
{
    private readonly Action<object> execute;
    private readonly Predicate<object> canExecute;

    internal DelegateCommand(Action<object> execute, Predicate<object> canExecute = null)
    {
        this.execute = execute ?? throw new ArgumentNullException(nameof(execute));
        this.canExecute = canExecute;
    }

    public event EventHandler CanExecuteChanged;

    public bool CanExecute(object parameter) => canExecute?.Invoke(parameter) ?? true;

    public void Execute(object parameter) => execute(parameter);

    internal void RaiseCanExecuteChanged()
        => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
```

`ShellViewModel` constructs three real view models and uses the following state transition method. A later plan may pass its fully implemented workspace through this method; this plan does not add a no-op backup command or a disabled control.

```csharp
internal void NavigateTo(object workspace, string workflowLabel)
{
    if (workspace == null)
        throw new ArgumentNullException(nameof(workspace));
    if (string.IsNullOrWhiteSpace(workflowLabel))
        throw new ArgumentException("A workflow label is required.", nameof(workflowLabel));

    CurrentWorkspace = workspace;
    WorkflowLabel = workflowLabel;
    OnPropertyChanged(nameof(CurrentWorkspace));
    OnPropertyChanged(nameof(WorkflowLabel));
}

internal void ShowTimeline() => NavigateTo(timeline, "Timeline");
```

The Timeline VM is intentionally small and truthful:

```csharp
internal sealed class TimelineWorkspaceViewModel
{
    public string EmptyStateTitle => "No local snapshots are available.";
    public string EmptyStateDescription => "Create a snapshot to begin protecting this PC.";
    public bool HasEvents => false;
}
```

It does not expose sample rows, a generated timestamp, a selection, a Compare command, or any hardcoded restore state. Timeline event reading is introduced by the Timeline plan.

`SettingsViewModel` exposes an `IReadOnlyList<ThemeMode> AvailableModes`, a `ThemeMode SelectedTheme`, and display labels exactly `Follow system`, `Light`, and `Dark`; assigning `SelectedTheme` calls `IThemeService.SetMode`. `AboutViewModel` exposes the Application-derived current version, `CheckForUpdatesCommand`, and `OpenReleaseNotesCommand`; it delegates user interaction to `WpfUpdatePresenter`/`IExternalLinkService` and never parses update or registry content.

- [ ] **Step 3: Implement accessible declarative workspace views and the top bar.**

Create `TimelineWorkspaceView` as a keyboard-focusable empty state, not a mock list. The only usable action in its body is the text explanation; do not add a fake `Create snapshot` control in this plan because the real backup workspace is not yet implemented.

```xml
<UserControl x:Class="WinRestoreKit.Wpf.Views.TimelineWorkspaceView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="clr-namespace:WinRestoreKit.Wpf.ViewModels"
             AutomationProperties.Name="Timeline workspace">
  <Border Background="{DynamicResource WindowBackgroundBrush}" Padding="40">
    <StackPanel MaxWidth="640" VerticalAlignment="Center">
      <TextBlock Text="Timeline" FontSize="28" FontWeight="SemiBold"
                 Foreground="{DynamicResource TextBrush}" />
      <TextBlock Text="{Binding EmptyStateTitle}" Margin="0,20,0,0" FontSize="20"
                 Foreground="{DynamicResource TextBrush}" />
      <TextBlock Text="{Binding EmptyStateDescription}" Margin="0,8,0,0" TextWrapping="Wrap"
                 Foreground="{DynamicResource MutedTextBrush}" />
    </StackPanel>
  </Border>
</UserControl>
```

Create `SettingsView` with a labeled `ComboBox` bound two-way to `SelectedTheme`. Create `AboutView` with version text, a **Check for updates** button, and a labeled **Release notes** link/button. Both bind entirely to their view model commands. Set AutomationProperties names on all controls and retain keyboard focus through normal WPF controls.

- [ ] **Step 4: Implement the MainWindow content host and XAML data templates.**

Use a compact top bar with wordmark, `WorkflowLabel`, Settings, and About commands; it has no permanent sidebar. The grid has a minimum usable width of 1024 and a content host bound to `CurrentWorkspace`.

```xml
<Window x:Class="WinRestoreKit.Wpf.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:views="clr-namespace:WinRestoreKit.Wpf.Views"
        xmlns:vm="clr-namespace:WinRestoreKit.Wpf.ViewModels"
        Title="WinRestoreKit" MinWidth="1024" MinHeight="640"
        Background="{DynamicResource WindowBackgroundBrush}">
  <Window.Resources>
    <DataTemplate DataType="{x:Type vm:TimelineWorkspaceViewModel}"><views:TimelineWorkspaceView /></DataTemplate>
    <DataTemplate DataType="{x:Type vm:SettingsViewModel}"><views:SettingsView /></DataTemplate>
    <DataTemplate DataType="{x:Type vm:AboutViewModel}"><views:AboutView /></DataTemplate>
  </Window.Resources>
  <DockPanel>
    <Border DockPanel.Dock="Top" Background="{DynamicResource SurfaceBrush}"
            BorderBrush="{DynamicResource DividerBrush}" BorderThickness="0,0,0,1" Padding="24,14">
      <Grid>
        <Grid.ColumnDefinitions><ColumnDefinition /><ColumnDefinition Width="Auto" /><ColumnDefinition Width="Auto" /></Grid.ColumnDefinitions>
        <StackPanel Orientation="Horizontal">
          <TextBlock Text="WinRestoreKit" FontSize="16" FontWeight="SemiBold" Foreground="{DynamicResource TextBrush}" />
          <TextBlock Text="{Binding WorkflowLabel}" Margin="18,2,0,0" Foreground="{DynamicResource MutedTextBrush}" />
        </StackPanel>
        <Button Grid.Column="1" Content="Settings" Command="{Binding ShowSettingsCommand}" AutomationProperties.Name="Settings" />
        <Button Grid.Column="2" Content="About" Command="{Binding ShowAboutCommand}" Margin="8,0,0,0" AutomationProperties.Name="About" />
      </Grid>
    </Border>
    <ContentControl Content="{Binding CurrentWorkspace}" />
  </DockPanel>
</Window>
```

The later Backup/Progress plan adds the real **Create snapshot** command and workspace at the same top-bar location; do not place a disabled button, a message-only action, or placeholder content there now.

- [ ] **Step 5: Run WPF construction tests to verify they pass.**

Run:

```powershell
dotnet test src\WinRestoreKit.Tests\WinRestoreKit.Tests.csproj --filter FullyQualifiedName~WpfShellTests
```

Expected: PASS. The test constructs view models on STA, proves Timeline is the default and only empty-state workspace, proves no rail navigation is required, and validates Settings/About state transitions without opening a window or accessing network/registry state.

- [ ] **Step 6: Commit the MVVM shell views.**

```powershell
git add src\WinRestoreKit.Wpf\Infrastructure src\WinRestoreKit.Wpf\ViewModels src\WinRestoreKit.Wpf\Views src\WinRestoreKit.Tests\WpfShellTests.cs
git commit -m "feat: add WPF timeline shell empty state"
```

### Task 6: Compose WPF startup, implement framework adapters, and smoke-test both shells

**Files:**
- Modify: `src/WinRestoreKit.Wpf/App.xaml`
- Modify: `src/WinRestoreKit.Wpf/App.xaml.cs`
- Create: `src/WinRestoreKit.Wpf/MainWindow.xaml.cs`
- Create: `src/WinRestoreKit.Wpf/Services/WpfDispatcher.cs`
- Create: `src/WinRestoreKit.Wpf/Services/IRunPresentation.cs`
- Create: `src/WinRestoreKit.Wpf/Services/IRunDialogService.cs`
- Create: `src/WinRestoreKit.Wpf/Services/WpfRunUi.cs`
- Create: `src/WinRestoreKit.Wpf/Services/WpfLogSink.cs`
- Modify: `src/WinRestoreKit.Tests/WpfShellTests.cs`

**Interfaces:**
- Consumes: Application update/settings/orchestration contracts from Tasks 2–4; WPF services/theme/view models from Tasks 3–5; Core `ILogSink` and `Utils.UrlFailureUi` seam.
- Produces: A real WPF process that creates Application settings/update/theme services, registers owner-bound error presentation, binds `MainWindow` to `ShellViewModel`, and opens the Timeline empty state. It also produces the exact run adapter contracts future workflow plans instantiate.

- [ ] **Step 1: Write the failing WPF window-composition test.**

Extend `WpfShellTests` to construct a `MainWindow` from a composed `ShellViewModel` and inspect its observable state. Do not call `Show`, create sample snapshots, or invoke network operations.

```csharp
[Fact]
public void MainWindow_ComposesTheTimelineWorkspaceWithoutAnySnapshotRows()
{
    WpfTestHost.Run(() =>
    {
        ShellViewModel shell = CreateShell();
        MainWindow window = new MainWindow(shell);
        Assert.Equal("WinRestoreKit", window.Title);
        Assert.Same(shell, window.DataContext);
        Assert.IsType<TimelineWorkspaceViewModel>(shell.CurrentWorkspace);
        Assert.False(((TimelineWorkspaceViewModel)shell.CurrentWorkspace).HasEvents);
    });
}
```

Run:

```powershell
dotnet test src\WinRestoreKit.Tests\WinRestoreKit.Tests.csproj --filter FullyQualifiedName~WpfShellTests
```

Expected: FAIL to compile because `MainWindow` code-behind and WPF startup composition do not exist.

- [ ] **Step 2: Implement WPF dispatcher, dialog, log, and run adapters with shell-owned modal behavior.**

`WpfDispatcher` is a thin wrapper around `Dispatcher` used by adapters to check access and invoke work. `WpfLogSink` implements Core’s internal `ILogSink` and always dispatches append/clear callbacks rather than touching a view from a worker thread.

```csharp
internal sealed class WpfLogSink : ILogSink
{
    private readonly Dispatcher dispatcher;
    private readonly Action<string> append;
    private readonly Action clear;

    internal WpfLogSink(Dispatcher dispatcher, Action<string> append, Action clear)
    {
        this.dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        this.append = append ?? throw new ArgumentNullException(nameof(append));
        this.clear = clear ?? throw new ArgumentNullException(nameof(clear));
    }

    public void Append(string text) => dispatcher.BeginInvoke(append, text);
    public void Clear() => dispatcher.BeginInvoke(clear);
}
```

`WpfRunUi` implements every Application callback using the exact constructor and interfaces declared earlier. Progress and summary calls marshal to `IRunPresentation`; consent, snapshot override, and plan-composition methods invoke `IRunDialogService` synchronously on the WPF dispatcher. For the unchanged Core AppStore seam, it supplies the current shell `Window` only through the framework-neutral `object IRunUi.DialogOwner` property; Application never casts it or references WPF.

```csharp
internal T InvokeDialog<T>(Func<T> action)
{
    return dispatcher.CheckAccess() ? action() : dispatcher.Invoke(action);
}

object IRunUi.DialogOwner
    => InvokeDialog(() => (object)ownerProvider());
```

The first concrete `IRunDialogService` implementations arrive with the real Confirm and Backup/Progress views. The adapter does not intercept or replace Core’s AppStore dialog seam.

- [ ] **Step 3: Compose startup and register Core’s shell-owned URL failure seam.**

`App.xaml` merges only the framework-independent control styles. `App.OnStartup` creates Application settings/update services, WPF theme/detector/dialog/link services, a WPF update presenter, the Shell VM, and the `MainWindow`; it calls `Show` only after the window is available as an owner. It registers `Utils.UrlFailureUi` with an action that dispatcher-marshals an owner-bound warning through `IWpfDialogService`.

```xml
<!-- src/WinRestoreKit.Wpf/App.xaml -->
<Application x:Class="WinRestoreKit.Wpf.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
  <Application.Resources>
    <ResourceDictionary>
      <ResourceDictionary.MergedDictionaries>
        <ResourceDictionary Source="Themes/Controls.xaml" />
      </ResourceDictionary.MergedDictionaries>
    </ResourceDictionary>
  </Application.Resources>
</Application>
```

```csharp
private WpfThemeService themes;

protected override void OnStartup(StartupEventArgs e)
{
    base.OnStartup(e);

    RegistryThemeSettings settings = new RegistryThemeSettings();
    WpfDialogService dialogs = new WpfDialogService(Dispatcher, () => MainWindow);
    themes = new WpfThemeService(Resources, settings, new WindowsThemeDetector());
    WpfUpdatePresenter updates = new WpfUpdatePresenter(new UpdateCheckService(), dialogs,
                                                         new ExternalLinkService());
    ShellViewModel shell = new ShellViewModel(themes, updates,
        VersionInfo.GetCurrentVersion(typeof(App).Assembly));
    MainWindow window = new MainWindow(shell);

    Utils.UrlFailureUi = (url, exception) => Dispatcher.BeginInvoke(() =>
        dialogs.ShowWarning("Could not open this link in your browser:\n\n" + url + "\n\n" +
                            exception.Message, "Unable to open link"));

    MainWindow = window;
    window.Show();
}

protected override void OnExit(ExitEventArgs e)
{
    themes?.Dispose();
    base.OnExit(e);
}
```

The `OnExit` override above disposes `WpfThemeService` before calling `base.OnExit(e)` so the static system-preference handler is always removed. Do not register a global exception policy, alter Core’s existing static app-restore seam, or start a backup/restore run in the shell startup.

```csharp
public partial class MainWindow : Window
{
    public MainWindow(ShellViewModel shell)
    {
        InitializeComponent();
        DataContext = shell ?? throw new ArgumentNullException(nameof(shell));
    }
}
```

- [ ] **Step 4: Re-run the construction test and focused foundation tests.**

Run:

```powershell
dotnet test src\WinRestoreKit.Tests\WinRestoreKit.Tests.csproj --filter "FullyQualifiedName~WpfShellTests|FullyQualifiedName~ThemeSettingsTests|FullyQualifiedName~ThemeServiceTests|FullyQualifiedName~VersionParsingTests|FullyQualifiedName~UpdateCheckVerdictTests|FullyQualifiedName~RunSummaryTests|FullyQualifiedName~RunCoordinatorTests|FullyQualifiedName~RunControlTests|FullyQualifiedName~BackupDestinationLifecycleTests"
```

Expected: PASS. Tests construct `MainWindow` on an STA, verify the default Timeline empty state, prove all theme/update/run contracts, and preserve the existing pure run/root tests.

- [ ] **Step 5: Build both hosts and perform the real WPF smoke test.**

Run:

```powershell
dotnet build src\WinRestoreKit.Wpf\WinRestoreKit.Wpf.csproj
dotnet build src\WinRestoreKit\WinRestoreKit.csproj
dotnet run --project src\WinRestoreKit.Wpf\WinRestoreKit.Wpf.csproj --no-build
```

Expected: both build commands PASS. The last command opens a `WinRestoreKit`-titled WPF window with the compact top bar, Timeline workflow label, and the exact empty state **No local snapshots are available.** / **Create a snapshot to begin protecting this PC.** It displays no fabricated snapshot rows, no permanent sidebar, no comparison/restore controls, and no backup/progress workflow. Use Settings to select Light, Dark, and Follow system; close the window normally after confirming each selection changes resources and persists `ThemeMode` under the product registry key. Open About, invoke **Check for updates** only if network access is available, and verify any result/error is owner-bound; use **Release notes** only to verify the existing URL error seam when a launch failure is forced in a controlled test environment.

- [ ] **Step 6: Commit the runnable WPF foundation.**

```powershell
git add src\WinRestoreKit.Wpf\App.xaml src\WinRestoreKit.Wpf\App.xaml.cs src\WinRestoreKit.Wpf\MainWindow.xaml.cs src\WinRestoreKit.Wpf\Services\WpfDispatcher.cs src\WinRestoreKit.Wpf\Services\IRunPresentation.cs src\WinRestoreKit.Wpf\Services\IRunDialogService.cs src\WinRestoreKit.Wpf\Services\WpfRunUi.cs src\WinRestoreKit.Wpf\Services\WpfLogSink.cs src\WinRestoreKit.Tests\WpfShellTests.cs
git commit -m "feat: compose runnable WPF application shell"
```

## Final Foundation Verification

- [ ] Confirm `src/WinRestoreKit.Application/WinRestoreKit.Application.csproj` contains neither `UseWindowsForms` nor `UseWPF`, while `dotnet build` for it succeeds against Core.
- [ ] Confirm `IRunUi` has `object DialogOwner` and no WinForms-typed `Owner` member; confirm `BackupRestoreOrchestrator` preserves the `AppStoreApps.RestoreAsync(currentRestorePath, ui.DialogOwner)` Core seam; confirm `RunSummary` has `RunSeverity` rather than `MessageBoxIcon`; and confirm the WinForms project still builds/runs through its updated `ProgressPageView` adapter.
- [ ] Confirm no duplicate `BackupRootRegistry`, `RunControl`, `RunCoordinator`, `BackupRestoreOrchestrator`, `IRunUi`, `RunSummary`, or `UpdateCheck` source remains under the old WinForms paths.
- [ ] Confirm WPF uses the new `ThemeMode` DWORD and resource dictionaries rather than WinForms `PaletteMode`/Voltage/Flux, with owner-bound settings/about/update interactions.
- [ ] Confirm the side-by-side WPF project still links the existing manifest/icon and preserves the final single-file publish settings without consuming the legacy AssemblyInfo source or changing the temporary `WinRestoreKit.Wpf` identity.
- [ ] Re-run the Task 6 focused test command, build both app projects, and repeat the real WPF window smoke test. Expected: all named tests/builds pass; the WPF app opens directly to the real empty Timeline workspace; the WinForms app remains runnable; no Timeline catalog, Compare, Confirm, backup workflow, progress UI, or cutover deletion has been introduced.
