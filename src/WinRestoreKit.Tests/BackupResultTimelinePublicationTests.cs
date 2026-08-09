using System;
using Conf;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using WinRestoreKit;
using WinRestoreKit.Wpf.ViewModels;
using Xunit;

namespace WinRestoreKit.Tests
{
    public sealed class BackupResultTimelinePublicationTests
    {
        [Fact]
        public async Task CanceledRunWithoutRetainedFolder_PublishesOneSessionFailureBeforeShowingResult()
        {
            using var isolation = new BackupRunIsolation();
            BackupRootRegistry.Remember(isolation.DestinationRoot);
            RunCoordinator.SetRunning(false);

            try
            {
                string attemptedPath = Path.Combine(isolation.DestinationRoot, "canceled-before-rollover");
                RunSummary summary = RunSummary.Incomplete(Array.Empty<ModuleOutcome>(), RunVerb.Backup,
                    "Cancellation was requested. No further group was started.");
                var catalog = new SnapshotEventCatalog();
                var shell = ShellViewModel.ForTest(
                    _ => Task.FromResult(new BackupRunCompletion(summary, Array.Empty<ModuleOutcome>(), attemptedPath)),
                    catalog,
                    () => Task.CompletedTask);

                shell.CreateSnapshotCommand.Execute(null);
                BackupWorkspaceViewModel selection = Assert.IsType<BackupWorkspaceViewModel>(shell.CurrentWorkspace);
                selection.Scopes.First().IsSelected = true;
                selection.SnapshotName = "canceled-cleanup";
                await selection.StartAsync();

                SnapshotEvent failure = Assert.Single(catalog.Read(), snapshot =>
                    snapshot.Kind == SnapshotEventKind.Failed
                    && snapshot.DisplayName == "canceled-cleanup");
                Assert.False(failure.IsRestorable);
                Assert.Equal(summary.Detail, failure.DiagnosticReason);
                Assert.IsType<ResultWorkspaceViewModel>(shell.CurrentWorkspace);
            }
            finally
            {
                RunCoordinator.SetRunning(false);
            }
        }

        [Fact]
        public async Task CanceledRunWithRetainedPartialFolder_UsesDiscoveredEventWithoutSessionFailure()
        {
            using var isolation = new BackupRunIsolation();
            BackupRootRegistry.Remember(isolation.DestinationRoot);
            RunCoordinator.SetRunning(false);

            try
            {
                string attemptedPath = Directory.CreateDirectory(
                    Path.Combine(isolation.DestinationRoot, "retained-partial-before-rollover")).FullName;
                File.WriteAllText(Path.Combine(attemptedPath, BackupManifest.FileName), BackupManifest.Compose(
                    new BackupBase[] { new DMouse() }, Array.Empty<ModuleResult>(),
                    new DateTime(2026, 8, 9, 9, 0, 0), "test-machine", "test-user", "test-build", "0.0.0"));
                RunSummary summary = RunSummary.Incomplete(Array.Empty<ModuleOutcome>(), RunVerb.Backup,
                    "Cancellation was requested. No further group was started.");
                var catalog = new SnapshotEventCatalog();
                var shell = ShellViewModel.ForTest(
                    _ => Task.FromResult(new BackupRunCompletion(summary, Array.Empty<ModuleOutcome>(), attemptedPath)),
                    catalog,
                    () => Task.CompletedTask);

                shell.CreateSnapshotCommand.Execute(null);
                BackupWorkspaceViewModel selection = Assert.IsType<BackupWorkspaceViewModel>(shell.CurrentWorkspace);
                selection.Scopes.First().IsSelected = true;
                selection.SnapshotName = "retained-partial";
                await selection.StartAsync();

                string canonicalPath = Path.GetFullPath(attemptedPath);
                SnapshotEvent discovered = Assert.Single(catalog.Read(), snapshot =>
                    string.Equals(snapshot.CanonicalPath, canonicalPath,
                        StringComparison.OrdinalIgnoreCase));
                Assert.Equal(SnapshotEventKind.Partial, discovered.Kind);
                Assert.True(discovered.IsRestorable);
                Assert.DoesNotContain(catalog.Read(), snapshot => snapshot.Kind == SnapshotEventKind.Failed
                    && snapshot.DisplayName == "retained-partial");
            }
            finally
            {
                RunCoordinator.SetRunning(false);
            }
        }
    }
}
