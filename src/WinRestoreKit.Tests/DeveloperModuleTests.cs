using WinRestoreKit;
using Conf;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Xunit;

namespace WinRestoreKit.Tests
{
    // The five Phase 3b Developer modules, pinned at the level of what they DECLARE rather than
    // what they do: the base-class behaviour is held by FileModuleTests, and nothing here touches
    // the real hosts file, the real registry, or anything needing elevation.
    public class DeveloperModuleTests
    {
        private static List<string> FilesOf(BackupBase module)
            => (List<string>)module.GetType().GetField("Files").GetValue(module);

        // AbsenceIsNormal is protected, so this reads it the only way a test can. Walking the
        // hierarchy rather than a single GetMethod call, for the reason RestoreDeclarationTests
        // walks it for Key: a subclass that inherited the member instead of overriding it would
        // otherwise be silently unreachable here.
        private static bool AbsenceIsNormalFor(BackupBase module, string file)
        {
            for (Type t = module.GetType(); t != null; t = t.BaseType)
            {
                MethodInfo m = t.GetMethod("AbsenceIsNormal",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                    null, new[] { typeof(string) }, null);

                if (m != null)
                    return (bool)m.Invoke(module, new object[] { file });
            }

            throw new InvalidOperationException(
                module.GetType().Name + " does not declare AbsenceIsNormal(string).");
        }

        private static string BackupFileNameFor(BackupBase module, string file)
        {
            for (Type t = module.GetType(); t != null; t = t.BaseType)
            {
                MethodInfo m = t.GetMethod("BackupFileNameFor",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                    null, new[] { typeof(string) }, null);

                if (m != null)
                    return (string)m.Invoke(module, new object[] { file });
            }

            throw new InvalidOperationException(
                module.GetType().Name + " does not declare BackupFileNameFor(string).");
        }

        // --- ESsh: the private-key exclusion ---

        // PRIVATE KEYS ARE DELIBERATELY EXCLUDED (user decision, 2026-07-21). WinRestoreKit writes
        // backups as ordinary unencrypted files beside the executable, which is the wrong home for
        // key material: a copy of id_rsa there is a credential in plaintext, surviving in every
        // backup folder the user forgets to delete, with the passphrase protection on the original
        // bypassed.
        //
        // This is the test that stands between that decision and a future well-meaning "the backup
        // is incomplete without the keys" change. If you are here because this failed: the
        // exclusion was a choice, not an oversight. Take it to the user before widening it.
        [Fact]
        public void Ssh_BacksUpConfigAndKnownHostsAndNothingElse()
        {
            ESsh m = new ESsh();

            string[] names = FilesOf(m).Select(Path.GetFileName).ToArray();

            Assert.Equal(new[] { "config", "known_hosts" }, names);
        }

        // Same fact from the other side: whatever the paths resolve to on this machine, none of
        // them may be a key file. Asserted separately from the list above because a future edit
        // that appends to Files would have to defeat both.
        [Fact]
        public void Ssh_DeclaresNoPrivateKeyAndNoWholeDirectory()
        {
            ESsh m = new ESsh();

            Assert.All(m.RestoreTargets, t => Assert.Equal(RestoreTargetKind.File, t.Kind));

            string[] forbidden = { "id_rsa", "id_ed25519", "id_ecdsa", "id_dsa", ".pem", ".ppk" };

            foreach (RestoreTarget t in m.RestoreTargets)
            {
                string name = Path.GetFileName(t.Path);

                Assert.DoesNotContain(forbidden, f => name.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0);
            }
        }

        [Fact]
        public void Ssh_TreatsAbsenceAsNormalAndClosesNothing()
        {
            ESsh m = new ESsh();

            Assert.All(FilesOf(m), f => Assert.True(AbsenceIsNormalFor(m, f)));
            Assert.Empty(m.ProcessesToCloseBeforeRestore);

            // Closing nothing means keeping the base default, so it can never be quietly skipped.
            Assert.True(m.HasBackupIn(null));
        }

        // --- ETerminal ---

        // Three installs, three files that are ALL called settings.json - so this module is the
        // reason the file-side naming seam exists.
        //
        // Read this as a SEAM test and nothing more: it calls BackupFileNameFor directly, so it
        // cannot by itself prove the backup writes those names or that the restore reads them
        // back. What closes that gap is not this assertion but two other facts, asserted rather
        // than assumed below: FileModule's async pair is a SEALED override, so this module cannot
        // have a divergent call site, and FileModuleTests round-trips the colliding-name case
        // through the real pair. Repointing Files at temp paths would NOT buy a behavioural test
        // here, because the override matches on the real install paths and synthetic files fall
        // through to the base name and collide by design.
        [Fact]
        public void Terminal_CoversThreeInstallsWithDistinctBackupNames()
        {
            ETerminal m = new ETerminal();
            List<string> files = FilesOf(m);

            Assert.Equal(3, files.Count);
            Assert.All(files, f => Assert.Equal("settings.json", Path.GetFileName(f)));

            string[] backupNames = files.Select(f => BackupFileNameFor(m, f)).ToArray();

            Assert.Equal(backupNames.Length, backupNames.Distinct(StringComparer.OrdinalIgnoreCase).Count());

            // The dependency that makes the seam test sufficient. A future conversion of this
            // module to a hand-rolled BackupBase - the EVSCode shape - removes the sealing that
            // guarantees the call sites, and fails here rather than silently.
            Assert.IsAssignableFrom<FileModule>(m);
        }

        [Fact]
        public void Terminal_DeclaresAConsentedCloseOfWindowsTerminal()
        {
            RestoreCloseRequirement req = Assert.Single(new ETerminal().ProcessesToCloseBeforeRestore);

            Assert.Equal("WindowsTerminal", req.ProcessName);
            Assert.True(req.NeedsConsent);
        }

        [Fact]
        public void Terminal_TreatsEveryFilesAbsenceAsNormal()
        {
            ETerminal m = new ETerminal();

            Assert.All(FilesOf(m), f => Assert.True(AbsenceIsNormalFor(m, f)));
        }

        // --- EVSCode ---

        // Two files then the snippets folder, in the order RestoreAsync applies them. The mixed
        // kinds are why this module is hand-rolled rather than a FileModule.
        [Fact]
        public void VSCode_DeclaresItsFilesThenItsSnippetsFolder()
        {
            EVSCode m = new EVSCode();
            IReadOnlyList<RestoreTarget> targets = m.RestoreTargets;

            Assert.Equal(3, targets.Count);
            Assert.Equal(RestoreTargetKind.File, targets[0].Kind);
            Assert.Equal(RestoreTargetKind.File, targets[1].Kind);
            Assert.Equal(RestoreTargetKind.Folder, targets[2].Kind);

            Assert.Equal(new[] { "settings.json", "keybindings.json" },
                targets.Take(2).Select(t => Path.GetFileName(t.Path)).ToArray());

            Assert.Equal(m.SnippetsFolder, targets[2].Path);
        }

        [Fact]
        public void VSCode_DeclaresAConsentedCloseOfCode()
        {
            RestoreCloseRequirement req = Assert.Single(new EVSCode().ProcessesToCloseBeforeRestore);

            Assert.Equal("Code", req.ProcessName);
            Assert.True(req.NeedsConsent);
        }

        // It closes VS Code, so it has to answer honestly whether the chosen backup holds anything
        // for it - the check that stops the user losing unsaved editor buffers to a restore that
        // then copies nothing. The directory is deliberately not enough; see VSCodeModuleTests for
        // the empty-snippets-folder case that makes an empty directory easy to produce.
        [Fact]
        public void VSCode_RequiresAnActualArtifactBeforeEarningItsClose()
        {
            EVSCode m = new EVSCode();

            string root = Path.Combine(Path.GetTempPath(), "acvscode_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);

            try
            {
                Assert.False(m.HasBackupIn(null));
                Assert.False(m.HasBackupIn(root));

                Directory.CreateDirectory(Path.Combine(root, m.Title));
                Assert.False(m.HasBackupIn(root));

                Directory.CreateDirectory(Path.Combine(root, m.Title, "snippets"));
                Assert.False(m.HasBackupIn(root));

                File.WriteAllText(Path.Combine(root, m.Title, "settings.json"), "{}");
                Assert.True(m.HasBackupIn(root));
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        // --- EHosts ---

        [Fact]
        public void Hosts_DeclaresTheSystemHostsFileAndTreatsAbsenceAsAFault()
        {
            EHosts m = new EHosts();

            string file = Assert.Single(FilesOf(m));

            Assert.Equal("hosts", Path.GetFileName(file));
            Assert.EndsWith(@"System32\drivers\etc\hosts", file, StringComparison.OrdinalIgnoreCase);

            // The judgement call: every Windows install ships this file, so absent is a fault
            // rather than a machine that simply never used the feature.
            Assert.False(AbsenceIsNormalFor(m, file));

            RestoreTarget only = Assert.Single(m.RestoreTargets);
            Assert.Equal(RestoreTargetKind.File, only.Kind);
        }

        // It writes outside the current user's profile, which nothing else in this category does.
        // A machine-wide write with no warning is the case the confirmation dialog cannot make up
        // for, because the warning is what the dialog shows.
        [Fact]
        public void Hosts_CarriesAWarningAboutBeingMachineWide()
        {
            EHosts m = new EHosts();

            Assert.False(string.IsNullOrWhiteSpace(m.WarningMessage));
            Assert.Contains("every user", m.WarningMessage, StringComparison.OrdinalIgnoreCase);
        }

        // --- EEnvironment ---

        [Fact]
        public void Environment_DeclaresTheUsersEnvironmentKey()
        {
            EEnvironment m = new EEnvironment();

            RestoreTarget only = Assert.Single(m.RestoreTargets);

            Assert.Equal(RestoreTargetKind.RegistryKey, only.Kind);
            Assert.Equal(@"HKEY_CURRENT_USER\Environment", only.Path);
        }

        // Restoring PATH from a backup can remove entries added since, and that is disclosed
        // rather than engineered around - so the disclosure has to actually be there.
        [Fact]
        public void Environment_DisclosesThatRestoringOverwritesPath()
        {
            EEnvironment m = new EEnvironment();

            Assert.False(string.IsNullOrWhiteSpace(m.WarningMessage));
            Assert.Contains("PATH", m.WarningMessage, StringComparison.OrdinalIgnoreCase);
        }

        // --- The category as a whole ---

        // Every Developer module must be able to say what its restore overwrites, and none may
        // reach the undeclared marker. RestoreDeclarationTests sweeps this over the whole app;
        // repeated here so a failure names the phase that introduced it.
        [Fact]
        public void EveryDeveloperModule_DeclaresWhatItOverwrites()
        {
            BackupBase[] modules = { new ETerminal(), new EVSCode(), new ESsh(), new EEnvironment(), new EHosts() };

            foreach (BackupBase m in modules)
            {
                Assert.False(string.IsNullOrWhiteSpace(m.Title));
                Assert.False(string.IsNullOrWhiteSpace(m.Info));
                Assert.NotEmpty(m.RestoreTargets);
                Assert.DoesNotContain(m.RestoreTargets, t => t == null);
                Assert.DoesNotContain(m.RestoreTargets, t => t.IsUndeclared);
                Assert.True(m.RestoreMakesChanges);
            }
        }

        // The file-side counterpart of BackupFileNamingTests' registry sweep, which discovers
        // multi-key modules by their Keys field and pins one file per key. Nothing did the same
        // for FileModule, so a future module with two same-named files and no BackupFileNameFor
        // override would ship silently - the second copy overwriting the first while both steps
        // report success, which is precisely the WThemes defect the seam exists to prevent.
        //
        // Reflection over the shipped assembly rather than a hand-written list, for the reason
        // RestoreDeclarationTests gives: a hand-written list is what a forgetful author also
        // forgets to extend.
        [Fact]
        public void EveryFileModule_WritesADistinctNamePerFile()
        {
            List<FileModule> modules = typeof(BackupBase).Assembly
                .GetTypes()
                .Where(t => typeof(FileModule).IsAssignableFrom(t) && !t.IsAbstract)
                .Select(t => (FileModule)Activator.CreateInstance(t))
                .ToList();

            // If this trips, the sweep stopped finding anything and is no longer protecting.
            Assert.NotEmpty(modules);

            foreach (FileModule m in modules)
            {
                List<string> files = FilesOf(m);
                string[] names = files.Select(f => BackupFileNameFor(m, f)).ToArray();

                Assert.All(names, n => Assert.False(string.IsNullOrWhiteSpace(n)));

                Assert.True(
                    names.Distinct(StringComparer.OrdinalIgnoreCase).Count() == names.Length,
                    m.GetType().Name + " writes two files under one name: "
                        + string.Join(", ", names));
            }
        }

        // Titles become directory names inside the backup folder, so a character Windows rejects
        // in a path would make the module fail at backup time on every machine.
        [Fact]
        public void EveryDeveloperModuleTitle_IsUsableAsAFolderName()
        {
            BackupBase[] modules = { new ETerminal(), new EVSCode(), new ESsh(), new EEnvironment(), new EHosts() };

            char[] invalid = Path.GetInvalidFileNameChars();

            foreach (BackupBase m in modules)
                Assert.DoesNotContain(m.Title, c => Array.IndexOf(invalid, c) >= 0);
        }
    }
}
