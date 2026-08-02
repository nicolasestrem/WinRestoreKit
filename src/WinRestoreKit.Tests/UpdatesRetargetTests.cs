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
    /// What WUpdates and APinnedApps say after the Phase 3c retarget, pinned as literals.
    /// </summary>
    /// <remarks>
    /// Written the way ModuleTargetTests is written, and for the same reason it had to be written
    /// at all: a test phrased generically over a module's own fields agrees with whatever those
    /// fields happen to say, so it cannot hold a CORRECTION in place. The suite passed happily
    /// while WUpdates carried a policy key measured absent on the development machine. The keys
    /// below are therefore spelled out here rather than read off the module, so that changing the
    /// module changes this file too and the change has to be argued for.
    ///
    /// Nothing here runs regedit or reads the live registry - these are declaration assertions.
    /// </remarks>
    public class UpdatesRetargetTests
    {
        // The two keys this module is supposed to capture after the retarget. Literals on purpose;
        // see the class remarks.
        private const string WindowsUpdateKey =
            @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate";

        private const string DeliveryOptimizationPolicyKey =
            @"HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization";

        // AbsenceIsNormal and RegFileNameFor are protected, so this walks the hierarchy with
        // DeclaredOnly rather than a single GetMethod call - the shape DeveloperModuleTests uses,
        // for its reason: a member inherited rather than overridden would otherwise be unreachable.
        private static MethodInfo FindProtected(BackupBase module, string name, Type argType)
        {
            for (Type t = module.GetType(); t != null; t = t.BaseType)
            {
                MethodInfo m = t.GetMethod(name,
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                    null, new[] { argType }, null);

                if (m != null)
                    return m;
            }

            throw new InvalidOperationException(
                module.GetType().Name + " does not declare " + name + "(" + argType.Name + ").");
        }

        private static bool AbsenceIsNormalFor(BackupBase module, string key)
            => (bool)FindProtected(module, "AbsenceIsNormal", typeof(string))
                .Invoke(module, new object[] { key });

        private static string RegFileNameFor(BackupBase module, string key)
            => (string)FindProtected(module, "RegFileNameFor", typeof(string))
                .Invoke(module, new object[] { key });

        private static bool Mentions(string text, string needle)
            => text != null && text.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;

        // --- WUpdates: what it points at ---

        [Fact]
        public void Updates_DeclaresTheServicingKeyThenTheDeliveryOptimizationPolicyKey()
        {
            WUpdates m = new WUpdates();

            IReadOnlyList<RestoreTarget> targets = m.RestoreTargets;

            Assert.Equal(2, targets.Count);
            Assert.All(targets, t => Assert.Equal(RestoreTargetKind.RegistryKey, t.Kind));

            Assert.Equal(WindowsUpdateKey, targets[0].Path);
            Assert.Equal(DeliveryOptimizationPolicyKey, targets[1].Path);
        }

        /// <summary>
        /// The \AU policy key is gone and must stay gone.
        /// </summary>
        /// <remarks>
        /// It was measured ABSENT on the development machine (Windows 11 Pro, 2026-07-21), which is
        /// the ordinary state on a consumer PC - it appears only where WSUS or Group Policy created
        /// it. Declared AbsenceIsNormal, it never cried wolf; it just reported Skipped forever. It
        /// was dropped by user decision rather than demoted, and "put the key back, it looks like
        /// an omission" is exactly the well-meaning change this asserts against.
        /// </remarks>
        [Fact]
        public void Updates_NoLongerDeclaresTheAutomaticUpdatesPolicyKey()
        {
            WUpdates m = new WUpdates();

            Assert.DoesNotContain(m.Keys, k => k.EndsWith(@"\AU", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(m.RestoreTargets,
                t => t.Path.EndsWith(@"\AU", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// The per-key Skipped-vs-Failed judgement, which is the whole reason the two keys are not
        /// interchangeable.
        /// </summary>
        /// <remarks>
        /// Wrong in one direction this marks healthy machines red; wrong in the other it hides a
        /// broken Windows Update. Both values are measurements, not guesses: the servicing key was
        /// found present and the Delivery Optimization policy key absent on 2026-07-21.
        /// </remarks>
        [Fact]
        public void Updates_TreatsOnlyTheDeliveryOptimizationKeysAbsenceAsNormal()
        {
            WUpdates m = new WUpdates();

            Assert.False(AbsenceIsNormalFor(m, WindowsUpdateKey));
            Assert.True(AbsenceIsNormalFor(m, DeliveryOptimizationPolicyKey));
        }

        /// <summary>
        /// Two keys, two files. The WThemes defect in miniature.
        /// </summary>
        /// <remarks>
        /// BackupFileNamingTests sweeps this over every multi-key module; repeated here so a
        /// failure caused by this retarget names the module that caused it. If these ever collided
        /// the second export would delete and overwrite the first while both steps reported
        /// Succeeded, and the restore would import the survivor once per key while the post-import
        /// probe found both keys present on the live machine either way.
        /// </remarks>
        [Fact]
        public void Updates_WritesADistinctFileForEachKey()
        {
            WUpdates m = new WUpdates();

            string[] names = m.Keys.Select(k => RegFileNameFor(m, k)).ToArray();

            Assert.Equal(2, names.Length);
            Assert.All(names, n => Assert.False(string.IsNullOrWhiteSpace(n)));
            Assert.NotEqual(names[0], names[1], StringComparer.OrdinalIgnoreCase);
        }

        // --- WUpdates: what it tells the user ---

        /// <summary>
        /// Both disclosures, asserted by intent rather than by exact sentence.
        /// </summary>
        /// <remarks>
        /// Any wording that still tells the user the fact is allowed to pass. Pinning prose would
        /// make every reword a red suite and train the next author to edit the test until it went
        /// green, which is how a disclosure quietly loses a clause.
        /// </remarks>
        [Fact]
        public void Updates_DisclosesTheMachineIdentityAndTheNotConfiguredCases()
        {
            WUpdates m = new WUpdates();

            Assert.False(string.IsNullOrWhiteSpace(m.WarningMessage));

            // 1. The captured key holds SusClientId / SusClientIdValidation - the identifiers
            //    Windows Update uses to recognise this PC. Restoring onto another machine copies
            //    that identity across and can confuse Windows Update or WSUS reporting for both.
            //    Nothing downstream can detect it, so the text is the only warning there is.
            Assert.True(Mentions(m.WarningMessage, "PC"),
                "WarningMessage no longer talks about the PC. Restoring this key onto a different " +
                "machine copies this PC's Windows Update identity across; that must stay disclosed.");

            Assert.True(
                new[] { "identity", "identif", "ID number", "recognise", "recognize", "which machine" }
                    .Any(n => Mentions(m.WarningMessage, n)),
                "WarningMessage no longer discloses that this key carries the identifiers Windows " +
                "Update uses to tell this PC apart from others. Reword freely, but this is read out " +
                "in the restore consent dialog before anything is overwritten.");

            // 2. Delivery Optimization is captured only where it has been configured by policy.
            //    Without this, a user on a stock machine reads a permanently non-green row as a
            //    fault - the cry-wolf direction, arriving through the text rather than the state.
            Assert.True(Mentions(m.WarningMessage, "normal"),
                "WarningMessage no longer says that a missing Delivery Optimization policy is " +
                "normal. It is absent on any PC where nobody configured peer-to-peer update " +
                "sharing, which is most of them.");
        }

        /// <summary>
        /// The reinstall case is named, because it is this app's headline use case.
        /// </summary>
        /// <remarks>
        /// Added by the Phase 3c review, which caught the first draft closing with "restoring it onto
        /// the same PC it came from is what this item is for". That reads as reassurance for exactly
        /// the sequence WinRestoreKit exists to serve - back up, reinstall Windows, restore - and it is
        /// wrong for it: a reinstalled Windows issues a NEW SusClientId, so restoring the old one
        /// re-points the fresh install at an identity already registered with Windows Update. Same
        /// physical machine, same confusion the warning describes for a different PC.
        ///
        /// Pinned because the defect was not a missing warning but a warning that named the risky
        /// scenario as the safe one, and that is the kind of sentence a later edit restores by
        /// accident while every other assertion here stays green.
        /// </remarks>
        [Fact]
        public void Updates_DisclosesThatAReinstallIsNotTheSafeCase()
        {
            WUpdates m = new WUpdates();

            Assert.True(
                new[] { "reinstall", "re-install", "fresh install" }
                    .Any(n => Mentions(m.WarningMessage, n)),
                "WarningMessage no longer names the reinstall case. Back up, reinstall Windows, " +
                "restore is this app's main use case, and a reinstalled Windows gets new Windows " +
                "Update ID numbers - so restoring the old ones is the risky path, not the safe one.");

            Assert.False(
                Mentions(m.WarningMessage, "the same PC it came from is what this item is for"),
                "WarningMessage has regained the sentence the Phase 3c review removed. It tells the " +
                "user the same-PC case is the intended one, which is false immediately after a " +
                "Windows reinstall - the case they are most likely to be in when using this app.");
        }

        // The AU-era phrasing named policy options this module no longer captures at all. Stale
        // disclosure is worse than none: it promises settings the backup does not contain.
        [Fact]
        public void Updates_InfoNoLongerPromisesTheDroppedPolicySettings()
        {
            WUpdates m = new WUpdates();

            Assert.False(string.IsNullOrWhiteSpace(m.Info));
            Assert.False(Mentions(m.Info, "DetectionFrequency"), m.Info);
            Assert.False(Mentions(m.Info, "AutoInstallMinorUpdates"), m.Info);
            Assert.True(Mentions(m.Info, "Delivery Optimization"),
                "Info does not mention Delivery Optimization, which is now half of what this " +
                "module captures.");
        }

        // --- APinnedApps: a text change that must stay a text change ---

        [Fact]
        public void PinnedApps_DisclosesThatTheStartMenuDatabaseIsNotPortable()
        {
            APinnedApps m = new APinnedApps();

            Assert.NotEqual("This is reserved for Windows 11.", m.WarningMessage);

            Assert.True(Mentions(m.WarningMessage, "different PC"),
                "WarningMessage no longer warns that restoring onto a different PC can go wrong. " +
                "The Start menu database is built for one machine and one Windows build.");

            Assert.True(Mentions(m.WarningMessage, "same PC"),
                "WarningMessage no longer states the honest use case - restoring onto the PC the " +
                "backup came from. Without it the warning reads as a reason never to use the item.");

            // The clause that makes the other two matter: a restore here is a folder copy, so it
            // reports Succeeded whether or not the pins survive. Nothing but this sentence tells
            // the user that a green row is not evidence.
            Assert.True(
                new[] { "cannot detect", "can't detect", "still report success" }
                    .Any(n => Mentions(m.WarningMessage, n)),
                "WarningMessage no longer says the app cannot detect a bad outcome. The row reads " +
                "Succeeded regardless, so this is the only thing standing between the user and a " +
                "silently empty Start menu.");
        }

        /// <summary>
        /// The behaviour the text change was not allowed to touch.
        /// </summary>
        /// <remarks>
        /// Pinned so that a later edit cannot turn a wording change into a change of what gets
        /// copied or which process gets killed. The close is deliberately unconsented: Windows
        /// restarts StartMenuExperienceHost within seconds by itself, so the checkbox would spend
        /// the dialog's attention budget on the one close that costs nothing.
        /// </remarks>
        [Fact]
        public void PinnedApps_KeepsItsFolderBaseClassAndUnconsentedClose()
        {
            APinnedApps m = new APinnedApps();

            Assert.IsAssignableFrom<FolderModule>(m);

            RestoreTarget only = Assert.Single(m.RestoreTargets);
            Assert.Equal(RestoreTargetKind.Folder, only.Kind);
            Assert.EndsWith(
                @"\Packages\Microsoft.Windows.StartMenuExperienceHost_cw5n1h2txyewy\LocalState",
                only.Path,
                StringComparison.OrdinalIgnoreCase);

            RestoreCloseRequirement req = Assert.Single(m.ProcessesToCloseBeforeRestore);
            Assert.Equal("StartMenuExperienceHost", req.ProcessName);
            Assert.False(req.NeedsConsent);
        }
    }
}
