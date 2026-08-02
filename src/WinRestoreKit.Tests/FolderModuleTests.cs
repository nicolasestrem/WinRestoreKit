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
    // The decision logic of the FolderModule base, exercised through fake subclasses against real
    // directories - the same pattern the RegistryModule rules are held by. Per-module behaviour is
    // deliberately NOT retested here; a module that inherits the base inherits these facts.
    public class FolderModuleTests
    {
        private sealed class PlainFolderModule : FolderModule
        {
            public PlainFolderModule(string folder) : base(folder)
            {
                Title = "Plain";
            }
        }

        private sealed class MandatoryFolderModule : FolderModule
        {
            public MandatoryFolderModule(string folder) : base(folder)
            {
                Title = "Mandatory";
            }

            protected override bool AbsenceIsNormal => false;
        }

        private sealed class ClosingFolderModule : FolderModule
        {
            public ClosingFolderModule(string folder) : base(folder)
            {
                Title = "Closing";
            }

            public override IReadOnlyList<RestoreCloseRequirement> ProcessesToCloseBeforeRestore
                => new[] { new RestoreCloseRequirement("someapp", "Some App", true) };
        }

        private static string NewTempDir()
        {
            string dir = Path.Combine(Path.GetTempPath(), "acfolder_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static string MissingDir()
            => Path.Combine(Path.GetTempPath(), "acfolder_missing_" + Guid.NewGuid().ToString("N"));

        [Fact]
        public void Declaration_IsTheFolderTheRestoreWrites()
        {
            PlainFolderModule m = new PlainFolderModule(@"C:\Users\me\AppData\Local\Something");

            RestoreTarget only = Assert.Single(m.RestoreTargets);

            Assert.Equal(RestoreTargetKind.Folder, only.Kind);
            Assert.Equal(m.Folder, only.Path);
        }

        // Absent + normal is Skipped; absent + not normal is Failed. The same rule the registry
        // path enforces, held here for the folder path.
        [Fact]
        public async Task Backup_MissingSource_IsSkippedWhenAbsenceIsNormal()
        {
            string backup = NewTempDir();

            try
            {
                ModuleResult r = await new PlainFolderModule(MissingDir()).BackupAsync(backup);
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
                ModuleResult r = await new MandatoryFolderModule(MissingDir()).BackupAsync(backup);
                Assert.Equal(ResultState.Failed, r.State);
            }
            finally
            {
                Directory.Delete(backup, recursive: true);
            }
        }

        // On restore the absent thing is the BACKUP, and the wording must say so rather than
        // claiming the item is "not present on this system" - a statement about a machine that
        // was never examined.
        [Fact]
        public async Task Restore_WithNothingBackedUp_SaysSoInTheBackupFoldersTerms()
        {
            string backup = NewTempDir();
            string live = NewTempDir();

            try
            {
                ModuleResult r = await new PlainFolderModule(live).RestoreAsync(backup);

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
        // direction: a backup that predates the module legitimately holds nothing for it, and
        // failing that with "expected folder is missing" would be backup-side wording about the
        // wrong machine.
        [Fact]
        public async Task Restore_WithNothingBackedUp_IsSkippedEvenWhenAbsenceIsNotNormal()
        {
            string backup = NewTempDir();
            string live = NewTempDir();

            try
            {
                ModuleResult r = await new MandatoryFolderModule(live).RestoreAsync(backup);

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
        public async Task BackupThenRestore_RoundTripsTheFolder()
        {
            string source = NewTempDir();
            string backup = NewTempDir();
            string restored = NewTempDir();

            try
            {
                File.WriteAllText(Path.Combine(source, "a.txt"), "alpha");

                PlainFolderModule writer = new PlainFolderModule(source);
                ModuleResult backedUp = await writer.BackupAsync(backup);
                Assert.Equal(ResultState.Succeeded, backedUp.State);

                PlainFolderModule reader = new PlainFolderModule(restored);
                ModuleResult restoredResult = await reader.RestoreAsync(backup);

                Assert.Equal(ResultState.Succeeded, restoredResult.State);
                Assert.Equal("alpha", File.ReadAllText(Path.Combine(restored, "a.txt")));
            }
            finally
            {
                Directory.Delete(source, recursive: true);
                Directory.Delete(backup, recursive: true);
                Directory.Delete(restored, recursive: true);
            }
        }

        // The real HasBackupIn check is earned only by modules that close a process; a module
        // that closes nothing keeps the base default of true, so it can never be quietly skipped
        // (the invariant RestoreDeclarationTests sweeps over every shipped module).
        [Fact]
        public void HasBackupIn_ModuleThatClosesNothing_AlwaysAssumesSomething()
        {
            PlainFolderModule m = new PlainFolderModule(MissingDir());

            Assert.True(m.HasBackupIn(MissingDir()));
            Assert.True(m.HasBackupIn(null));
        }

        [Fact]
        public void HasBackupIn_ModuleThatClosesSomething_ChecksTheBackupFolder()
        {
            ClosingFolderModule m = new ClosingFolderModule(MissingDir());

            string root = NewTempDir();

            try
            {
                Assert.False(m.HasBackupIn(null));
                Assert.False(m.HasBackupIn(root));

                Directory.CreateDirectory(Path.Combine(root, m.Title));
                Assert.True(m.HasBackupIn(root));
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
