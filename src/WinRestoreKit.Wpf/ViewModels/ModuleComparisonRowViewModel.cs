using System;
using WinRestoreKit;
using WinRestoreKit.Wpf.Infrastructure;

namespace WinRestoreKit.Wpf.ViewModels
{
    internal sealed class ModuleComparisonRowViewModel : ObservableObject
    {
        private readonly RestoreSetViewModel restoreSet;

        internal ModuleComparisonRowViewModel(BackupModuleRegistration registration, RestoreSetViewModel restoreSet)
        {
            Registration = registration ?? throw new ArgumentNullException(nameof(registration));
            this.restoreSet = restoreSet ?? throw new ArgumentNullException(nameof(restoreSet));
            Impact = new ModuleImpactViewModel(registration.Module);
            ToggleRestoreSetCommand = new DelegateCommand(_ => ToggleRestoreSet(), _ => CanChangeRestoreSet);
        }

        internal BackupModuleRegistration Registration { get; }
        internal ModuleComparison Comparison { get; private set; }
        public string Title => Registration.Title;
        public string Category => Registration.Category;
        public ModuleImpactViewModel Impact { get; }
        public bool IsChecking => Comparison == null;
        public bool CanChangeRestoreSet => Comparison?.CanRestore == true;
        public bool IsInRestoreSet => restoreSet.Contains(Registration.Module);
        public string StateLabel => IsChecking ? "Checking" : Comparison.State.ToString();
        public string ArtifactSummary => IsChecking ? "Comparison has not finished." : Comparison.ArtifactSummary;
        public string Reason => Comparison == null ? string.Empty : Comparison.Reason;
        public string RestoreActionLabel => IsInRestoreSet ? "Remove from restore" : "Add to restore";
        public string RestoreActionAutomationName => RestoreActionLabel + " " + Title;
        public DelegateCommand ToggleRestoreSetCommand { get; }

        internal void Apply(ModuleComparison comparison)
        {
            Comparison = comparison ?? throw new ArgumentNullException(nameof(comparison));
            OnPropertyChanged(nameof(IsChecking));
            OnPropertyChanged(nameof(StateLabel));
            OnPropertyChanged(nameof(ArtifactSummary));
            OnPropertyChanged(nameof(Reason));
            OnPropertyChanged(nameof(CanChangeRestoreSet));
            ToggleRestoreSetCommand.RaiseCanExecuteChanged();
        }

        internal void RefreshRestoreSetState()
        {
            OnPropertyChanged(nameof(IsInRestoreSet));
            OnPropertyChanged(nameof(RestoreActionLabel));
            OnPropertyChanged(nameof(RestoreActionAutomationName));
        }

        private void ToggleRestoreSet()
        {
            if (!CanChangeRestoreSet)
                return;

            if (IsInRestoreSet)
                restoreSet.Remove(Registration.Module);
            else
                restoreSet.Add(Comparison);

            RefreshRestoreSetState();
        }
    }
}
