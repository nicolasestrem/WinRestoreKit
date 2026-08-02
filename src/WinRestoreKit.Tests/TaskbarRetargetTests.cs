using WinRestoreKit;
using Conf;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Xunit;

namespace WinRestoreKit.Tests
{
    /// <summary>
    /// What WTaskbar actually points at after the Phase 3c retarget, pinned as literals.
    /// </summary>
    /// <remarks>
    /// Written the way ModuleTargetTests explains: a test phrased generically over a module's own
    /// Folders and Keys agrees with whatever those fields happen to say, so it goes on passing
    /// while the module is retargeted underneath it. The whole point of these assertions is that
    /// the module now captures pinned apps, so the paths are spelled out here rather than read back
    /// out of the thing under test. The one exception is the folder root, which is built from the
    /// same Data.* property the module uses because it is machine-dependent.
    ///
    /// The compatibility half matters more than the coverage half. WTaskbar shipped as a single-key
    /// RegistryModule whose export was named Taskbar.reg, and every backup already on disk holds
    /// that file. Leaving RegistryModule changes the naming rule, so the legacy name is held here as
    /// a literal: if it ever stops being produced, those backups silently restore as "nothing was
    /// backed up for this item" with no error shown.
    ///
    /// Nothing here runs regedit or touches a hive; every assertion reads declarations.
    /// </remarks>
    public class TaskbarRetargetTests
    {
        private const string AdvancedKey =
            @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";

        private const string TaskbandKey =
            @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\Taskband";

        // Built from the same root the module uses. The rest of the path is a literal: it is the
        // retarget, and Windows chose this location, not us.
        private static string PinnedShortcutsFolder
            => global::DataHelper.Data.RoamingAppData +
               @"\Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar";

        [Fact]
        public void DeclaresThePinnedShortcutsFolderThenBothKeys()
        {
            WTaskbar m = new WTaskbar();

            IReadOnlyList<RestoreTarget> targets = m.RestoreTargets;

            // Folder first, and the order is load-bearing rather than cosmetic: the .lnk shortcuts
            // have to be on disk before the Taskband blob that references them is imported, and
            // this list is also what the consent dialog reads out.
            Assert.Equal(
                new[] { RestoreTargetKind.Folder, RestoreTargetKind.RegistryKey, RestoreTargetKind.RegistryKey },
                targets.Select(t => t.Kind).ToArray());

            Assert.Equal(
                new[] { PinnedShortcutsFolder, AdvancedKey, TaskbandKey },
                targets.Select(t => t.Path).ToArray());
        }

        /// <summary>
        /// The compatibility promise: the key this module always had keeps the filename it always had.
        /// </summary>
        /// <remarks>
        /// RegistryModule named every export Title + ".reg". Leaving that base changes the default to
        /// a key-derived name, so without the override the Advanced key would start being written to
        /// Taskbar_HKEY_CURRENT_USER_..._Advanced.reg and every existing Taskbar.reg would be
        /// orphaned - restoring as a skip, with nothing red to notice.
        /// </remarks>
        [Fact]
        public void TheLegacyFileNameIsKeptForTheKeyThatAlreadyUsesIt()
        {
            WTaskbar m = new WTaskbar();

            Assert.Equal("Taskbar.reg", FileNameFor(m, AdvancedKey));

            // Registry key paths are case-insensitive, so the same key spelled differently is the
            // same key to regedit. Matching case-sensitively would send that spelling to a second
            // file while both spellings kept exporting the one live key.
            Assert.Equal("Taskbar.reg", FileNameFor(m, AdvancedKey.ToLowerInvariant()));
            Assert.Equal("Taskbar.reg", FileNameFor(m, AdvancedKey.ToUpperInvariant()));
        }

        /// <summary>
        /// The new key must not land on the legacy filename.
        /// </summary>
        /// <remarks>
        /// The failure this pins is not a crash. Two keys resolving to one file means the second
        /// export deletes the first and writes over it while both steps report Succeeded, and the
        /// restore imports that one file once per key while the post-import probe finds both keys
        /// present - because they exist on any live machine regardless of what the file held.
        /// </remarks>
        [Fact]
        public void TheNewKeyGetsItsOwnFileName()
        {
            WTaskbar m = new WTaskbar();

            string taskband = FileNameFor(m, TaskbandKey);

            Assert.NotEqual("Taskbar.reg", taskband);
            Assert.Contains("Taskband", taskband, StringComparison.Ordinal);
            Assert.EndsWith(".reg", taskband, StringComparison.Ordinal);
        }

        // Taskband is read by Explorer at start, so a restored pin list changes nothing visible
        // until Explorer restarts. The module never set this while it captured Advanced alone.
        [Fact]
        public void RequiresAnExplorerRestart()
        {
            Assert.True(new WTaskbar().RequiresExplorerRestart);
        }

        /// <summary>
        /// The per-source absence judgements, which differ and are supposed to.
        /// </summary>
        /// <remarks>
        /// Both keys exist on any healthy profile, so an absent one is a real fault. The pinned
        /// shortcuts folder is created when something is first pinned, so an account that has never
        /// pinned anything legitimately has none - marking that Failed is the cry-wolf direction,
        /// a red row on a machine with nothing wrong with it.
        /// </remarks>
        [Fact]
        public void AbsenceIsAFaultForBothKeysButNotForTheFolder()
        {
            WTaskbar m = new WTaskbar();

            Assert.False(InvokeAbsenceRule(m, "AbsenceIsNormal", AdvancedKey));
            Assert.False(InvokeAbsenceRule(m, "AbsenceIsNormal", TaskbandKey));
            Assert.True(InvokeAbsenceRule(m, "AbsenceIsNormalForFolder", PinnedShortcutsFolder));
        }

        /// <summary>
        /// The two things a user has to be told, pinned by intent rather than by prose.
        /// </summary>
        /// <remarks>
        /// Any wording that still tells them passes. Asserting exact sentences would make every
        /// reword a red suite and train the next author to edit the test until it went green, which
        /// is how a disclosure quietly loses a clause.
        ///
        /// The sign-out hazard is real and is not engineered around: Explorer holds the pin list in
        /// memory and writes it back when it exits normally, so a user who restores and then signs
        /// out without restarting Explorer can have the running Explorer overwrite what was just
        /// restored. The app's own restart path kills Explorer, and a killed Explorer never flushes,
        /// so using that button first is the fix - and this text is the only thing that says so.
        /// </remarks>
        [Fact]
        public void WarningDisclosesTheSignOutHazardAndTheMerge()
        {
            WTaskbar m = new WTaskbar();

            AssertDiscloses(m.WarningMessage, new[] { "Explorer" },
                "that Explorer is what overwrites the restored pins");

            AssertDiscloses(m.WarningMessage, new[] { "sign out", "signing out", "sign-out" },
                "that signing out without restarting Explorer first can undo the restore");

            AssertDiscloses(m.WarningMessage, new[] { "merge", "merges", "stay on disk" },
                "that the shortcut copy merges rather than replaces, so pins added since the " +
                "backup are left on disk");
        }

        /// <summary>
        /// The pins only come back on the account they were saved from, and a wrong one looks fine.
        /// </summary>
        /// <remarks>
        /// Added by the Phase 3c review, which dumped the live Favorites blob this module captures
        /// (29,531 bytes, measured 2026-07-21) and found it is a shell ItemID list carrying
        /// account-bearing absolute paths - AppData\Roaming\...\Quick Launch\TaskBar\&lt;App&gt;.lnk -
        /// along with the profile display name.
        ///
        /// The failure it produces is the shape this whole project exists to remove. Restore onto a
        /// different account name or a rebuilt profile: the folder copy genuinely copies 32 files,
        /// both registry imports genuinely apply, Aggregate returns Succeeded - and Explorer cannot
        /// resolve the blob, prunes the pins, and the taskbar comes back empty. Every row green, the
        /// thing the user asked for gone.
        ///
        /// Nothing in the code can detect it, so this text is the entire mitigation, which is why it
        /// is pinned rather than left to survive the next edit on trust. WFonts and APinnedApps
        /// disclose the identical hazard in the same words; this module shipped without it.
        /// </remarks>
        [Fact]
        public void WarningDisclosesThatPinsOnlyReturnOnTheSameAccount()
        {
            WTaskbar m = new WTaskbar();

            AssertDiscloses(m.WarningMessage, new[] { "user name", "username", "same Windows account" },
                "that the pin list records paths containing the Windows user name");

            AssertDiscloses(m.WarningMessage, new[] { "cannot detect", "still report success" },
                "that a restore onto a differently-named account fails invisibly and still " +
                "reports success");

            // The Info text is the one a user actually reads while browsing the tree, and CLAUDE.md
            // records a module whose Info disagreed with its own warning. Both must carry the limit.
            AssertDiscloses(m.Info, new[] { "user name", "username", "same account" },
                "that Info carries the same-account limitation, not only WarningMessage");
        }

        /// <summary>
        /// The declaration is read from the fields on every access, not snapshotted at construction.
        /// </summary>
        /// <remarks>
        /// A cached list would let the consent dialog describe a different set of targets from the
        /// one the restore actually writes. Mutating the field after construction is the only way to
        /// tell a live read from a cached one.
        /// </remarks>
        [Fact]
        public void TargetsAreReadFromTheFieldsAtAccessTime()
        {
            WTaskbar m = new WTaskbar();

            int before = m.RestoreTargets.Count;

            m.Keys.Add(@"HKEY_CURRENT_USER\Software\Appcopier\SyntheticTaskbarKey");

            Assert.Equal(before + 1, m.RestoreTargets.Count);
            Assert.Contains(m.RestoreTargets,
                t => t.Path == @"HKEY_CURRENT_USER\Software\Appcopier\SyntheticTaskbarKey");
        }

        /// <summary>
        /// The hybrid shape itself.
        /// </summary>
        /// <remarks>
        /// RegistryModule can capture exactly one key and no folders, so re-parenting this module
        /// back onto it would silently drop the pinned apps - which is the state the retarget
        /// existed to leave. The public Keys field is load-bearing too: BackupFileNamingTests finds
        /// multi-key modules by reflecting for a field of that name, so a module that renames it
        /// drops out of the collision sweep rather than failing it.
        /// </remarks>
        [Fact]
        public void IsAHandRolledHybridAndNotARegistryModule()
        {
            Assert.Equal(typeof(BackupBase), typeof(WTaskbar).BaseType);
            Assert.False(typeof(RegistryModule).IsAssignableFrom(typeof(WTaskbar)));

            Assert.NotNull(typeof(WTaskbar).GetField("Keys"));
            Assert.NotNull(typeof(WTaskbar).GetField("Folders"));
        }

        /// <summary>
        /// No pre-restore process close, deliberately.
        /// </summary>
        /// <remarks>
        /// Killing Explorer before the import would not help: AutoRestartShell relaunches the shell
        /// within about a second, so a fresh Explorer holding the OLD Taskband would very likely be
        /// running before the import finished - the same overwrite hazard, plus a blanked desktop
        /// while the restore runs. The restart button AFTER the import is the ordering that works,
        /// and the residual risk is disclosed in WarningMessage instead.
        /// </remarks>
        [Fact]
        public void ClosesNoProcessBeforeRestore()
        {
            Assert.Empty(new WTaskbar().ProcessesToCloseBeforeRestore);
        }

        private static bool InvokeAbsenceRule(BackupBase module, string name, string argument)
        {
            MethodInfo m = module.GetType().GetMethod(
                name, BindingFlags.Static | BindingFlags.NonPublic);

            Assert.True(m != null, module.GetType().Name + " has no " + name + " rule to check");

            return (bool)m.Invoke(null, new object[] { argument });
        }

        private static void AssertDiscloses(string text, string[] anyOf, string what)
            => Assert.True(
                anyOf.Any(n => text != null && text.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0),
                "WarningMessage no longer discloses " + what + " (looked for any of: " +
                string.Join(", ", anyOf) + "). Reword freely, but this must stay disclosed - it is " +
                "read out in the restore consent dialog before anything is overwritten.");

        /// <summary>
        /// The name this build writes for one key, read through the seam the module itself uses.
        /// </summary>
        /// <remarks>
        /// Protected, so reflection is the only way in. The hierarchy is walked rather than queried
        /// once because the member is declared as an override - the same helper shape, and the same
        /// reason for it, as BackupFileNamingTests.FileNameFor.
        /// </remarks>
        private static string FileNameFor(BackupBase module, string key)
        {
            for (Type t = module.GetType(); t != null; t = t.BaseType)
            {
                MethodInfo m = t.GetMethod("RegFileNameFor",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);

                if (m != null)
                    return (string)m.Invoke(module, new object[] { key });
            }

            throw new InvalidOperationException("No RegFileNameFor on " + module.GetType().Name);
        }
    }
}
