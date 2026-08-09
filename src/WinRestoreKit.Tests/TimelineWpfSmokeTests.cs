using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WinRestoreKit;
using WinRestoreKit.Wpf.Navigation;
using WinRestoreKit.Wpf.ViewModels.Timeline;
using WinRestoreKit.Wpf.Views;
using Xunit;

namespace WinRestoreKit.Tests
{
    public sealed class TimelineWpfSmokeTests
    {
        [Fact]
        public async Task TimelineView_LoadsSelectionAndTransfersPreparedSnapshot()
        {
            await WpfTestHost.RunAsync(async () =>
            {
                RecordingNavigator navigator = new RecordingNavigator();
                SnapshotEvent snapshot = NewEvent(SnapshotEventKind.Verified, @"C:\timeline-smoke");
                TimelineViewModel viewModel = new TimelineViewModel(
                    new FakeCatalog(snapshot), new FakePreparationService(snapshot), navigator);
                TimelineView view = new TimelineView { DataContext = viewModel };
                Window host = new Window { Content = view, Width = 1024, Height = 720 };

                host.Show();
                try
                {
                    await viewModel.RefreshAsync();
                    ListBox list = FindDescendant<ListBox>(view);
                    Assert.NotNull(list);
                    list.SelectedIndex = 0;
                    await viewModel.OpenSelectedAsync();

                    Assert.NotNull(navigator.Prepared);
                    Assert.Equal(SnapshotEventKind.Verified, navigator.Prepared.Snapshot.Kind);
                }
                finally
                {
                    navigator.Prepared?.Dispose();
                    host.Close();
                }
            });
        }

        private static SnapshotEvent NewEvent(SnapshotEventKind kind, string path)
            => new SnapshotEvent(kind, new DateTime(2026, 8, 9, 12, 0, 0, DateTimeKind.Local),
                Path.GetFileName(path), Path.GetFullPath(path), null, "TEST-PC", 0, true, null);

        private static T FindDescendant<T>(DependencyObject root) where T : DependencyObject
        {
            if (root is T matched)
                return matched;

            for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            {
                T found = FindDescendant<T>(VisualTreeHelper.GetChild(root, index));
                if (found != null)
                    return found;
            }

            return null;
        }

        private sealed class FakeCatalog : ISnapshotEventReader
        {
            private readonly IReadOnlyList<SnapshotEvent> events;

            internal FakeCatalog(params SnapshotEvent[] events) => this.events = events;

            public IReadOnlyList<SnapshotEvent> Read() => events;
        }

        private sealed class FakePreparationService : ISnapshotPayloadPreparationService
        {
            private readonly SnapshotEvent preparedEvent;

            internal FakePreparationService(SnapshotEvent preparedEvent) => this.preparedEvent = preparedEvent;

            public Task<SnapshotPayloadPreparation> PrepareAsync(SnapshotEvent snapshot,
                CancellationToken cancellationToken)
                => Task.FromResult(new SnapshotPayloadPreparation(preparedEvent, null, null));
        }

        private sealed class RecordingNavigator : ITimelineNavigator
        {
            internal SnapshotPayloadPreparation Prepared { get; private set; }

            public void OpenCompare(SnapshotPayloadPreparation preparation) => Prepared = preparation;

            public void ShowSnapshotDiagnostic(SnapshotEvent snapshot) { }
        }
    }
}
