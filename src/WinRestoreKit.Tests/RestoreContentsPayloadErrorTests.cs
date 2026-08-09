using System;
using System.IO;
using WinRestoreKit;
using Xunit;

namespace WinRestoreKit.Tests
{
    public sealed class RestoreContentsPayloadErrorTests : IDisposable
    {
        private readonly string backupPath = Path.Combine(
            Path.GetTempPath(), "WinRestoreKit-PayloadError-" + Guid.NewGuid().ToString("N"));

        public RestoreContentsPayloadErrorTests()
        {
            Directory.CreateDirectory(backupPath);
            File.WriteAllText(Path.Combine(backupPath, BackupPayload.FileName), "not a zip archive");
        }

        public void Dispose()
        {
            try { Directory.Delete(backupPath, true); } catch { }
        }

        [Fact]
        public void For_CorruptPayload_ReportsThatManifestSilentModuleCouldNotBeRead()
        {
            RestoreContentsRow row = Assert.Single(RestoreContents.For(
                new BackupBase[] { new ManifestSilentModule() }, backupPath, null));

            Assert.False(row.HasBackup);
            Assert.True(row.CouldNotBeRead);
            Assert.Contains("could not be read", row.Warning, StringComparison.OrdinalIgnoreCase);
        }

        private sealed class ManifestSilentModule : BackupBase
        {
            public ManifestSilentModule()
            {
                Title = "Manifest-silent";
            }

            public override bool? HasArtifactIn(string path)
            {
                return false;
            }
        }
    }
}
