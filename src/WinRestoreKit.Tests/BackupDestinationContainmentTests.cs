using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Conf;
using DataHelper;
using WinRestoreKit;
using Xunit;

namespace WinRestoreKit.Tests
{
    /// <summary>
    /// Regression coverage for the PR #4 bot finding at BackupRestoreOrchestrator.cs:105: choosing a
    /// destination inside a folder being backed up makes the timestamped backup a descendant of that
    /// source, and WindowsHelper.CopyFolderInto then copies the backup into itself until the path
    /// length limit or the disk is exhausted.
    /// </summary>
    public sealed class BackupDestinationContainmentTests
    {
        private sealed class FolderSourceModule : FolderModule
        {
            internal FolderSourceModule(string folder) : base(folder)
            {
                Title = "Source";
            }
        }

        [Fact]
        public async Task RunBackup_DestinationInsideASelectedSourceFolder_IsRejectedBeforeAnyCopy()
        {
            string source = Directory.CreateDirectory(
                Path.Combine(Path.GetTempPath(), "WinRestoreKitTests", Guid.NewGuid().ToString("N"))).FullName;
            string destination = Path.Combine(source, "backups-here");

            try
            {
                TestRunUi ui = new TestRunUi();
                BackupRestoreOrchestrator runner = new BackupRestoreOrchestrator(ui);

                await runner.RunBackup(new BackupBase[] { new FolderSourceModule(source) },
                    destination, "nested", SnapshotCompression.None);
                Assert.Equal(Path.Combine(destination, Data.NowShort), runner.BackupOutputPath);

                Assert.NotNull(ui.LastSummary);
                Assert.Equal(RunState.DidNotRun, ui.LastSummary.State);
                // No timestamp folder was created under the destination, so no copy began.
                Assert.False(Directory.Exists(Path.Combine(destination, Data.NowShort)));
            }
            finally
            {
                if (Directory.Exists(source))
                    Directory.Delete(source, true);
            }
        }

        [Fact]
        public async Task RunBackup_DestinationOutsideEverySource_IsAccepted()
        {
            string source = Directory.CreateDirectory(
                Path.Combine(Path.GetTempPath(), "WinRestoreKitTests", Guid.NewGuid().ToString("N"))).FullName;
            string destination = Path.Combine(Path.GetTempPath(), "WinRestoreKitTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(destination);

            object originalRoots = null;
            Microsoft.Win32.RegistryValueKind? originalRootsKind = null;
            using (Microsoft.Win32.RegistryKey key =
                Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\WinRestoreKit"))
            {
                if (key != null && Array.IndexOf(key.GetValueNames(), "BackupRoots") >= 0)
                {
                    originalRoots = key.GetValue("BackupRoots", null,
                        Microsoft.Win32.RegistryValueOptions.DoNotExpandEnvironmentNames);
                    originalRootsKind = key.GetValueKind("BackupRoots");
                }
            }

            try
            {
                TestRunUi ui = new TestRunUi();
                BackupRestoreOrchestrator runner = new BackupRestoreOrchestrator(ui);

                await runner.RunBackup(new BackupBase[] { new FolderSourceModule(source) },
                    destination, "outside", SnapshotCompression.None);

                Assert.NotNull(ui.LastSummary);
                Assert.NotEqual(RunState.DidNotRun, ui.LastSummary.State);
                Assert.True(Directory.Exists(runner.BackupOutputPath));
            }
            finally
            {
                using (Microsoft.Win32.RegistryKey key =
                    Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\WinRestoreKit"))
                {
                    if (originalRoots == null)
                        key.DeleteValue("BackupRoots", throwOnMissingValue: false);
                    else
                        key.SetValue("BackupRoots", originalRoots, originalRootsKind.Value);
                }

                if (Directory.Exists(source))
                    Directory.Delete(source, true);
                if (Directory.Exists(destination))
                    Directory.Delete(destination, true);
            }
        }

        private sealed class TestRunUi : IRunUi
        {
            internal RunSummary LastSummary { get; private set; }

            public object DialogOwner => null;
            public void SetProgressText(string text) { }
            public void SetProgressPercent(int percent) { }
            public void SetProgressDetail(string groupInfo, string elapsed, string remaining,
                string throughput, long bytesWritten, int errors, int warnings) { }
            public void ShowSummary(RunSummary summary, string caption, IReadOnlyList<ModuleOutcome> outcomes)
                => LastSummary = summary;
            public IReadOnlyList<string> ShowConsentDialog(RestorePlan plan) => null;
            public bool ConfirmSnapshotOverride(string text, string caption) => false;
            public void ShowPlanCompositionError(string text, string caption) { }
            public void SetExplorerRestartVisible(bool visible) { }
        }
    }
}
