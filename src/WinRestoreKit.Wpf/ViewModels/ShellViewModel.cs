using DataHelper;
using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using WinRestoreKit.Wpf.Infrastructure;
using WinRestoreKit.Wpf.Services;
using WinRestoreKit.Wpf.ViewModels.Timeline;

namespace WinRestoreKit.Wpf.ViewModels
{
    internal sealed class ShellViewModel : ObservableObject
    {
        private readonly Dispatcher dispatcher;
        private readonly Func<Window> ownerProvider;
        private readonly IWpfDialogService dialogs;
        private readonly SettingsViewModel settings;
        private readonly AboutViewModel about;
        private readonly SnapshotEventCatalog snapshotEventCatalog;
        private readonly BackupCompletionPublisher completionPublisher;
        private readonly Func<BackupRunRequest, Task<BackupRunCompletion>> runBackupAsync;
        private readonly Func<Task> refreshTimelineAsync;
        private TimelineViewModel timelineWorkspace;
        private BackupWorkspaceViewModel backupWorkspace;

        internal ShellViewModel(IThemeService themes, WpfUpdatePresenter updates, string currentVersion)
        {
            if (themes == null)
                throw new ArgumentNullException(nameof(themes));
            if (updates == null)
                throw new ArgumentNullException(nameof(updates));

            dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
            ownerProvider = () => Application.Current?.MainWindow;
            dialogs = new WpfDialogService(dispatcher, ownerProvider);
            settings = new SettingsViewModel(themes);
            about = new AboutViewModel(updates, currentVersion);
            snapshotEventCatalog = new SnapshotEventCatalog();
            completionPublisher = new BackupCompletionPublisher(snapshotEventCatalog);
            WpfAppRestoreDialog.Register(snapshotEventCatalog);
            runBackupAsync = RunWpfBackupAsync;
            InitializeCommands();
        }

        private ShellViewModel(Func<BackupRunRequest, Task<BackupRunCompletion>> runBackup,
            SnapshotEventCatalog catalog, Func<Task> refreshTimeline)
        {
            runBackupAsync = runBackup ?? throw new ArgumentNullException(nameof(runBackup));
            snapshotEventCatalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            completionPublisher = new BackupCompletionPublisher(snapshotEventCatalog);
            refreshTimelineAsync = refreshTimeline ?? throw new ArgumentNullException(nameof(refreshTimeline));
            InitializeCommands();
        }

        public object CurrentWorkspace { get; private set; }
        public string WorkflowLabel { get; private set; }
        public ICommand CreateSnapshotCommand { get; private set; }
        public ICommand ShowTimelineCommand { get; private set; }
        public ICommand ShowSettingsCommand { get; private set; }
        public ICommand ShowAboutCommand { get; private set; }

        internal SnapshotEventCatalog SnapshotEventCatalog => snapshotEventCatalog;

        internal static ShellViewModel ForTest(
            Func<BackupRunRequest, Task<BackupRunCompletion>> runBackup,
            SnapshotEventCatalog catalog, Func<Task> refreshTimeline)
            => new ShellViewModel(runBackup, catalog, refreshTimeline);

        internal void SetTimeline(TimelineViewModel value)
        {
            timelineWorkspace = value ?? throw new ArgumentNullException(nameof(value));
            (ShowTimelineCommand as DelegateCommand)?.RaiseCanExecuteChanged();
            ShowTimeline();
        }

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

        internal void ShowTimeline()
        {
            if (timelineWorkspace != null)
                NavigateTo(timelineWorkspace, "Timeline");
        }

        internal void ShowCompare(ComparisonWorkspaceViewModel workspace)
            => NavigateTo(workspace ?? throw new ArgumentNullException(nameof(workspace)), "Compare");

        internal void ShowConfirm(ConfirmViewModel confirm)
            => NavigateTo(confirm ?? throw new ArgumentNullException(nameof(confirm)), "Confirm");

        internal void ShowInlineWorkflowError(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                throw new ArgumentException("An error message is required.", nameof(text));

            WorkflowLabel = text;
            OnPropertyChanged(nameof(WorkflowLabel));
        }

        private void InitializeCommands()
        {
            CreateSnapshotCommand = new DelegateCommand(_ => ShowCreateSnapshot(),
                _ => !RunCoordinator.IsRunning);
            ShowTimelineCommand = new DelegateCommand(_ => ShowTimeline(), _ => timelineWorkspace != null);
            ShowSettingsCommand = new DelegateCommand(_ => NavigateTo(settings, "Settings"));
            ShowAboutCommand = new DelegateCommand(_ => NavigateTo(about, "About"));
            RunCoordinator.RunningChanged += OnRunningChanged;
        }

        private void ShowCreateSnapshot()
        {
            backupWorkspace = new BackupWorkspaceViewModel(StartBackupAsync, Data.DataRootDir);
            NavigateTo(backupWorkspace, "Create snapshot");
        }

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

            if (completion == null)
                throw new InvalidOperationException("The backup run completed without a result.");

            completionPublisher.Publish(completion.AttemptedBackupPath, request.SnapshotName,
                completion.Summary, DateTime.Now);
            await RefreshTimelineAsync();
            NavigateTo(ResultWorkspaceViewModel.From(completion.Summary, completion.Outcomes,
                ReturnToTimelineAsync), "Snapshot result");
        }

        private async Task<BackupRunCompletion> RunWpfBackupAsync(BackupRunRequest request)
        {
            Window owner = ownerProvider();
            var progress = new ProgressWorkspaceViewModel(dispatcher, ownerProvider,
                new RestoreRunDialogService(owner), dialogs);
            NavigateTo(progress, "Creating snapshot");
            RunSummary summary = await progress.RunBackupAsync(request);
            return new BackupRunCompletion(summary, progress.Outcomes, progress.AttemptedBackupPath);
        }

        private Task ReturnToTimelineAsync()
        {
            ShowTimeline();
            return Task.CompletedTask;
        }

        private Task RefreshTimelineAsync()
        {
            if (refreshTimelineAsync != null)
                return refreshTimelineAsync();

            return timelineWorkspace?.RefreshAsync() ?? Task.CompletedTask;
        }

        private void OnRunningChanged(bool running)
        {
            Action refresh = () => (CreateSnapshotCommand as DelegateCommand)?.RaiseCanExecuteChanged();
            if (dispatcher == null || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished
                || dispatcher.CheckAccess())
            {
                refresh();
                return;
            }

            try
            {
                dispatcher.BeginInvoke(refresh);
            }
            catch (InvalidOperationException)
            {
            }
        }
    }
}
