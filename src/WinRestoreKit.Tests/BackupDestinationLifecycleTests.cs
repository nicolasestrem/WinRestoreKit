using DataHelper;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Views;
using Xunit;

namespace WinRestoreKit.Tests
{
    public class BackupDestinationLifecycleTests
    {
        [Fact]
        public async Task RunBackup_RemembersCustomDestinationBeforeFirstModuleRuns()
        {
            string root = Path.Combine(Path.GetTempPath(), "WinRestoreKitTests", Guid.NewGuid().ToString("N"));
            object originalRoots = null;
            RegistryValueKind? originalRootsKind = null;

            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\WinRestoreKit"))
            {
                if (key != null && key.GetValueNames().Contains("BackupRoots"))
                {
                    originalRoots = key.GetValue("BackupRoots", null,
                        RegistryValueOptions.DoNotExpandEnvironmentNames);
                    originalRootsKind = key.GetValueKind("BackupRoots");
                }
            }

            Directory.CreateDirectory(root);

            try
            {
                RootObservingModule module = new RootObservingModule(root);
                BackupRestoreOrchestrator runner = new BackupRestoreOrchestrator(new TestRunUi());

                await runner.RunBackup(new BackupBase[] { module }, root, "remember-early", SnapshotCompression.None);

                Assert.True(module.RootWasRememberedWhenBackupStarted);
            }
            finally
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Software\WinRestoreKit"))
                {
                    if (originalRoots == null)
                        key.DeleteValue("BackupRoots", throwOnMissingValue: false);
                    else
                        key.SetValue("BackupRoots", originalRoots, originalRootsKind.Value);
                }

                if (Directory.Exists(root))
                    Directory.Delete(root, true);
            }
        }

        [Fact]
        public async Task RunBackup_CancelledInExistingFolderRetainsTheExistingFolder()
        {
            string root = Path.Combine(Path.GetTempPath(), "WinRestoreKitTests", Guid.NewGuid().ToString("N"));
            string backupPath = Path.Combine(root, Data.NowShort);
            Directory.CreateDirectory(backupPath);
            string sentinelPath = Path.Combine(backupPath, "preexisting.txt");
            File.WriteAllText(sentinelPath, "preserve existing folder");

            try
            {
                using (RunControl control = new RunControl())
                {
                    BackupRestoreOrchestrator runner = new BackupRestoreOrchestrator(new TestRunUi(), control);

                    await runner.RunBackup(new BackupBase[] { new CancellingModule(control) }, backupPath);
                }

                Assert.True(Directory.Exists(backupPath));
                Assert.True(File.Exists(sentinelPath));
            }
            finally
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, true);
            }
        }

        private sealed class RootObservingModule : BackupBase
        {
            private readonly string root;

            internal RootObservingModule(string root)
            {
                this.root = root;
                Title = "Observe root";
            }

            internal bool RootWasRememberedWhenBackupStarted { get; private set; }

            public override ModuleResult Backup(string path)
            {
                RootWasRememberedWhenBackupStarted = BackupRootRegistry.Read().Any(candidate =>
                    string.Equals(candidate, root, StringComparison.OrdinalIgnoreCase));
                return ModuleResult.Aggregate(new[] { StepResult.Succeeded(Title, "observed root") });
            }
        }

        private sealed class CancellingModule : BackupBase
        {
            private readonly RunControl control;

            internal CancellingModule(RunControl control)
            {
                this.control = control;
                Title = "Cancel";
            }

            public override ModuleResult Backup(string path)
            {
                control.RequestCancellation();
                return ModuleResult.Aggregate(new[] { StepResult.Succeeded(Title, "requested cancellation") });
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
