using WinRestoreKit;
using Conf;
using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace WinRestoreKit.Tests
{
    public class WPowerPlansManifestPreClearTests
    {
        private sealed class FailedListPowerPlans : WPowerPlans
        {
            protected override Task<ToolCapture> CaptureAsync(params string[] args)
            {
                Assert.Equal(new[] { "/list" }, args);

                return Task.FromResult(new ToolCapture
                {
                    Outcome = ProcessOutcome.NeverStarted("test failure")
                });
            }
        }

        [Fact]
        public async Task Backup_WhenListingFails_RemovesThePreviousManifest()
        {
            string backup = Path.Combine(Path.GetTempPath(), "power_manifest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(backup);
            WPowerPlans module = new FailedListPowerPlans();
            string manifest = WPowerPlans.ManifestPathIn(backup);

            try
            {
                File.WriteAllText(manifest, "{ \"activeSchemeGuid\": \"previous-plan\" }");
                Assert.True(module.HasArtifactIn(backup));

                ModuleResult result = await module.BackupAsync(backup);

                Assert.Equal(ResultState.Failed, result.State);
                Assert.False(File.Exists(manifest));
                Assert.False(module.HasArtifactIn(backup));
            }
            finally
            {
                Directory.Delete(backup, recursive: true);
            }
        }
    }
}
