using DataHelper;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using WinRestoreKit;
using WinRestoreKit.Wpf.Infrastructure;

namespace WinRestoreKit.Wpf.ViewModels
{
    internal sealed class BackupWorkspaceViewModel : ObservableObject
    {
        private readonly Func<BackupRunRequest, Task> startRunAsync;
        private string snapshotName = string.Empty;
        private string destination;
        private SnapshotCompression compression;
        private string validationMessage;

        internal BackupWorkspaceViewModel(Func<BackupRunRequest, Task> startRunAsync,
                                          string defaultDestination)
        {
            this.startRunAsync = startRunAsync ?? throw new ArgumentNullException(nameof(startRunAsync));
            destination = defaultDestination ?? string.Empty;
            compression = SnapshotCompression.Fast;
            Scopes = new ObservableCollection<BackupScopeItemViewModel>(
                ScopeGroups.Build().Select(scope => new BackupScopeItemViewModel(scope)));
            foreach (BackupScopeItemViewModel scope in Scopes)
            {
                scope.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(BackupScopeItemViewModel.IsSelected))
                        SetValidationMessage(null);
                };
            }
            StartCommand = new AsyncDelegateCommand(StartAsync, ex => SetValidationMessage(ex.Message));
            SelectAllCommand = new DelegateCommand(_ => SelectAll());
            ClearCommand = new DelegateCommand(_ => Clear());
            DeveloperMachineCommand = new DelegateCommand(_ =>
                SelectModulesByTypeName(BackupPresets.DeveloperMachine, selectScopesContainingType: true));
            MinimalPrivacySafeCommand = new DelegateCommand(_ =>
                SelectModulesByTypeName(BackupPresets.MinimalPrivacySafeExclusions,
                    selectScopesContainingType: false));
        }

        public ObservableCollection<BackupScopeItemViewModel> Scopes { get; }

        public IReadOnlyList<SnapshotCompression> CompressionOptions { get; } =
            new[] { SnapshotCompression.None, SnapshotCompression.Fast, SnapshotCompression.Max };

        public string SnapshotName
        {
            get => snapshotName;
            set
            {
                if (SetProperty(ref snapshotName, value ?? string.Empty, nameof(SnapshotName)))
                    SetValidationMessage(null);
            }
        }

        public string Destination
        {
            get => destination;
            set
            {
                if (SetProperty(ref destination, value ?? string.Empty, nameof(Destination)))
                    SetValidationMessage(null);
            }
        }

        public SnapshotCompression Compression
        {
            get => compression;
            set
            {
                if (SetProperty(ref compression, value, nameof(Compression)))
                    SetValidationMessage(null);
            }
        }

        public string ValidationMessage
        {
            get => validationMessage;
            private set => SetProperty(ref validationMessage, value, nameof(ValidationMessage));
        }

        public string ValidationAutomationName => string.IsNullOrWhiteSpace(ValidationMessage)
            ? "Snapshot validation"
            : "Snapshot validation: " + ValidationMessage;

        public ICommand StartCommand { get; }
        public ICommand SelectAllCommand { get; }
        public ICommand ClearCommand { get; }
        public ICommand DeveloperMachineCommand { get; }
        public ICommand MinimalPrivacySafeCommand { get; }

        internal async Task StartAsync()
        {
            IReadOnlyList<BackupBase> modules = Scopes.Where(scope => scope.IsSelected)
                .SelectMany(scope => scope.Modules)
                .Distinct()
                .ToArray();
            if (modules.Count == 0)
            {
                SetValidationMessage("Select at least one scope with supported items.");
                return;
            }

            if (string.IsNullOrWhiteSpace(Destination))
            {
                SetValidationMessage("Choose a destination folder before capturing.");
                return;
            }

            SetValidationMessage(null);
            await startRunAsync(new BackupRunRequest(modules, SnapshotName.Trim(), Compression,
                Destination.Trim()));
        }

        internal void ReportAdmissionRejected(string message)
            => SetValidationMessage(message);

        private void SelectAll()
            => SelectModulesByTypeName(BackupModuleCatalog.CreateAll()
                .Select(registration => registration.Module.GetType().Name), selectScopesContainingType: true);

        private void Clear()
            => SelectModulesByTypeName(Array.Empty<string>(), selectScopesContainingType: true);

        private void SelectModulesByTypeName(IEnumerable<string> moduleTypeNames,
                                             bool selectScopesContainingType)
        {
            var typeNames = new HashSet<string>((moduleTypeNames ?? Array.Empty<string>())
                .Where(name => !string.IsNullOrWhiteSpace(name)), StringComparer.Ordinal);
            foreach (BackupScopeItemViewModel scope in Scopes)
            {
                bool containsRequestedType = scope.Modules.Any(module =>
                    typeNames.Contains(module.GetType().Name));
                scope.IsSelected = selectScopesContainingType
                    ? containsRequestedType
                    : !containsRequestedType;
            }
        }

        private void SetValidationMessage(string message)
        {
            ValidationMessage = string.IsNullOrWhiteSpace(message) ? null : message;
            OnPropertyChanged(nameof(ValidationAutomationName));
        }
    }
}
