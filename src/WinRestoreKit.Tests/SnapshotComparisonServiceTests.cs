using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace WinRestoreKit.Tests
{
    public sealed class SnapshotComparisonServiceTests
    {
        [Theory]
        [InlineData(true, ComparisonState.Changed)]
        [InlineData(false, ComparisonState.Same)]
        public async Task CompareAsync_ManifestSucceeded_MapsDriftAfterArtifactProbe(
            bool drifted, ComparisonState expected)
        {
            using (TestDirectory backup = TestDirectory.Create())
            {
                ProbeModule module = new ProbeModule("Display", artifact: true, drifted: drifted);
                SnapshotEvent snapshot = Snapshot(backup.Path, Manifest(Entry(module, BackupManifest.StateSucceeded)));

                ModuleComparison row = Assert.Single(await new SnapshotComparisonService()
                    .CompareAsync(snapshot, new[] { (BackupBase)module }, CancellationToken.None));

                Assert.Equal(expected, row.State);
                Assert.True(row.HasUsableArtifact);
                Assert.True(row.CanRestore);
                Assert.Equal(1, module.ArtifactProbeCount);
            }
        }

        [Fact]
        public async Task CompareAsync_ManifestSucceededButArtifactMissing_IsNotRestorable()
        {
            using (TestDirectory backup = TestDirectory.Create())
            {
                ProbeModule module = new ProbeModule("Missing", artifact: false, drifted: true);

                ModuleComparison row = Assert.Single(await new SnapshotComparisonService().CompareAsync(
                    Snapshot(backup.Path, Manifest(Entry(module, BackupManifest.StateSucceeded))),
                    new[] { (BackupBase)module }, CancellationToken.None));

                Assert.Equal(ComparisonState.NotCaptured, row.State);
                Assert.False(row.HasUsableArtifact);
                Assert.False(row.CanRestore);
                Assert.Contains("artifact is missing", row.ArtifactSummary);
                Assert.Equal(1, module.ArtifactProbeCount);
                Assert.Equal(0, module.DriftProbeCount);
            }
        }

        [Theory]
        [InlineData(BackupManifest.StateSkipped)]
        [InlineData(BackupManifest.StateFailed)]
        public async Task CompareAsync_ManifestStatesWithoutArtifact_AreNotCaptured(string state)
        {
            using (TestDirectory backup = TestDirectory.Create())
            {
                ProbeModule module = new ProbeModule("Fonts", artifact: true, drifted: true);

                ModuleComparison row = Assert.Single(await new SnapshotComparisonService().CompareAsync(
                    Snapshot(backup.Path, Manifest(Entry(module, state))), new[] { (BackupBase)module },
                    CancellationToken.None));

                Assert.Equal(ComparisonState.NotCaptured, row.State);
                Assert.False(row.HasUsableArtifact);
                Assert.Equal(0, module.ArtifactProbeCount);
                Assert.Equal(0, module.DriftProbeCount);
            }
        }

        [Fact]
        public async Task CompareAsync_ManifestSilentIndeterminateArtifact_IsNotCaptured()
        {
            using (TestDirectory backup = TestDirectory.Create())
            {
                ProbeModule module = new ProbeModule("Terminal", artifact: null, drifted: false);

                ModuleComparison row = Assert.Single(await new SnapshotComparisonService().CompareAsync(
                    Snapshot(backup.Path, Manifest(EntryForDifferentModule())), new[] { (BackupBase)module },
                    CancellationToken.None));

                Assert.Equal(ComparisonState.NotCaptured, row.State);
                Assert.False(row.HasUsableArtifact);
                Assert.Equal(0, module.DriftProbeCount);
            }
        }

        [Fact]
        public async Task CompareAsync_NoManifestIndeterminateArtifact_UsesRestoreContentsFallback()
        {
            using (TestDirectory backup = TestDirectory.Create())
            {
                ProbeModule module = new ProbeModule("Legacy", artifact: null, drifted: false);

                ModuleComparison row = Assert.Single(await new SnapshotComparisonService().CompareAsync(
                    Snapshot(backup.Path, manifest: null), new[] { (BackupBase)module }, CancellationToken.None));

                Assert.Equal(ComparisonState.Same, row.State);
                Assert.True(row.HasUsableArtifact);
                Assert.Equal(1, module.DriftProbeCount);
            }
        }

        [Fact]
        public async Task CompareAsync_ProvenMissingArtifact_IsNotCapturedWithoutDriftProbe()
        {
            using (TestDirectory backup = TestDirectory.Create())
            {
                ProbeModule module = new ProbeModule("Missing", artifact: false, drifted: true);

                ModuleComparison row = Assert.Single(await new SnapshotComparisonService().CompareAsync(
                    Snapshot(backup.Path, manifest: null), new[] { (BackupBase)module }, CancellationToken.None));

                Assert.Equal(ComparisonState.NotCaptured, row.State);
                Assert.False(row.HasUsableArtifact);
                Assert.Equal(0, module.DriftProbeCount);
            }
        }

        [Fact]
        public async Task CompareAsync_ArtifactProbeException_IsUnavailableWithoutDriftProbe()
        {
            using (TestDirectory backup = TestDirectory.Create())
            {
                CreateCompressedBackupWithArtifact(backup.Path);
                HashSet<string> payloadDirectoriesBefore = PayloadExtractionDirectories();
                ProbeModule module = new ProbeModule("Broken artifact", artifact: true, drifted: true)
                {
                    ThrowOnArtifact = true
                };

                ModuleComparison row = Assert.Single(await new SnapshotComparisonService().CompareAsync(
                    Snapshot(backup.Path, manifest: null), new[] { (BackupBase)module }, CancellationToken.None));

                Assert.Equal(ComparisonState.Unavailable, row.State);
                Assert.False(row.HasUsableArtifact);
                Assert.Equal(0, module.DriftProbeCount);
                Assert.Empty(PayloadExtractionDirectories().Except(payloadDirectoriesBefore));
            }
        }

        [Fact]
        public async Task CompareAsync_IndeterminateDrift_RemainsRestorableWithUsableArtifact()
        {
            using (TestDirectory backup = TestDirectory.Create())
            {
                ProbeModule module = new ProbeModule("Unknown", artifact: true, drifted: null);

                ModuleComparison row = Assert.Single(await new SnapshotComparisonService().CompareAsync(
                    Snapshot(backup.Path, manifest: null), new[] { (BackupBase)module }, CancellationToken.None));

                Assert.Equal(ComparisonState.Unavailable, row.State);
                Assert.True(row.HasUsableArtifact);
                Assert.True(row.CanRestore);
            }
        }

        [Fact]
        public async Task CompareAsync_ManifestLookupUsesExactClrTypeName()
        {
            using (TestDirectory backup = TestDirectory.Create())
            {
                ProbeModule module = new ProbeModule("Exact", artifact: false, drifted: true);
                ManifestModule lowerCaseEntry = new ManifestModule(module.GetType().Name.ToLowerInvariant(),
                    module.Title, BackupManifest.StateSucceeded, string.Empty);

                ModuleComparison row = Assert.Single(await new SnapshotComparisonService().CompareAsync(
                    Snapshot(backup.Path, Manifest(lowerCaseEntry)), new[] { (BackupBase)module },
                    CancellationToken.None));

                Assert.Equal(ComparisonState.NotCaptured, row.State);
                Assert.Equal(1, module.ArtifactProbeCount);
                Assert.Equal(0, module.DriftProbeCount);
            }
        }

        [Fact]
        public async Task CompareAsync_PayloadPreparationFailure_PreservesManifestAbsenceAndReason()
        {
            using (TestDirectory backup = TestDirectory.Create())
            {
                ProbeModule skipped = new ProbeModule("Skipped", artifact: true, drifted: true);
                AlternateProbeModule unavailable = new AlternateProbeModule("Unavailable", artifact: true, drifted: true);
                SnapshotEvent snapshot = Snapshot(backup.Path, Manifest(
                    Entry(skipped, BackupManifest.StateSkipped)));
                SnapshotPayloadPreparation preparation = new SnapshotPayloadPreparation(snapshot, null,
                    "The selected payload is corrupt.");
                List<ComparisonProgress> progress = new List<ComparisonProgress>();

                IReadOnlyList<ModuleComparison> rows = await new SnapshotComparisonService().CompareAsync(
                    preparation, new[] { (BackupBase)skipped, unavailable }, CancellationToken.None,
                    new RecordingProgress(progress));
                preparation.Dispose();

                Assert.Collection(rows,
                    row => Assert.Equal(ComparisonState.NotCaptured, row.State),
                    row =>
                    {
                        Assert.Equal(ComparisonState.Unavailable, row.State);
                        Assert.False(row.HasUsableArtifact);
                        Assert.Equal("The selected payload is corrupt.", row.Reason);
                    });
                Assert.Equal(new[] { 0, 1 }, progress.Select(item => item.Ordinal));
                Assert.Equal(0, skipped.ArtifactProbeCount + unavailable.ArtifactProbeCount);
                Assert.Equal(0, skipped.DriftProbeCount + unavailable.DriftProbeCount);
            }
        }

        [Fact]
        public async Task CompareAsync_OneThrowingModule_DoesNotAbortLaterCatalogRows()
        {
            ProbeModule broken = new ProbeModule("Broken", artifact: true, drifted: null) { ThrowOnDrift = true };
            ProbeModule same = new ProbeModule("Same", artifact: true, drifted: false);

            IReadOnlyList<ModuleComparison> rows = await Compare(broken, same);

            Assert.Collection(rows,
                row => Assert.Equal(ComparisonState.Unavailable, row.State),
                row => Assert.Equal(ComparisonState.Same, row.State));
        }

        [Fact]
        public async Task CompareAsync_BoundsProbeConcurrencyAndReturnsCatalogOrder()
        {
            ConcurrentProbeModule.Reset();
            ConcurrentProbeModule[] modules = Enumerable.Range(0, 9)
                .Select(index => new ConcurrentProbeModule("Module " + index)).ToArray();

            IReadOnlyList<ModuleComparison> rows = await Compare(modules);

            Assert.True(ConcurrentProbeModule.MaximumObserved <= 4);
            Assert.Equal(modules.Select(module => module.Title), rows.Select(row => row.Module.Title));
        }

        [Fact]
        public async Task CompareAsync_CancellationWaitsForWorkersThenDeletesCompressedExtraction()
        {
            using (TestDirectory backup = TestDirectory.Create())
            using (CancellationTokenSource cancellation = new CancellationTokenSource())
            {
                CreateCompressedBackupWithArtifact(backup.Path);
                HashSet<string> payloadDirectoriesBefore = PayloadExtractionDirectories();
                BlockingProbeModule module = new BlockingProbeModule("Blocking");
                SnapshotComparisonService service = new SnapshotComparisonService();

                try
                {
                    Task<IReadOnlyList<ModuleComparison>> task = service.CompareAsync(
                        Snapshot(backup.Path, manifest: null), new[] { (BackupBase)module }, cancellation.Token);
                    await module.Started.Task;
                    cancellation.Cancel();
                    module.Release.Set();

                    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
                    Assert.Empty(PayloadExtractionDirectories().Except(payloadDirectoriesBefore));
                }
                finally
                {
                    module.Release.Dispose();
                }
            }
        }

        [Fact]
        public async Task CompareAsync_SuccessDisposesCompressedExtraction()
        {
            using (TestDirectory backup = TestDirectory.Create())
            {
                CreateCompressedBackupWithArtifact(backup.Path);
                HashSet<string> payloadDirectoriesBefore = PayloadExtractionDirectories();

                ModuleComparison row = Assert.Single(await new SnapshotComparisonService().CompareAsync(
                    Snapshot(backup.Path, manifest: null),
                    new[] { (BackupBase)new ProbeModule("Compressed", artifact: true, drifted: false) },
                    CancellationToken.None));

                Assert.Equal(ComparisonState.Same, row.State);
                Assert.Empty(PayloadExtractionDirectories().Except(payloadDirectoriesBefore));
            }
        }

        private static async Task<IReadOnlyList<ModuleComparison>> Compare(params BackupBase[] modules)
        {
            using (TestDirectory backup = TestDirectory.Create())
            {
                return await new SnapshotComparisonService().CompareAsync(
                    Snapshot(backup.Path, manifest: null), modules, CancellationToken.None);
            }
        }

        private static SnapshotEvent Snapshot(string path, ManifestData manifest)
            => new SnapshotEvent(SnapshotEventKind.Verified, DateTime.UtcNow, "snapshot", path,
                string.Empty, "TEST-PC", 0, true, manifest);

        private static ManifestData Manifest(params ManifestModule[] modules)
            => new ManifestData(BackupManifest.Version, "test", DateTime.UtcNow.ToString("O"), "TEST-PC",
                "test-user", "test-os", modules);

        private static ManifestModule Entry(BackupBase module, string state)
            => new ManifestModule(module.GetType().Name, module.Title, state, "test reason");

        private static ManifestModule EntryForDifferentModule()
            => new ManifestModule("DifferentModule", "Different", BackupManifest.StateSucceeded, string.Empty);

        private static void CreateCompressedBackupWithArtifact(string backupPath)
        {
            using (ZipArchive archive = ZipFile.Open(Path.Combine(backupPath, BackupPayload.FileName),
                ZipArchiveMode.Create))
            using (StreamWriter writer = new StreamWriter(archive.CreateEntry("artifact.txt").Open()))
                writer.Write("captured");
        }
        private static HashSet<string> PayloadExtractionDirectories()
        {
            string root = Path.Combine(Path.GetTempPath(), "WinRestoreKit");
            return !Directory.Exists(root)
                ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                : Directory.EnumerateDirectories(root, "payload-*", SearchOption.TopDirectoryOnly)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }


        private class ProbeModule : BackupBase
        {
            private readonly bool? artifact;
            private readonly bool? drifted;

            internal ProbeModule(string title, bool? artifact, bool? drifted)
            {
                Title = title;
                this.artifact = artifact;
                this.drifted = drifted;
            }

            internal int ArtifactProbeCount { get; private set; }
            internal int DriftProbeCount { get; private set; }
            internal bool ThrowOnArtifact { get; set; }
            internal bool ThrowOnDrift { get; set; }

            public override bool? HasArtifactIn(string backupPath)
            {
                ArtifactProbeCount++;
                if (ThrowOnArtifact)
                    throw new IOException("artifact probe failed");
                return artifact;
            }

            public override bool? HasDriftedFrom(string backupPath)
            {
                DriftProbeCount++;
                if (ThrowOnDrift)
                    throw new IOException("drift probe failed");
                return drifted;
            }
        }
        private sealed class AlternateProbeModule : ProbeModule
        {
            internal AlternateProbeModule(string title, bool? artifact, bool? drifted)
                : base(title, artifact, drifted)
            {
            }
        }


        private sealed class ConcurrentProbeModule : ProbeModule
        {
            private static int active;
            private static int maximumObserved;

            internal ConcurrentProbeModule(string title)
                : base(title, artifact: true, drifted: false)
            {
            }

            internal static int MaximumObserved => Volatile.Read(ref maximumObserved);

            internal static void Reset()
            {
                Interlocked.Exchange(ref active, 0);
                Interlocked.Exchange(ref maximumObserved, 0);
            }

            public override bool? HasDriftedFrom(string backupPath)
            {
                int observed = Interlocked.Increment(ref active);
                SetMaximum(observed);
                try
                {
                    Thread.Sleep(50);
                    return false;
                }
                finally
                {
                    Interlocked.Decrement(ref active);
                }
            }


            private static void SetMaximum(int observed)
            {
                int current;
                while ((current = Volatile.Read(ref maximumObserved)) < observed
                    && Interlocked.CompareExchange(ref maximumObserved, observed, current) != current)
                {
                }
            }
        }
        private sealed class RecordingProgress : IProgress<ComparisonProgress>
        {
            private readonly ICollection<ComparisonProgress> items;

            internal RecordingProgress(ICollection<ComparisonProgress> items)
            {
                this.items = items;
            }

            public void Report(ComparisonProgress value)
            {
                items.Add(value);
            }
        }


        private sealed class BlockingProbeModule : ProbeModule
        {
            internal BlockingProbeModule(string title)
                : base(title, artifact: true, drifted: false)
            {
            }

            internal TaskCompletionSource<object> Started { get; }
                = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);

            internal ManualResetEventSlim Release { get; } = new ManualResetEventSlim(false);

            public override bool? HasDriftedFrom(string backupPath)
            {
                Started.TrySetResult(null);
                Release.Wait();
                return false;
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

            public void Dispose()
            {
                if (Directory.Exists(Path))
                    Directory.Delete(Path, true);
            }
        }
    }
}
