using System;
using System.IO;
using System.Reflection;
using Xunit;

namespace WinRestoreKit.Tests
{
    /// <summary>
    /// Covers the update-checker version handling, which is the one piece of logic in this app that
    /// an ALREADY-DEPLOYED v0.30.0 client depends on. That client downloads
    /// src/WinRestoreKit/Properties/AssemblyInfo.cs as raw text from GitHub and string-parses
    /// [assembly: AssemblyFileVersion("x.y.z")] out of it. If the local and remote sides of that
    /// comparison ever disagree, every existing user either stops being offered updates or is
    /// offered one they already have - silently, with no build-time signal. These tests pin the
    /// contract down on both sides.
    ///
    /// Several tests deliberately assert CURRENT behavior for malformed input rather than desirable
    /// behavior. The parse is fragile (raw index arithmetic), but hardening it is a later phase; the
    /// point here is that any change to it shows up as a failing test instead of a silent shift.
    /// </summary>
    public class VersionParsingTests
    {
        /// <summary>
        /// The real production AssemblyInfo.cs, copied to the output directory by the csproj.
        /// Using the actual file - not a literal - means these tests keep testing the true input
        /// even after a version bump.
        /// </summary>
        private static string RealAssemblyInfoText()
        {
            string path = Path.Combine(AppContext.BaseDirectory, "TestData", "AssemblyInfo.cs");

            Assert.True(
                File.Exists(path),
                $"Expected the production AssemblyInfo.cs to be copied to '{path}'. " +
                "Check the <None Include=\"..\\WinRestoreKit\\Properties\\AssemblyInfo.cs\"> item in WinRestoreKit.Tests.csproj.");

            return File.ReadAllText(path);
        }

        // ---------------------------------------------------------------------------------------
        // Against the real deployed input
        // ---------------------------------------------------------------------------------------

        [Fact]
        public void ParseLatestVersion_OnRealAssemblyInfo_ReturnsThreePartVersion()
        {
            string parsed = global::DataHelper.Data.ParseLatestVersion(RealAssemblyInfoText());

            Assert.False(string.IsNullOrWhiteSpace(parsed));
            Assert.Equal(3, parsed.Split('.').Length);
            // No stray quotes/parens leaked in from the index arithmetic.
            Assert.Equal(parsed, new Version(parsed).ToString(3));
        }

        [Fact]
        public void ParseLatestVersion_OnRealAssemblyInfo_MatchesCompiledAssemblyFileVersion()
        {
            // The remote side of the update check (parsing the file) and the compiled assembly's
            // own metadata must agree. If AssemblyInfo.cs is ever reformatted such that the parse
            // breaks, this is what catches it.
            string parsed = global::DataHelper.Data.ParseLatestVersion(RealAssemblyInfoText());

            string compiled = typeof(global::WinRestoreKit.MainForm).Assembly
                .GetCustomAttribute<AssemblyFileVersionAttribute>()
                .Version;

            Assert.Equal(compiled, parsed);
        }

        [Fact]
        public void ParseLatestVersion_OnRealAssemblyInfo_AgreesWithGetCurrentVersion()
        {
            // The fallback-version invariant. UpdateCheckService.Decide normalizes and compares both
            // sides, so a difference for an up-to-date client must never become a phantom update offer.
            string remoteSide = global::DataHelper.Data.ParseLatestVersion(RealAssemblyInfoText());
            string localSide = global::WinRestoreKit.VersionInfo.GetCurrentVersion(
                typeof(global::WinRestoreKit.MainForm).Assembly);

            Assert.Equal(localSide, remoteSide);
        }

        // ---------------------------------------------------------------------------------------
        // Well-formed input
        // ---------------------------------------------------------------------------------------

        [Fact]
        public void ParseLatestVersion_WellFormedLine_ReturnsVersion()
        {
            string text = "[assembly: AssemblyFileVersion(\"1.2.3\")]";

            Assert.Equal("1.2.3", global::DataHelper.Data.ParseLatestVersion(text));
        }

        [Fact]
        public void ParseLatestVersion_CrLfLineEndings_ReturnsVersion()
        {
            // Split('\n') leaves a trailing '\r' on each line. GitHub raw content can be CRLF, so
            // this path is real, not hypothetical - the index arithmetic must survive it.
            string text = "using System.Reflection;\r\n[assembly: AssemblyFileVersion(\"4.5.6\")]\r\n";

            Assert.Equal("4.5.6", global::DataHelper.Data.ParseLatestVersion(text));
        }

        [Fact]
        public void ParseLatestVersion_LeadingIndentation_ReturnsVersion()
        {
            // Offsets are computed relative to '(' and ')', so whitespace OUTSIDE the parentheses
            // shifts everything uniformly and is harmless.
            string text = "\t    [assembly: AssemblyFileVersion(\"7.8.9\")]   ";

            Assert.Equal("7.8.9", global::DataHelper.Data.ParseLatestVersion(text));
        }

        [Fact]
        public void ParseLatestVersion_PrefersAssemblyFileVersionOverAssemblyVersion()
        {
            // "[assembly: AssemblyVersion" is not a substring of "[assembly: AssemblyFileVersion",
            // so the filter cleanly ignores it even when the two values differ.
            string text = string.Join("\n", new[]
            {
                "[assembly: AssemblyVersion(\"9.9.9\")]",
                "[assembly: AssemblyFileVersion(\"1.2.3\")]"
            });

            Assert.Equal("1.2.3", global::DataHelper.Data.ParseLatestVersion(text));
        }

        // ---------------------------------------------------------------------------------------
        // Documented current behavior for odd / malformed input.
        // These assert what the code DOES today, not what it ideally should do.
        // ---------------------------------------------------------------------------------------

        [Fact]
        public void ParseLatestVersion_MultipleFileVersionLines_LastOneWins()
        {
            // The loop assigns rather than breaking, so later lines overwrite earlier ones.
            string text = string.Join("\n", new[]
            {
                "[assembly: AssemblyFileVersion(\"1.0.0\")]",
                "[assembly: AssemblyFileVersion(\"2.0.0\")]"
            });

            Assert.Equal("2.0.0", global::DataHelper.Data.ParseLatestVersion(text));
        }

        [Fact]
        public void ParseLatestVersion_SpacesInsideParentheses_LeaksTheQuoteCharacters()
        {
            // CURRENT BEHAVIOR, not desired behavior: the +2 / -3 offsets assume the quote sits
            // immediately inside the parenthesis. Padding shifts the window and the surrounding
            // quotes end up in the result, which would then fail new Version(...) downstream.
            // Recorded so a future hardening pass has a baseline to change deliberately.
            string text = "[assembly: AssemblyFileVersion( \"1.2.3\" )]";

            Assert.Equal("\"1.2.3\"", global::DataHelper.Data.ParseLatestVersion(text));
        }

        [Fact]
        public void ParseLatestVersion_NoMatchingLine_ReturnsEmptyString()
        {
            // Empty string - not null, and no throw. UpdateCheckService.Decide classifies it as
            // LatestVersionUnreadable and shows no download offer.
            string text = "using System;\n[assembly: AssemblyVersion(\"1.2.3\")]\n";

            Assert.Equal(string.Empty, global::DataHelper.Data.ParseLatestVersion(text));
        }

        [Fact]
        public void ParseLatestVersion_EmptyInput_ReturnsEmptyString()
        {
            Assert.Equal(string.Empty, global::DataHelper.Data.ParseLatestVersion(string.Empty));
        }

        [Fact]
        public void ParseLatestVersion_MatchingLineWithoutParentheses_Throws()
        {
            // CURRENT BEHAVIOR: IndexOf returns -1 for both parens, producing a negative Substring
            // length. In production this escapes the fallback parser and CheckForUpdatesAsync shows
            // its owner-guarded update-check failure prompt.
            string text = "[assembly: AssemblyFileVersion is mentioned in a comment";

            Assert.Throws<ArgumentOutOfRangeException>(
                () => global::DataHelper.Data.ParseLatestVersion(text));
        }

        [Fact]
        public void ParseLatestVersion_NullInput_Throws()
        {
            // CURRENT BEHAVIOR: no null guard; Split dereferences the argument directly.
            Assert.Throws<NullReferenceException>(
                () => global::DataHelper.Data.ParseLatestVersion(null));
        }

        // ---------------------------------------------------------------------------------------
        // The local side of the comparison
        // ---------------------------------------------------------------------------------------

        [Fact]
        public void GetCurrentVersion_UsesTheAssemblyFileVersionAttribute()
        {
            string version = global::WinRestoreKit.VersionInfo.GetCurrentVersion(
                typeof(VersionParsingTests).Assembly);

            Assert.NotEqual(global::WinRestoreKit.VersionInfo.UnknownVersion, version);
            Assert.Matches("^\\d+\\.\\d+\\.\\d+$", version);
        }

        // ---------------------------------------------------------------------------------------
        // VersionInfo.Normalize - the pure half of the local side. VersionInfo is used during shell
        // construction, so anything that makes this throw is a startup crash.
        // ---------------------------------------------------------------------------------------

        [Theory]
        [InlineData("0.30.0", "0.30.0")]
        [InlineData("0.30.0.0", "0.30.0")]   // The four-part Win32 form collapses to three.
        [InlineData("1.2.3", "1.2.3")]
        [InlineData("  1.2.3  ", "1.2.3")]
        public void NormalizeVersion_WellFormed_ReturnsThreePartVersion(string raw, string expected)
        {
            Assert.Equal(expected, global::WinRestoreKit.VersionInfo.Normalize(raw));
        }

        [Theory]
        [InlineData("1.2.3+abc1234", "1.2.3")]
        [InlineData("1.2.3-preview.1", "1.2.3")]
        public void NormalizeVersion_SemVerSuffix_IsStripped(string raw, string expected)
        {
            // What an AssemblyInformationalVersion would look like if one were ever introduced.
            Assert.Equal(expected, global::WinRestoreKit.VersionInfo.Normalize(raw));
        }

        [Fact]
        public void NormalizeVersion_TwoComponents_ReturnsInputInsteadOfThrowing()
        {
            // The trap that made the naive Version.TryParse fix wrong: "1.2" parses just fine, but
            // Version.ToString(3) throws ArgumentException because Build was never set.
            Assert.Equal("1.2", global::WinRestoreKit.VersionInfo.Normalize("1.2"));
        }

        [Theory]
        [InlineData("not a version")]
        [InlineData("1.2.3.4.5")]
        [InlineData("-")]
        [InlineData("+")]
        public void NormalizeVersion_Unparseable_ReturnsInputVerbatim(string raw)
        {
            // Passed through, not replaced with a plausible-looking placeholder. "-" and "+" also
            // cover the suffix strip producing an empty candidate, which must not turn into empty.
            Assert.Equal(raw, global::WinRestoreKit.VersionInfo.Normalize(raw));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void NormalizeVersion_Missing_ReturnsUnknown(string raw)
        {
            Assert.Equal(global::WinRestoreKit.VersionInfo.UnknownVersion,
                         global::WinRestoreKit.VersionInfo.Normalize(raw));
        }

        [Fact]
        public void NormalizeVersion_UnknownPlaceholder_IsNotMistakableForAVersion()
        {
            Assert.False(Version.TryParse(global::WinRestoreKit.VersionInfo.UnknownVersion, out _));
        }

        [Fact]
        public void Assembly_DeclaresNoInformationalVersion_SoProductVersionCannotDrift()
        {
            // Guards the source-of-truth invariant in VersionInfo. Adding an
            // AssemblyInformationalVersion can make release metadata diverge from the Win32 resource.
            // Asserting absence outright (rather than "absent OR clean") is deliberate: it makes the
            // failure fire at the moment someone introduces the attribute, which is when the decision
            // needs re-examining, instead of waiting for the suffix to actually appear in a release build.
            var informational = typeof(global::WinRestoreKit.MainForm).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>();

            Assert.True(
                informational == null,
                "WinRestoreKit must not declare AssemblyInformationalVersion. Found: " +
                $"'{informational?.InformationalVersion}'. AssemblyInfo.cs's AssemblyFileVersion is the " +
                "single source of truth read by both sides of the update check; see WinRestoreKit.csproj.");
        }

        // ---------------------------------------------------------------------------------------
        // DescribeStartupFailure - the text of the last-resort startup MessageBox in Program.Main.
        // Program's coverage lives in this file, so it goes here rather than in OsVersionTests.
        // ---------------------------------------------------------------------------------------

        [Fact]
        public void DescribeStartupFailure_IncludesTheExceptionTypeAndMessage()
        {
            // Both halves matter to whoever is diagnosing this: the message says what went wrong,
            // the type says what kind of failure it was (a NullReference in a constructor and an
            // IO failure reading a path are investigated in completely different places).
            var ex = new InvalidOperationException("registry key vanished");

            string described = global::WinRestoreKit.Program.DescribeStartupFailure(ex);

            Assert.Contains("InvalidOperationException", described);
            Assert.Contains("registry key vanished", described);
        }

        [Fact]
        public void DescribeStartupFailure_NullException_DoesNotThrow()
        {
            // This runs on the way out of a startup failure. Throwing while DESCRIBING the first
            // failure would destroy the only diagnostic the user ever sees.
            string described = global::WinRestoreKit.Program.DescribeStartupFailure(null);

            Assert.False(string.IsNullOrWhiteSpace(described));
        }

        [Fact]
        public void DescribeStartupFailure_BracesInMessage_SurviveVerbatim()
        {
            // The composed text goes to a MessageBox, which - unlike LogHelper - has no
            // Console.WriteLine fallback: if a brace in the message were ever treated as a format
            // placeholder, the FormatException would take out the error dialog itself and the user
            // would be back to a process that vanishes silently. Hence plain concatenation.
            var ex = new InvalidOperationException("bad {0} key");

            string described = global::WinRestoreKit.Program.DescribeStartupFailure(ex);

            Assert.Contains("bad {0} key", described);
        }
    }
}
