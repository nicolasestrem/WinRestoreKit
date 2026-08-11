using DataHelper;
using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using WinRestoreKit.Wpf.Infrastructure;
using WinRestoreKit.Wpf.Services;
using WinRestoreKit.Wpf.ViewModels.History;
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
        private readonly AdvancedHistoryViewModel advancedHistory;
        private readonly BackupCompletionPublisher completionPublisher;
        private readonly Func<BackupRunRequest, Task<BackupRunCompletion>> runBackupAsync;
        private readonly Func<Task> refreshTimelineAsync;
        private Func<Task> beforeUserNavigationAsync = () => Task.CompletedTask;
        private TimelineViewModel timelineWorkspace;
        private BackupWorkspaceViewModel backupWorkspace;
        private bool navigationInProgress;

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
            advancedHistory = new AdvancedHistoryViewModel(snapshotEventCatalog);
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
            advancedHistory = new AdvancedHistoryViewModel(snapshotEventCatalog);
            completionPublisher = new BackupCompletionPublisher(snapshotEventCatalog);
            refreshTimelineAsync = refreshTimeline ?? throw new ArgumentNullException(nameof(refreshTimeline));
            InitializeCommands();
        }

        public object CurrentWorkspace { get; private set; }
        public string WorkflowLabel { get; private set; }
        public ICommand CreateSnapshotCommand { get; private set; }
        public ICommand ShowTimelineCommand { get; private set; }
        public ICommand ShowAdvancedHistoryCommand { get; private set; }
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
            (ShowTimelineCommand as AsyncDelegateCommand)?.RaiseCanExecuteChanged();
            ShowTimeline();
        }

        internal void SetBeforeUserNavigation(Func<Task> callback)
            => beforeUserNavigationAsync = callback ?? throw new ArgumentNullException(nameof(callback));

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

        internal bool RequestWindowClose()
        {
            if (!RunCoordinator.IsRunning)
                return true;

            const string message = "A backup or restore is still running. Wait for it to finish, "
                + "or use Cancel in the active run before closing WinRestoreKit.";
            WorkflowLabel = "Run in progress";
            OnPropertyChanged(nameof(WorkflowLabel));
            dialogs?.ShowWarning(message, "Run in progress");
            return false;
        }

        internal void ShowInlineWorkflowError(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                throw new ArgumentException("An error message is required.", nameof(text));

            WorkflowLabel = text;
            OnPropertyChanged(nameof(WorkflowLabel));
        }

        private void InitializeCommands()
        {
            Action<Exception> reportNavigationFailure = ex =>
                ShowInlineWorkflowError("Navigation could not complete: " + ex.Message);
            CreateSnapshotCommand = new AsyncDelegateCommand(ShowCreateSnapshotAsync,
                reportNavigationFailure, CanNavigate);
            ShowTimelineCommand = new AsyncDelegateCommand(
                () => NavigateUserAsync(() => timelineWorkspace, "Timeline"),
                reportNavigationFailure,
                () => timelineWorkspace != null && CanNavigate());
            ShowAdvancedHistoryCommand = new AsyncDelegateCommand(
                () => NavigateUserAsync(() => advancedHistory, "Advanced history"),
                reportNavigationFailure,
                CanNavigate);
            ShowSettingsCommand = new AsyncDelegateCommand(
                () => NavigateUserAsync(() => settings, "Settings"),
                reportNavigationFailure,
                () => settings != null && CanNavigate());
            ShowAboutCommand = new AsyncDelegateCommand(
                () => NavigateUserAsync(() => about, "About"),
                reportNavigationFailure,
                () => about != null && CanNavigate());
            RunCoordinator.RunningChanged += OnRunningChanged;
        }

        private Task ShowCreateSnapshotAsync()
            => NavigateUserAsync(CreateBackupWorkspace, "Create snapshot");

        private object CreateBackupWorkspace()
        {
            backupWorkspace = new BackupWorkspaceViewModel(StartBackupAsync, Data.DataRootDir);
            return backupWorkspace;
        }

        private async Task NavigateUserAsync(Func<object> workspaceFactory, string workflowLabel)
        {
            if (!CanNavigate())
                return;

            navigationInProgress = true;
            RefreshNavigationCommandStates();
            try
            {
                await beforeUserNavigationAsync();
                if (RunCoordinator.IsRunning)
                    return;

                object workspace = workspaceFactory?.Invoke();
                if (workspace != null)
                    NavigateTo(workspace, workflowLabel);
            }
            finally
            {
                navigationInProgress = false;
                RefreshNavigationCommandStates();
            }
        }

        private bool CanNavigate() => !navigationInProgress && !RunCoordinator.IsRunning;

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
            => NavigateUserAsync(() => timelineWorkspace, "Timeline");

        private Task RefreshTimelineAsync()
        {
            if (refreshTimelineAsync != null)
                return refreshTimelineAsync();

            return timelineWorkspace?.RefreshAsync() ?? Task.CompletedTask;
        }

        private void OnRunningChanged(bool running)
        {
            Action refresh = RefreshNavigationCommandStates;
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

        private void RefreshNavigationCommandStates()
        {
            (CreateSnapshotCommand as AsyncDelegateCommand)?.RaiseCanExecuteChanged();
            (ShowTimelineCommand as AsyncDelegateCommand)?.RaiseCanExecuteChanged();
            (ShowAdvancedHistoryCommand as AsyncDelegateCommand)?.RaiseCanExecuteChanged();
            (ShowSettingsCommand as AsyncDelegateCommand)?.RaiseCanExecuteChanged();
            (ShowAboutCommand as AsyncDelegateCommand)?.RaiseCanExecuteChanged();
        }
    }
}
