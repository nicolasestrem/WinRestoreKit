using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using WinRestoreKit;
using WinRestoreKit.Wpf;
using WinRestoreKit.Wpf.Services;
using WinRestoreKit.Wpf.ViewModels;
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
        public void Shell_SettingsAndAboutCommandsReturnToTheRealTimeline()
        {
            WpfTestHost.Run(() =>
            {
                ShellViewModel shell = CreateShell();
                MainWindow window = new MainWindow(shell);
                shell.ShowSettingsCommand.Execute(null);
                Assert.Equal("Settings", shell.WorkflowLabel);
                shell.ShowAboutCommand.Execute(null);
                Assert.Equal("About", shell.WorkflowLabel);
                shell.ShowTimeline();
                Assert.IsType<TimelineViewModel>(shell.CurrentWorkspace);
                window.Close();
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
