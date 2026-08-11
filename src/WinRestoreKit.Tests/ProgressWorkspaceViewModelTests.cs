using System;
using DataHelper;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using WinRestoreKit;
using WinRestoreKit.Wpf.Services;
using WinRestoreKit.Wpf.ViewModels;
using Xunit;

namespace WinRestoreKit.Tests
{
    public sealed class ProgressWorkspaceViewModelTests
    {
        [Fact]
        public void PauseCommand_ChangesOnlyBoundaryStateAndLogsExactMessages()
        {
            WpfTestHost.Run(() =>
            {
                ProgressWorkspaceViewModel vm = CreateViewModel(new ConfirmingDialogs(false));

                vm.PauseCommand.Execute(null);

                Assert.True(vm.IsPaused);
                Assert.Equal("Resume", vm.PauseCaption);
                Assert.Equal("Run paused. The active group will finish before pausing.",
                    Assert.Single(vm.LogLines).Text);

                vm.PauseCommand.Execute(null);

                Assert.False(vm.IsPaused);
                Assert.Equal("Pause", vm.PauseCaption);
                Assert.Equal(new[]
                {
                    "Run paused. The active group will finish before pausing.",
                    "Run resumed. The next group may start."
                }, vm.LogLines.Select(line => line.Text));
            });
        }

        [Fact]
        public void CancelCommand_WhenConfirmed_DisablesActionsAndLogsActiveGroupWarning()
        {
            WpfTestHost.Run(() =>
            {
                ProgressWorkspaceViewModel vm = CreateViewModel(new ConfirmingDialogs(true));

                vm.CancelCommand.Execute(null);

                Assert.True(vm.IsCancellationRequested);
                Assert.False(vm.CanPause);
                Assert.False(vm.CanCancel);
                Assert.Equal("Cancellation requested. The active group will finish before cancellation.",
                    Assert.Single(vm.LogLines).Text);
            });
        }

        [Fact]
        public void ArchiveProgress_DisablesCancellationWithoutInventingMetrics()
        {
            WpfTestHost.Run(() =>
            {
                ProgressWorkspaceViewModel vm = CreateViewModel(new ConfirmingDialogs(false));

                vm.SetProgressDetail("Group 1 of 1. Mouse", "00:01", "N/A", "N/A", 0, 2, 1);
                vm.SetProgressText(BackupRestoreOrchestrator.ArchiveProgressText);

                Assert.False(vm.CanCancel);
                Assert.Equal("N/A", vm.Remaining);
                Assert.Equal("N/A", vm.Throughput);
                Assert.Equal(0, vm.BytesWritten);
                Assert.Equal(2, vm.Errors);
                Assert.Equal(1, vm.Warnings);
            });
        }

        [Fact]
        public async Task RunBackupAsync_ExecutorWithoutSummary_UsesDidNotRunFallback()
        {
            await WpfTestHost.RunAsync(async () =>
            {
                ProgressWorkspaceViewModel vm = CreateViewModel(new ConfirmingDialogs(false),
                    _ => Task.CompletedTask);

                RunSummary summary = await vm.RunBackupAsync(new BackupRunRequest(
                    Array.Empty<BackupBase>(), "nightly", SnapshotCompression.Fast, @"C:\snapshots"));

                Assert.Equal(RunState.DidNotRun, summary.State);
                Assert.Equal("Backup did not run.", summary.Headline);
                Assert.Equal(summary, vm.Summary);
            });
        }

        [Fact]
        public void CancelCommand_WhenDeclined_LeavesBoundaryStateAndLogUntouched()
        {
            WpfTestHost.Run(() =>
            {
                ProgressWorkspaceViewModel vm = CreateViewModel(new ConfirmingDialogs(false));

                vm.CancelCommand.Execute(null);

                Assert.False(vm.IsCancellationRequested);
                Assert.True(vm.CanPause);
                Assert.True(vm.CanCancel);
                Assert.Empty(vm.LogLines);
            });
        }

        [Fact]
        public void NoByteMeasurement_RendersTheCoreNotAvailableValue()
        {
            WpfTestHost.Run(() =>
            {
                ProgressWorkspaceViewModel vm = CreateViewModel(new ConfirmingDialogs(false));

                vm.SetProgressDetail("Group 1 of 1. Mouse", "00:01", "N/A", "N/A", -1, 0, 0);

                Assert.Equal(-1, vm.BytesWritten);
                Assert.Equal(ProgressMetrics.NotAvailable, vm.BytesWrittenText);
            });
        }

        [Fact]
        public void ProgressView_RendersRunControlsWithoutWritingReadOnlyProgress()
        {
            WpfTestHost.Run(() =>
            {
                var view = new WinRestoreKit.Wpf.Views.ProgressWorkspaceView
                {
                    DataContext = CreateViewModel(new ConfirmingDialogs(false))
                };
                var window = new Window { Content = view };
                window.Show();
                window.UpdateLayout();
                Dispatcher.CurrentDispatcher.Invoke(
                    DispatcherPriority.ApplicationIdle,
                    new Action(() => { }));

                Assert.NotNull(view.FindName("ProgressPercentBar"));
                Assert.NotNull(view.FindName("ProgressLogList"));
                Assert.NotNull(view.FindName("PauseRunButton"));
                Assert.NotNull(view.FindName("CancelRunButton"));
                window.Close();
            });
        }

        private static ProgressWorkspaceViewModel CreateViewModel(IWpfDialogService dialogs)
            => new ProgressWorkspaceViewModel(Dispatcher.CurrentDispatcher, () => null,
                new NoOpRunDialogs(), dialogs);

        private static ProgressWorkspaceViewModel CreateViewModel(IWpfDialogService dialogs,
                                                                    Func<BackupRunRequest, Task> executor)
            => new ProgressWorkspaceViewModel(Dispatcher.CurrentDispatcher, () => null,
                new NoOpRunDialogs(), dialogs, executor);

        private sealed class ConfirmingDialogs : IWpfDialogService
        {
            private readonly bool confirmation;

            internal ConfirmingDialogs(bool confirmation)
            {
                this.confirmation = confirmation;
            }

            public void ShowInformation(string text, string caption) { }
            public void ShowWarning(string text, string caption) { }
            public void ShowError(string text, string caption) { }
            public bool Confirm(string text, string caption) => confirmation;
        }

        private sealed class NoOpRunDialogs : IRunDialogService
        {
            public IReadOnlyList<string> ShowRestoreConsent(RestorePlan plan) => null;
            public bool ConfirmSnapshotOverride(string text, string caption) => false;
            public void ShowPlanCompositionError(string text, string caption) { }
        }
    }
}
