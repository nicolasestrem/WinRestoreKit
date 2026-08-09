using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using WinRestoreKit;
using WinRestoreKit.Wpf.Infrastructure;
using WinRestoreKit.Wpf.Services;

namespace WinRestoreKit.Wpf.ViewModels
{
    internal sealed class Notice
    {
        internal Notice(string title, string text)
        {
            Title = title;
            Text = text;
        }

        public string Title { get; }
        public string Text { get; }
    }

    internal sealed class ConfirmViewModel : ObservableObject, IRunPresentation
    {
        private readonly Action backToCompare;
        private readonly ObservableCollection<string> logLines = new ObservableCollection<string>();
        private Dispatcher dispatcher;
        private IRunDialogService dialogService;
        private Func<Window> ownerProvider;
        private RunControl activeControl;
        private bool isRestoring;

        internal ConfirmViewModel(SnapshotEvent snapshot, IReadOnlyList<BackupBase> modules, Action backToCompare = null)
        {
            Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
            Modules = modules == null ? Array.Empty<BackupBase>() : modules.Where(module => module != null).ToArray();
            this.backToCompare = backToCompare;
            LogLines = new ReadOnlyObservableCollection<string>(logLines);
            FidelityCaveat = RestorePlan.FidelityCaveat;
            ConsentProcesses = SelectProcesses(true);
            InformationalProcesses = SelectProcesses(false);
            ExplorerRestartModules = Modules.Where(module => module.RequiresExplorerRestart).ToArray();
            ModuleWarnings = Modules.Where(module => !string.IsNullOrWhiteSpace(module.WarningMessage))
                .Select(module => new Notice(module.Title, module.WarningMessage)).ToArray();

            StartRestoreCommand = new DelegateCommand(_ => _ = StartFromCommandAsync(), _ => CanStartRestore);
            CancelRestoreCommand = new DelegateCommand(_ => activeControl?.RequestCancellation(), _ => IsRestoring);
            BackToCompareCommand = new DelegateCommand(_ => backToCompare?.Invoke(), _ => CanNavigate);
        }

        public SnapshotEvent Snapshot { get; }
        public IReadOnlyList<BackupBase> Modules { get; }
        public IReadOnlyList<RestoreCloseRequirement> ConsentProcesses { get; }
        public IReadOnlyList<RestoreCloseRequirement> InformationalProcesses { get; }
        public IReadOnlyList<BackupBase> ExplorerRestartModules { get; }
        public IReadOnlyList<Notice> ModuleWarnings { get; }
        public string FidelityCaveat { get; }
        public bool IsSourcePartial => Snapshot.Kind == SnapshotEventKind.Partial;
        public bool IsRestoring
        {
            get => isRestoring;
            private set
            {
                if (isRestoring == value)
                    return;

                isRestoring = value;
                OnPropertyChanged(nameof(IsRestoring));
                OnPropertyChanged(nameof(CanStartRestore));
                OnPropertyChanged(nameof(CanNavigate));
                StartRestoreCommand.RaiseCanExecuteChanged();
                CancelRestoreCommand.RaiseCanExecuteChanged();
                BackToCompareCommand.RaiseCanExecuteChanged();
            }
        }

        public bool CanStartRestore => Modules.Count != 0 && !IsRestoring && dialogService != null && ownerProvider != null;
        public bool CanNavigate => !IsRestoring;
        public string ProgressText { get; private set; } = string.Empty;
        public RunSummary Summary { get; private set; }
        public bool ExplorerRestartVisible { get; private set; }
        public DelegateCommand StartRestoreCommand { get; }
        public DelegateCommand CancelRestoreCommand { get; }
        public DelegateCommand BackToCompareCommand { get; }
        public ReadOnlyObservableCollection<string> LogLines { get; }

        internal void AttachRunSurfaces(Dispatcher dispatcher, Func<Window> ownerProvider, IRunDialogService dialogs)
        {
            this.dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            this.ownerProvider = ownerProvider ?? throw new ArgumentNullException(nameof(ownerProvider));
            dialogService = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
            OnPropertyChanged(nameof(CanStartRestore));
            StartRestoreCommand.RaiseCanExecuteChanged();
        }

        internal async Task StartRestoreAsync()
        {
            if (Modules.Count == 0 || IsRestoring || dialogService == null || ownerProvider == null || !RunCoordinator.TryStart())
                return;

            activeControl = new RunControl();
            bool logSinkInstalled = false;
            try
            {
                WpfLogSink logSink = new WpfLogSink(dispatcher, AppendLog, ClearLog);
                WpfRunUi runUi = new WpfRunUi(dispatcher, this, dialogService, ownerProvider);
                LogHelper.Instance.SetSink(logSink);
                logSinkInstalled = true;
                IsRestoring = true;
                await new BackupRestoreOrchestrator(runUi, activeControl).RunRestore(Modules, Snapshot.CanonicalPath);
            }
            catch (Exception ex)
            {
                ShowSummary(RunSummary.For(Array.Empty<ModuleOutcome>(), false, RunVerb.Restore, ex.Message),
                    "Restore", Array.Empty<ModuleOutcome>());
            }
            finally
            {
                if (logSinkInstalled)
                    LogHelper.Instance.SetSink(null);
                activeControl?.Dispose();
                activeControl = null;
                IsRestoring = false;
                RunCoordinator.SetRunning(false);
            }
        }

        public void SetProgressText(string text)
        {
            ProgressText = text ?? string.Empty;
            OnPropertyChanged(nameof(ProgressText));
        }

        public void SetProgressPercent(int percent) { }
        public void SetProgressDetail(string groupInfo, string elapsed, string remaining, string throughput, long bytesWritten, int errors, int warnings) { }

        public void ShowSummary(RunSummary summary, string caption, IReadOnlyList<ModuleOutcome> outcomes)
        {
            Summary = summary;
            OnPropertyChanged(nameof(Summary));
        }

        public void SetExplorerRestartVisible(bool visible)
        {
            ExplorerRestartVisible = visible;
            OnPropertyChanged(nameof(ExplorerRestartVisible));
        }

        private IReadOnlyList<RestoreCloseRequirement> SelectProcesses(bool needsConsent)
            => Modules.SelectMany(module => module.ProcessesToCloseBeforeRestore ?? Array.Empty<RestoreCloseRequirement>())
                .Where(requirement => requirement != null && requirement.NeedsConsent == needsConsent)
                .GroupBy(requirement => requirement.ProcessName, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First()).ToArray();

        private async Task StartFromCommandAsync()
        {
            try
            {
                await StartRestoreAsync();
            }
            catch (Exception ex)
            {
                SetProgressText("Restore could not start: " + ex.Message);
            }
        }

        private void AppendLog(string text) => logLines.Add(text ?? string.Empty);
        private void ClearLog() => logLines.Clear();
    }
}
