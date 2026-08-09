using WinRestoreKit;
using Conf;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace WinRestoreKit.Tests
{
    public class FileFolderStaleArtifactTests
    {
        private sealed class ClosingFileModule : FileModule
        {
            public ClosingFileModule(string file)
            {
                Title = "Files";
                Files.Add(file);
            }

            protected override bool AbsenceIsNormal(string file) => true;

            public override IReadOnlyList<RestoreCloseRequirement> ProcessesToCloseBeforeRestore
                => new[] { new RestoreCloseRequirement("someapp", "Some App", true) };
        }

        private sealed class ClosingFolderModule : FolderModule
        {
            public ClosingFolderModule(string folder) : base(folder)
            {
                Title = "Folder";
            }

            public override IReadOnlyList<RestoreCloseRequirement> ProcessesToCloseBeforeRestore
                => new[] { new RestoreCloseRequirement("someapp", "Some App", true) };
        }

        private static string NewTempDir()
        {
            string dir = Path.Combine(Path.GetTempPath(), "stale_artifact_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        [Fact]
        public async Task FileBackup_AfterSourceDisappears_RemovesThePreviousArtifact()
        {
            string source = NewTempDir();
            string backup = NewTempDir();
            string liveFile = Path.Combine(source, "config");
            ClosingFileModule module = new ClosingFileModule(liveFile);

            try
            {
                File.WriteAllText(liveFile, "first backup");
                Assert.Equal(ResultState.Succeeded, (await module.BackupAsync(backup)).State);

                File.Delete(liveFile);

                ModuleResult rerun = await module.BackupAsync(backup);

                Assert.Equal(ResultState.Skipped, rerun.State);
                Assert.False(File.Exists(Path.Combine(backup, module.Title, "config")));
                Assert.False(module.HasArtifactIn(backup));
                Assert.False(module.HasBackupIn(backup));
            }
            finally
            {
                Directory.Delete(source, recursive: true);
                Directory.Delete(backup, recursive: true);
            }
        }

        [Fact]
        public async Task FolderBackup_AfterSourceDisappears_RemovesThePreviousArtifactDirectory()
        {
            string sourceRoot = NewTempDir();
            string source = Path.Combine(sourceRoot, "source");
            string backup = NewTempDir();
            Directory.CreateDirectory(source);
            ClosingFolderModule module = new ClosingFolderModule(source);

            try
            {
                File.WriteAllText(Path.Combine(source, "config"), "first backup");
                Assert.Equal(ResultState.Succeeded, (await module.BackupAsync(backup)).State);

                Directory.Delete(source, recursive: true);

                ModuleResult rerun = await module.BackupAsync(backup);

                Assert.Equal(ResultState.Skipped, rerun.State);
                Assert.False(Directory.Exists(Path.Combine(backup, module.Title)));
                Assert.False(module.HasArtifactIn(backup));
                Assert.False(module.HasBackupIn(backup));
            }
            finally
            {
                Directory.Delete(sourceRoot, recursive: true);
                Directory.Delete(backup, recursive: true);
            }
        }
    }
}
