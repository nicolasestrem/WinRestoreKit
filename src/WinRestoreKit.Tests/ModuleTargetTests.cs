using WinRestoreKit;
using Conf;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace WinRestoreKit.Tests
{
    /// <summary>
    /// What WTelemetry and WThemes actually point at, after Phase 2c corrected both.
    /// </summary>
    /// <remarks>
    /// Neither correction was pinned by anything before this file existed: the whole suite passed
    /// with WTelemetry exporting a stale control set and WThemes copying 20 MB of stock wallpapers,
    /// because every existing assertion was written generically over the modules' own fields and so
    /// agreed with whatever those fields happened to say.
    ///
    /// Nothing here runs regedit. The restore-side assertions use an empty folder, which
    /// ImportRegistryKey answers from RegFile.Validate without reaching the tool.
    /// </remarks>
    public class ModuleTargetTests
    {
        private static string NewTempDir()
        {
            string dir = Path.Combine(Path.GetTempPath(), "appcopier-targets-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        // The direct regression test for the ControlSet001 defect.
        //
        // The failure it guards against is not a crash and not a skip: ControlSet001 usually still
        // exists as a stale hive after a LastKnownGood boot promotes a different set, so the export
        // succeeded, the row was green, and the data was the configuration the running system was
        // NOT using.
        [Fact]
        public void Telemetry_ExportsTheControlSetWindowsIsRunning()
        {
            WTelemetry m = new WTelemetry();

            Assert.Contains(m.Keys, k => k.Contains(@"\CurrentControlSet\Services\DiagTrack"));
            Assert.DoesNotContain(m.Keys, k => k.Contains("ControlSet001"));
        }

        // Swept across every module rather than asserted on WTelemetry alone: a numbered control set
        // is wrong wherever it appears, and WTelemetry was the only one only because nobody had
        // written this yet.
        [Fact]
        public void NoModuleExportsANumberedControlSet()
        {
            List<string> keys = AllDeclaredKeys().ToList();

            // Without this the sweep would pass vacuously if the module shapes ever changed.
            Assert.True(keys.Count >= 15, "only found " + keys.Count + " keys to sweep");

            Assert.DoesNotContain(keys, k =>
                System.Text.RegularExpressions.Regex.IsMatch(k, @"ControlSet\d", System.Text.RegularExpressions.RegexOptions.IgnoreCase));
        }

        /// <summary>
        /// Replaces Themes_DeclaresBothFoldersThenItsKey, whose name became a lie.
        /// </summary>
        /// <remarks>
        /// That test was written generically over m.Folders and m.Keys, so it went on passing while
        /// a folder was removed and a key added underneath it - the assertion tracked the fields
        /// rather than the intent. These are literals for that reason.
        /// </remarks>
        [Fact]
        public void Themes_DeclaresItsProfileFolderThenBothKeys()
        {
            WThemes m = new WThemes();

            IReadOnlyList<RestoreTarget> targets = m.RestoreTargets;

            Assert.Equal(
                new[] { RestoreTargetKind.Folder, RestoreTargetKind.RegistryKey, RestoreTargetKind.RegistryKey },
                targets.Select(t => t.Kind).ToArray());

            Assert.Single(m.Folders);
            Assert.DoesNotContain(m.Folders, f => f.Contains(@"\Web\Wallpaper"));
            Assert.Contains(m.Keys, k => k == @"HKEY_CURRENT_USER\Control Panel\Desktop");
        }

        /// <summary>
        /// The invariant dropping the stock-wallpaper folder actually bought.
        /// </summary>
        /// <remarks>
        /// Deliberately NOT "every folder is inside the user profile". Data.RoamingAppData is
        /// SpecialFolder.ApplicationData, which under Folder Redirection is a UNC path on a file
        /// server - so that assertion would fail on a correctly-working domain machine, which is
        /// the cry-wolf direction this codebase treats as a defect in its own right.
        /// </remarks>
        [Fact]
        public void Themes_WritesNothingMachineWide()
        {
            WThemes m = new WThemes();

            string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

            foreach (RestoreTarget t in m.RestoreTargets)
            {
                if (t.Kind == RestoreTargetKind.Folder)
                {
                    Assert.False(t.Path.StartsWith(windows, StringComparison.OrdinalIgnoreCase), t.Path);
                    Assert.False(t.Path.StartsWith(programFiles, StringComparison.OrdinalIgnoreCase), t.Path);
                }
                else
                {
                    Assert.False(t.Path.StartsWith(@"HKEY_LOCAL_MACHINE\", StringComparison.OrdinalIgnoreCase), t.Path);
                }
            }
        }

        /// <summary>
        /// The disclosure text is part of the fix, so it is pinned by intent rather than by prose.
        /// </summary>
        /// <remarks>
        /// Each assertion below stands for one thing a user has to be told before consenting, and
        /// any wording that still tells them is allowed to pass. Asserting on exact sentences would
        /// make every reword a red suite and would train the next author to edit the test until it
        /// went green - which is how a disclosure quietly loses a clause.
        /// </remarks>
        [Fact]
        public void Themes_ClaimsMatchWhatItActuallyTouches()
        {
            WThemes m = new WThemes();

            // 1. No machine-wide claim. The old text apologised for writing into a folder shared by
            //    every account on the PC; the module no longer touches one, and a warning about
            //    something that cannot happen sits in the dialog next to warnings that can.
            AssertOmits(m.WarningMessage, "every account", "WarningMessage",
                "the module writes nothing machine-wide any more");
            AssertOmits(m.WarningMessage, "shared by", "WarningMessage",
                "the module writes nothing machine-wide any more");
            AssertOmits(m.Info, "default Windows wallpapers", "Info",
                "the stock-wallpaper folder is no longer backed up");

            // 2. The passengers measured in the Control Panel\Desktop subtree are disclosed,
            //    because regedit /e cannot leave them behind. Sizing was always named; colours
            //    (the Colors subkey) were not, and a High Contrast backup can restore foreground
            //    and background colours that make the classic UI unreadable.
            AssertDiscloses(m.WarningMessage, new[] { "display", "scaling", "font" },
                "the display/sizing settings that ride along with this key");
            AssertDiscloses(m.WarningMessage, new[] { "colour", "color" },
                "the system colours the Colors subkey restores");

            // 3. RequiresExplorerRestart is true, but an Explorer restart applies neither DPI nor
            //    system colours. Without this clause a user who restarts as prompted sees a
            //    partially-applied state and concludes the restore did nothing.
            AssertDiscloses(m.WarningMessage, new[] { "sign out", "signing out", "sign-out" },
                "that some of these settings need a sign-out, not the Explorer restart offered");

            // 4. The wallpaper pointer is an absolute path containing the user name, so a restore
            //    onto a differently-named account leaves a black desktop while the row still reads
            //    Succeeded. Honesty is the whole deliverable here - the path is not rewritten.
            Assert.True(
                Mentions(m.Info, "user name") || Mentions(m.WarningMessage, "user name"),
                "Neither Info nor WarningMessage mentions the user name in the wallpaper path. " +
                "That path is absolute and user-specific, and a cross-account restore reports " +
                "Succeeded while the desktop goes black - the text is the only thing warning anyone.");
        }

        private static bool Mentions(string text, string needle)
            => text != null && text.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;

        private static void AssertOmits(string text, string needle, string property, string why)
            => Assert.False(Mentions(text, needle),
                property + " still says \"" + needle + "\", but " + why + ". Stale disclosure is " +
                "worse than none: it spends the user's attention on a risk that cannot occur.");

        private static void AssertDiscloses(string text, string[] anyOf, string what)
            => Assert.True(anyOf.Any(n => Mentions(text, n)),
                "WarningMessage no longer discloses " + what + " (looked for any of: " +
                string.Join(", ", anyOf) + "). Reword freely, but this must stay disclosed - it is " +
                "read out in the restore consent dialog before anything is overwritten.");

        // Restoring from a folder holding none of this module's files must say so for every target
        // rather than claiming anything was applied. Three steps, because the folder copy passes
        // NothingBackedUp explicitly and each key reports its own missing file.
        [Fact]
        public async Task Themes_RestoreWithAnEmptyFolder_SkipsEverything()
        {
            WThemes m = new WThemes();
            string dir = NewTempDir();

            try
            {
                ModuleResult result = await m.RestoreAsync(dir);

                Assert.Equal(ResultState.Skipped, result.State);
                Assert.Equal(3, result.Steps.Count);
                Assert.All(result.Steps,
                    s => Assert.Equal("nothing was backed up for this item", s.Reason));
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public async Task Telemetry_RestoreWithAnEmptyFolder_SkipsBothKeys()
        {
            WTelemetry m = new WTelemetry();
            string dir = NewTempDir();

            try
            {
                ModuleResult result = await m.RestoreAsync(dir);

                Assert.Equal(ResultState.Skipped, result.State);
                Assert.Equal(2, result.Steps.Count);
                Assert.All(result.Steps,
                    s => Assert.Equal("nothing was backed up for this item", s.Reason));
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        // Every registry key any module declares, from both module shapes: the public Keys field
        // and RegistryModule's protected Key property.
        private static IEnumerable<string> AllDeclaredKeys()
            => typeof(BackupBase).Assembly
                .GetTypes()
                .Where(t => typeof(BackupBase).IsAssignableFrom(t) && !t.IsAbstract)
                .OrderBy(t => t.Name)
                .Select(t => (BackupBase)Activator.CreateInstance(t))
                .SelectMany(m => m.RestoreTargets
                    .Where(t => t.Kind == RestoreTargetKind.RegistryKey)
                    .Select(t => t.Path));
    }
}
