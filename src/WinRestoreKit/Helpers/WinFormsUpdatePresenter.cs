using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using DataHelper;

namespace WinRestoreKit
{
    internal sealed class WinFormsUpdatePresenter
    {
        private readonly IUpdateCheckService updates;
        private readonly IWin32Window owner;
        private readonly string currentVersion;

        internal WinFormsUpdatePresenter(IUpdateCheckService updates, IWin32Window owner,
                                        string currentVersion)
        {
            this.updates = updates ?? throw new ArgumentNullException(nameof(updates));
            this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
            this.currentVersion = currentVersion;
        }

        internal async Task CheckAsync(CancellationToken cancellationToken)
        {
            UpdateCheckResult result = await updates.CheckAsync(currentVersion, cancellationToken);
            if (!CanShowDialog())
                return;

            switch (result.Verdict)
            {
                case UpdateVerdict.CannotDetermineCurrentVersion:
                    MessageBox.Show(owner,
                        "The installed WinRestoreKit version could not be determined, so no update download can be offered.",
                        "WinRestoreKit Update", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                case UpdateVerdict.LatestVersionUnreadable:
                    if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
                    {
                        MessageBox.Show(owner,
                            "Checking for WinRestoreKit updates failed.\n" + result.ErrorMessage,
                            "WinRestoreKit Update", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                    else
                    {
                        MessageBox.Show(owner,
                            "Could not read the latest WinRestoreKit version number from the update file.",
                            "WinRestoreKit Update", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }

                    return;
                case UpdateVerdict.UpToDate:
                    MessageBox.Show(owner, "No new WinRestoreKit updates are available.",
                        "WinRestoreKit Update", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                case UpdateVerdict.UpdateAvailable:
                    if (MessageBox.Show(owner,
                        "WinRestoreKit version " + result.LatestVersion +
                        " is available.\nDo you want to open the download page?",
                        "WinRestoreKit Update Available", MessageBoxButtons.YesNo,
                        MessageBoxIcon.Information, MessageBoxDefaultButton.Button1) == DialogResult.Yes)
                    {
                        Utils.OpenUrl(Data.Uri.URL_GITLATEST);
                    }

                    return;
                default:
                    throw new InvalidOperationException("The update result has an unknown verdict.");
            }
        }

        private bool CanShowDialog()
        {
            return !(owner is Control control) || (!control.IsDisposed && control.Visible);
        }
    }
}
