using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using WinRestoreKit;
using WinRestoreKit.Wpf.Navigation;
using WinRestoreKit.Wpf.ViewModels.Snapshots;
using WinRestoreKit.Wpf.ViewModels.Timeline;
using Xunit;

namespace WinRestoreKit.Tests
{
    public sealed class TimelineViewModelTests
    {
        [Fact]
        public async Task OpenSelectedAsync_PreparesPartialAndTransfersOwnershipToNavigator()
        {
            SnapshotEvent partial = NewEvent(SnapshotEventKind.Partial, @"C:\snapshot");
            RecordingNavigator navigator = new RecordingNavigator();
            TimelineViewModel viewModel = new TimelineViewModel(
                new FakeCatalog(partial), new FakePreparationService(partial), navigator);

            await viewModel.RefreshAsync();
            viewModel.SelectedEvent = Assert.Single(viewModel.Events);
            await viewModel.OpenSelectedAsync();

            Assert.Same(partial, navigator.Prepared.Snapshot);
            Assert.Null(navigator.Diagnostic);
            navigator.Prepared.Dispose();
        }

        [Fact]
        public async Task OpenSelectedAsync_ShowsFailedEvidenceWithoutPreparingPayload()
        {
            SnapshotEvent failed = NewEvent(SnapshotEventKind.Failed, @"C:\failed", "disk full");
            FakePreparationService service = new FakePreparationService();
            RecordingNavigator navigator = new RecordingNavigator();
            TimelineViewModel viewModel = new TimelineViewModel(new FakeCatalog(failed), service, navigator);

            await viewModel.RefreshAsync();
            viewModel.SelectedEvent = Assert.Single(viewModel.Events);
            await viewModel.OpenSelectedAsync();

            Assert.Same(failed, navigator.Diagnostic);
            Assert.Equal(0, service.Calls);
        }

        [Fact]
        public async Task OpenSelectedAsync_ReportsPreparationFailureInline()
        {
            SnapshotEvent verified = NewEvent(SnapshotEventKind.Verified, @"C:\verified");
            TimelineViewModel viewModel = new TimelineViewModel(
                new FakeCatalog(verified), new FakePreparationService(), new RecordingNavigator());

            await viewModel.RefreshAsync();
            viewModel.SelectedEvent = Assert.Single(viewModel.Events);
            await viewModel.OpenSelectedAsync();

            Assert.True(viewModel.HasSelectionError);
            Assert.Equal("unexpected preparation", viewModel.SelectionError);
        }

        [Theory]
        [InlineData(SnapshotEventKind.Verified, "Verified", false)]
        [InlineData(SnapshotEventKind.Partial, "Partial snapshot", false)]
        [InlineData(SnapshotEventKind.Failed, "Backup failed", true)]
        [InlineData(SnapshotEventKind.Unreadable, "Details unavailable", true)]
        public void SnapshotEventViewModel_MapsEveryKindToItsStableStatus(
            SnapshotEventKind kind, string label, bool isDiagnosticOnly)
        {
            SnapshotEventViewModel viewModel = new SnapshotEventViewModel(NewEvent(kind, @"C:\status", "details"));

            Assert.Equal(label, viewModel.Status.Label);
            Assert.Equal(isDiagnosticOnly, viewModel.Status.IsDiagnosticOnly);
        }

        private static SnapshotEvent NewEvent(SnapshotEventKind kind, string path, string reason = null)
            => new SnapshotEvent(kind, new DateTime(2026, 8, 9, 12, 0, 0, DateTimeKind.Local),
                Path.GetFileName(path), Path.GetFullPath(path), reason, "TEST-PC", 0, true, null);

        private sealed class FakeCatalog : ISnapshotEventReader
        {
            private readonly IReadOnlyList<SnapshotEvent> events;

            internal FakeCatalog(params SnapshotEvent[] events) => this.events = events;

            public IReadOnlyList<SnapshotEvent> Read() => events;
        }

        private sealed class FakePreparationService : ISnapshotPayloadPreparationService
        {
            private readonly SnapshotEvent preparedEvent;

            internal FakePreparationService(SnapshotEvent preparedEvent = null) => this.preparedEvent = preparedEvent;

            internal int Calls { get; private set; }

            public Task<SnapshotPayloadPreparation> PrepareAsync(SnapshotEvent snapshot,
                CancellationToken cancellationToken)
            {
                Calls++;
                return Task.FromResult(new SnapshotPayloadPreparation(
                    preparedEvent ?? snapshot, null, preparedEvent == null ? "unexpected preparation" : null));
            }
        }

        private sealed class RecordingNavigator : ITimelineNavigator
        {
            internal SnapshotPayloadPreparation Prepared { get; private set; }

            internal SnapshotEvent Diagnostic { get; private set; }

            public void OpenCompare(SnapshotPayloadPreparation preparation) => Prepared = preparation;

            public void ShowSnapshotDiagnostic(SnapshotEvent snapshot) => Diagnostic = snapshot;
        }
    }
}
