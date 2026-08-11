using System;
using System.Threading;
using System.Threading.Tasks;
using DataHelper;
using WinRestoreKit;

namespace WinRestoreKit.Wpf.Services
{
    internal sealed class WpfUpdatePresenter
    {
        private readonly IUpdateCheckService updates;
        private readonly IWpfDialogService dialogs;
        private readonly IExternalLinkService links;

        internal WpfUpdatePresenter(IUpdateCheckService updates, IWpfDialogService dialogs,
                                    IExternalLinkService links)
        {
            this.updates = updates ?? throw new ArgumentNullException(nameof(updates));
            this.dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
            this.links = links ?? throw new ArgumentNullException(nameof(links));
        }

        internal async Task CheckAsync(string currentVersion, CancellationToken cancellationToken)
        {
            UpdateCheckResult result = await updates.CheckAsync(currentVersion, cancellationToken);
            switch (result.Verdict)
            {
                case UpdateVerdict.CannotDetermineCurrentVersion:
                    dialogs.ShowWarning(
                        "The installed WinRestoreKit version could not be determined, so no update download can be offered.",
                        "WinRestoreKit Update");
                    return;
                case UpdateVerdict.LatestVersionUnreadable:
                    if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
                        dialogs.ShowError("Checking for WinRestoreKit updates failed.\n" + result.ErrorMessage,
                                          "WinRestoreKit Update");
                    else
                        dialogs.ShowWarning(
                            "Could not read the latest WinRestoreKit version number from the update file.",
                            "WinRestoreKit Update");
                    return;
                case UpdateVerdict.UpToDate:
                    dialogs.ShowInformation("No new WinRestoreKit updates are available.", "WinRestoreKit Update");
                    return;
                case UpdateVerdict.UpdateAvailable:
                    if (dialogs.Confirm(
                        "WinRestoreKit version " + result.LatestVersion +
                        " is available.\nDo you want to open the download page?",
                        "WinRestoreKit Update Available"))
                    {
                        links.Open(Data.Uri.URL_GITLATEST);
                    }

                    return;
                default:
                    throw new InvalidOperationException("The update result has an unknown verdict.");
            }
        }

        internal void OpenReleaseNotes() => links.Open(Data.Uri.URL_GITLATEST);
    }
}
