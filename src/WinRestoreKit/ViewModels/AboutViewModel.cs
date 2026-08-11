using System;
using System.Threading;
using WinRestoreKit;
using WinRestoreKit.Wpf.Infrastructure;
using WinRestoreKit.Wpf.Services;

namespace WinRestoreKit.Wpf.ViewModels
{
    internal sealed class AboutViewModel : ObservableObject
    {
        private readonly WpfUpdatePresenter updates;

        internal AboutViewModel(WpfUpdatePresenter updates, string currentVersion)
        {
            this.updates = updates ?? throw new ArgumentNullException(nameof(updates));
            CurrentVersion = string.IsNullOrWhiteSpace(currentVersion)
                ? VersionInfo.UnknownVersion
                : currentVersion;
            CheckForUpdatesCommand = new DelegateCommand(_ => CheckForUpdates());
            OpenReleaseNotesCommand = new DelegateCommand(_ => updates.OpenReleaseNotes());
        }

        public string CurrentVersion { get; }

        public DelegateCommand CheckForUpdatesCommand { get; }

        public DelegateCommand OpenReleaseNotesCommand { get; }

        private async void CheckForUpdates()
        {
            await updates.CheckAsync(CurrentVersion, CancellationToken.None);
        }
    }
}
