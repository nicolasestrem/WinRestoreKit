using DataHelper;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace WinRestoreKit.Tests
{
    public sealed class SnapshotEventCatalogTests
    {
        [Fact]
        public void Read_OrdersSameTimestampByOrdinalCanonicalPath()
        {
            using (TestDirectory root = TestDirectory.Create())
            {
                WriteManifest(root.Create("zulu"), "2026-08-09T12:00:00.0000000Z", "succeeded");
                WriteManifest(root.Create("alpha"), "2026-08-09T12:00:00.0000000Z", "succeeded");

                UseBackupRoots(root.Path, () =>
                {
                    IReadOnlyList<SnapshotEvent> events = new SnapshotEventCatalog().Read();

                    Assert.Equal(new[] { "alpha", "zulu" }, events.Select(snapshot => snapshot.DisplayName));
                });
            }
        }

        [Fact]
        public void Read_ClassifiesFailedPartialAndUnreadableWithoutMakingThemRestorable()
        {
            using (TestDirectory root = TestDirectory.Create())
            {
                WriteManifest(root.Create("verified"), "2026-08-09T11:00:00.0000000Z", "succeeded");
                WriteManifest(root.Create("partial"), "2026-08-09T10:00:00.0000000Z", "skipped");
                WriteManifest(root.Create("failed"), "2026-08-09T09:00:00.0000000Z", "failed");
                File.WriteAllText(Path.Combine(root.Create("broken"), BackupManifest.FileName), "not-json");

                UseBackupRoots(root.Path, () =>
                {
                    SnapshotEvent[] events = new SnapshotEventCatalog().Read().ToArray();

                    Assert.Equal(SnapshotEventKind.Verified, events.Single(snapshot => snapshot.DisplayName == "verified").Kind);
                    Assert.Equal(SnapshotEventKind.Partial, events.Single(snapshot => snapshot.DisplayName == "partial").Kind);
                    Assert.Equal(SnapshotEventKind.Failed, events.Single(snapshot => snapshot.DisplayName == "failed").Kind);
                    SnapshotEvent unreadable = events.Single(snapshot => snapshot.DisplayName == "broken");
                    Assert.Equal(SnapshotEventKind.Unreadable, unreadable.Kind);
                    Assert.False(events.Single(snapshot => snapshot.DisplayName == "failed").IsRestorable);
                    Assert.False(unreadable.IsRestorable);
                    Assert.NotEmpty(unreadable.DiagnosticReason);
                });
            }
        }

        [Fact]
        public void Read_EmptyManifestIsFailed()
        {
            using (TestDirectory root = TestDirectory.Create())
            {
                WriteManifest(root.Create("empty"), "2026-08-09T12:00:00.0000000Z");

                UseBackupRoots(root.Path, () =>
                {
                    SnapshotEvent snapshot = Assert.Single(new SnapshotEventCatalog().Read());

                    Assert.Equal(SnapshotEventKind.Failed, snapshot.Kind);
                    Assert.False(snapshot.IsRestorable);
                });
            }
        }

        [Fact]
        public void Read_ClassifiesMixedAndUnknownModulesAsPartial()
        {
            using (TestDirectory root = TestDirectory.Create())
            {
                WriteManifest(root.Create("skipped"), "2026-08-09T12:00:00.0000000Z", "succeeded", "skipped");
                WriteManifest(root.Create("failed"), "2026-08-09T11:00:00.0000000Z", "succeeded", "failed");
                WriteManifest(root.Create("unknown"), "2026-08-09T10:00:00.0000000Z", "succeeded", "unknown");

                UseBackupRoots(root.Path, () =>
                {
                    Assert.All(new SnapshotEventCatalog().Read(), snapshot =>
                    {
                        Assert.Equal(SnapshotEventKind.Partial, snapshot.Kind);
                        Assert.True(snapshot.IsRestorable);
                    });
                });
            }
        }

        [Fact]
        public void Read_ManifestSilentLegacyFolderIsPartial()
        {
            using (TestDirectory root = TestDirectory.Create())
            {
                root.Create("legacy");

                UseBackupRoots(root.Path, () =>
                {
                    SnapshotEvent snapshot = Assert.Single(new SnapshotEventCatalog().Read());

                    Assert.Equal(SnapshotEventKind.Partial, snapshot.Kind);
                    Assert.True(snapshot.IsRestorable);
                });
            }
        }

        [Fact]
        public void Read_UnreadableCustomRootRetainsActualErrorAlongsideReadableRoot()
        {
            using (TestDirectory parent = TestDirectory.Create())
            {
                string defaultRoot = parent.Create("default");
                string backup = parent.Create(Path.Combine("default", "backup"));
                string unreadableRoot = Path.Combine(parent.Path, "custom-root-file");
                File.WriteAllText(unreadableRoot, "not a directory");
                Exception expected = Record.Exception(() => Directory.GetDirectories(unreadableRoot));
                Assert.NotNull(expected);

                UseBackupRoots(defaultRoot, new[] { unreadableRoot }, () =>
                {
                    SnapshotEvent[] events = new SnapshotEventCatalog().Read().ToArray();

                    Assert.Contains(events, snapshot => string.Equals(snapshot.CanonicalPath, backup,
                        StringComparison.OrdinalIgnoreCase));
                    SnapshotEvent unreadable = Assert.Single(events,
                        snapshot => snapshot.Kind == SnapshotEventKind.Unreadable);
                    Assert.Equal(expected.Message, unreadable.DiagnosticReason);
                    Assert.False(unreadable.IsRestorable);
                });
            }
        }

        [Fact]
        public void RecordSessionFailureExistsOnlyForCatalogLifetime()
        {
            using (TestDirectory root = TestDirectory.Create())
            {
                UseBackupRoots(root.Path, () =>
                {
                    SnapshotEventCatalog catalog = new SnapshotEventCatalog();
                    catalog.RecordSessionFailure(new DateTime(2026, 8, 9, 12, 0, 0), "Failed run", "folder creation failed");

                    SnapshotEvent recorded = Assert.Single(catalog.Read());
                    Assert.Equal(SnapshotEventKind.Failed, recorded.Kind);
                    Assert.Equal("session://failure/1", recorded.CanonicalPath);
                    Assert.False(recorded.IsRestorable);

                    Assert.Empty(new SnapshotEventCatalog().Read());
                    Assert.Throws<ArgumentException>(() => catalog.RecordSessionFailure(DateTime.UtcNow, "", " "));
                });
            }
        }

        [Fact]
        public void Read_ComputesCompleteFolderSize()
        {
            using (TestDirectory root = TestDirectory.Create())
            {
                string backup = root.Create("size");
                File.WriteAllText(Path.Combine(backup, "one.txt"), "abc");
                Directory.CreateDirectory(Path.Combine(backup, "nested"));
                File.WriteAllText(Path.Combine(backup, "nested", "two.txt"), "de");

                UseBackupRoots(root.Path, () =>
                {
                    SnapshotEvent snapshot = Assert.Single(new SnapshotEventCatalog().Read());

                    Assert.Equal(5, snapshot.SizeBytes);
                    Assert.True(snapshot.IsSizeComplete);
                });
            }
        }

        private static void WriteManifest(string folder, string created, params string[] states)
        {
            string modules = string.Join(",", states.Select(state =>
                "{\"type\":\"TestModule\",\"title\":\"Test module\",\"state\":\"" + state
                + "\",\"reason\":\"\"}"));
            string manifest = "{\"manifest_version\":1,\"app_version\":\"0.0.1\",\"created\":\""
                + created + "\",\"machine_name\":\"TEST-MACHINE\",\"user_name\":\"tester\",\"os_build\":\"test\",\"modules\":["
                + modules + "]}";

            File.WriteAllText(Path.Combine(folder, BackupManifest.FileName), manifest);
        }

        private static void UseBackupRoots(string defaultRoot, Action action)
            => UseBackupRoots(defaultRoot, Array.Empty<string>(), action);

        private static void UseBackupRoots(string defaultRoot, string[] customRoots, Action action)
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

        private sealed class TestDirectory : IDisposable
        {
            private TestDirectory(string path)
            {
                Path = path;
            }

            internal string Path { get; }

            internal static TestDirectory Create()
            {
                string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "WinRestoreKitTests",
                    Guid.NewGuid().ToString("N"));
                return new TestDirectory(Directory.CreateDirectory(path).FullName);
            }

            internal string Create(string relativePath)
                => Directory.CreateDirectory(System.IO.Path.Combine(Path, relativePath)).FullName;

            public void Dispose()
            {
                if (Directory.Exists(Path))
                    Directory.Delete(Path, true);
            }
        }
    }
}
