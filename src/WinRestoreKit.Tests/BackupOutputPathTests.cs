using Conf;
using DataHelper;
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
            string expectedPath = Path.Combine(destination, Data.NowShort);
            var ui = new TestRunUi();

            try
            {
                var runner = new BackupRestoreOrchestrator(ui);

                await runner.RunBackup(new BackupBase[] { new FolderSourceModule(source) }, destination,
                    "contained", SnapshotCompression.None);

                Assert.Equal(expectedPath, runner.BackupOutputPath);
                Assert.Equal(RunState.DidNotRun, ui.Summary.State);
            }
            finally
            {
                if (Directory.Exists(source))
                    Directory.Delete(source, true);
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
                                          long bytesWritten, int errors, int warnings) { }
            public void ShowSummary(RunSummary summary, string caption, IReadOnlyList<ModuleOutcome> outcomes)
                => Summary = summary;
            public IReadOnlyList<string> ShowConsentDialog(RestorePlan plan) => null;
            public bool ConfirmSnapshotOverride(string text, string caption) => false;
            public void ShowPlanCompositionError(string text, string caption) { }
            public void SetExplorerRestartVisible(bool visible) { }
        }
    }
}
