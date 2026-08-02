using DataHelper;
using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace WinRestoreKit
{
    /// <summary>
    /// The interactive half of the update check: everything that talks to the user, and the version
    /// comparison that decides what it says.
    /// </summary>
    /// <remarks>
    /// This lived on <see cref="Data"/> until Phase 4 PR 2, for two reasons that both point the same
    /// way. It is almost entirely MessageBoxes, and Data is engine code that no longer references
    /// WinForms; and it calls <see cref="Program"/>, which is the application entry point and cannot
    /// be reached from a library the application itself depends on.
    ///
    /// Phase 4 retargeted it at the GitHub Releases API and replaced WebClient with HttpClient. The
    /// Releases API is the PRIMARY source and Properties/AssemblyInfo.cs is the FALLBACK, taken on
    /// any primary failure at all - non-2xx (including the rate-limit 403 that shared IPs hit),
    /// timeout, malformed JSON, or an empty tag. The fallback is the exact pre-Phase-4 behaviour, so
    /// a rate-limited client is no worse off than before rather than being told "no update".
    ///
    /// The five message texts name WinRestoreKit clearly, and both sides of the comparison still go through
    /// Program.NormalizeVersion - normalizing only one side makes an up-to-date client report a
    /// phantom update on every check.
    /// </remarks>
    internal static class UpdateCheck
    {
        /// <summary>
        /// One client for the whole process; a new one per call leaks sockets in TIME_WAIT.
        /// </summary>
        /// <remarks>
        /// The User-Agent is not optional: api.github.com answers 403 to a request without one.
        /// </remarks>
        private static readonly HttpClient Client = CreateClient();

        private static HttpClient CreateClient()
        {
            HttpClient client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd(Data.UserAgent);
            return client;
        }

        internal static async Task CheckForUpdatesAsync()
        {
            // Probed ONCE, and the else below must stay an else. IsInet returns bool, not bool?, so
            // an "else if (IsInet() == false)" is not a third case - it is a second five-second
            // synchronous probe on the UI thread, and it only ever runs for the offline user who
            // already waited out the first one. That doubled the freeze to ten seconds before the
            // "problem on Internet connection" message appeared.
            if (Data.IsInet())
            {
                try
                {
                    string parsed = await ReadLatestVersionAsync();

                    if (string.IsNullOrWhiteSpace(parsed))
                    {
                        // Neither source held a version we could read. Saying so beats the old
                        // behavior, which compared "" against the current version, found them
                        // unequal, and offered a download for a nonexistent release.
                        MessageBox.Show(
                            "Could not read the latest WinRestoreKit version number from the update file.",
                            "WinRestoreKit Update", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }

                    // Both sides go through the same normalization. Comparing a raw remote string
                    // against a normalized local one means a four-part or suffixed version upstream
                    // would never match, and every up-to-date user would be offered a phantom
                    // update on every check, forever.
                    string latestVersion = Program.NormalizeVersion(parsed);
                    string currentVersion = Program.GetCurrentVersionTostring();

                    if (latestVersion == currentVersion)                          // Up-to-date
                    {
                        MessageBox.Show("No new WinRestoreKit updates are available.", "WinRestoreKit Update", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    else                                                          // Update available
                    {
                        if (MessageBox.Show($"WinRestoreKit version {latestVersion} is available.\nDo you want to open the download page?", "WinRestoreKit Update Available", MessageBoxButtons.YesNo, MessageBoxIcon.Information, MessageBoxDefaultButton.Button1) == DialogResult.Yes)
                            Utils.OpenUrl(Data.Uri.URL_GITLATEST);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Checking for WinRestoreKit updates failed.\n{ex.Message}");
                }
            }
            else
            {
                MessageBox.Show("Problem with the internet connection: checking for WinRestoreKit updates failed.");
            }
        }

        /// <summary>
        /// The newest published version: the Releases API tag, or the AssemblyInfo version when that
        /// path fails for any reason.
        /// </summary>
        private static async Task<string> ReadLatestVersionAsync()
        {
            try
            {
                using (HttpResponseMessage response = await Client.GetAsync(Data.Uri.URL_GITAPI_LATEST))
                {
                    response.EnsureSuccessStatusCode();

                    string tag = Data.ParseLatestReleaseTag(await response.Content.ReadAsStringAsync());

                    if (!string.IsNullOrWhiteSpace(tag))
                        return tag;
                }
            }
            catch (Exception)
            {
                // Any primary failure falls through to the file the deployed clients already read.
                // Swallowed deliberately and narrowly: the fallback below reports its own failure to
                // the caller's catch, so a total outage is still surfaced rather than hidden.
            }

            return Data.ParseLatestVersion(await Client.GetStringAsync(Data.Uri.URL_ASSEMBLY));
        }
    }
}
