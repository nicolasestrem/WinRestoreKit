using DataHelper;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using Xunit;

namespace WinRestoreKit.Tests
{
    public class SnapshotFolderPathTests
    {
        [Fact]
        public async Task RunBackup_CustomSnapshotNameKeepsTheFrozenTimestampFolderName()
        {
            string root = Path.Combine(Path.GetTempPath(), "WinRestoreKitTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);

            try
            {
                BackupRestoreOrchestrator runner = new BackupRestoreOrchestrator(new TestRunUi());

                await runner.RunBackup(new BackupBase[] { new EmptyModule() }, root, "before-driver-update",
                    SnapshotCompression.None);

                string timestampFolder = Path.Combine(root, Data.NowShort);
                Assert.True(Directory.Exists(timestampFolder));
                Assert.False(Directory.Exists(Path.Combine(root, "before-driver-update")));

                ManifestData manifest = BackupManifest.TryParse(
                    File.ReadAllText(Path.Combine(timestampFolder, BackupManifest.FileName)));
                Assert.Equal("before-driver-update", manifest.SnapshotName);
            }
            finally
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, true);
            }
        }

        private sealed class EmptyModule : BackupBase
        {
            internal EmptyModule()
            {
                Title = "Empty";
            }
        }

        private sealed class TestRunUi : IRunUi
        {
            public IWin32Window Owner => null;

            public void SetProgressText(string text) { }
            public void SetProgressPercent(int percent) { }
            public void SetProgressDetail(string groupInfo, string elapsed, string remaining, string throughput,
                                          long bytesWritten, int errors, int warnings) { }
            public void ShowSummary(RunSummary summary, string caption, IReadOnlyList<ModuleOutcome> outcomes) { }
            public IReadOnlyList<string> ShowConsentDialog(RestorePlan plan) => null;
            public bool ConfirmSnapshotOverride(string text, string caption) => false;
            public void ShowPlanCompositionError(string text, string caption) { }
            public void SetExplorerRestartVisible(bool visible) { }
        }
    }
}
