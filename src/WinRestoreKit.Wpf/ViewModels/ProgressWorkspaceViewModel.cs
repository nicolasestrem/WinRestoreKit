using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using WinRestoreKit;
using WinRestoreKit.Wpf.Infrastructure;
using WinRestoreKit.Wpf.Services;

namespace WinRestoreKit.Wpf.ViewModels
{
    internal sealed class LogLineViewModel
    {
        internal LogLineViewModel(string text)
        {
            Text = text ?? string.Empty;
        }

        public string Text { get; }
    }

    internal sealed class ProgressWorkspaceViewModel : ObservableObject, IRunPresentation
    {
        private readonly RunControl runControl;
        private readonly BackupRestoreOrchestrator runner;
        private readonly WpfLogSink logSink;
        private readonly IWpfDialogService dialogs;
        private readonly Func<BackupRunRequest, Task> backupRunAsyncOverride;
        private readonly ObservableCollection<LogLineViewModel> logLines = new();
        private bool sinkInstalled;
        private bool runStarted;
        private bool isPaused;
        private bool isCancellationRequested;
        private bool isArchivePhase;
        private bool isTerminal;
        private RunVerb activeVerb = RunVerb.Backup;
        private string progressText = string.Empty;
        private int percent;
        private string groupInfo = string.Empty;
        private string elapsed = string.Empty;
        private string remaining = string.Empty;
        private string throughput = string.Empty;
        private long bytesWritten;
        private string bytesWrittenText = ProgressMetrics.NotAvailable;
        private int errors;
        private int warnings;
        private RunSummary summary;
        private IReadOnlyList<ModuleOutcome> outcomes = Array.Empty<ModuleOutcome>();
        private string attemptedBackupPath;
        private bool explorerRestartVisible;

        internal ProgressWorkspaceViewModel(Dispatcher dispatcher, Func<Window> ownerProvider,
                                            IRunDialogService runDialogService,
                                            IWpfDialogService dialogs)
            : this(dispatcher, ownerProvider, runDialogService, dialogs, null)
        {
        }

        internal ProgressWorkspaceViewModel(Dispatcher dispatcher, Func<Window> ownerProvider,
                                            IRunDialogService runDialogService,
                                            IWpfDialogService dialogs,
                                            Func<BackupRunRequest, Task> backupRunAsyncOverride)
        {
            if (dispatcher == null)
                throw new ArgumentNullException(nameof(dispatcher));
            if (ownerProvider == null)
                throw new ArgumentNullException(nameof(ownerProvider));
            if (runDialogService == null)
                throw new ArgumentNullException(nameof(runDialogService));

            LogLines = new ReadOnlyObservableCollection<LogLineViewModel>(logLines);
            this.dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
            this.backupRunAsyncOverride = backupRunAsyncOverride;
            runControl = new RunControl();
            var runUi = new WpfRunUi(dispatcher, this, runDialogService, ownerProvider);
            runner = new BackupRestoreOrchestrator(runUi, runControl);
            logSink = new WpfLogSink(dispatcher, AppendLog, ClearLog);
            PauseCommand = new DelegateCommand(_ => TogglePause(), _ => CanPause);
            CancelCommand = new DelegateCommand(_ => RequestCancellation(), _ => CanCancel);
        }

        public string ProgressText
        {
            get => progressText;
            private set => SetProperty(ref progressText, value ?? string.Empty, nameof(ProgressText));
        }

        public int Percent
        {
            get => percent;
            private set => SetProperty(ref percent, value, nameof(Percent));
        }

        public string GroupInfo
        {
            get => groupInfo;
            private set => SetProperty(ref groupInfo, value ?? string.Empty, nameof(GroupInfo));
        }

        public string Elapsed
        {
            get => elapsed;
            private set => SetProperty(ref elapsed, value ?? string.Empty, nameof(Elapsed));
        }

        public string Remaining
        {
            get => remaining;
            private set => SetProperty(ref remaining, value ?? string.Empty, nameof(Remaining));
        }

        public string Throughput
        {
            get => throughput;
            private set => SetProperty(ref throughput, value ?? string.Empty, nameof(Throughput));
        }

        public long BytesWritten
        {
            get => bytesWritten;
            private set
            {
                if (!SetProperty(ref bytesWritten, value, nameof(BytesWritten)))
                    return;

                BytesWrittenText = value < 0 ? ProgressMetrics.NotAvailable : value.ToString();
            }
        }

        public int Errors
        {
            get => errors;
            private set => SetProperty(ref errors, value, nameof(Errors));
        }

        public string BytesWrittenText
        {
            get => bytesWrittenText;
            private set => SetProperty(ref bytesWrittenText, value ?? ProgressMetrics.NotAvailable,
                nameof(BytesWrittenText));
        }

        public int Warnings
        {
            get => warnings;
            private set => SetProperty(ref warnings, value, nameof(Warnings));
        }

        public bool ExplorerRestartVisible
        {
            get => explorerRestartVisible;
            private set => SetProperty(ref explorerRestartVisible, value, nameof(ExplorerRestartVisible));
        }

        public RunSummary Summary
        {
            get => summary;
            private set => SetProperty(ref summary, value, nameof(Summary));
        }

        public IReadOnlyList<ModuleOutcome> Outcomes
        {
            get => outcomes;
            private set => SetProperty(ref outcomes, value ?? Array.Empty<ModuleOutcome>(), nameof(Outcomes));
        }

        public string AttemptedBackupPath
        {
            get => attemptedBackupPath;
            private set => SetProperty(ref attemptedBackupPath, value, nameof(AttemptedBackupPath));
        }

        public ReadOnlyObservableCollection<LogLineViewModel> LogLines { get; }

        public bool IsPaused => isPaused;
        public bool IsCancellationRequested => isCancellationRequested;
        public bool CanPause => !isTerminal && !isCancellationRequested;
        public bool CanCancel => !isTerminal && !isCancellationRequested && !isArchivePhase;
        public string PauseCaption => isPaused ? "Resume" : "Pause";
        public ICommand PauseCommand { get; }
        public ICommand CancelCommand { get; }

        internal async Task<RunSummary> RunBackupAsync(BackupRunRequest request)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            BeginRun(RunVerb.Backup);
            SetProgressText("Started snapshot " + request.SnapshotName + ".");
            try
            {
                if (backupRunAsyncOverride == null)
                {
                    await runner.RunBackup(request.Modules, request.Destination, request.SnapshotName,
                        request.Compression);
                }
                else
                {
                    await backupRunAsyncOverride(request);
                }
                return EnsureSummary(RunVerb.Backup, "the backup runner returned without a result");
            }
            catch (Exception ex)
            {
                SetSummary(RunSummary.For(Array.Empty<ModuleOutcome>(), false, RunVerb.Backup, ex.Message),
                    Array.Empty<ModuleOutcome>());
                return Summary;
            }
            finally
            {
                AttemptedBackupPath = runner.BackupOutputPath;
                EndRun();
            }
        }

        internal async Task<RunSummary> RunRestoreAsync(IReadOnlyList<BackupBase> modules, string backupPath)
        {
            BeginRun(RunVerb.Restore);
            SetProgressText("Started restore.");
            try
            {
                await runner.RunRestore(modules, backupPath);
                return EnsureSummary(RunVerb.Restore, "the restore runner returned without a result");
            }
            catch (Exception ex)
            {
                SetSummary(RunSummary.For(Array.Empty<ModuleOutcome>(), false, RunVerb.Restore, ex.Message),
                    Array.Empty<ModuleOutcome>());
                return Summary;
            }
            finally
            {
                EndRun();
            }
        }

        public void SetProgressText(string text)
        {
            ProgressText = text;
            isArchivePhase = string.Equals(text, BackupRestoreOrchestrator.ArchiveProgressText,
                StringComparison.Ordinal);
            RefreshCommandAvailability();
        }

        public void SetProgressPercent(int value)
            => Percent = value;

        public void SetProgressDetail(string newGroupInfo, string newElapsed, string newRemaining,
                                      string newThroughput, long newBytesWritten, int newErrors,
                                      int newWarnings)
        {
            GroupInfo = newGroupInfo;
            Elapsed = newElapsed;
            Remaining = newRemaining;
            Throughput = newThroughput;
            BytesWritten = newBytesWritten;
            Errors = newErrors;
            Warnings = newWarnings;
        }

        public void ShowSummary(RunSummary runSummary, string caption,
                                IReadOnlyList<ModuleOutcome> runOutcomes)
            => SetSummary(runSummary, runOutcomes);

        public void SetExplorerRestartVisible(bool visible)
            => ExplorerRestartVisible = visible;

        private void BeginRun(RunVerb verb)
        {
            if (runStarted)
                throw new InvalidOperationException("A progress workspace can run only one admitted operation.");

            runStarted = true;
            activeVerb = verb;
            ClearLog();
            LogHelper.Instance.SetSink(logSink);
            sinkInstalled = true;
        }

        private void EndRun()
        {
            if (sinkInstalled)
                LogHelper.Instance.SetSink(null);
            sinkInstalled = false;
            logSink.Dispose();
            runControl.Dispose();
        }

        private RunSummary EnsureSummary(RunVerb verb, string reason)
        {
            if (Summary == null)
                SetSummary(RunSummary.For(Array.Empty<ModuleOutcome>(), false, verb, reason),
                    Array.Empty<ModuleOutcome>());
            return Summary;
        }

        private void TogglePause()
        {
            if (!CanPause)
                return;

            if (isPaused)
            {
                runControl.Resume();
                isPaused = false;
                OnPropertyChanged(nameof(IsPaused));
                OnPropertyChanged(nameof(PauseCaption));
                AppendLog("Run resumed. The next group may start.");
            }
            else
            {
                runControl.Pause();
                isPaused = true;
                OnPropertyChanged(nameof(IsPaused));
                OnPropertyChanged(nameof(PauseCaption));
                AppendLog("Run paused. The active group will finish before pausing.");
            }
        }

        private void RequestCancellation()
        {
            if (!CanCancel)
                return;

            string operation = activeVerb == RunVerb.Restore ? "restore" : "backup";
            if (!dialogs.Confirm("Cancel this " + operation +
                "? The active group will finish before cancellation. No further groups will start. " +
                "Already applied restored settings are not rolled back. Partial backup output created by this run " +
                "is removed only when it can be identified as new. Otherwise it is preserved without a trusted manifest.",
                "Cancel " + operation))
            {
                return;
            }

            runControl.RequestCancellation();
            isCancellationRequested = true;
            OnPropertyChanged(nameof(IsCancellationRequested));
            RefreshCommandAvailability();
            AppendLog("Cancellation requested. The active group will finish before cancellation.");
        }

        private void SetSummary(RunSummary runSummary, IReadOnlyList<ModuleOutcome> runOutcomes)
        {
            Summary = runSummary ?? throw new ArgumentNullException(nameof(runSummary));
            Outcomes = runOutcomes ?? Array.Empty<ModuleOutcome>();
            isTerminal = true;
            OnPropertyChanged(nameof(CanPause));
            OnPropertyChanged(nameof(CanCancel));
            RefreshCommandAvailability();
        }

        private void AppendLog(string text)
        {
            string message = text ?? string.Empty;
            if (message.Length != 0)
                logLines.Add(new LogLineViewModel(message));
        }

        private void ClearLog()
            => logLines.Clear();

        private void RefreshCommandAvailability()
        {
            OnPropertyChanged(nameof(CanPause));
            OnPropertyChanged(nameof(CanCancel));
            (PauseCommand as DelegateCommand)?.RaiseCanExecuteChanged();
            (CancelCommand as DelegateCommand)?.RaiseCanExecuteChanged();
        }
    }
}
