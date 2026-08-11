using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using WinRestoreKit;
using WinRestoreKit.Wpf.Services;
using WinRestoreKit.Wpf.ViewModels;
using Xunit;

namespace WinRestoreKit.Tests
{
    public sealed class ConfirmViewModelTests
    {
        [Fact]
        public void Confirm_GroupsOnlyExistingProcessAndExplorerImpacts()
        {
            WpfTestHost.Run(() =>
            {
                TestModule consent = new TestModule("Code")
                {
                    Processes = new[] { new RestoreCloseRequirement("code", "Visual Studio Code", true) }
                };
                TestModule explorer = new TestModule("Taskbar")
                {
                    Explorer = true,
                    Warning = "Existing sign-out warning."
                };
                ConfirmViewModel viewModel = new ConfirmViewModel(Snapshot(), new BackupBase[] { consent, explorer });

                Assert.Equal(RestorePlan.FidelityCaveat, viewModel.FidelityCaveat);
                Assert.Contains(viewModel.ConsentProcesses, item => item.DisplayName == "Visual Studio Code");
                Assert.Contains(viewModel.ExplorerRestartModules, item => item.Title == "Taskbar");
                Assert.Contains(viewModel.ModuleWarnings, item => item.Text == "Existing sign-out warning.");
                Assert.Equal("2 selected modules will be evaluated for restore from this snapshot.",
                    viewModel.RestoreSelectionSummary);
            });
        }

        [Fact]
        public void Confirm_InteractiveOnlyModuleUsesSingularGrammarAndNoSnapshotNotice()
        {
            WpfTestHost.Run(() =>
            {
                ConfirmViewModel viewModel = new ConfirmViewModel(Snapshot(),
                    new BackupBase[] { new InteractiveModule("Remember installed apps") });

                Assert.Equal("1 selected module will be evaluated for restore from this snapshot.",
                    viewModel.RestoreSelectionSummary);
                Assert.Equal(RestorePlan.NoSnapshotNotice, viewModel.FidelityCaveat);
            });
        }

        [Fact]
        public void Confirm_PartialSourceStaysExplicitWithoutBypassingSnapshotGate()
        {
            WpfTestHost.Run(() =>
            {
                ConfirmViewModel viewModel = new ConfirmViewModel(Snapshot(SnapshotEventKind.Partial), new BackupBase[] { new TestModule("Module") });

                Assert.Equal(SnapshotEventKind.Partial, viewModel.Snapshot.Kind);
                Assert.True(viewModel.IsSourcePartial);
            });
        }

        [Fact]
        public async Task Confirm_StartRestore_InvokesExistingOrchestratorWithSelectedSource()
        {
            await WpfTestHost.RunAsync(async () =>
            {
                string source = Path.Combine(Path.GetTempPath(), "WinRestoreKit.Tests", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(source);
                try
                {
                    CancelingRunDialogService dialogs = new CancelingRunDialogService();
                    int completions = 0;
                    bool callbackSawReleasedRun = false;
                    RunSummary completedSummary = null;
                    ConfirmViewModel viewModel = new ConfirmViewModel(
                        Snapshot(SnapshotEventKind.Verified, source),
                        new BackupBase[] { new TestModule("Module") },
                        showResult: (summary, _) =>
                        {
                            completions++;
                            completedSummary = summary;
                            callbackSawReleasedRun = !RunCoordinator.IsRunning;
                        });
                    Window owner = new Window();
                    viewModel.AttachRunSurfaces(Dispatcher.CurrentDispatcher, () => owner, dialogs);

                    await viewModel.StartRestoreAsync();

                    Assert.Equal(Path.GetFullPath(source), dialogs.LastRestorePlan.RestoreSourcePath);
                    Assert.Single(dialogs.LastRestorePlan.Modules);
                    Assert.Contains("canceled", viewModel.Summary.Headline, StringComparison.OrdinalIgnoreCase);
                    Assert.Same(viewModel.Summary, completedSummary);
                    Assert.Equal(1, completions);
                    Assert.True(callbackSawReleasedRun);
                    Assert.True(viewModel.HasCompleted);
                    Assert.False(viewModel.CanStartRestore);
                    Assert.False(RunCoordinator.IsRunning);

                    await viewModel.StartRestoreAsync();
                    Assert.Equal(1, completions);
                    owner.Close();
                }
                finally
                {
                    if (Directory.Exists(source))
                        Directory.Delete(source, true);
                }
            });
        }

        private static SnapshotEvent Snapshot(SnapshotEventKind kind = SnapshotEventKind.Verified, string path = null)
        {
            path = path ?? Path.GetTempPath();
            return new SnapshotEvent(kind, DateTime.UtcNow, "snapshot", path, string.Empty, string.Empty, 0, true, null);
        }

        private sealed class CancelingRunDialogService : IRunDialogService
        {
            public RestorePlan LastRestorePlan { get; private set; }
            public IReadOnlyList<string> ShowRestoreConsent(RestorePlan plan) { LastRestorePlan = plan; return null; }
            public bool ConfirmSnapshotOverride(string text, string caption) => false;
            public void ShowPlanCompositionError(string text, string caption) { }
        }

        private class TestModule : BackupBase
        {
            internal TestModule(string title) => Title = title;
            internal IReadOnlyList<RestoreCloseRequirement> Processes { get; set; } = Array.Empty<RestoreCloseRequirement>();
            internal bool Explorer { get; set; }
            internal string Warning { get; set; } = string.Empty;
            public override IReadOnlyList<RestoreCloseRequirement> ProcessesToCloseBeforeRestore => Processes;
            public override bool RequiresExplorerRestart => Explorer;
            public override string WarningMessage => Warning;
        }

        private sealed class InteractiveModule : TestModule
        {
            internal InteractiveModule(string title) : base(title) { }
            public override bool RestoreMakesChanges => false;
        }
    }
}
