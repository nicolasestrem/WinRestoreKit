using WinRestoreKit;
using Conf;
using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace WinRestoreKit.Tests
{
    // These exercise the module shapes against the real registry, unelevated, using keys whose
    // presence or absence is knowable. They cover the SHAPE of a module's decision, not regedit.
    public class ModuleShapeTests
    {
        [Fact]
        public void EveryRegisteredModule_HasATitle()
        {
            foreach (BackupBase m in new BackupBase[]
            {
                new WAccessibility(), new DMouse(), new DKeyboard(), new WTaskbar(),
                new WAPrivacy(), new WOther(), new WPrivacy(), new WVisualEffects(),
                new DTouchpad(), new WPersonalization(), new WTelemetry(),
                new WUpdates(), new DPrinters(), new GGaming(), new APinnedApps(),
                new WThemes(), new WNetworkConf(), new CWiFiConf(), new AppStoreApps(),
                // Phase 3c power-user settings.
                new WPowerPlans(), new WFonts(), new WMappedDrives(), new WRegional()
            })
            {
                Assert.False(string.IsNullOrWhiteSpace(m.Title));
            }
        }

        // The base default must be a failure, not a reassuring skip.
        private sealed class ForgetfulModule : BackupBase
        {
            public ForgetfulModule() { Title = "Forgetful"; }
        }

        [Fact]
        public void BackupBase_UnimplementedBackup_IsFailed()
            => Assert.Equal(ResultState.Failed, new ForgetfulModule().Backup("C:\\nowhere").State);

        [Fact]
        public void BackupBase_UnimplementedRestore_IsFailed()
            => Assert.Equal(ResultState.Failed, new ForgetfulModule().Restore("C:\\nowhere").State);

        [Fact]
        public async Task BackupBase_AsyncWrapper_CarriesTheResultThrough()
        {
            ModuleResult r = await new ForgetfulModule().BackupAsync("C:\\nowhere");
            Assert.Equal(ResultState.Failed, r.State);
        }

        /// <summary>
        /// Stands in for AppStoreApps so the thread behaviour can be observed without a dialog.
        /// </summary>
        /// <remarks>
        /// Deliberately does NOT call base.Restore: that opens RestAppsForm modally, which in a test
        /// run means a message loop nobody will ever close.
        /// </remarks>
        private sealed class ProbeStoreApps : AppStoreApps
        {
            public int RestoreThreadId { get; private set; }

            public override ModuleResult Restore(string path)
            {
                RestoreThreadId = Environment.CurrentManagedThreadId;

                return ModuleResult.Aggregate(new[] { StepResult.Skipped(Title, "probe") });
            }
        }

        // AppStoreApps is the one module whose Restore opens a window, and Windows Forms windows
        // belong to the STA thread that owns the message loop. The base RestoreAsync hands the body
        // to Task.Run - a thread-pool thread, which is MTA - so this module overrides it.
        //
        // Both assertions are needed. The thread id alone is weak: the caller here is itself a pool
        // thread, so a Task.Run continuation could in principle land back on it. Having already
        // completed before the Task was handed back is what pins "ran inline, not scheduled".
        [Fact]
        public async Task AppStoreApps_RestoreAsync_RunsInlineOnTheCallingThread()
        {
            ProbeStoreApps probe = new ProbeStoreApps();
            int callerThreadId = Environment.CurrentManagedThreadId;

            Task<ModuleResult> pending = probe.RestoreAsync("C:\\nowhere");

            Assert.True(pending.IsCompleted);
            Assert.Equal(callerThreadId, probe.RestoreThreadId);

            Assert.Equal(ResultState.Skipped, (await pending).State);
        }

        // The app-restore dialog used to discard RunWinget's outcome entirely, so with winget
        // missing the loop finished instantly having installed nothing and said nothing.
        //
        // This covers the mapping, not the loop that calls it: whether RestorePackages actually
        // surfaces these needs a dialog and a real winget, and is on the manual smoke matrix.
        [Fact]
        public void AppRestoreService_Describe_NamesEveryWayWingetCanFail()
        {
            Assert.Null(AppRestoreService.Describe(ProcessOutcome.Ran(0)));

            Assert.Contains("could not run winget",
                AppRestoreService.Describe(ProcessOutcome.NeverStarted("not installed")));

            Assert.Contains("did not finish",
                AppRestoreService.Describe(ProcessOutcome.Timeout()));

            // Started-but-unknown must NOT read as "could not run winget": winget may have
            // half-installed the package, and reporting it as untouched would be a false statement
            // about whether this machine was changed.
            string unknown = AppRestoreService.Describe(ProcessOutcome.OutcomeUnknown("pipe closed"));
            Assert.Contains("could not be determined", unknown);
            Assert.DoesNotContain("could not run winget", unknown);

            Assert.Contains("code 1", AppRestoreService.Describe(ProcessOutcome.Ran(1)));

            Assert.NotNull(AppRestoreService.Describe(null));
        }

        // Restoring from a folder containing no .reg file must be Skipped, not a false success.
        //
        // A freshly created, uniquely named folder rather than %TEMP% itself: this used to pass only
        // while the machine's temp directory happened not to contain a Mouse.reg, so a stray file
        // left there by anything at all would silently invert the assertion.
        [Fact]
        public void S1Module_RestoreWithNoBackedUpFile_IsSkipped()
        {
            string dir = Path.Combine(Path.GetTempPath(), "acshape_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);

            try
            {
                ModuleResult r = new DMouse().Restore(dir);
                Assert.Equal(ResultState.Skipped, r.State);
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }
    }
}
