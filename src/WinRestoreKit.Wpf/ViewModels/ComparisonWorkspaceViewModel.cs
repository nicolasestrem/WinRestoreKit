using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WinRestoreKit;
using WinRestoreKit.Wpf.Infrastructure;

namespace WinRestoreKit.Wpf.ViewModels
{
    internal sealed class ComparisonWorkspaceViewModel : ObservableObject
    {
        private readonly IReadOnlyList<BackupModuleRegistration> registrations;
        private readonly SnapshotComparisonService comparisonService;
        private readonly CancellationTokenSource comparisonCancellation = new CancellationTokenSource();
        private readonly Action<SnapshotEvent, IReadOnlyList<BackupBase>> showConfirm;
        private Task comparisonTask;
        private ComparisonFilter selectedFilter = ComparisonFilter.All;
        private ModuleComparisonRowViewModel selectedRow;

        internal ComparisonWorkspaceViewModel(
            SnapshotEvent snapshot,
            IReadOnlyList<BackupModuleRegistration> registrations,
            SnapshotComparisonService comparisonService,
            Action<SnapshotEvent, IReadOnlyList<BackupBase>> showConfirm)
        {
            Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
            this.registrations = registrations ?? throw new ArgumentNullException(nameof(registrations));
            this.comparisonService = comparisonService ?? throw new ArgumentNullException(nameof(comparisonService));
            this.showConfirm = showConfirm ?? throw new ArgumentNullException(nameof(showConfirm));

            Rows = new ObservableCollection<ModuleComparisonRowViewModel>();
            VisibleRows = new ObservableCollection<ModuleComparisonRowViewModel>();
            RestoreSet = new RestoreSetViewModel();
            RestoreSet.PropertyChanged += RestoreSet_PropertyChanged;
            ContinueToConfirmCommand = new DelegateCommand(_ => ContinueToConfirm(), _ => CanContinueToConfirm);
        }

        public SnapshotEvent Snapshot { get; }
        public ObservableCollection<ModuleComparisonRowViewModel> Rows { get; }
        public ObservableCollection<ModuleComparisonRowViewModel> VisibleRows { get; }
        public RestoreSetViewModel RestoreSet { get; }
        public bool IsComparing { get; private set; }
        public string ComparisonStatus { get; private set; } = string.Empty;
        public ComparisonFilter SelectedFilter
        {
            get => selectedFilter;
            set
            {
                if (selectedFilter == value)
                    return;

                selectedFilter = value;
                OnPropertyChanged(nameof(SelectedFilter));
                OnPropertyChanged(nameof(IsAllFilter));
                OnPropertyChanged(nameof(IsChangedOnlyFilter));
                RefreshVisibleRows();
            }
        }

        public bool IsAllFilter
        {
            get => SelectedFilter == ComparisonFilter.All;
            set
            {
                if (value)
                    SelectedFilter = ComparisonFilter.All;
            }
        }

        public bool IsChangedOnlyFilter
        {
            get => SelectedFilter == ComparisonFilter.ChangedOnly;
            set
            {
                if (value)
                    SelectedFilter = ComparisonFilter.ChangedOnly;
            }
        }

        public ModuleComparisonRowViewModel SelectedRow
        {
            get => selectedRow;
            set
            {
                if (ReferenceEquals(selectedRow, value))
                    return;

                selectedRow = value;
                OnPropertyChanged(nameof(SelectedRow));
                OnPropertyChanged(nameof(IsDetailTrayOpen));
            }
        }

        public bool IsDetailTrayOpen => SelectedRow != null;
        public bool CanContinueToConfirm => RestoreSet.HasItems && !IsComparing;
        public DelegateCommand ContinueToConfirmCommand { get; }

        internal Task StartAsync(SnapshotPayloadPreparation preparation)
            => comparisonTask ?? (comparisonTask = StartCoreAsync(preparation));

        internal async Task CancelAsync()
        {
            comparisonCancellation.Cancel();
            if (comparisonTask != null)
                await comparisonTask;
        }

        private async Task StartCoreAsync(SnapshotPayloadPreparation preparation)
        {
            if (preparation == null)
                throw new ArgumentNullException(nameof(preparation));

            Rows.Clear();
            foreach (BackupModuleRegistration registration in registrations)
                Rows.Add(new ModuleComparisonRowViewModel(registration, RestoreSet));
            RefreshVisibleRows();

            IsComparing = true;
            OnPropertyChanged(nameof(IsComparing));
            OnPropertyChanged(nameof(CanContinueToConfirm));
            ContinueToConfirmCommand.RaiseCanExecuteChanged();
            try
            {
                IProgress<ComparisonProgress> progress = new Progress<ComparisonProgress>(item =>
                {
                    Rows[item.Ordinal].Apply(item.Comparison);
                    RefreshVisibleRows();
                });
                await comparisonService.CompareAsync(preparation,
                    registrations.Select(registration => registration.Module).ToArray(),
                    comparisonCancellation.Token, progress);
            }
            catch (OperationCanceledException) when (comparisonCancellation.IsCancellationRequested)
            {
                ComparisonStatus = "Comparison canceled.";
                OnPropertyChanged(nameof(ComparisonStatus));
            }
            finally
            {
                IsComparing = false;
                OnPropertyChanged(nameof(IsComparing));
                OnPropertyChanged(nameof(CanContinueToConfirm));
                ContinueToConfirmCommand.RaiseCanExecuteChanged();
                preparation.Dispose();
                RefreshVisibleRows();
            }
        }

        private void RestoreSet_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(RestoreSetViewModel.HasItems))
                return;

            foreach (ModuleComparisonRowViewModel row in Rows)
                row.RefreshRestoreSetState();
            OnPropertyChanged(nameof(CanContinueToConfirm));
            ContinueToConfirmCommand.RaiseCanExecuteChanged();
        }

        private void ContinueToConfirm()
        {
            if (CanContinueToConfirm)
                showConfirm(Snapshot, RestoreSet.Modules.ToArray());
        }

        private void RefreshVisibleRows()
        {
            VisibleRows.Clear();
            foreach (ModuleComparisonRowViewModel row in Rows)
            {
                if (SelectedFilter == ComparisonFilter.All ||
                    (row.Comparison != null && row.Comparison.State == ComparisonState.Changed))
                    VisibleRows.Add(row);
            }
        }
    }
}
