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
    public class SnapshotFolderPathTests
    {
        [Fact]
        public async Task RunBackup_CustomSnapshotNameKeepsTheFrozenTimestampFolderName()
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
                // This overload always remembers a non-null custom destination root (see
                // BackupRestoreOrchestrator.RunBackup), so this test writes to the real
                // HKCU\Software\WinRestoreKit BackupRoots value exactly like the sibling test
                // below, and must restore it the same way.
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
        public async Task RunBackup_ToCustomDestinationMakesSnapshotDiscoverable()
        {
            string root = Path.Combine(Path.GetTempPath(), "WinRestoreKitTests", Guid.NewGuid().ToString("N"));
            object originalRoots = null;
            RegistryValueKind? originalRootsKind = null;

            try
            {
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
                BackupRestoreOrchestrator runner = new BackupRestoreOrchestrator(new TestRunUi());

                await runner.RunBackup(new BackupBase[] { new EmptyModule() }, root, "custom-root",
                    SnapshotCompression.None);

                string timestampFolder = Path.Combine(root, Data.NowShort);
                BackupFolders folders = BackupFolders.Read();

                Assert.Contains(folders.Backups, folder =>
                    string.Equals(folder.Path, timestampFolder, StringComparison.OrdinalIgnoreCase));
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
