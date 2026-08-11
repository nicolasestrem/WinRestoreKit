using Conf;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using WinRestoreKit;
using Xunit;

namespace WinRestoreKit.Tests
{
    public sealed class BackupOutputPathTests
    {
        [Fact]
        public async Task RunBackup_DirectPath_RetainsTheExactCallerSuppliedFolderBeforeValidation()
        {
            string root = Path.Combine(Path.GetTempPath(), "WinRestoreKitTests", Guid.NewGuid().ToString("N"));
            string suppliedPath = Path.Combine(root, "snapshot-started-before-rollover");

            try
            {
                var runner = new BackupRestoreOrchestrator(new TestRunUi());

                await runner.RunBackup(new BackupBase[] { new EmptyModule() }, suppliedPath);

                Assert.Equal(suppliedPath, runner.BackupOutputPath);
            }
            finally
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, true);
            }
        }

        [Fact]
        public async Task RunBackup_DestinationOverload_RetainsComputedPathWhenContainmentRejectsTheRun()
        {
            string source = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "WinRestoreKitTests",
                Guid.NewGuid().ToString("N"))).FullName;
            string destination = Path.Combine(source, "backups");
            var ui = new TestRunUi();

            try
            {
                var runner = new BackupRestoreOrchestrator(ui);

                await runner.RunBackup(new BackupBase[] { new FolderSourceModule(source) }, destination,
                    "contained", SnapshotCompression.None);

                Assert.Equal(Path.GetFullPath(destination),
                    Path.GetDirectoryName(runner.BackupOutputPath), StringComparer.OrdinalIgnoreCase);
                Assert.Matches(@"^\d{4}-\d{2}-\d{2} - \d{2}\.\d{2}\.\d{2}$",
                    Path.GetFileName(runner.BackupOutputPath));
                Assert.Equal(RunState.DidNotRun, ui.Summary.State);
            }
            finally
            {
                if (Directory.Exists(source))
                    Directory.Delete(source, true);
            }
        }

        [Fact]
        public async Task RunBackup_TwoUserBackupsNeverReuseTheFirstRestorePoint()
        {
            using (BackupRunIsolation isolation = new BackupRunIsolation())
            {
                var runner = new BackupRestoreOrchestrator(new TestRunUi());

                await runner.RunBackup(new BackupBase[] { new EmptyModule() }, isolation.DestinationRoot,
                    "first", SnapshotCompression.None);
                string first = runner.BackupOutputPath;

                await runner.RunBackup(new BackupBase[] { new EmptyModule() }, isolation.DestinationRoot,
                    "second", SnapshotCompression.None);
                string second = runner.BackupOutputPath;

                Assert.NotEqual(first, second, StringComparer.OrdinalIgnoreCase);
                Assert.NotNull(BackupManifest.TryParse(
                    File.ReadAllText(Path.Combine(first, BackupManifest.FileName))));
                Assert.NotNull(BackupManifest.TryParse(
                    File.ReadAllText(Path.Combine(second, BackupManifest.FileName))));
            }
        }

        [Fact]
        public async Task RunBackup_ConcurrentUserBackupsClaimDifferentFolders()
        {
            using (BackupRunIsolation isolation = new BackupRunIsolation())
            {
                var firstRunner = new BackupRestoreOrchestrator(new TestRunUi());
                var secondRunner = new BackupRestoreOrchestrator(new TestRunUi());

                await Task.WhenAll(
                    firstRunner.RunBackup(new BackupBase[] { new EmptyModule() }, isolation.DestinationRoot,
                        "first", SnapshotCompression.None),
                    secondRunner.RunBackup(new BackupBase[] { new EmptyModule() }, isolation.DestinationRoot,
                        "second", SnapshotCompression.None));

                Assert.NotEqual(firstRunner.BackupOutputPath, secondRunner.BackupOutputPath,
                    StringComparer.OrdinalIgnoreCase);
                Assert.True(Directory.Exists(firstRunner.BackupOutputPath));
                Assert.True(Directory.Exists(secondRunner.BackupOutputPath));
            }
        }

        private sealed class EmptyModule : BackupBase
        {
            internal EmptyModule()
            {
                Title = "Empty";
            }
        }

        private sealed class FolderSourceModule : FolderModule
        {
            internal FolderSourceModule(string path) : base(path)
            {
                Title = "Source";
            }
        }

        private sealed class TestRunUi : IRunUi
        {
            internal RunSummary Summary { get; private set; }

            public object DialogOwner => null;
            public void SetProgressText(string text) { }
            public void SetProgressPercent(int percent) { }
            public void SetProgressDetail(string groupInfo, string elapsed, string remaining, string throughput,
                                          long bytesWritten, int errors, int warnings)
            { }
            public void ShowSummary(RunSummary summary, string caption, IReadOnlyList<ModuleOutcome> outcomes)
                => Summary = summary;
            public IReadOnlyList<string> ShowConsentDialog(RestorePlan plan) => null;
            public bool ConfirmSnapshotOverride(string text, string caption) => false;
            public void ShowPlanCompositionError(string text, string caption) { }
            public void SetExplorerRestartVisible(bool visible) { }
        }
    }
}
