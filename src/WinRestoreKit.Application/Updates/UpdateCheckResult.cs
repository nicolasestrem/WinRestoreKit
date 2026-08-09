namespace WinRestoreKit
{
    internal sealed class UpdateCheckResult
    {
        internal UpdateCheckResult(UpdateVerdict verdict, string currentVersion, string latestVersion,
                                   string errorMessage = null)
        {
            Verdict = verdict;
            CurrentVersion = currentVersion;
            LatestVersion = latestVersion;
            ErrorMessage = errorMessage;
        }

        public UpdateVerdict Verdict { get; }

        public string CurrentVersion { get; }

        public string LatestVersion { get; }

        public string ErrorMessage { get; }
    }
}
