using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Threading;
using WinRestoreKit;
using WinRestoreKit.Wpf.ViewModels;
using WinRestoreKit.Wpf.Navigation;
using WinRestoreKit.Wpf.Services;
using Xunit;

namespace WinRestoreKit.Tests
{
    public sealed class ComparisonWorkspaceViewModelTests
    {
        [Fact]
        public async Task Workspace_DefaultsToAllAndChangedOnlyDoesNotChangeRestoreSet()
        {
            await WpfTestHost.RunAsync(async () =>
            {
                ComparisonWorkspaceViewModel workspace = await LoadedWorkspace(
                    new TestModule("Changed", artifact: true, drift: true),
                    new TestModule("Same", artifact: true, drift: false),
                    new TestModule("Unknown", artifact: true, drift: null),
                    new TestModule("Absent", artifact: false, drift: null));

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
        public async Task Workspace_SelectedRowExposesOnlyDeclaredImpacts()
        {
            await WpfTestHost.RunAsync(async () =>
            {
                TestModule module = new TestModule("Settings", true, true)
                {
                    Targets = new[] { RestoreTarget.RegistryKey(@"HKCU\\Software\\Test") },
                    Processes = new[] { new RestoreCloseRequirement("Code", "Visual Studio Code", true) },
                    Explorer = true,
                    Warning = "Existing module warning."
                };
                ComparisonWorkspaceViewModel workspace = await LoadedWorkspace(module, category: "Settings");
                workspace.SelectedRow = workspace.Rows[0];

                Assert.True(workspace.IsDetailTrayOpen);
                Assert.Equal("Settings", workspace.SelectedRow.Category);
                Assert.Contains(workspace.SelectedRow.Impact.Targets, item => item.Kind == RestoreTargetKind.RegistryKey);
                Assert.Contains(workspace.SelectedRow.Impact.Processes,
                    item => item.NeedsConsent && item.DisplayName == "Visual Studio Code");
                Assert.True(workspace.SelectedRow.Impact.RequiresExplorerRestart);
                Assert.Equal("Existing module warning.", workspace.SelectedRow.Impact.WarningMessage);
            });
        }

        [Fact]
        public async Task Workspace_ContinueToConfirmUsesTheCurrentWholeModuleRestoreSet()
        {
            await WpfTestHost.RunAsync(async () =>
            {
                SnapshotEvent receivedSnapshot = null;
                IReadOnlyList<BackupBase> receivedModules = null;
                TestModule module = new TestModule("Changed", true, true);
                ComparisonWorkspaceViewModel workspace = await LoadedWorkspace(
                    new[] { module },
                    (snapshot, modules) => { receivedSnapshot = snapshot; receivedModules = modules; });
                workspace.RestoreSet.Add(workspace.Rows[0].Comparison);

                workspace.ContinueToConfirmCommand.Execute(null);

                Assert.Same(workspace.Snapshot, receivedSnapshot);
                Assert.Single(receivedModules);
                Assert.Same(module, receivedModules[0]);
            });
        }

        [Fact]
        public async Task Navigator_ChangingSnapshotWithRestoreSet_CancelKeepsOriginalSetAndDisposesIncomingScope()
        {
            await WpfTestHost.RunAsync(async () =>
            {
                Window owner = new Window();
                TestDiscardDialog dialogs = new TestDiscardDialog(false);
                ShellViewModel shell = TestShell();
                CompareWorkflowNavigator navigator = new CompareWorkflowNavigator(shell, owner, dialogs);
                navigator.OpenCompare(Prepared("first"));
                await navigator.PendingTransition;

                ModuleComparison selected = new ModuleComparison(
                    navigator.CurrentWorkspace.Rows[0].Registration.Module,
                    ComparisonState.Unavailable, true, "Artifact captured.", "Comparison unavailable.");
                navigator.CurrentWorkspace.RestoreSet.Add(selected);

                string incomingOwnedPath = Path.Combine(Path.GetTempPath(), "WinRestoreKit.Tests", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(incomingOwnedPath);
                SnapshotPayloadPreparation incoming = new SnapshotPayloadPreparation(
                    SnapshotFor(incomingOwnedPath, "second"),
                    new BackupPayload.ReadScope(incomingOwnedPath, incomingOwnedPath), null);

                navigator.OpenCompare(incoming);
                await navigator.PendingTransition;

                Assert.Equal("first", navigator.CurrentWorkspace.Snapshot.DisplayName);
                Assert.True(navigator.CurrentWorkspace.RestoreSet.HasItems);
                Assert.False(Directory.Exists(incomingOwnedPath));
                owner.Close();
            });
        }

        [Fact]
        public async Task Navigator_LeavingCompareClearsStateAndAllowsTheSameSnapshotToOpenAgain()
        {
            await WpfTestHost.RunAsync(async () =>
            {
                Window owner = new Window();
                ShellViewModel shell = TestShell();
                CompareWorkflowNavigator navigator = new CompareWorkflowNavigator(
                    shell, owner, new TestDiscardDialog(true));
                string path = Path.Combine(Path.GetTempPath(), "WinRestoreKit.Tests", Guid.NewGuid().ToString("N"));

                Directory.CreateDirectory(path);
                navigator.OpenCompare(PreparedAt(path, "same snapshot"));
                await navigator.PendingTransition;
                ComparisonWorkspaceViewModel firstWorkspace = navigator.CurrentWorkspace;
                firstWorkspace.RestoreSet.Add(new ModuleComparison(
                    firstWorkspace.Rows[0].Registration.Module,
                    ComparisonState.Unavailable, true, "Artifact captured.", "Comparison unavailable."));

                await navigator.LeaveCompareAsync();

                Assert.Null(navigator.CurrentWorkspace);
                Assert.False(firstWorkspace.RestoreSet.HasItems);

                Directory.CreateDirectory(path);
                navigator.OpenCompare(PreparedAt(path, "same snapshot"));
                await navigator.PendingTransition;

                Assert.NotNull(navigator.CurrentWorkspace);
                Assert.NotSame(firstWorkspace, navigator.CurrentWorkspace);
                Assert.Equal(Path.GetFullPath(path), navigator.CurrentWorkspace.Snapshot.CanonicalPath);

                await navigator.LeaveCompareAsync();
                owner.Close();
            });
        }

        private static async Task<ComparisonWorkspaceViewModel> LoadedWorkspace(
            params TestModule[] modules)
            => await LoadedWorkspace(modules, (_, __) => { });

        private static async Task<ComparisonWorkspaceViewModel> LoadedWorkspace(
            TestModule module, string category)
            => await LoadedWorkspace(new[] { module }, (_, __) => { }, category);

        private static async Task<ComparisonWorkspaceViewModel> LoadedWorkspace(
            TestModule[] modules, Action<SnapshotEvent, IReadOnlyList<BackupBase>> onConfirm, string category = "General")
        {
            string folder = Path.Combine(Path.GetTempPath(), "WinRestoreKit.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            SnapshotEvent snapshot = new SnapshotEvent(SnapshotEventKind.Verified, DateTime.UtcNow, "snapshot",
                folder, string.Empty, string.Empty, 0, true, null);
            SnapshotPayloadPreparation preparation = new SnapshotPayloadPreparation(
                snapshot, new BackupPayload.ReadScope(folder, folder), null);
            BackupModuleRegistration[] registrations = modules
                .Select(module => new BackupModuleRegistration(module, category)).ToArray();
            ComparisonWorkspaceViewModel workspace = new ComparisonWorkspaceViewModel(
                snapshot, registrations, new SnapshotComparisonService(), onConfirm);
            await workspace.StartAsync(preparation);
            return workspace;
        }

        private static SnapshotPayloadPreparation Prepared(string name)
        {
            string path = Path.Combine(Path.GetTempPath(), "WinRestoreKit.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return PreparedAt(path, name);
        }

        private static SnapshotPayloadPreparation PreparedAt(string path, string name)
            => new SnapshotPayloadPreparation(
                SnapshotFor(path, name), new BackupPayload.ReadScope(path, path), null);

        private static SnapshotEvent SnapshotFor(string path, string name)
            => new SnapshotEvent(SnapshotEventKind.Verified, DateTime.UtcNow, name, path,
                string.Empty, string.Empty, 0, true, null);

        private static ShellViewModel TestShell()
            => new ShellViewModel(new TestThemes(),
                new WpfUpdatePresenter(new TestUpdates(), new TestDialogs(), new TestLinks()), "0.0.1");

        private sealed class TestDiscardDialog : ICompareDialogService
        {
            private readonly bool answer;

            internal TestDiscardDialog(bool answer) => this.answer = answer;
            public bool ConfirmDiscardRestoreSet(Window owner, SnapshotEvent current, SnapshotEvent incoming) => answer;
            public void ShowSnapshotDiagnostic(Window owner, SnapshotEvent snapshot) { }
        }

        private sealed class TestThemes : IThemeService
        {
            public ThemeMode Mode => ThemeMode.Light;
            public ThemeMode EffectiveMode => ThemeMode.Light;
            public event EventHandler ThemeChanged;
            public void SetMode(ThemeMode mode) => ThemeChanged?.Invoke(this, EventArgs.Empty);
            public void Dispose() { }
        }

        private sealed class TestUpdates : IUpdateCheckService
        {
            public Task<UpdateCheckResult> CheckAsync(string currentVersion, CancellationToken cancellationToken)
                => Task.FromResult(new UpdateCheckResult(UpdateVerdict.UpToDate, currentVersion, currentVersion));
        }

        private sealed class TestDialogs : IWpfDialogService
        {
            public void ShowInformation(string text, string caption) { }
            public void ShowWarning(string text, string caption) { }
            public void ShowError(string text, string caption) { }
            public bool Confirm(string text, string caption) => false;
        }

        private sealed class TestLinks : IExternalLinkService
        {
            public void Open(string url) { }
        }

        private sealed class TestModule : BackupBase
        {
            private readonly bool artifact;
            private readonly bool? drift;

            internal TestModule(string title, bool artifact, bool? drift)
            {
                Title = title;
                this.artifact = artifact;
                this.drift = drift;
            }

            internal IReadOnlyList<RestoreTarget> Targets { get; set; } = Array.Empty<RestoreTarget>();
            internal IReadOnlyList<RestoreCloseRequirement> Processes { get; set; } = Array.Empty<RestoreCloseRequirement>();
            internal bool Explorer { get; set; }
            internal string Warning { get; set; } = string.Empty;
            public override IReadOnlyList<RestoreTarget> RestoreTargets => Targets;
            public override IReadOnlyList<RestoreCloseRequirement> ProcessesToCloseBeforeRestore => Processes;
            public override bool RequiresExplorerRestart => Explorer;
            public override string WarningMessage => Warning;
            public override bool? HasArtifactIn(string backupPath) => artifact;
            public override bool? HasDriftedFrom(string backupPath) => drift;
        }
    }
}
