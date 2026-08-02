using Xunit;

namespace WinRestoreKit.Tests
{
    /// <summary>
    /// Covers the guard on Utils.OpenUrl. WinRestoreKit's manifest requests highestAvailable, so it
    /// normally runs elevated - and OpenUrl hands its argument to the Windows shell. A string that
    /// is not a web URL is therefore not a harmless bad argument; it is a file or program that gets
    /// launched, at whatever privilege level the app is running with.
    ///
    /// Every caller passes a compile-time constant today, so nothing reaches this from user input.
    /// These tests exist to keep the helper safe by construction as callers are added, since the
    /// launch itself cannot be unit tested.
    /// </summary>
    public class UrlLaunchTests
    {
        [Theory]
        [InlineData("https://github.com/nicolasestrem/WinRestoreKit")]
        [InlineData("http://example.com")]
        [InlineData("https://www.flaticon.com/free-icon/backup_10426480")]
        [InlineData("https://github.com/nicolasestrem/WinRestoreKit/releases/latest")]
        public void IsWebUrl_HttpAndHttps_Accepted(string url)
        {
            Assert.True(Utils.IsWebUrl(url));
        }

        [Fact]
        public void IsWebUrl_AcceptsEveryUrlTheAppActuallyOpens()
        {
            // If a URL constant is ever edited into something OpenUrl would refuse, the link goes
            // dead silently - the refusal only writes a log line. This catches that at build time.
            Assert.True(Utils.IsWebUrl(global::DataHelper.Data.Uri.URL_GITREPO));
            Assert.True(Utils.IsWebUrl(global::DataHelper.Data.Uri.URL_GITLATEST));
            Assert.True(Utils.IsWebUrl(global::DataHelper.Data.Uri.URL_ICONATTRIBUTION));
            Assert.True(Utils.IsWebUrl(global::DataHelper.Data.Uri.URL_MAINTAINER));
            Assert.True(Utils.IsWebUrl(global::DataHelper.Data.Uri.URL_ORIGINAL_PROJECT));
            Assert.True(Utils.IsWebUrl(global::DataHelper.Data.Uri.URL_ASSEMBLY));
        }

        [Theory]
        [InlineData(@"C:\Windows\System32\cmd.exe")]      // Would execute, elevated.
        [InlineData(@"\\attacker\share\payload.exe")]     // UNC path, same problem.
        [InlineData("file:///C:/Windows/System32/cmd.exe")]
        [InlineData("cmd.exe")]
        [InlineData("javascript:alert(1)")]
        [InlineData("ms-settings:windowsupdate")]
        [InlineData("ftp://example.com/payload")]
        public void IsWebUrl_NonWebSchemes_Rejected(string url)
        {
            Assert.False(Utils.IsWebUrl(url));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("not a url")]
        [InlineData("github.com")]                        // Relative - no scheme to trust.
        public void IsWebUrl_MalformedOrRelative_Rejected(string url)
        {
            Assert.False(Utils.IsWebUrl(url));
        }
    }
}
