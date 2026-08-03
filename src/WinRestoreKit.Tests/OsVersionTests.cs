using Microsoft.Win32;
using System;
using System.Linq;
using System.Reflection;
using Xunit;

namespace WinRestoreKit.Tests
{
    /// <summary>
    /// Covers OsHelper, whose GetVersion runs from ConfPageView.SetStyle -> ConfPageView ctor ->
    /// MainForm ctor -> the ARGUMENT to Application.Run. That is outside the message pump, so an
    /// exception there is not a dialog, it is process termination via WER with no window and no log.
    ///
    /// WHICH OF THESE TESTS IS THE REGRESSION TEST: the "reading path" tests. The original defect
    /// was <c>key.GetValue("UBR").ToString()</c> - a null dereference against a REGISTRY VALUE that
    /// is absent on imaged and sysprepped installs. The ComposeVersion tests below do NOT cover
    /// that: ComposeVersion is a pure function that can only ever see the strings a caller hands it,
    /// so reintroducing the null deref in the reader leaves every one of them green. The tests that
    /// actually fail on the reintroduced bug are the ones that drive OsHelper.GetVersion(openKey)
    /// against a real key created under HKCU\Software\WinRestoreKit\Tests - in particular
    /// GetVersion_KeyHasBuildButNoUbr_ReturnsTheBuildAlone, which is the exact shape of the bug.
    ///
    /// Everything here runs unelevated, opens no dialog and starts no message loop. HKCU is
    /// writable without elevation; HKLM is never written. ConfPageView is never instantiated -
    /// IntroTemplate is a compile-time const, reachable via InternalsVisibleTo.
    /// </summary>
    public class OsVersionTests
    {
        // -------------------------------------------------------------------------------------
        // The reading path, against a real registry key this test owns. THE regression coverage.
        //
        // Each test creates a throwaway key under HKCU\Software\WinRestoreKit\Tests\<guid>, shapes it,
        // points OsHelper at it through the key-opener seam, and deletes it in a finally. A guid
        // per test keeps concurrent xunit collections from colliding.
        // -------------------------------------------------------------------------------------

        private const string TestRoot = @"Software\WinRestoreKit\Tests";

        /// <summary>
        /// Runs <paramref name="body"/> against a freshly created, empty HKCU key, then deletes it.
        /// </summary>
        /// <param name="body">
        /// Receives a factory that opens the key for reading - the same shape OsHelper's production
        /// opener has. It is a factory, not a key, because OsHelper disposes what it opens, so a
        /// caller that reads twice needs two handles.
        /// </param>
        private static void WithTempKey(Action<RegistryKey, Func<RegistryKey>> body)
        {
            string path = TestRoot + @"\" + Guid.NewGuid().ToString("N");

            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(path))
                {
                    Assert.NotNull(key);   // A failure to create is a broken test, not a pass.
                    body(key, () => Registry.CurrentUser.OpenSubKey(path));
                }
            }
            finally
            {
                // DeleteSubKeyTree with throwOnMissingSubKey:false - a test that failed before it
                // created anything must not have its real failure masked by a cleanup throw.
                Registry.CurrentUser.DeleteSubKeyTree(path, throwOnMissingSubKey: false);
            }
        }

        [Fact]
        public void GetVersion_KeyHasBuildAndUbr_JoinsThemWithADot()
        {
            WithTempKey((key, open) =>
            {
                key.SetValue("CurrentBuild", "26100");
                key.SetValue("UBR", 4652, RegistryValueKind.DWord);

                Assert.Equal("Build 26100.4652", global::DataHelper.OsHelper.GetVersion(open));
            });
        }

        [Fact]
        public void GetVersion_KeyHasBuildButNoUbr_ReturnsTheBuildAlone()
        {
            // THE regression test. This is the exact registry shape the old code died on: the key
            // is present and readable, CurrentBuild is there, UBR simply is not. The old
            // key.GetValue("UBR").ToString() throws NullReferenceException here; the reader's catch
            // then turns it into the unreadable token, so this assertion fails on the unfixed code.
            WithTempKey((key, open) =>
            {
                key.SetValue("CurrentBuild", "26100");

                string version = global::DataHelper.OsHelper.GetVersion(open);

                Assert.Equal("Build 26100", version);
                // Specifically NOT the unreadable token: the key read fine, it just has no UBR.
                Assert.NotEqual(global::DataHelper.OsHelper.BuildUnreadable, version);
            });
        }

        [Fact]
        public void GetVersion_KeyHasOnlyTheLegacyBuildName_StillReadsIt()
        {
            // CurrentBuildNumber is the legacy spelling and is the fallback in the reader.
            WithTempKey((key, open) =>
            {
                key.SetValue("CurrentBuildNumber", "26100");

                Assert.Equal("Build 26100", global::DataHelper.OsHelper.GetVersion(open));
            });
        }

        [Fact]
        public void GetVersion_KeyIsPresentButEmpty_ReturnsTheUnknownToken()
        {
            // Read succeeded, there is just no build number in it. Unknown, not unreadable.
            WithTempKey((key, open) =>
                Assert.Equal(global::DataHelper.OsHelper.BuildUnknown,
                             global::DataHelper.OsHelper.GetVersion(open)));
        }

        [Fact]
        public void GetVersion_ValueIsPresentButBlank_ReturnsTheUnknownToken()
        {
            // A whitespace-only value is not a build number. It must not render as "Build   ".
            WithTempKey((key, open) =>
            {
                key.SetValue("CurrentBuild", "   ");

                Assert.Equal(global::DataHelper.OsHelper.BuildUnknown,
                             global::DataHelper.OsHelper.GetVersion(open));
            });
        }

        [Fact]
        public void GetVersion_KeyIsAbsent_ReturnsTheUnknownToken()
        {
            // OpenSubKey returns null for a key that does not exist. That is a SUCCESSFUL read of a
            // machine that carries nothing - so it is the unknown token, not the unreadable one.
            string path = TestRoot + @"\" + Guid.NewGuid().ToString("N") + @"\never-created";

            Assert.Equal(global::DataHelper.OsHelper.BuildUnknown,
                global::DataHelper.OsHelper.GetVersion(() => Registry.CurrentUser.OpenSubKey(path)));
        }

        [Fact]
        public void GetVersion_KeyCannotBeOpened_ReturnsTheUnreadableToken()
        {
            // The third OpenSubKey outcome: it throws, as it does on HKLM\SECURITY, whose ACL
            // denies even an elevated administrator. A throwing stand-in rather than that real key,
            // because the point here is the branch, and asserting on HKLM\SECURITY would make the
            // result depend on the host's ACLs. ProbeKeyTests already pins the real-throw case.
            string version = global::DataHelper.OsHelper.GetVersion(
                () => throw new System.Security.SecurityException("denied"));

            Assert.Equal(global::DataHelper.OsHelper.BuildUnreadable, version);
        }

        [Fact]
        public void GetVersion_KeyCannotBeOpened_DoesNotPropagateTheException()
        {
            // The whole contract of this file: nothing escapes, because there is no pump to catch it.
            Exception caught = Record.Exception(() => global::DataHelper.OsHelper.GetVersion(
                () => throw new InvalidOperationException("boom")));

            Assert.Null(caught);
        }

        [Fact]
        public void GetVersion_ReadsTheKeyAfreshEachCall()
        {
            // OsHelper disposes the handle it is given. A reader that cached or reused it would
            // throw ObjectDisposedException on the second call rather than returning the same text.
            WithTempKey((key, open) =>
            {
                key.SetValue("CurrentBuild", "26100");

                Assert.Equal(global::DataHelper.OsHelper.GetVersion(open),
                             global::DataHelper.OsHelper.GetVersion(open));
            });
        }

        // -------------------------------------------------------------------------------------
        // ComposeVersion - the pure formatting surface. Covers the RENDERING rules (no trailing
        // dot, no invented ".0", no double space in the greeting). NOT the null-deref regression:
        // see the class remarks.
        // -------------------------------------------------------------------------------------

        [Fact]
        public void ComposeVersion_BuildAndUbr_JoinsThemWithADot()
        {
            Assert.Equal("Build 26100.4652",
                global::DataHelper.OsHelper.ComposeVersion("26100", "4652"));
        }

        [Fact]
        public void ComposeVersion_BuildOnly_OmitsTheRevisionEntirely()
        {
            string version = global::DataHelper.OsHelper.ComposeVersion("26100", null);

            Assert.Equal("Build 26100", version);
            // Not padded to a revision the machine never reported...
            Assert.DoesNotContain(".0", version);
            // ...and not left with a dangling separator either.
            Assert.False(version.EndsWith("."), $"Trailing dot in '{version}'.");
        }

        [Theory]
        [InlineData(null, "4652")]
        [InlineData("", "4652")]
        [InlineData("   ", null)]
        [InlineData(null, null)]
        public void ComposeVersion_NoUsableBuild_ReturnsTheUnknownToken(string build, string ubr)
        {
            string version = global::DataHelper.OsHelper.ComposeVersion(build, ubr);

            Assert.Equal(global::DataHelper.OsHelper.BuildUnknown, version);
        }

        [Theory]
        [InlineData("26100", "4652")]
        [InlineData("26100", null)]
        [InlineData(null, "4652")]
        [InlineData("", "4652")]
        [InlineData("   ", null)]
        [InlineData(null, null)]
        public void ComposeVersion_AnyInput_IsNeverEmptyOrPadded(string build, string ubr)
        {
            string version = global::DataHelper.OsHelper.ComposeVersion(build, ubr);

            Assert.False(string.IsNullOrWhiteSpace(version));
            Assert.Equal(version.Trim(), version);
            Assert.DoesNotContain("  ", version);
        }


        [Fact]
        public void UnreadableToken_IsDistinctFromUnknownToken()
        {
            // "The registry said there is no build number" and "the registry could not be read" are
            // different situations. The log line that would otherwise carry that distinction is
            // discarded at startup (LogHelper only writes once SetTarget has run, and ConfPageView
            // calls SetTarget on the line AFTER the greeting), so the tokens must differ.
            Assert.NotEqual(global::DataHelper.OsHelper.BuildUnknown,
                            global::DataHelper.OsHelper.BuildUnreadable);
        }

        // -------------------------------------------------------------------------------------
        // Happy-path smoke, against the real HKLM key on whatever host runs the suite. NOT
        // regression coverage: the unfixed code passes these on a healthy Windows install.
        // -------------------------------------------------------------------------------------

        [Fact]
        public void GetVersion_OnThisHost_MatchesOneOfTheAllowedShapes()
        {
            string version = global::DataHelper.OsHelper.GetVersion();

            Assert.Matches(
                @"^(Build \d+(\.\d+)?|\(build unknown\)|\(build unreadable\))$",
                version);
        }

        [Fact]
        public void GetVersion_DoesNotThrow()
        {
            // Smoke only - see the class remarks. A healthy host cannot exercise the null paths;
            // the reading-path tests above supply that failing input.
            Exception caught = Record.Exception(() => global::DataHelper.OsHelper.GetVersion());

            Assert.Null(caught);
        }

        [Fact]
        public void GetVersion_IsRepeatable()
        {
            // Would catch a reader that consumes or disposes shared state on the first call.
            Assert.Equal(global::DataHelper.OsHelper.GetVersion(),
                         global::DataHelper.OsHelper.GetVersion());
        }

        // -------------------------------------------------------------------------------------
        // Structural guard
        // -------------------------------------------------------------------------------------

        [Fact]
        public void OsHelper_HasNoEagerStatics()
        {
            // OsHelper used to carry "public static readonly string thisOS = IsWin11() + GetVersion()",
            // read by nothing. GetVersion runs outside the message pump, and a throw inside a static
            // field INITIALIZER surfaces as TypeInitializationException, which leaves the type
            // unusable for the rest of the process - so a recoverable registry hiccup becomes a hard
            // startup failure with no window and no log. That is what this test forbids.
            //
            // Consts are excluded via IsLiteral, and that exclusion is correct rather than a
            // loophole: a const has no initializer, contributes no .cctor and is inlined at every
            // call site, so it cannot throw at type load. It is categorically not the hazard.
            // "static readonly" IS the hazard - it does emit a .cctor - and is still rejected here.
            // Do not convert the consts in OsHelper into properties to satisfy this test; that was
            // tried, and it is the test wagging the code.
            //
            // Asserting the ABSENCE OF EAGER STATIC FIELDS rather than calling RunClassConstructor:
            // with none, the compiler emits no .cctor at all, so RunClassConstructor would be a
            // guaranteed no-op - a test with no possible failing input. This assertion does fail the
            // moment someone reintroduces an eagerly-initialised static.
            FieldInfo[] eager = typeof(global::DataHelper.OsHelper)
                .GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(f => !f.IsLiteral)
                .ToArray();

            Assert.True(eager.Length == 0,
                "OsHelper must declare no eagerly-initialised static fields. A throw in a static " +
                "field initializer becomes TypeInitializationException and permanently bricks the " +
                "type, and GetVersion runs before the message pump exists, so that kills startup " +
                "with no dialog and no log. Use a const (inlined, no .cctor, cannot throw) or a " +
                "method. Offending field(s): " + string.Join(", ", eager.Select(f => f.Name)) + ".");
        }
    }
}
