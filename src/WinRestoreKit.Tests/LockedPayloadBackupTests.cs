using DataHelper;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace WinRestoreKit.Tests
{
    public class LockedPayloadBackupTests
    {
        [Fact]
        public async Task RunBackup_LockedPayloadInReusedFolder_DoesNotRunOrPublishManifest()
        {
            using (BackupRunIsolation isolation = new BackupRunIsolation())
            {
                string backupPath = Directory.CreateDirectory(Path.Combine(isolation.DestinationRoot, Data.NowShort)).FullName;
                string payloadPath = Path.Combine(backupPath, BackupPayload.FileName);
                File.WriteAllText(payloadPath, "previous compressed backup");

                TestRunUi ui = new TestRunUi();
                ArtifactModule module = new ArtifactModule();

                FileStream payloadLock = new FileStream(payloadPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                try
                {
                    BackupRestoreOrchestrator runner = new BackupRestoreOrchestrator(ui);

                    await runner.RunBackup(new BackupBase[] { module }, backupPath);
                    Assert.Equal(backupPath, runner.BackupOutputPath);

                    Assert.NotNull(ui.Summary);
                    Assert.Equal(RunState.DidNotRun, ui.Summary.State);
                    Assert.False(module.BackupCalled);
                    Assert.False(File.Exists(Path.Combine(backupPath, BackupManifest.FileName)));
                    Assert.Contains("previous compressed backup", ui.Summary.Detail,
                        StringComparison.OrdinalIgnoreCase);
                }
                finally
                {
                    payloadLock.Dispose();
                }
            }
        }

        [Fact]
        public async Task RunBackup_ReusableFolderWithoutPayload_CompletesAndWritesManifest()
        {
            using (BackupRunIsolation isolation = new BackupRunIsolation())
            {
                string backupPath = Directory.CreateDirectory(Path.Combine(isolation.DestinationRoot, Data.NowShort)).FullName;
                TestRunUi ui = new TestRunUi();
                ArtifactModule module = new ArtifactModule();
                BackupRestoreOrchestrator runner = new BackupRestoreOrchestrator(ui);

                await runner.RunBackup(new BackupBase[] { module }, backupPath);
                Assert.Equal(backupPath, runner.BackupOutputPath);

                Assert.NotNull(ui.Summary);
                Assert.Equal(RunState.Done, ui.Summary.State);
                Assert.True(module.BackupCalled);
                Assert.NotNull(BackupManifest.TryParse(
                    File.ReadAllText(Path.Combine(backupPath, BackupManifest.FileName))));
            }
        }

        private sealed class ArtifactModule : BackupBase
        {
            internal bool BackupCalled { get; private set; }

            internal ArtifactModule()
            {
                Title = "Artifact";
            }

            public override ModuleResult Backup(string path)
            {
                BackupCalled = true;
                File.WriteAllText(Path.Combine(path, "artifact.txt"), "new backup");
                return ModuleResult.Aggregate(new[] { StepResult.Succeeded(Title, "wrote artifact") });
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
            {
                Summary = summary;
            }

            public IReadOnlyList<string> ShowConsentDialog(RestorePlan plan) => null;
            public bool ConfirmSnapshotOverride(string text, string caption) => false;
            public void ShowPlanCompositionError(string text, string caption) { }
            public void SetExplorerRestartVisible(bool visible) { }
        }
    }
}
