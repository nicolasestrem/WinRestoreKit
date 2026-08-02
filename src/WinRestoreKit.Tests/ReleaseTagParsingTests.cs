using DataHelper;
using Newtonsoft.Json;
using Xunit;

namespace WinRestoreKit.Tests
{
    /// <summary>
    /// Covers the Phase 4 update-checker primary source: the GitHub Releases API tag.
    /// </summary>
    /// <remarks>
    /// Sibling to <see cref="VersionParsingTests"/>, which pins the AssemblyInfo FALLBACK that
    /// already-deployed clients depend on. Both paths stay tested because both ship: the API is the
    /// primary and the raw file is what answers when the API rate-limits.
    ///
    /// The no-match contract matters as much as the happy path. An empty result makes the caller say
    /// "could not read the latest version number"; a wrong non-empty result would offer a download
    /// for a release that does not exist.
    /// </remarks>
    public class ReleaseTagParsingTests
    {
        [Fact]
        public void BareTag_IsReturnedAsIs()
        {
            Assert.Equal("0.31.0", Data.ParseLatestReleaseTag("{\"tag_name\":\"0.31.0\"}"));
        }

        // This repo tags bare x.y.z; the v-strip is tolerance for a future tagging habit.
        [Theory]
        [InlineData("v0.31.0")]
        [InlineData("V0.31.0")]
        public void LeadingV_IsStripped(string tag)
        {
            Assert.Equal("0.31.0", Data.ParseLatestReleaseTag("{\"tag_name\":\"" + tag + "\"}"));
        }

        [Fact]
        public void OnlyOneLeadingV_IsStripped()
        {
            Assert.Equal("v0.31.0", Data.ParseLatestReleaseTag("{\"tag_name\":\"vv0.31.0\"}"));
        }

        [Fact]
        public void MissingTagName_YieldsEmpty()
        {
            Assert.Equal("", Data.ParseLatestReleaseTag("{\"name\":\"Release 0.31.0\"}"));
        }

        [Fact]
        public void EmptyTagName_YieldsEmpty()
        {
            Assert.Equal("", Data.ParseLatestReleaseTag("{\"tag_name\":\"\"}"));
        }

        [Fact]
        public void NullTagName_YieldsEmpty()
        {
            Assert.Equal("", Data.ParseLatestReleaseTag("{\"tag_name\":null}"));
        }

        /// <summary>A realistic release payload: the tag must survive the surrounding fields.</summary>
        [Fact]
        public void RealisticReleaseBody_YieldsTheTag()
        {
            const string body = @"{
              ""url"": ""https://api.github.com/repos/builtbybel/Appcopier/releases/1"",
              ""html_url"": ""https://github.com/builtbybel/Appcopier/releases/tag/0.31.0"",
              ""id"": 1,
              ""tag_name"": ""0.31.0"",
              ""target_commitish"": ""main"",
              ""name"": ""Release 0.31.0"",
              ""draft"": false,
              ""prerelease"": false,
              ""created_at"": ""2026-07-21T10:00:00Z"",
              ""published_at"": ""2026-07-21T10:05:00Z"",
              ""assets"": [ { ""name"": ""Appcopier.exe"", ""size"": 72351744 } ],
              ""body"": ""Phase 4.""
            }";

            Assert.Equal("0.31.0", Data.ParseLatestReleaseTag(body));
        }

        // Malformed JSON throws rather than returning empty: the caller catches it and falls back to
        // the AssemblyInfo path. Reporting a broken response as "no version" would hide an outage.
        [Theory]
        [InlineData("not json at all")]
        [InlineData("{\"tag_name\": ")]
        [InlineData("[]")]
        public void MalformedJson_Throws(string json)
        {
            Assert.ThrowsAny<JsonException>(() => Data.ParseLatestReleaseTag(json));
        }
    }
}
