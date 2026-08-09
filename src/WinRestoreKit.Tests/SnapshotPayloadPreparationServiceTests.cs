using System;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace WinRestoreKit.Tests
{
    public sealed class SnapshotPayloadPreparationServiceTests
    {
        [Fact]
        public async Task PrepareAsync_CompressedSnapshotDeletesPrivateExtractionWhenDisposed()
        {
            using (TestDirectory backup = TestDirectory.Create())
            {
                CreateCompressedPayload(backup.Path, "registry/mouse.reg", "Windows Registry Editor Version 5.00");
                SnapshotEvent snapshot = NewEvent(SnapshotEventKind.Verified, backup.Path);
                SnapshotPayloadPreparationService service = new SnapshotPayloadPreparationService();

                SnapshotPayloadPreparation prepared = await service.PrepareAsync(snapshot, CancellationToken.None);
                string extractedPath = prepared.Path;

                Assert.True(prepared.IsPrepared);
                Assert.True(File.Exists(Path.Combine(extractedPath, "registry", "mouse.reg")));
                prepared.Dispose();
                Assert.False(Directory.Exists(extractedPath));
                prepared.Dispose();
            }
        }

        [Fact]
        public async Task PrepareAsync_LooseSnapshotKeepsBackupPathAndDoesNotOwnIt()
        {
            using (TestDirectory backup = TestDirectory.Create())
            {
                SnapshotPayloadPreparation prepared = await new SnapshotPayloadPreparationService().PrepareAsync(
                    NewEvent(SnapshotEventKind.Partial, backup.Path), CancellationToken.None);

                Assert.True(prepared.IsPrepared);
                Assert.Equal(backup.Path, prepared.Path);
                prepared.Dispose();
                Assert.True(Directory.Exists(backup.Path));
            }
        }

        [Fact]
        public async Task PrepareAsync_CorruptArchiveReturnsCoreDiagnosticWithoutScope()
        {
            using (TestDirectory backup = TestDirectory.Create())
            {
                File.WriteAllText(Path.Combine(backup.Path, BackupPayload.FileName), "not a zip archive");

                SnapshotPayloadPreparation prepared = await new SnapshotPayloadPreparationService().PrepareAsync(
                    NewEvent(SnapshotEventKind.Verified, backup.Path), CancellationToken.None);

                Assert.False(prepared.IsPrepared);
                Assert.Null(prepared.Path);
                Assert.Contains("could not be prepared", prepared.Error, StringComparison.OrdinalIgnoreCase);
                Assert.NotEmpty(prepared.Error);
            }
        }

        [Fact]
        public async Task PrepareAsync_CancellationBeforeExtractionDoesNotOpenPayload()
        {
            using (TestDirectory backup = TestDirectory.Create())
            {
                CreateCompressedPayload(backup.Path, "registry/mouse.reg", "payload");
                using (CancellationTokenSource cancellation = new CancellationTokenSource())
                {
                    cancellation.Cancel();

                    await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                        new SnapshotPayloadPreparationService().PrepareAsync(
                            NewEvent(SnapshotEventKind.Verified, backup.Path), cancellation.Token));
                }
            }
        }

        [Fact]
        public async Task PrepareAsync_FailedSnapshotReturnsDiagnosticWithoutOpeningPayload()
        {
            SnapshotPayloadPreparation prepared = await new SnapshotPayloadPreparationService().PrepareAsync(
                NewEvent(SnapshotEventKind.Failed, @"C:\retained-failure"), CancellationToken.None);

            Assert.False(prepared.IsPrepared);
            Assert.Contains("cannot be selected", prepared.Error, StringComparison.OrdinalIgnoreCase);
            Assert.Null(prepared.Path);
        }

        [Fact]
        public async Task PrepareAsync_NullSnapshotThrows()
        {
            await Assert.ThrowsAsync<ArgumentNullException>(() =>
                new SnapshotPayloadPreparationService().PrepareAsync(null, CancellationToken.None));
        }

        private static SnapshotEvent NewEvent(SnapshotEventKind kind, string path)
            => new SnapshotEvent(kind, DateTime.UtcNow, "snapshot", path, string.Empty, string.Empty, 0, true, null);

        private static void CreateCompressedPayload(string root, string entryName, string contents)
        {
            using (ZipArchive archive = ZipFile.Open(Path.Combine(root, BackupPayload.FileName), ZipArchiveMode.Create))
            using (StreamWriter writer = new StreamWriter(archive.CreateEntry(entryName).Open()))
                writer.Write(contents);
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

            public void Dispose()
            {
                if (Directory.Exists(Path))
                    Directory.Delete(Path, true);
            }
        }
    }
}
