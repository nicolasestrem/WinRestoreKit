using System;
using System.IO;
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
    }
}
