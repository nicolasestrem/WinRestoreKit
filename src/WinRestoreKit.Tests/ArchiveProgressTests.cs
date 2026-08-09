using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace WinRestoreKit.Tests
{
    public class ArchiveProgressTests
    {
        [Fact]
        public async Task RunBackup_FastCompressionReportsArchivingAndWritesPayloadManifest()
        {
            using (BackupRunIsolation isolation = new BackupRunIsolation())
            {
                TestRunUi ui = new TestRunUi();
                BackupRestoreOrchestrator runner = new BackupRestoreOrchestrator(ui);

                await runner.RunBackup(new BackupBase[] { new ArtifactModule() }, isolation.DestinationRoot,
                    "archive-progress", SnapshotCompression.Fast);

                string backupPath = runner.BackupOutputPath;
                Assert.Equal(Path.GetFullPath(isolation.DestinationRoot), Path.GetDirectoryName(backupPath),
                    StringComparer.OrdinalIgnoreCase);
                ManifestData manifest = BackupManifest.TryParse(
                    File.ReadAllText(Path.Combine(backupPath, BackupManifest.FileName)));

                Assert.Contains("Archiving backup payload", ui.ProgressTexts);
                Assert.True(File.Exists(Path.Combine(backupPath, BackupPayload.FileName)));
                Assert.NotNull(manifest);
                Assert.Equal("fast", manifest.Compression);
                Assert.Equal(BackupPayload.FileName, manifest.PayloadFile);
            }
        }

        private sealed class ArtifactModule : BackupBase
        {
            internal ArtifactModule()
            {
                Title = "Artifact";
            }

            public override ModuleResult Backup(string path)
            {
                File.WriteAllText(Path.Combine(path, "artifact.txt"), "payload");
                return ModuleResult.Aggregate(new[] { StepResult.Succeeded(Title, "wrote payload") });
            }
        }

        private sealed class TestRunUi : IRunUi
        {
            internal List<string> ProgressTexts { get; } = new List<string>();

            public object DialogOwner => null;

            public void SetProgressText(string text) => ProgressTexts.Add(text);
            public void SetProgressPercent(int percent) { }
            public void SetProgressDetail(string groupInfo, string elapsed, string remaining, string throughput,
                                          long bytesWritten, int errors, int warnings)
            { }
            public void ShowSummary(RunSummary summary, string caption, IReadOnlyList<ModuleOutcome> outcomes) { }
            public IReadOnlyList<string> ShowConsentDialog(RestorePlan plan) => null;
            public bool ConfirmSnapshotOverride(string text, string caption) => false;
            public void ShowPlanCompositionError(string text, string caption) { }
            public void SetExplorerRestartVisible(bool visible) { }
        }
    }
}
