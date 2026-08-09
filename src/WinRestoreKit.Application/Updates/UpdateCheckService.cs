using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using DataHelper;

namespace WinRestoreKit
{
    internal sealed class UpdateCheckService : IUpdateCheckService
    {
        private static readonly HttpClient Client = CreateClient();

        public async Task<UpdateCheckResult> CheckAsync(string currentVersion,
                                                         CancellationToken cancellationToken)
        {
            if (Decide(currentVersion, string.Empty) == UpdateVerdict.CannotDetermineCurrentVersion)
            {
                return new UpdateCheckResult(UpdateVerdict.CannotDetermineCurrentVersion,
                                             currentVersion,
                                             null);
            }

            try
            {
                if (!await Task.Run(Data.IsInet, cancellationToken))
                {
                    return new UpdateCheckResult(
                        UpdateVerdict.LatestVersionUnreadable,
                        currentVersion,
                        null,
                        "Problem with the internet connection: checking for WinRestoreKit updates failed.");
                }

                string latestTag = await ReadLatestVersionAsync(cancellationToken);
                UpdateVerdict verdict = Decide(currentVersion, latestTag);
                string latestVersion = string.IsNullOrWhiteSpace(latestTag)
                    ? null
                    : VersionInfo.Normalize(latestTag);

                return new UpdateCheckResult(verdict, currentVersion, latestVersion);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                return new UpdateCheckResult(UpdateVerdict.LatestVersionUnreadable,
                                             currentVersion,
                                             null,
                                             ex.Message);
            }
        }

        internal static UpdateVerdict Decide(string currentVersion, string latestTag)
        {
            if (string.Equals(currentVersion, VersionInfo.UnknownVersion, StringComparison.Ordinal))
                return UpdateVerdict.CannotDetermineCurrentVersion;

            if (string.IsNullOrWhiteSpace(latestTag))
                return UpdateVerdict.LatestVersionUnreadable;

            string latestVersion = VersionInfo.Normalize(latestTag);
            if (string.Equals(currentVersion, latestVersion, StringComparison.Ordinal))
                return UpdateVerdict.UpToDate;

            if (Version.TryParse(currentVersion, out Version current)
                && Version.TryParse(latestVersion, out Version latest))
            {
                return latest.CompareTo(current) > 0
                    ? UpdateVerdict.UpdateAvailable
                    : UpdateVerdict.UpToDate;
            }

            return UpdateVerdict.UpdateAvailable;
        }

        private static HttpClient CreateClient()
        {
            HttpClient client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd(Data.UserAgent);
            return client;
        }

        private static async Task<string> ReadLatestVersionAsync(CancellationToken cancellationToken)
        {
            try
            {
                using (HttpResponseMessage response = await Client.GetAsync(
                           Data.Uri.URL_GITAPI_LATEST, cancellationToken))
                {
                    response.EnsureSuccessStatusCode();
                    string tag = Data.ParseLatestReleaseTag(
                        await response.Content.ReadAsStringAsync(cancellationToken));

                    if (!string.IsNullOrWhiteSpace(tag))
                        return tag;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
            }

            return Data.ParseLatestVersion(
                await Client.GetStringAsync(Data.Uri.URL_ASSEMBLY, cancellationToken));
        }
    }
}
