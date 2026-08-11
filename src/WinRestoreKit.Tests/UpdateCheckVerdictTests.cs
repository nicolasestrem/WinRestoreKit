using WinRestoreKit;
using Xunit;

namespace WinRestoreKit.Tests
{
    public class UpdateCheckVerdictTests
    {
        [Fact]
        public void Decide_ReturnsWarningVerdictWhenInstalledVersionIsUnknown()
        {
            UpdateVerdict verdict = UpdateCheckService.Decide(VersionInfo.UnknownVersion, "1.2.3");

            Assert.Equal(UpdateVerdict.CannotDetermineCurrentVersion, verdict);
            Assert.NotEqual(UpdateVerdict.UpdateAvailable, verdict);
        }

        [Fact]
        public void Decide_ReturnsUpToDateWhenVersionsMatch()
        {
            UpdateVerdict verdict = UpdateCheckService.Decide("1.2.3", "1.2.3");

            Assert.Equal(UpdateVerdict.UpToDate, verdict);
        }

        [Theory]
        [InlineData("1.2.2", "1.2.3", true)]
        [InlineData("1.2.4", "1.2.3", false)]
        public void Decide_ComparesInstalledAndLatestVersions(string currentVersion, string latestTag,
                                                               bool updateAvailable)
        {
            UpdateVerdict verdict = UpdateCheckService.Decide(currentVersion, latestTag);

            Assert.Equal(updateAvailable ? UpdateVerdict.UpdateAvailable : UpdateVerdict.UpToDate, verdict);
        }

        [Fact]
        public void UpdateCheckResult_PreservesTheNeutralErrorMessage()
        {
            UpdateCheckResult result = new UpdateCheckResult(
                UpdateVerdict.LatestVersionUnreadable,
                "1.2.3",
                null,
                "network unavailable");

            Assert.Equal(UpdateVerdict.LatestVersionUnreadable, result.Verdict);
            Assert.Equal("1.2.3", result.CurrentVersion);
            Assert.Null(result.LatestVersion);
            Assert.Equal("network unavailable", result.ErrorMessage);
        }
    }
}
