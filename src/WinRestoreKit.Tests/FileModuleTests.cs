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
    // The decision logic of the FileModule base, exercised through fake subclasses against real
    // files - the pattern FolderModuleTests and the RegistryModule rules already use. Per-module
    // behaviour is deliberately NOT retested here; a module that inherits the base inherits these
    // facts. The shipped modules' own declarations are pinned in DeveloperModuleTests.
    public class FileModuleTests
    {
        private sealed class PlainFileModule : FileModule
        {
            public PlainFileModule(params string[] files)
            {
                Title = "Plain";
                Files.AddRange(files);
            }

            protected override bool AbsenceIsNormal(string file) => true;
        }

        private sealed class MandatoryFileModule : FileModule
        {
            public MandatoryFileModule(params string[] files)
            {
                Title = "Mandatory";
                Files.AddRange(files);
            }

            protected override bool AbsenceIsNormal(string file) => false;
        }

        private sealed class ClosingFileModule : FileModule
        {
            public ClosingFileModule(params string[] files)
            {
                Title = "Closing";
                Files.AddRange(files);
            }

            protected override bool AbsenceIsNormal(string file) => true;

            public override IReadOnlyList<RestoreCloseRequirement> ProcessesToCloseBeforeRestore
                => new[] { new RestoreCloseRequirement("someapp", "Some App", true) };
        }

        // Two files with the SAME base name, disambiguated by an override - the ETerminal shape,
        // which is the collision the naming seam exists for.
        private sealed class CollidingFileModule : FileModule
        {
            private readonly string second;

            public CollidingFileModule(string first, string second)
            {
                Title = "Colliding";
                this.second = second;
                Files.Add(first);
                Files.Add(second);
            }

            protected override bool AbsenceIsNormal(string file) => true;

            protected override string BackupFileNameFor(string file)
                => string.Equals(file, second, StringComparison.OrdinalIgnoreCase)
                    ? "settings (second).json"
                    : base.BackupFileNameFor(file);
        }

        private static string NewTempDir()
        {
            string dir = Path.Combine(Path.GetTempPath(), "acfile_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static string MissingFile()
            => Path.Combine(Path.GetTempPath(), "acfile_missing_" + Guid.NewGuid().ToString("N"), "gone.json");

        private static string WriteFile(string dir, string name, string content)
        {
            string full = Path.Combine(dir, name);
            File.WriteAllText(full, content);
            return full;
        }

        [Fact]
        public void Declaration_IsOneFileTargetPerListedFile_InOrder()
        {
            PlainFileModule m = new PlainFileModule(@"C:\Users\me\.ssh\config", @"C:\Users\me\.ssh\known_hosts");

            Assert.Equal(2, m.RestoreTargets.Count);
            Assert.All(m.RestoreTargets, t => Assert.Equal(RestoreTargetKind.File, t.Kind));
            Assert.Equal(m.Files, m.RestoreTargets.Select(t => t.Path).ToList());
        }

        // Files is a public mutable field filled by subclass constructors, so the declaration has
        // to be computed on every access. A snapshot taken at construction would describe a
        // different set of files than the one RestoreAsync overwrites - the defect this mirrors on
        // the multi-key registry side.
        [Fact]
        public void Declaration_IsReadAtAccessTimeNotAtConstruction()
        {
            PlainFileModule m = new PlainFileModule(@"C:\Users\me\.ssh\config");

            Assert.Single(m.RestoreTargets);

            m.Files.Add(@"C:\Users\me\.ssh\known_hosts");

            Assert.Equal(2, m.RestoreTargets.Count);
            Assert.Equal(@"C:\Users\me\.ssh\known_hosts", m.RestoreTargets.Last().Path);
        }

        // Absent + normal is Skipped; absent + not normal is Failed. The same rule the registry and
        // folder paths enforce, held here for the single-file path.
        [Fact]
        public async Task Backup_MissingSource_IsSkippedWhenAbsenceIsNormal()
        {
            string backup = NewTempDir();

            try
            {
                ModuleResult r = await new PlainFileModule(MissingFile()).BackupAsync(backup);
                Assert.Equal(ResultState.Skipped, r.State);
            }
            finally
            {
                Directory.Delete(backup, recursive: true);
            }
        }

        [Fact]
        public async Task Backup_MissingSource_IsFailedWhenAbsenceIsNotNormal()
        {
            string backup = NewTempDir();

            try
            {
                ModuleResult r = await new MandatoryFileModule(MissingFile()).BackupAsync(backup);

                Assert.Equal(ResultState.Failed, r.State);

                // The noun matters: this is a file, and telling the user a FOLDER is missing sends
                // them to look at the wrong thing on disk.
                Assert.Contains("expected file", r.Steps.Single().Reason);
            }
            finally
            {
                Directory.Delete(backup, recursive: true);
            }
        }

        // A module can list several files with different absence rules; each step decides on its
        // own file rather than on a module-wide flag.
        [Fact]
        public async Task Backup_MixedPresenceAndAbsence_AggregatesPerFile()
        {
            string source = NewTempDir();
            string backup = NewTempDir();

            try
            {
                string present = WriteFile(source, "config", "Host example");

                ModuleResult r = await new PlainFileModule(present, MissingFile()).BackupAsync(backup);

                Assert.Equal(2, r.Steps.Count);
                Assert.Equal(ResultState.Succeeded, r.Steps[0].State);
                Assert.Equal(ResultState.Skipped, r.Steps[1].State);
                Assert.Equal(ResultState.Succeeded, r.State);
            }
            finally
            {
                Directory.Delete(source, recursive: true);
                Directory.Delete(backup, recursive: true);
            }
        }

        // On restore the absent thing is the BACKUP, and the wording must say so rather than
        // claiming the item is "not present on this system" - a statement about a machine that was
        // never examined.
        [Fact]
        public async Task Restore_WithNothingBackedUp_SaysSoInTheBackupFoldersTerms()
        {
            string backup = NewTempDir();
            string live = NewTempDir();

            try
            {
                ModuleResult r = await new PlainFileModule(Path.Combine(live, "config")).RestoreAsync(backup);

                Assert.Equal(ResultState.Skipped, r.State);
                Assert.Equal("nothing was backed up for this item", r.Steps.Single().Reason);
            }
            finally
            {
                Directory.Delete(backup, recursive: true);
                Directory.Delete(live, recursive: true);
            }
        }

        // The AbsenceIsNormal flag describes the LIVE machine and must not leak into the restore
        // direction. EHosts is the real module this protects: its flag is false, and without this
        // rule a backup taken before the module existed would fail its restore with
        // "expected file ... is missing" - backup-side wording about the wrong machine.
        [Fact]
        public async Task Restore_WithNothingBackedUp_IsSkippedEvenWhenAbsenceIsNotNormal()
        {
            string backup = NewTempDir();
            string live = NewTempDir();

            try
            {
                ModuleResult r = await new MandatoryFileModule(Path.Combine(live, "hosts")).RestoreAsync(backup);

                Assert.Equal(ResultState.Skipped, r.State);
                Assert.Equal("nothing was backed up for this item", r.Steps.Single().Reason);
            }
            finally
            {
                Directory.Delete(backup, recursive: true);
                Directory.Delete(live, recursive: true);
            }
        }

        [Fact]
        public async Task BackupThenRestore_RoundTripsTheFileContents()
        {
            string source = NewTempDir();
            string backup = NewTempDir();
            string restored = NewTempDir();

            try
            {
                string original = WriteFile(source, "config", "Host example\n  User me");

                ModuleResult backedUp = await new PlainFileModule(original).BackupAsync(backup);
                Assert.Equal(ResultState.Succeeded, backedUp.State);

                string target = Path.Combine(restored, "config");
                ModuleResult restoredResult = await new PlainFileModule(target).RestoreAsync(backup);

                Assert.Equal(ResultState.Succeeded, restoredResult.State);
                Assert.Equal("Host example\n  User me", File.ReadAllText(target));
            }
            finally
            {
                Directory.Delete(source, recursive: true);
                Directory.Delete(backup, recursive: true);
                Directory.Delete(restored, recursive: true);
            }
        }

        // Restoring onto a machine that has never run the tool being restored: %USERPROFILE%\.ssh
        // does not exist, and a restore that failed there would be reporting "could not copy" for
        // a restore that is simply the first one.
        [Fact]
        public async Task Restore_CreatesTheDestinationDirectoryWhenItIsMissing()
        {
            string source = NewTempDir();
            string backup = NewTempDir();
            string restored = NewTempDir();

            try
            {
                string original = WriteFile(source, "config", "Host example");
                await new PlainFileModule(original).BackupAsync(backup);

                // Two levels that do not exist yet.
                string target = Path.Combine(restored, ".ssh", "nested", "config");

                ModuleResult r = await new PlainFileModule(target).RestoreAsync(backup);

                Assert.Equal(ResultState.Succeeded, r.State);
                Assert.True(File.Exists(target));
            }
            finally
            {
                Directory.Delete(source, recursive: true);
                Directory.Delete(backup, recursive: true);
                Directory.Delete(restored, recursive: true);
            }
        }

        // The artifacts go in {Title}\ rather than loose at the backup root, which is what lets
        // HasBackupIn probe one directory and what keeps base names like "config" from colliding
        // between two modules that both back one up.
        [Fact]
        public async Task Backup_WritesUnderTheModulesOwnSubfolder()
        {
            string source = NewTempDir();
            string backup = NewTempDir();

            try
            {
                string original = WriteFile(source, "config", "Host example");
                PlainFileModule m = new PlainFileModule(original);

                await m.BackupAsync(backup);

                Assert.True(File.Exists(Path.Combine(backup, m.Title, "config")));
            }
            finally
            {
                Directory.Delete(source, recursive: true);
                Directory.Delete(backup, recursive: true);
            }
        }

        // The name comes from the file's BASE NAME, never the full path: paths are composed from
        // Data.* roots and carry the backing-up account's user name, which would stop resolving on
        // restore under any other account.
        [Fact]
        public async Task BackupFileName_DefaultsToTheBaseNameNotThePath()
        {
            string source = NewTempDir();
            string backup = NewTempDir();

            try
            {
                string original = WriteFile(source, "known_hosts", "example.com ssh-ed25519 AAAA");
                PlainFileModule m = new PlainFileModule(original);

                await m.BackupAsync(backup);

                string[] written = Directory.GetFiles(Path.Combine(backup, m.Title));

                Assert.Equal(new[] { "known_hosts" }, written.Select(Path.GetFileName).ToArray());
            }
            finally
            {
                Directory.Delete(source, recursive: true);
                Directory.Delete(backup, recursive: true);
            }
        }

        // A loop over N files must produce N distinct artifacts. Without the override the second
        // copy would overwrite the first while BOTH steps reported success, and the restore would
        // then write one file to both destinations - the FileModule form of the WThemes defect.
        //
        // Observed through the filenames BackupAsync actually writes and RestoreAsync actually
        // reads, not by calling the seam: a broken call site would still pass a seam-only test.
        [Fact]
        public async Task CollidingBaseNames_ProduceDistinctArtifactsAndRoundTripSeparately()
        {
            string source = NewTempDir();
            string backup = NewTempDir();
            string restored = NewTempDir();

            try
            {
                string first = WriteFile(source, "settings.json", "{\"first\":true}");

                string secondDir = Path.Combine(source, "preview");
                Directory.CreateDirectory(secondDir);
                string second = WriteFile(secondDir, "settings.json", "{\"second\":true}");

                CollidingFileModule writer = new CollidingFileModule(first, second);
                ModuleResult backedUp = await writer.BackupAsync(backup);
                Assert.Equal(ResultState.Succeeded, backedUp.State);

                // Two files, not one overwritten twice.
                Assert.Equal(2, Directory.GetFiles(Path.Combine(backup, writer.Title)).Length);

                string restoredFirst = Path.Combine(restored, "settings.json");
                string restoredSecondDir = Path.Combine(restored, "preview");
                Directory.CreateDirectory(restoredSecondDir);
                string restoredSecond = Path.Combine(restoredSecondDir, "settings.json");

                CollidingFileModule reader = new CollidingFileModule(restoredFirst, restoredSecond);
                ModuleResult restoredResult = await reader.RestoreAsync(backup);

                Assert.Equal(ResultState.Succeeded, restoredResult.State);
                Assert.Equal("{\"first\":true}", File.ReadAllText(restoredFirst));
                Assert.Equal("{\"second\":true}", File.ReadAllText(restoredSecond));
            }
            finally
            {
                Directory.Delete(source, recursive: true);
                Directory.Delete(backup, recursive: true);
                Directory.Delete(restored, recursive: true);
            }
        }

        // The real HasBackupIn check is earned only by modules that close a process; a module that
        // closes nothing keeps the base default of true, so it can never be quietly skipped.
        [Fact]
        public void HasBackupIn_ModuleThatClosesNothing_AlwaysAssumesSomething()
        {
            PlainFileModule m = new PlainFileModule(MissingFile());

            Assert.True(m.HasBackupIn(MissingFile()));
            Assert.True(m.HasBackupIn(null));
        }

        // It asks for an ARTIFACT, not for the directory. Utils.CopyFile creates the destination
        // directory before it knows the copy will succeed, so a backup where every file failed
        // leaves an empty {Title}\ behind - and a directory-level probe would let that empty
        // folder buy a process kill for a restore with nothing to copy.
        [Fact]
        public void HasBackupIn_ModuleThatClosesSomething_RequiresAnActualArtifact()
        {
            string root = NewTempDir();
            string source = NewTempDir();

            try
            {
                string live = Path.Combine(source, "config");
                ClosingFileModule m = new ClosingFileModule(live);

                Assert.False(m.HasBackupIn(null));
                Assert.False(m.HasBackupIn(root));

                // The directory alone is NOT enough.
                Directory.CreateDirectory(Path.Combine(root, m.Title));
                Assert.False(m.HasBackupIn(root));

                // The artifact under the name the restore will look for IS.
                File.WriteAllText(Path.Combine(root, m.Title, "config"), "Host example");
                Assert.True(m.HasBackupIn(root));
            }
            finally
            {
                Directory.Delete(root, recursive: true);
                Directory.Delete(source, recursive: true);
            }
        }

        // A stray "<name>.WinRestoreKit-tmp" in the backup folder must not read as a captured artifact -
        // which would earn a process kill and then restore nothing.
        //
        // Written when Utils.CopyFile staged through exactly such a sibling and renamed it into
        // place, so a crash between those steps left one behind. That staging is GONE as of the
        // in-place write, so this is now a REGRESSION GUARD rather than a live hazard: it holds the
        // property that both readers compose the exact artifact name they want, and so would not be
        // fooled if any future scheme reintroduced a scratch file. The behaviour it guards is worth
        // keeping; the hazard that motivated it no longer exists.
        [Fact]
        public void HasBackupIn_IsNotFooledByALeftoverTemporaryFile()
        {
            string root = NewTempDir();
            string source = NewTempDir();

            try
            {
                ClosingFileModule m = new ClosingFileModule(Path.Combine(source, "config"));

                Directory.CreateDirectory(Path.Combine(root, m.Title));
                File.WriteAllText(Path.Combine(root, m.Title, "config.WinRestoreKit-tmp"), "half a file");

                Assert.False(m.HasBackupIn(root));
            }
            finally
            {
                Directory.Delete(root, recursive: true);
                Directory.Delete(source, recursive: true);
            }
        }

        // Same fact on the restore side: a half-written temp file must not be restored over the
        // user's live file under the real name.
        [Fact]
        public async Task Restore_IgnoresALeftoverTemporaryFile()
        {
            string backup = NewTempDir();
            string live = NewTempDir();

            try
            {
                string target = Path.Combine(live, "config");
                PlainFileModule m = new PlainFileModule(target);

                Directory.CreateDirectory(Path.Combine(backup, m.Title));
                File.WriteAllText(Path.Combine(backup, m.Title, "config.WinRestoreKit-tmp"), "half a file");

                ModuleResult r = await m.RestoreAsync(backup);

                Assert.Equal(ResultState.Skipped, r.State);
                Assert.Equal("nothing was backed up for this item", r.Steps.Single().Reason);
                Assert.False(File.Exists(target));
            }
            finally
            {
                Directory.Delete(backup, recursive: true);
                Directory.Delete(live, recursive: true);
            }
        }

        // The backup holds an artifact, but not one this module's file list asks for.
        //
        // A base-class rule, deliberately not a claim about ETerminal: ETerminal populates Files
        // unconditionally, so it always asks about all three installs and a Preview-only backup
        // does answer true for it - correctly, since its restore writes that preview file. This
        // covers a module whose Files differ between the backing-up and restoring machines, which
        // is what the artifact probe has to get right in general.
        [Fact]
        public void HasBackupIn_IsFalseWhenTheBackupHoldsSomeoneElsesArtifact()
        {
            string root = NewTempDir();
            string source = NewTempDir();

            try
            {
                ClosingFileModule m = new ClosingFileModule(Path.Combine(source, "settings.json"));

                Directory.CreateDirectory(Path.Combine(root, m.Title));
                File.WriteAllText(Path.Combine(root, m.Title, "settings (preview).json"), "{}");

                Assert.False(m.HasBackupIn(root));
            }
            finally
            {
                Directory.Delete(root, recursive: true);
                Directory.Delete(source, recursive: true);
            }
        }

        [Fact]
        public void IsInstalled_IsTrueWhenAnyListedFileExists()
        {
            string source = NewTempDir();

            try
            {
                string present = WriteFile(source, "config", "Host example");

                Assert.True(new PlainFileModule(MissingFile(), present).IsInstalled());
                Assert.False(new PlainFileModule(MissingFile(), MissingFile()).IsInstalled());
            }
            finally
            {
                Directory.Delete(source, recursive: true);
            }
        }
    }
}
