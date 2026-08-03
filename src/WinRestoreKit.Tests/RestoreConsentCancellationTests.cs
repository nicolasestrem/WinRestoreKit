using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using WinRestoreKit;
using Xunit;

namespace WinRestoreKit.Tests
{
    public class RestoreConsentCancellationTests
    {
        [Fact]
        public async Task ConsentCancellation_ShowsInformationalNoChangesSummary()
        {
            string restorePath = Path.Combine(Path.GetTempPath(), "WinRestoreKit.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(restorePath);

            try
            {
                CancelingUi ui = new CancelingUi();
                BackupRestoreOrchestrator runner = new BackupRestoreOrchestrator(ui);

                await runner.RunRestore(new BackupBase[] { new TestModule() }, restorePath);

                RunSummary summary = Assert.IsType<RunSummary>(ui.Summary);
                Assert.Equal(1, ui.SummaryCount);
                Assert.NotEqual(RunState.Problems, summary.State);
                Assert.NotEqual(RunState.DidNotRun, summary.State);
                Assert.Equal(MessageBoxIcon.Information, summary.Icon);
                Assert.Contains("canceled", summary.Headline, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("no changes", summary.Headline + " " + summary.Detail,
                    StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("runner returned without a result", summary.Detail,
                    StringComparison.OrdinalIgnoreCase);
                Assert.Equal("You canceled the confirmation dialog before the restore began.", summary.Detail);
            }
            finally
            {
                Directory.Delete(restorePath, true);
            }
        }

        private sealed class TestModule : BackupBase
        {
            public TestModule()
            {
                Title = "Test setting";
            }
        }

        private sealed class CancelingUi : IRunUi
        {
            public RunSummary Summary { get; private set; }
            public int SummaryCount { get; private set; }

            public IWin32Window Owner => null;
            public void SetProgressText(string text) { }
            public void SetProgressPercent(int percent) { }
            public void SetProgressDetail(string groupInfo, string elapsed, string remaining, string throughput,
                                          long bytesWritten, int errors, int warnings) { }

            public void ShowSummary(RunSummary summary, string caption, IReadOnlyList<ModuleOutcome> outcomes)
            {
                Summary = summary;
                SummaryCount++;
            }

            public IReadOnlyList<string> ShowConsentDialog(RestorePlan plan) => null;
            public bool ConfirmSnapshotOverride(string text, string caption) => false;
            public void ShowPlanCompositionError(string text, string caption) { }
            public void SetExplorerRestartVisible(bool visible) { }
        }
    }
}
