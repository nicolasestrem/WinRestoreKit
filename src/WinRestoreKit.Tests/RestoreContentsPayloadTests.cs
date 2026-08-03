using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using WinRestoreKit;
using Xunit;

namespace WinRestoreKit.Tests
{
    public class RestoreContentsPayloadTests
    {
        private abstract class ProbeModule : BackupBase
        {
            internal List<string> ProbePaths { get; } = new List<string>();
            internal bool SawPayloadArtifact { get; private set; }

            protected ProbeModule(string title)
            {
                Title = title;
            }

            public override bool? HasArtifactIn(string backupPath)
            {
                ProbePaths.Add(backupPath);
                SawPayloadArtifact = File.Exists(Path.Combine(backupPath, "artifact.txt"));
                return SawPayloadArtifact;
            }
        }

        private sealed class SucceededModule : ProbeModule
        {
            internal SucceededModule() : base("Succeeded") { }
        }

        private sealed class SkippedModule : ProbeModule
        {
            internal SkippedModule() : base("Skipped") { }
        }

        private sealed class FailedModule : ProbeModule
        {
            internal FailedModule() : base("Failed") { }
        }

        private sealed class UnlistedProbeModuleOne : ProbeModule
        {
            internal UnlistedProbeModuleOne() : base("One") { }
        }

        private sealed class UnlistedProbeModuleTwo : ProbeModule
        {
            internal UnlistedProbeModuleTwo() : base("Two") { }
        }

        private sealed class ExtractionMonitor : IDisposable
        {
            private readonly ManualResetEventSlim extractionCreated = new ManualResetEventSlim(false);
            private readonly FileSystemWatcher watcher;
            private int extractionCount;

            internal ExtractionMonitor()
            {
                Directory.CreateDirectory(ExtractionParent);
                watcher = new FileSystemWatcher(ExtractionParent, "payload-*")
                {
                    NotifyFilter = NotifyFilters.DirectoryName,
                    IncludeSubdirectories = false,
                    EnableRaisingEvents = true
                };
                watcher.Created += OnCreated;
            }

            internal int ExtractionCount => Volatile.Read(ref extractionCount);

            internal bool WaitForExtraction() => extractionCreated.Wait(TimeSpan.FromSeconds(1));

            public void Dispose()
            {
                watcher.Dispose();
                extractionCreated.Dispose();
            }

            private void OnCreated(object sender, FileSystemEventArgs e)
            {
                Interlocked.Increment(ref extractionCount);
                extractionCreated.Set();
            }
        }

        private static string ExtractionParent => Path.Combine(Path.GetTempPath(), "WinRestoreKit");

        private static ManifestData Manifest(params ManifestModule[] modules)
            => new ManifestData(1, "0.0.1", "now", "machine", "user", "build", modules);

        private static ManifestModule Entry(BackupBase module, string state)
            => new ManifestModule(module.GetType().Name, module.Title, state, "");

        private static string CreateCompressedBackup()
        {
            string root = Path.Combine(Path.GetTempPath(), "WinRestoreKitTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "artifact.txt"), "payload");

            Assert.True(BackupPayload.TryArchive(root, SnapshotCompression.Fast, out string error), error);
            Assert.True(File.Exists(Path.Combine(root, BackupPayload.FileName)));
            Assert.False(File.Exists(Path.Combine(root, "artifact.txt")));
            return root;
        }

        [Fact]
        public void For_CompleteManifest_DoesNotExtractPayloadOrProbeArtifacts()
        {
            string root = CreateCompressedBackup();

            try
            {
                SucceededModule succeeded = new SucceededModule();
                SkippedModule skipped = new SkippedModule();
                FailedModule failed = new FailedModule();

                using (ExtractionMonitor monitor = new ExtractionMonitor())
                {
                    IReadOnlyList<RestoreContentsRow> rows = RestoreContents.For(
                        new BackupBase[] { succeeded, skipped, failed },
                        root,
                        Manifest(
                            Entry(succeeded, BackupManifest.StateSucceeded),
                            Entry(skipped, BackupManifest.StateSkipped),
                            Entry(failed, BackupManifest.StateFailed)));

                    Assert.False(monitor.WaitForExtraction());
                    Assert.Equal(0, monitor.ExtractionCount);
                    Assert.Empty(succeeded.ProbePaths);
                    Assert.Empty(skipped.ProbePaths);
                    Assert.Empty(failed.ProbePaths);
                    Assert.True(rows[0].HasBackup);
                    Assert.False(rows[1].HasBackup);
                    Assert.False(rows[2].HasBackup);
                }
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Fact]
        public void For_NoManifest_ExtractsPayloadWhenArtifactProbeNeedsIt()
        {
            string root = CreateCompressedBackup();

            try
            {
                UnlistedProbeModuleOne module = new UnlistedProbeModuleOne();

                using (ExtractionMonitor monitor = new ExtractionMonitor())
                {
                    IReadOnlyList<RestoreContentsRow> rows = RestoreContents.For(
                        new BackupBase[] { module }, root, null);

                    Assert.True(monitor.WaitForExtraction());
                    Assert.Equal(1, monitor.ExtractionCount);
                    Assert.Single(module.ProbePaths);
                    Assert.NotEqual(root, module.ProbePaths[0]);
                    Assert.True(module.SawPayloadArtifact);
                    Assert.True(rows[0].HasBackup);
                }
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Fact]
        public void For_MixedManifest_ExtractsOneSharedPayloadForUnlistedModules()
        {
            string root = CreateCompressedBackup();

            try
            {
                SucceededModule succeeded = new SucceededModule();
                UnlistedProbeModuleOne first = new UnlistedProbeModuleOne();
                UnlistedProbeModuleTwo second = new UnlistedProbeModuleTwo();

                using (ExtractionMonitor monitor = new ExtractionMonitor())
                {
                    IReadOnlyList<RestoreContentsRow> rows = RestoreContents.For(
                        new BackupBase[] { succeeded, first, second },
                        root,
                        Manifest(Entry(succeeded, BackupManifest.StateSucceeded)));

                    Assert.True(monitor.WaitForExtraction());
                    Assert.Equal(1, monitor.ExtractionCount);
                    Assert.Empty(succeeded.ProbePaths);
                    Assert.Single(first.ProbePaths);
                    Assert.Single(second.ProbePaths);
                    Assert.Equal(first.ProbePaths[0], second.ProbePaths[0]);
                    Assert.True(first.SawPayloadArtifact);
                    Assert.True(second.SawPayloadArtifact);
                    Assert.True(rows[0].HasBackup);
                    Assert.True(rows[1].HasBackup);
                    Assert.True(rows[2].HasBackup);
                }
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }
    }
}
