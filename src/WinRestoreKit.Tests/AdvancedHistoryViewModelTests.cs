using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WinRestoreKit;
using WinRestoreKit.Wpf.Navigation;
using WinRestoreKit.Wpf.ViewModels.History;
using WinRestoreKit.Wpf.ViewModels.Snapshots;
using WinRestoreKit.Wpf.ViewModels.Timeline;
using Xunit;

namespace WinRestoreKit.Tests
{
    public sealed class AdvancedHistoryViewModelTests
    {
        [Fact]
        public async Task SearchText_FiltersTheSharedProjectionByDisplayMachinePathAndStatus()
        {
            await WpfTestHost.RunAsync(async () =>
            {
                SnapshotEvent verified = NewEvent(SnapshotEventKind.Verified, @"C:\history\alpha", "ALPHA-PC");
                SnapshotEvent partial = NewEvent(SnapshotEventKind.Partial, @"C:\history\bravo", "BRAVO-PC");
                SnapshotEvent failed = NewEvent(SnapshotEventKind.Failed, @"C:\history\charlie", "CHARLIE-PC", "disk full");
                FakeCatalog catalog = new FakeCatalog(verified, partial, failed);
                AdvancedHistoryViewModel history = new AdvancedHistoryViewModel(catalog);

                await history.RefreshAsync();

                AssertVisible(history, verified, "alpha");
                AssertVisible(history, partial, "BRAVO-PC");
                AssertVisible(history, partial, "history\\bravo");
                AssertVisible(history, partial, "partial snapshot");
            });
        }

        [Fact]
        public async Task RefreshAsync_ReusesTheSameStableStatusProjectionAsTimeline()
        {
            await WpfTestHost.RunAsync(async () =>
            {
                SnapshotEvent failed = NewEvent(SnapshotEventKind.Failed, @"C:\history\failed", "TEST-PC", "disk full");
                FakeCatalog catalog = new FakeCatalog(failed);
                TimelineViewModel timeline = new TimelineViewModel(
                    catalog, new FakePreparationService(), new RecordingNavigator());
                AdvancedHistoryViewModel history = new AdvancedHistoryViewModel(catalog);

                await timeline.RefreshAsync();
                await history.RefreshAsync();

                SnapshotEventViewModel timelineEvent = Assert.Single(timeline.Events);
                SnapshotEventViewModel historyEvent = Assert.Single(history.Events.Cast<SnapshotEventViewModel>());
                Assert.Same(timelineEvent.Status, historyEvent.Status);
                Assert.Equal("Backup failed", historyEvent.Status.Label);
                Assert.Equal("disk full", historyEvent.DiagnosticReason);
            });
        }

        [Fact]
        public async Task RefreshAsync_ReadsTheCatalogAwayFromTheUiThread()
        {
            await WpfTestHost.RunAsync(async () =>
            {
                int uiThreadId = Thread.CurrentThread.ManagedThreadId;
                FakeCatalog catalog = new FakeCatalog(
                    NewEvent(SnapshotEventKind.Verified, @"C:\history\alpha", "ALPHA-PC"));
                AdvancedHistoryViewModel history = new AdvancedHistoryViewModel(catalog);

                await history.RefreshAsync();

                Assert.NotEqual(uiThreadId, catalog.ReadThreadId);
                Assert.Single(history.Events.Cast<SnapshotEventViewModel>());
            });
        }

        private static void AssertVisible(AdvancedHistoryViewModel history, SnapshotEvent expected, string search)
        {
            history.SearchText = search;
            SnapshotEventViewModel visible = Assert.Single(history.Events.Cast<SnapshotEventViewModel>());
            Assert.Same(expected, visible.Event);
        }

        private static SnapshotEvent NewEvent(SnapshotEventKind kind, string path, string machine, string reason = null)
            => new SnapshotEvent(kind, new DateTime(2026, 8, 9, 12, 0, 0, DateTimeKind.Local),
                Path.GetFileName(path), Path.GetFullPath(path), reason, machine, 1024, true, null);

        private sealed class FakeCatalog : ISnapshotEventReader
        {
            private readonly IReadOnlyList<SnapshotEvent> events;

            internal FakeCatalog(params SnapshotEvent[] events) => this.events = events;

            internal int ReadThreadId { get; private set; }

            public IReadOnlyList<SnapshotEvent> Read()
            {
                ReadThreadId = Thread.CurrentThread.ManagedThreadId;
                return events;
            }
        }

        private sealed class FakePreparationService : ISnapshotPayloadPreparationService
        {
            public Task<SnapshotPayloadPreparation> PrepareAsync(SnapshotEvent snapshot,
                CancellationToken cancellationToken)
                => Task.FromResult(new SnapshotPayloadPreparation(snapshot, null, "unexpected preparation"));
        }

        private sealed class RecordingNavigator : ITimelineNavigator
        {
            public void OpenCompare(SnapshotPayloadPreparation preparation) => preparation.Dispose();

            public void ShowSnapshotDiagnostic(SnapshotEvent snapshot) { }
        }
    }
}
