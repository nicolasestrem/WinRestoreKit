using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using WinRestoreKit;
using WinRestoreKit.Wpf;
using WinRestoreKit.Wpf.Services;
using WinRestoreKit.Wpf.ViewModels;
using WinRestoreKit.Wpf.ViewModels.History;
using WinRestoreKit.Wpf.ViewModels.Timeline;
using Xunit;

namespace WinRestoreKit.Tests
{
    public class WpfShellTests
    {
        [Fact]
        public void Shell_ComposesTheRealTimelineWorkflow()
        {
            WpfTestHost.Run(() =>
            {
                ShellViewModel shell = CreateShell();
                MainWindow window = new MainWindow(shell);

                Assert.Equal("Timeline", shell.WorkflowLabel);
                Assert.IsType<TimelineViewModel>(shell.CurrentWorkspace);
                window.Close();
            });
        }

        [Fact]
        public void Shell_PrimaryNavigationCommandsReachEveryWorkspaceAndReturnToTimeline()
        {
            WpfTestHost.Run(() =>
            {
                ShellViewModel shell = CreateShell();
                MainWindow window = new MainWindow(shell);
                shell.ShowAdvancedHistoryCommand.Execute(null);
                Assert.Equal("Advanced history", shell.WorkflowLabel);
                Assert.IsType<AdvancedHistoryViewModel>(shell.CurrentWorkspace);
                shell.ShowSettingsCommand.Execute(null);
                Assert.Equal("Settings", shell.WorkflowLabel);
                shell.ShowAboutCommand.Execute(null);
                Assert.Equal("About", shell.WorkflowLabel);
                shell.ShowTimelineCommand.Execute(null);
                Assert.IsType<TimelineViewModel>(shell.CurrentWorkspace);
                window.Close();
            });
        }

        [Fact]
        public void MainWindow_EscapeReturnsFromCompareToTimeline()
        {
            WpfTestHost.Run(() =>
            {
                ShellViewModel shell = CreateShell();
                MainWindow window = new MainWindow(shell);
                var snapshot = new SnapshotEvent(SnapshotEventKind.Partial, DateTime.UtcNow, "snapshot",
                    string.Empty, string.Empty, string.Empty, 0, true, null);
                var comparison = new ComparisonWorkspaceViewModel(snapshot,
                    Array.Empty<BackupModuleRegistration>(), new SnapshotComparisonService(), (_, _) => { });

                shell.ShowCompare(comparison);
                Assert.True(window.ReturnToTimelineFromCompare());
                Dispatcher.CurrentDispatcher.Invoke(
                    DispatcherPriority.ApplicationIdle,
                    new Action(() => { }));

                Assert.Equal("Timeline", shell.WorkflowLabel);
                Assert.IsType<TimelineViewModel>(shell.CurrentWorkspace);
                window.Close();
            });
        }

        [Fact]
        public void Shell_PrimaryNavigationCommandsCannotHideAnActiveRun()
        {
            WpfTestHost.Run(() =>
            {
                RunCoordinator.SetRunning(false);
                ShellViewModel shell = CreateShell();
                MainWindow window = new MainWindow(shell);
                try
                {
                    RunCoordinator.SetRunning(true);

                    Assert.False(shell.CreateSnapshotCommand.CanExecute(null));
                    Assert.False(shell.ShowTimelineCommand.CanExecute(null));
                    Assert.False(shell.ShowAdvancedHistoryCommand.CanExecute(null));
                    Assert.False(shell.ShowSettingsCommand.CanExecute(null));
                    Assert.False(shell.ShowAboutCommand.CanExecute(null));

                    shell.ShowSettingsCommand.Execute(null);
                    Assert.Equal("Timeline", shell.WorkflowLabel);
                    Assert.IsType<TimelineViewModel>(shell.CurrentWorkspace);
                }
                finally
                {
                    RunCoordinator.SetRunning(false);
                    window.Close();
                }
            });
        }

        [Fact]
        public void MainWindow_CloseIsCanceledUntilTheActiveRunFinishes()
        {
            WpfTestHost.Run(() =>
            {
                RunCoordinator.SetRunning(false);
                ShellViewModel shell = ShellViewModel.ForTest(
                    _ => Task.FromResult<BackupRunCompletion>(null),
                    new SnapshotEventCatalog(),
                    () => Task.CompletedTask);
                MainWindow window = new MainWindow(shell);
                window.Show();
                try
                {
                    RunCoordinator.SetRunning(true);
                    window.Close();

                    Assert.True(window.IsVisible);
                    Assert.Equal("Run in progress", shell.WorkflowLabel);
                }
                finally
                {
                    RunCoordinator.SetRunning(false);
                    window.Close();
                }

                Assert.False(window.IsVisible);
            });
        }

        [Fact]
        public void Shell_ShowAbout_RendersReadOnlyVersionBinding()
        {
            WpfTestHost.Run(() =>
            {
                ShellViewModel shell = CreateShell();
                MainWindow window = new MainWindow(shell);
                window.Show();

                shell.ShowAboutCommand.Execute(null);
                window.UpdateLayout();
                Dispatcher.CurrentDispatcher.Invoke(
                    DispatcherPriority.ApplicationIdle,
                    new Action(() => { }));

                Assert.Equal("About", shell.WorkflowLabel);
                window.Close();
            });
        }

        [Fact]
        public void MainWindow_ComposesTheTimelineWorkflow()
        {
            WpfTestHost.Run(() =>
            {
                ShellViewModel shell = CreateShell();
                MainWindow window = new MainWindow(shell);
                Assert.Equal("WinRestoreKit", window.Title);
                Assert.Same(shell, window.DataContext);
                Assert.IsType<TimelineViewModel>(shell.CurrentWorkspace);
                window.Close();
            });
        }

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
    }
}
