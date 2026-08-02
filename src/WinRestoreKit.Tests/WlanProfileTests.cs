using WinRestoreKit;
using System;
using System.IO;
using Xunit;

namespace WinRestoreKit.Tests
{
    public class WlanProfileTests : IDisposable
    {
        private readonly string _dir;

        public WlanProfileTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "acwlan_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, true); } catch { }
        }

        // The real shape netsh produces (measured 2026-07-20), trimmed to its structure.
        private const string RealProfile =
            "<?xml version=\"1.0\"?>\r\n" +
            "<WLANProfile xmlns=\"http://www.microsoft.com/networking/WLAN/profile/v1\">\r\n" +
            "  <name>MyNetwork</name>\r\n" +
            "  <SSIDConfig><SSID><name>MyNetwork</name></SSID></SSIDConfig>\r\n" +
            "</WLANProfile>\r\n";

        private string Write(string name, string content)
        {
            string p = Path.Combine(_dir, name);
            File.WriteAllText(p, content);
            return p;
        }

        // The measured filename shape - note it does NOT start with "WLAN".
        [Fact]
        public void IsWlanProfile_RealNetshFilename_IsRecognised()
            => Assert.True(WlanProfile.IsWlanProfile(Write("Wi-Fi 2-MyNetwork.xml", RealProfile)));

        [Fact]
        public void IsWlanProfile_DifferentInterfaceName_IsStillRecognised()
            => Assert.True(WlanProfile.IsWlanProfile(Write("Wireless Network Connection-Cafe.xml", RealProfile)));

        [Fact]
        public void IsWlanProfile_UnrelatedXml_IsRejected()
            => Assert.False(WlanProfile.IsWlanProfile(Write("other.xml", "<Something><name>x</name></Something>")));

        [Fact]
        public void IsWlanProfile_MalformedXml_IsRejectedWithoutThrowing()
            => Assert.False(WlanProfile.IsWlanProfile(Write("broken.xml", "<WLANProfile>unclosed")));

        [Fact]
        public void IsWlanProfile_MissingFile_IsRejected()
            => Assert.False(WlanProfile.IsWlanProfile(Path.Combine(_dir, "nope.xml")));

        // The bug that made this task necessary: the old WLAN*.xml filter matched none of these.
        [Fact]
        public void FindIn_FindsEveryProfileRegardlessOfInterfaceName()
        {
            Write("Wi-Fi 2-Home.xml", RealProfile);
            Write("Wi-Fi 2-Cafe.xml", RealProfile);
            Write("Wi-Fi-Office.xml", RealProfile);
            Write("Network configuration.txt", "not xml");
            Write("unrelated.xml", "<Other/>");

            string[] found = WlanProfile.FindIn(_dir);

            Assert.Equal(3, found.Length);
        }

        [Fact]
        public void FindIn_MissingFolder_ReturnsEmptyWithoutThrowing()
            => Assert.Empty(WlanProfile.FindIn(Path.Combine(_dir, "nowhere")));

        [Fact]
        public void FindIn_NoProfiles_ReturnsEmpty()
        {
            Write("only.txt", "nothing here");
            Assert.Empty(WlanProfile.FindIn(_dir));
        }
    }
}
