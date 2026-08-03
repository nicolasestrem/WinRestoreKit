using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using Xunit;
namespace WinRestoreKit.Tests
{
    public class BackupPayloadTests
    {
        [Fact]
        public void TryArchiveAndPrepareForRead_RoundTripsModuleFilesWhileKeepingMetadataAtRoot()
        {
            string root = Path.Combine(Path.GetTempPath(), "WinRestoreKitTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);

            try
            {
                File.WriteAllText(Path.Combine(root, "backup_manifest.json"), "manifest");
                File.WriteAllText(Path.Combine(root, "backup_log.txt"), "log");
                Directory.CreateDirectory(Path.Combine(root, "Module"));
                Directory.CreateDirectory(Path.Combine(root, "Empty"));
                File.WriteAllText(Path.Combine(root, "Module", "settings.json"), "payload");

                bool archived = BackupPayload.TryArchive(root, SnapshotCompression.Fast, out string error);

                Assert.True(archived, error);
                Assert.True(File.Exists(Path.Combine(root, BackupPayload.FileName)));
                Assert.True(File.Exists(Path.Combine(root, "backup_manifest.json")));
                Assert.True(File.Exists(Path.Combine(root, "backup_log.txt")));
                Assert.False(File.Exists(Path.Combine(root, "Module", "settings.json")));

                Assert.True(BackupPayload.TryPrepareForRead(root, out BackupPayload.ReadScope payload, out error), error);
                using (payload)
                {
                    Assert.NotEqual(root, payload.Path);
                    Assert.Equal("payload", File.ReadAllText(Path.Combine(payload.Path, "Module", "settings.json")));
                    Assert.True(Directory.Exists(Path.Combine(payload.Path, "Empty")));
                }
            }
            finally
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, true);
            }
        }

        [Fact]
        public void TryArchive_WhenPayloadDestinationCannotBeReplaced_RemovesTemporaryPayload()
        {
            string root = Path.Combine(Path.GetTempPath(), "WinRestoreKitTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);

            try
            {
                File.WriteAllText(Path.Combine(root, "artifact.reg"), "payload");
                Directory.CreateDirectory(Path.Combine(root, BackupPayload.FileName));

                Assert.False(BackupPayload.TryArchive(root, SnapshotCompression.Fast, out _));

                Assert.Empty(Directory.EnumerateFiles(root, ".payload-*.tmp", SearchOption.TopDirectoryOnly));
            }
            finally
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, true);
            }
        }

        [Fact]
        public void TryPrepareForRead_WhenManifestDeclaresMissingPayload_ReturnsFailureWithoutReadScope()
        {
            string root = CreateRoot();

            try
            {
                WriteManifest(root, SnapshotCompression.Fast, BackupPayload.FileName);

                bool prepared = BackupPayload.TryPrepareForRead(root, out BackupPayload.ReadScope payload, out string error);

                Assert.False(prepared);
                Assert.Null(payload);
                Assert.Contains("payload", error, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("missing", error, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                DeleteRoot(root);
            }
        }

        [Fact]
        public void TryPrepareForRead_WhenNoManifestOrPayloadExists_UsesLooseBackupRoot()
        {
            string root = CreateRoot();

            try
            {
                Assert.True(BackupPayload.TryPrepareForRead(root, out BackupPayload.ReadScope payload, out string error), error);
                using (payload)
                {
                    Assert.Equal(root, payload.Path);
                }
            }
            finally
            {
                DeleteRoot(root);
            }
        }

        [Fact]
        public void TryPrepareForRead_WhenManifestDeclaresNoPayload_UsesLooseBackupRoot()
        {
            string root = CreateRoot();

            try
            {
                WriteManifest(root, SnapshotCompression.None, null);

                Assert.True(BackupPayload.TryPrepareForRead(root, out BackupPayload.ReadScope payload, out string error), error);
                using (payload)
                {
                    Assert.Equal(root, payload.Path);
                }
            }
            finally
            {
                DeleteRoot(root);
            }
        }

        [Fact]
        public void TryPrepareForRead_WhenDeclaredPayloadExists_ExtractsPayload()
        {
            string root = CreateRoot();

            try
            {
                WriteManifest(root, SnapshotCompression.Fast, BackupPayload.FileName);

                using (ZipArchive archive = ZipFile.Open(Path.Combine(root, BackupPayload.FileName), ZipArchiveMode.Create))
                using (StreamWriter writer = new StreamWriter(archive.CreateEntry("module/settings.json").Open()))
                    writer.Write("payload");

                Assert.True(BackupPayload.TryPrepareForRead(root, out BackupPayload.ReadScope payload, out string error), error);
                using (payload)
                {
                    Assert.NotEqual(root, payload.Path);
                    Assert.Equal("payload", File.ReadAllText(Path.Combine(payload.Path, "module", "settings.json")));
                }
            }
            finally
            {
                DeleteRoot(root);
            }
        }

        [Fact]
        public void TryPrepareForRead_WhenManifestIsUnreadable_FallsBackToLooseRootWithoutThrowing()
        {
            string root = CreateRoot();

            try
            {
                WriteManifest(root, SnapshotCompression.Fast, BackupPayload.FileName);

                // Hold the manifest open with no sharing, standing in for any read that throws:
                // a sharing violation from another process, an ACL denial, or a TOCTOU delete
                // between the existence check and the read. The reader must not throw into callers
                // that build the wizard page with no catch of their own.
                using (File.Open(Path.Combine(root, BackupManifest.FileName),
                    FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    bool prepared = BackupPayload.TryPrepareForRead(root,
                        out BackupPayload.ReadScope payload, out string error);

                    Assert.True(prepared, error);
                    using (payload)
                    {
                        Assert.Equal(root, payload.Path);
                    }
                }
            }
            finally
            {
                DeleteRoot(root);
            }
        }

        private static string CreateRoot()
        {
            string root = Path.Combine(Path.GetTempPath(), "WinRestoreKitTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return root;
        }

        private static void WriteManifest(string root, SnapshotCompression compression, string payloadFile)
        {
            string manifest = BackupManifest.Compose(
                new List<BackupBase>(),
                new List<ModuleResult>(),
                DateTime.UtcNow,
                "machine",
                "user",
                "os",
                "0.0.1",
                compression: compression,
                payloadFile: payloadFile);

            File.WriteAllText(Path.Combine(root, BackupManifest.FileName), manifest);
        }

        private static void DeleteRoot(string root)
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }
}
