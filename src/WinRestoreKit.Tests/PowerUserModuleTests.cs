using WinRestoreKit;
using Conf;
using DataHelper;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Xunit;

namespace WinRestoreKit.Tests
{
    // The Phase 3c power-user modules, pinned at the level of what they DECLARE rather than what
    // they do. Nothing here launches regedit, reads a real hive, or copies a real font: the
    // registry work needs elevation and would write to the machine running the suite, so coverage
    // deliberately stops at the declarations - the keys, the folders, the absence judgements and
    // the disclosures the user reads before consenting to a restore. The base-class behaviour
    // these modules inherit is held by RegistryModuleTests and BackupFileNamingTests.
    public class PowerUserModuleTests
    {
        // AbsenceIsNormal is protected in every shape it takes here, so this reads it the only way
        // a test can. Walking the hierarchy with DeclaredOnly rather than one GetMethod call, for
        // the reason DeveloperModuleTests walks it: a subclass that inherited the member instead
        // of overriding it would otherwise be silently unreachable.
        private static bool InvokeProtectedBool(BackupBase module, string name, Type[] argTypes, object[] args)
        {
            for (Type t = module.GetType(); t != null; t = t.BaseType)
            {
                MethodInfo m = t.GetMethod(name,
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                    null, argTypes, null);

                if (m != null)
                    return (bool)m.Invoke(module, args);
            }

            throw new InvalidOperationException(
                module.GetType().Name + " does not declare " + name + ".");
        }

        // RegistryModule's flag is a parameterless property, not a method.
        private static bool AbsenceIsNormalProperty(BackupBase module)
        {
            for (Type t = module.GetType(); t != null; t = t.BaseType)
            {
                PropertyInfo p = t.GetProperty("AbsenceIsNormal",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);

                if (p != null)
                    return (bool)p.GetValue(module);
            }

            throw new InvalidOperationException(
                module.GetType().Name + " does not declare an AbsenceIsNormal property.");
        }

        private static bool AbsenceIsNormalForKey(BackupBase module, string key)
            => InvokeProtectedBool(module, "AbsenceIsNormal", new[] { typeof(string) }, new object[] { key });

        private static bool AbsenceIsNormalForFolder(BackupBase module, string folder)
            => InvokeProtectedBool(module, "FolderAbsenceIsNormal", new[] { typeof(string) }, new object[] { folder });

        private static List<string> ListField(BackupBase module, string name)
            => (List<string>)module.GetType().GetField(name).GetValue(module);

        // --- WMappedDrives ---

        [Fact]
        public void MappedDrives_DeclaresTheUsersNetworkKeyAndNothingElse()
        {
            WMappedDrives m = new WMappedDrives();

            RestoreTarget only = Assert.Single(m.RestoreTargets);

            Assert.Equal(RestoreTargetKind.RegistryKey, only.Kind);
            Assert.Equal(@"HKEY_CURRENT_USER\Network", only.Path);
        }

        // The judgement call: Windows creates this key with the first mapping and removes it with
        // the last, so a PC that never mapped a drive legitimately has none. False here would mark
        // every standalone machine red.
        [Fact]
        public void MappedDrives_TreatsAnAbsentKeyAsNormal()
        {
            Assert.True(AbsenceIsNormalProperty(new WMappedDrives()));
        }

        // The whole reason a user might trust this module too far: the drive definitions come back
        // but the saved passwords do not, because they are in Credential Manager rather than in the
        // key this exports. If that sentence goes missing, a restore looks complete and is not.
        [Fact]
        public void MappedDrives_DisclosesThatSavedCredentialsAreNotIncluded()
        {
            WMappedDrives m = new WMappedDrives();

            Assert.False(string.IsNullOrWhiteSpace(m.WarningMessage));
            Assert.Contains("credential", m.WarningMessage, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("password", m.WarningMessage, StringComparison.OrdinalIgnoreCase);
        }

        // --- WRegional ---

        [Fact]
        public void Regional_DeclaresInternationalThenKeyboardLayout()
        {
            WRegional m = new WRegional();
            IReadOnlyList<RestoreTarget> targets = m.RestoreTargets;

            Assert.Equal(2, targets.Count);
            Assert.All(targets, t => Assert.Equal(RestoreTargetKind.RegistryKey, t.Kind));

            Assert.Equal(@"HKEY_CURRENT_USER\Control Panel\International", targets[0].Path);
            Assert.Equal(@"HKEY_CURRENT_USER\Keyboard Layout", targets[1].Path);
        }

        // Both keys exist in every user profile - measured present on the development machine - so
        // an absent one is a damaged profile rather than an unused feature.
        [Fact]
        public void Regional_TreatsEitherKeysAbsenceAsAFault()
        {
            WRegional m = new WRegional();

            Assert.All(m.Keys, k => Assert.False(AbsenceIsNormalForKey(m, k)));
        }

        // Keys is a public mutable field the constructor fills, so RestoreTargets must read it at
        // access time. Mutating after construction is the only way to tell that apart from a
        // snapshot taken in the constructor, which would describe a different set of keys than the
        // one Restore imports - and the declaration is what the user consents against.
        [Fact]
        public void Regional_ReadsItsKeysAtAccessTime()
        {
            WRegional m = new WRegional();

            m.Keys.Add(@"HKEY_CURRENT_USER\Software\Appcopier\SyntheticRegionalKey");

            IReadOnlyList<RestoreTarget> targets = m.RestoreTargets;

            Assert.Equal(3, targets.Count);
            Assert.Equal(@"HKEY_CURRENT_USER\Software\Appcopier\SyntheticRegionalKey", targets[2].Path);
        }

        // Restoring writes language and layout IDENTIFIERS; it does not fetch the language packs
        // those identifiers name. Without this sentence a user reads a green row as "my language
        // is back" while the layout never appears.
        [Fact]
        public void Regional_DisclosesThatItDoesNotInstallLanguagePacks()
        {
            WRegional m = new WRegional();

            Assert.False(string.IsNullOrWhiteSpace(m.WarningMessage));
            Assert.Contains("language pack", m.WarningMessage, StringComparison.OrdinalIgnoreCase);
        }

        // --- WFonts ---

        // The folder before the key, in the order both BackupAsync and RestoreAsync apply them:
        // the font files have to be on disk before the registry values that point at them.
        [Fact]
        public void Fonts_DeclaresItsFolderThenItsKey()
        {
            WFonts m = new WFonts();
            IReadOnlyList<RestoreTarget> targets = m.RestoreTargets;

            Assert.Equal(2, targets.Count);
            Assert.Equal(RestoreTargetKind.Folder, targets[0].Kind);
            Assert.Equal(RestoreTargetKind.RegistryKey, targets[1].Kind);

            // Built from the same Data root the module composes its path from, so this pins the
            // suffix rather than re-encoding one machine's user name into the expectation.
            Assert.Equal(Data.LocalAppData + "\\Microsoft\\Windows\\Fonts", targets[0].Path);
            Assert.Equal(@"HKEY_CURRENT_USER\Software\Microsoft\Windows NT\CurrentVersion\Fonts",
                targets[1].Path);
        }

        // The two halves answer the absence question differently, and that asymmetry is measured
        // rather than assumed: on a clean account the folder was absent while the key was present
        // with zero values. Collapsing them to one answer breaks one case or the other.
        [Fact]
        public void Fonts_TreatsAnAbsentFolderAsNormalAndAnAbsentKeyAsAFault()
        {
            WFonts m = new WFonts();

            Assert.All(ListField(m, "Folders"), f => Assert.True(AbsenceIsNormalForFolder(m, f)));
            Assert.All(ListField(m, "Keys"), k => Assert.False(AbsenceIsNormalForKey(m, k)));
        }

        // The point of the module. The restored registry values are absolute paths carrying the
        // backing-up account's user name; under a different name the files land but the pointers
        // do not resolve, and nothing in the import detects it - the row reads Succeeded either
        // way. Info is asserted alongside WarningMessage because Info is the text a user actually
        // reads in the tree, and the two must not disagree about what this module can promise.
        [Fact]
        public void Fonts_DisclosesTheUserNamePathLimitationInBothTextsUsersRead()
        {
            WFonts m = new WFonts();

            Assert.False(string.IsNullOrWhiteSpace(m.WarningMessage));
            Assert.False(string.IsNullOrWhiteSpace(m.Info));

            Assert.Contains("user name", m.WarningMessage, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("user name", m.Info, StringComparison.OrdinalIgnoreCase);
        }

        // Pins the hybrid shape. Converting this to a RegistryModule would drop the font files and
        // to a FolderModule would drop the pointers, and either change would leave a module that
        // still backs something up and still reports success - so the shape is asserted rather
        // than left to review.
        /// <summary>
        /// "Is this installed" is answered by the font folder, never by the registry key.
        /// </summary>
        /// <remarks>
        /// Caught by the PR review. The key is measured PRESENT with zero values on an account that
        /// has never installed a per-user font, so consulting it would make IsInstalled a constant
        /// true on essentially every profile - and IsInstalled is what "select installed" uses to
        /// tell machines that have something here from machines that do not. Always answering yes
        /// makes the convenience meaningless and pads every such backup with an empty item.
        ///
        /// Pinned by construction rather than by observation, because the honest assertion depends
        /// on whether the machine running the test happens to have per-user fonts: point Folders at
        /// a directory that certainly does not exist, leave the real key in place, and the answer
        /// must still be false. It could only be true if the key were being consulted.
        /// </remarks>
        [Fact]
        public void Fonts_IsInstalled_IgnoresTheKeyThatEveryProfileHas()
        {
            WFonts m = new WFonts();

            // The real key stays in Keys and really does exist on this machine - so if IsInstalled
            // consulted it, the assertion below would fail.
            Assert.NotEmpty(m.Keys);

            m.Folders.Clear();
            m.Folders.Add(Path.Combine(Path.GetTempPath(), "appcopier-no-such-fonts-" + Guid.NewGuid().ToString("N")));

            Assert.False(m.IsInstalled(),
                "WFonts reported itself installed with no font folder present. The HKCU fonts key " +
                "exists with zero values on every profile, so consulting it makes IsInstalled a " +
                "constant true and 'select installed' stops meaning anything.");

            // And the other direction: a real folder is what makes the answer yes.
            string real = Path.Combine(Path.GetTempPath(), "appcopier-fonts-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(real);

            try
            {
                m.Folders.Clear();
                m.Folders.Add(real);

                Assert.True(m.IsInstalled());
            }
            finally
            {
                Directory.Delete(real, recursive: true);
            }
        }

        [Fact]
        public void Fonts_IsAHybridAndNotASingleShapeModule()
        {
            WFonts m = new WFonts();

            Assert.IsNotAssignableFrom<RegistryModule>(m);
            Assert.IsNotAssignableFrom<MultiKeyRegistryModule>(m);
            Assert.IsNotAssignableFrom<FolderModule>(m);
            Assert.IsNotAssignableFrom<FileModule>(m);

            Assert.Equal(typeof(BackupBase), m.GetType().BaseType);
        }

        // --- The three as a group ---

        private static BackupBase[] Modules()
            => new BackupBase[] { new WMappedDrives(), new WRegional(), new WFonts() };

        [Fact]
        public void EveryPowerUserModule_DeclaresWhatItOverwrites()
        {
            foreach (BackupBase m in Modules())
            {
                Assert.False(string.IsNullOrWhiteSpace(m.Title));
                Assert.False(string.IsNullOrWhiteSpace(m.Info));
                Assert.NotEmpty(m.RestoreTargets);
                Assert.DoesNotContain(m.RestoreTargets, t => t == null);
                Assert.DoesNotContain(m.RestoreTargets, t => t.IsUndeclared);

                // Every one of these writes on restore, so none may be exempt from the pre-restore
                // snapshot - false here would mean a restore that cannot be undone.
                Assert.True(m.RestoreMakesChanges);
            }
        }

        // None of these three overwrites a live application's files: the registry keys are read by
        // Windows at sign-in and the font folder is not held open by an app that rewrites it. So
        // none declares a close, and declaring one would cost the user an app they did not need to
        // lose.
        [Fact]
        public void NoPowerUserModule_ClosesAnything()
        {
            foreach (BackupBase m in Modules())
            {
                Assert.Empty(m.ProcessesToCloseBeforeRestore);

                // Closing nothing means keeping the base HasBackupIn default, so none of them can
                // be quietly skipped at the dispatch gate.
                Assert.True(m.HasBackupIn(null));
            }
        }

        // Titles become file and directory names inside the backup folder, so a character Windows
        // rejects in a path would make the module fail at backup time on every machine. "Regional
        // & input settings" is the one worth checking: an ampersand is legal in a filename, and
        // this is what says so rather than assuming it.
        [Fact]
        public void EveryPowerUserModuleTitle_IsUsableAsAFolderName()
        {
            char[] invalid = Path.GetInvalidFileNameChars();

            foreach (BackupBase m in Modules())
                Assert.DoesNotContain(m.Title, c => Array.IndexOf(invalid, c) >= 0);
        }
    }
}
