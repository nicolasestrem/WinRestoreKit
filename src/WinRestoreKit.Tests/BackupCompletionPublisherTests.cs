using Conf;
using System;
using System.IO;
using System.Linq;
using Xunit;

namespace WinRestoreKit.Tests
{
    public sealed class BackupCompletionPublisherTests
    {
        [Fact]
        public void Publish_RetainedPartialFolder_DoesNotAddSessionFailure()
        {
            using (BackupRunIsolation isolation = new BackupRunIsolation())
            {
                string path = CreateRecognizedPartialFolder(isolation.DestinationRoot);
                BackupRootRegistry.Remember(isolation.DestinationRoot);
                SnapshotEventCatalog catalog = new SnapshotEventCatalog();
                BackupCompletionPublisher publisher = new BackupCompletionPublisher(catalog);

                publisher.Publish(path, "nightly", RunSummary.Incomplete(
                    Array.Empty<ModuleOutcome>(), RunVerb.Backup,
                    "Cancellation was requested. No further group was started."),
                    new DateTime(2026, 8, 9, 9, 0, 0));

                SnapshotEvent[] events = catalog.Read().ToArray();
                SnapshotEvent discovered = Assert.Single(events, snapshot =>
                    string.Equals(snapshot.CanonicalPath, Path.GetFullPath(path),
                        StringComparison.OrdinalIgnoreCase));
                Assert.Equal(SnapshotEventKind.Partial, discovered.Kind);
                Assert.DoesNotContain(events, snapshot =>
                    snapshot.CanonicalPath.StartsWith("session://failure/", StringComparison.Ordinal));
            }
        }

        [Fact]
        public void Publish_NoRetainedRecognizedFolder_RecordsSessionFailureOnly()
        {
            SnapshotEventCatalog catalog = new SnapshotEventCatalog();
            BackupCompletionPublisher publisher = new BackupCompletionPublisher(catalog);
            RunSummary summary = RunSummary.For(Array.Empty<ModuleOutcome>(), false, RunVerb.Backup,
                "the backup folder could not be created: access denied");
            string attemptedPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"),
                "snapshot-attempted-before-rollover");

            publisher.Publish(attemptedPath, "nightly", summary, new DateTime(2026, 8, 9, 9, 0, 0));

            SnapshotEvent failure = Assert.Single(catalog.Read(), snapshot =>
                snapshot.CanonicalPath.StartsWith("session://failure/", StringComparison.Ordinal));
            Assert.Equal("nightly", failure.DisplayName);
            Assert.Contains("could not be created", failure.DiagnosticReason, StringComparison.OrdinalIgnoreCase);
            Assert.False(failure.IsRestorable);
        }

        private static string CreateRecognizedPartialFolder(string root)
        {
            string path = Directory.CreateDirectory(Path.Combine(root, "snapshot-started-before-rollover")).FullName;
            string manifest = BackupManifest.Compose(
                new BackupBase[] { new DMouse() }, Array.Empty<ModuleResult>(),
                new DateTime(2026, 8, 9, 9, 0, 0), "test-machine", "test-user", "test-build", "0.0.0");
            File.WriteAllText(Path.Combine(path, BackupManifest.FileName), manifest);
            return path;
        }
    }
}
