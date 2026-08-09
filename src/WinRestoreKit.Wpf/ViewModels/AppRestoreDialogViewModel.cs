using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using WinRestoreKit;
using WinRestoreKit.Wpf.Infrastructure;

namespace WinRestoreKit.Wpf.ViewModels
{
    internal sealed class AppRestorePackageViewModel : ObservableObject
    {
        private readonly Action selectionChanged;
        private bool isSelected;

        internal AppRestorePackageViewModel(string identifier, Action selectionChanged)
        {
            Identifier = identifier ?? string.Empty;
            this.selectionChanged = selectionChanged;
        }

        public string Identifier { get; }

        public bool IsSelected
        {
            get => isSelected;
            set
            {
                if (SetProperty(ref isSelected, value, nameof(IsSelected)))
                    selectionChanged?.Invoke();
            }
        }
    }

    internal sealed class AppRestoreDialogViewModel : ObservableObject
    {
        internal const string StoppingText = "Stopping after the current app (or its timeout)";

        private readonly ObservableCollection<AppRestorePackageViewModel> packages =
            new ObservableCollection<AppRestorePackageViewModel>();
        private readonly Action close;
        private AppRestoreSource selectedSource;
        private bool isInstalling;
        private bool stopRequested;
        private string statusText = string.Empty;

        internal AppRestoreDialogViewModel(string selectedRestorePath,
            IReadOnlyList<SnapshotEvent> snapshots, Action close = null)
        {
            this.close = close;
            Sources = AppRestoreService.BuildSources(selectedRestorePath, snapshots);
            Packages = new ReadOnlyObservableCollection<AppRestorePackageViewModel>(packages);
            InstallCommand = new DelegateCommand(_ => _ = InstallSelectedAsync(), _ => CanInstall);
            StopOrCloseCommand = new DelegateCommand(_ => StopOrClose());

            if (Sources.Count > 0)
                SelectedSource = Sources[0];
            else
                StatusText = "No app backup source is available.";
        }

        public IReadOnlyList<AppRestoreSource> Sources { get; }
        public ReadOnlyObservableCollection<AppRestorePackageViewModel> Packages { get; }
        public DelegateCommand InstallCommand { get; }
        public DelegateCommand StopOrCloseCommand { get; }
        public AppRestoreOutcome Outcome { get; private set; }

        public AppRestoreSource SelectedSource
        {
            get => selectedSource;
            set
            {
                if (isInstalling || !SetProperty(ref selectedSource, value, nameof(SelectedSource)))
                    return;

                ReadSelectedSource();
            }
        }

        public bool IsInstalling
        {
            get => isInstalling;
            private set
            {
                if (!SetProperty(ref isInstalling, value, nameof(IsInstalling)))
                    return;

                OnPropertyChanged(nameof(CanChooseSource));
                OnPropertyChanged(nameof(CanChoosePackages));
                OnPropertyChanged(nameof(CanInstall));
                OnPropertyChanged(nameof(StopCaption));
                InstallCommand.RaiseCanExecuteChanged();
                StopOrCloseCommand.RaiseCanExecuteChanged();
            }
        }

        public bool CanChooseSource => !IsInstalling;
        public bool CanChoosePackages => !IsInstalling;
        public bool CanInstall => !IsInstalling && packages.Any(package => package.IsSelected);
        public string StopCaption => IsInstalling ? (stopRequested ? StoppingText : "Stop after the current app") : "Close";

        public string StatusText
        {
            get => statusText;
            private set => SetProperty(ref statusText, value ?? string.Empty, nameof(StatusText));
        }

        internal bool RequestStop()
        {
            if (!IsInstalling)
                return false;

            stopRequested = true;
            OnPropertyChanged(nameof(StopCaption));
            StopOrCloseCommand.RaiseCanExecuteChanged();
            return true;
        }
        private void StopOrClose()
        {
            if (!RequestStop())
                close?.Invoke();
        }


        internal async Task InstallSelectedAsync()
        {
            if (!CanInstall)
                return;

            string[] identifiers = packages.Where(package => package.IsSelected)
                .Select(package => package.Identifier).ToArray();
            stopRequested = false;
            IsInstalling = true;
            StatusText = "Installing selected apps...";
            Outcome = null;
            OnPropertyChanged(nameof(Outcome));

            try
            {
                Outcome = await AppRestoreService.InstallAsync(identifiers, () => stopRequested);
                StatusText = Outcome.Text;
                OnPropertyChanged(nameof(Outcome));
            }
            catch (Exception ex)
            {
                StatusText = "App installation failed: " + ex.Message;
            }
            finally
            {
                stopRequested = false;
                IsInstalling = false;
            }
        }

        private void ReadSelectedSource()
        {
            packages.Clear();
            Outcome = null;
            OnPropertyChanged(nameof(Outcome));
            if (SelectedSource == null)
            {
                StatusText = "No app backup source is selected.";
                RefreshInstallAvailability();
                return;
            }

            AppExport export = AppRestoreService.ReadFromSourceEntry(SelectedSource);
            AppRestoreListState listState = AppRestoreService.ComposeListState(export);
            foreach (string identifier in listState.Items)
                packages.Add(new AppRestorePackageViewModel(identifier, RefreshInstallAvailability));

            StatusText = export.Message;
            RefreshInstallAvailability();
        }

        private void RefreshInstallAvailability()
        {
            OnPropertyChanged(nameof(CanInstall));
            InstallCommand.RaiseCanExecuteChanged();
        }
    }
}
