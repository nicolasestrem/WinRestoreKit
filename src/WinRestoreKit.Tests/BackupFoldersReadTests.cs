using DataHelper;
using Microsoft.Win32;
using System;
using System.IO;
using System.Linq;
using Views;
using Xunit;

namespace WinRestoreKit.Tests
{
    public class BackupFoldersReadTests
    {
        [Fact]
        public void Read_UnreadableCustomRootKeepsReadableDefaultRoot()
        {
            string parent = NewTempDirectory();
            string defaultRoot = Directory.CreateDirectory(Path.Combine(parent, "default")).FullName;
            string unavailableCustomRoot = Path.Combine(parent, "custom-root-file");
            string backup = Directory.CreateDirectory(Path.Combine(defaultRoot, "legacy-backup")).FullName;
            File.WriteAllText(unavailableCustomRoot, "not a directory");

            try
            {
                RunWithConfiguredRoots(defaultRoot, new[] { unavailableCustomRoot }, () =>
                {
                    BackupFolders folders = BackupFolders.Read();

                    Assert.Null(folders.UnreadableReason);
                    Assert.Contains(folders.Backups, folder =>
                        string.Equals(folder.Path, backup, StringComparison.OrdinalIgnoreCase));
                });
            }
            finally
            {
                Directory.Delete(parent, true);
            }
        }

        [Fact]
        public void Read_UnreadableDefaultRootReportsFatalReason()
        {
            string parent = NewTempDirectory();
            string unreadableDefaultRoot = Path.Combine(parent, "default-root-file");
            File.WriteAllText(unreadableDefaultRoot, "not a directory");

            try
            {
                RunWithConfiguredRoots(unreadableDefaultRoot, Array.Empty<string>(), () =>
                {
                    BackupFolders folders = BackupFolders.Read();

                    Assert.NotNull(folders.UnreadableReason);
                });
            }
            finally
            {
                Directory.Delete(parent, true);
            }
        }

        [Fact]
        public void Read_CustomRootExcludesUnrelatedDirectory()
        {
            RunWithRoots((defaultRoot, customRoot) =>
            {
                string unrelated = Directory.CreateDirectory(Path.Combine(customRoot, "Taxes")).FullName;

                BackupFolders folders = BackupFolders.Read();

                Assert.DoesNotContain(folders.Backups, folder =>
                    string.Equals(folder.Path, unrelated, StringComparison.OrdinalIgnoreCase));
            });
        }

        [Fact]
        public void Read_CustomRootIncludesTimestampAndManifestFolders()
        {
            RunWithRoots((defaultRoot, customRoot) =>
            {
                string timestamp = Directory.CreateDirectory(
                    Path.Combine(customRoot, "2024-01-02 - 03.04 (3)")).FullName;
                string manifested = Directory.CreateDirectory(Path.Combine(customRoot, "named-backup")).FullName;
                File.WriteAllText(Path.Combine(manifested, BackupManifest.FileName), "{}");

                BackupFolders folders = BackupFolders.Read();

                Assert.Contains(folders.Backups, folder =>
                    string.Equals(folder.Path, timestamp, StringComparison.OrdinalIgnoreCase));
                Assert.Contains(folders.Backups, folder =>
                    string.Equals(folder.Path, manifested, StringComparison.OrdinalIgnoreCase));
            });
        }

        [Fact]
        public void Read_CustomRootClassifiesPreRestoreSnapshotSeparately()
        {
            RunWithRoots((defaultRoot, customRoot) =>
            {
                string snapshot = Directory.CreateDirectory(Path.Combine(customRoot,
                    SnapshotNaming.NameFor(new DateTime(2024, 1, 2, 3, 4, 5)) + " (2)")).FullName;

                BackupFolders folders = BackupFolders.Read();

                Assert.Contains(folders.Snapshots, folder =>
                    string.Equals(folder.Path, snapshot, StringComparison.OrdinalIgnoreCase));
                Assert.DoesNotContain(folders.Backups, folder =>
                    string.Equals(folder.Path, snapshot, StringComparison.OrdinalIgnoreCase));
            });
        }

        [Fact]
        public void Read_DefaultRootKeepsLooseLegacyFolder()
        {
            RunWithRoots((defaultRoot, customRoot) =>
            {
                string legacy = Directory.CreateDirectory(Path.Combine(defaultRoot, "loose-legacy-folder")).FullName;

                BackupFolders folders = BackupFolders.Read();

                Assert.Contains(folders.Backups, folder =>
                    string.Equals(folder.Path, legacy, StringComparison.OrdinalIgnoreCase));
            });
        }

        [Fact]
        public void Read_NestedRememberedRootExcludesDefaultRootContainerAndListsBackupOnce()
        {
            string parent = NewTempDirectory();
            string defaultRoot = Directory.CreateDirectory(Path.Combine(parent, "default")).FullName;
            string archive = Directory.CreateDirectory(Path.Combine(defaultRoot, "Archive")).FullName;
            string customRoot = Directory.CreateDirectory(Path.Combine(archive, "remembered-root")).FullName;
            string backup = Directory.CreateDirectory(
                Path.Combine(customRoot, "2024-01-02 - 03.04")).FullName;

            try
            {
                RunWithConfiguredRoots(defaultRoot, new[] { customRoot }, () =>
                {
                    BackupFolders folders = BackupFolders.Read();

                    Assert.DoesNotContain(folders.Backups, folder =>
                        string.Equals(folder.Path, archive, StringComparison.OrdinalIgnoreCase));
                    Assert.Equal(1, folders.Backups.Count(folder =>
                        string.Equals(folder.Path, backup, StringComparison.OrdinalIgnoreCase)));
                });
            }
            finally
            {
                Directory.Delete(parent, true);
            }
        }

        [Fact]
        public void Read_NestedRememberedRootKeepsOrdinaryDefaultRootBackup()
        {
            string parent = NewTempDirectory();
            string defaultRoot = Directory.CreateDirectory(Path.Combine(parent, "default")).FullName;
            string archive = Directory.CreateDirectory(Path.Combine(defaultRoot, "Archive")).FullName;
            string customRoot = Directory.CreateDirectory(Path.Combine(archive, "remembered-root")).FullName;
            string ordinaryBackup = Directory.CreateDirectory(
                Path.Combine(defaultRoot, "2024-01-03 - 04.05")).FullName;

            try
            {
                RunWithConfiguredRoots(defaultRoot, new[] { customRoot }, () =>
                {
                    BackupFolders folders = BackupFolders.Read();

                    Assert.Contains(folders.Backups, folder =>
                        string.Equals(folder.Path, ordinaryBackup, StringComparison.OrdinalIgnoreCase));
                });
            }
            finally
            {
                Directory.Delete(parent, true);
            }
        }

        [Fact]
        public void Read_RememberedRootOutsideDefaultRootIsStillListed()
        {
            string parent = NewTempDirectory();
            string defaultRoot = Directory.CreateDirectory(Path.Combine(parent, "default")).FullName;
            string customRoot = Directory.CreateDirectory(Path.Combine(parent, "custom")).FullName;
            string backup = Directory.CreateDirectory(
                Path.Combine(customRoot, "2024-01-04 - 05.06")).FullName;

            try
            {
                RunWithConfiguredRoots(defaultRoot, new[] { customRoot }, () =>
                {
                    BackupFolders folders = BackupFolders.Read();

                    Assert.Equal(1, folders.Backups.Count(folder =>
                        string.Equals(folder.Path, backup, StringComparison.OrdinalIgnoreCase)));
                });
            }
            finally
            {
                Directory.Delete(parent, true);
            }
        }

        private static void RunWithRoots(Action<string, string> action)
        {
            string parent = NewTempDirectory();
            string defaultRoot = Directory.CreateDirectory(Path.Combine(parent, "default")).FullName;
            string customRoot = Directory.CreateDirectory(Path.Combine(parent, "custom")).FullName;

            try
            {
                RunWithConfiguredRoots(defaultRoot, new[] { customRoot }, () => action(defaultRoot, customRoot));
            }
            finally
            {
                Directory.Delete(parent, true);
            }
        }

        private static void RunWithConfiguredRoots(string defaultRoot, string[] customRoots, Action action)
        {
            string originalDefaultRoot = Data.DataRootDir;
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

                Data.DataRootDir = defaultRoot;

                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Software\WinRestoreKit"))
                    key.SetValue("BackupRoots", customRoots, RegistryValueKind.MultiString);

                action();
            }
            finally
            {
                Data.DataRootDir = originalDefaultRoot;

                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Software\WinRestoreKit"))
                {
                    if (originalRoots == null)
                        key.DeleteValue("BackupRoots", throwOnMissingValue: false);
                    else
                        key.SetValue("BackupRoots", originalRoots, originalRootsKind.Value);
                }
            }
        }

        private static string NewTempDirectory()
        {
            string path = Path.Combine(Path.GetTempPath(), "WinRestoreKitTests", Guid.NewGuid().ToString("N"));
            return Directory.CreateDirectory(path).FullName;
        }
    }
}
