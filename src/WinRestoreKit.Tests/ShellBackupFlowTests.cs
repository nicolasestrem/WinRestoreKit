using System;
using System.Linq;
using System.Threading.Tasks;
using WinRestoreKit;
using WinRestoreKit.Wpf.ViewModels;
using Xunit;

namespace WinRestoreKit.Tests
{
    public sealed class ShellBackupFlowTests
    {
        [Fact]
        public async Task CreateSnapshot_AdmitsOneRun_ShowsResult_ThenRefreshesTimeline()
        {
            using var isolation = new BackupRunIsolation();
            BackupRootRegistry.Remember(isolation.DestinationRoot);
            RunCoordinator.SetRunning(false);

            try
            {
                int refreshes = 0;
                ModuleOutcome outcome = SucceededOutcome();
                RunSummary summary = RunSummary.For(new[] { outcome }, true, RunVerb.Backup);
                var shell = ShellViewModel.ForTest(
                    _ => Task.FromResult(new BackupRunCompletion(summary, new[] { outcome },
                        @"C:\snapshots\snapshot-started-before-rollover")),
                    new SnapshotEventCatalog(),
                    () =>
                    {
                        refreshes++;
                        return Task.CompletedTask;
                    });

                shell.CreateSnapshotCommand.Execute(null);
                BackupWorkspaceViewModel selection = Assert.IsType<BackupWorkspaceViewModel>(shell.CurrentWorkspace);
                selection.Scopes.First().IsSelected = true;
                await selection.StartAsync();

                ResultWorkspaceViewModel result = Assert.IsType<ResultWorkspaceViewModel>(shell.CurrentWorkspace);
                Assert.False(RunCoordinator.IsRunning);
                Assert.Equal(1, refreshes);

                result.ReturnToTimelineCommand.Execute(null);

                Assert.Equal(1, refreshes);
            }
            finally
            {
                RunCoordinator.SetRunning(false);
            }
        }

        [Fact]
        public async Task CreateSnapshot_WhenRunIsAlreadyActive_LeavesSelectionAndDoesNotConstructRunner()
        {
            using var isolation = new BackupRunIsolation();
            BackupRootRegistry.Remember(isolation.DestinationRoot);
            RunCoordinator.SetRunning(false);

            try
            {
                int runRequests = 0;
                var shell = ShellViewModel.ForTest(
                    _ =>
                    {
                        runRequests++;
                        return Task.FromResult(new BackupRunCompletion(
                            RunSummary.Canceled(RunVerb.Backup), Array.Empty<ModuleOutcome>(), string.Empty));
                    },
                    new SnapshotEventCatalog(),
                    () => Task.CompletedTask);

                shell.CreateSnapshotCommand.Execute(null);
                BackupWorkspaceViewModel selection = Assert.IsType<BackupWorkspaceViewModel>(shell.CurrentWorkspace);
                selection.Scopes.First().IsSelected = true;
                RunCoordinator.SetRunning(true);

                await selection.StartAsync();

                Assert.Same(selection, shell.CurrentWorkspace);
                Assert.Equal(0, runRequests);
                Assert.Equal("Another backup or restore is already running.", selection.ValidationMessage);
            }
            finally
            {
                RunCoordinator.SetRunning(false);
            }
        }

        private static ModuleOutcome SucceededOutcome()
        {
            return new ModuleOutcome("Mouse",
                ModuleResult.Aggregate(new[] { StepResult.Succeeded("key", "exported key") }));
        }
    }
}
