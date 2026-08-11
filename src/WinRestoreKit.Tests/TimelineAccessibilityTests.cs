using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WinRestoreKit;
using WinRestoreKit.Wpf.Navigation;
using WinRestoreKit.Wpf.ViewModels.Timeline;
using WinRestoreKit.Wpf.Views;
using Xunit;

namespace WinRestoreKit.Tests
{
    public sealed class TimelineAccessibilityTests
    {
        [Fact]
        public void TimelineView_ExposesEquivalentNamedListAndKeyboardSelection()
        {
            WpfTestHost.Run(() =>
            {
                FakePreparationService service = new FakePreparationService();
                TimelineViewModel viewModel = NewTimelineViewModel(service);
                TimelineView view = new TimelineView { DataContext = viewModel };
                Window host = new Window { Content = view, Width = 1024, Height = 720 };

                host.Show();
                try
                {
                    Layout(view);

                    ListBox list = Assert.IsType<ListBox>(view.FindName("TimelineEventList"));
                    Assert.Equal("Snapshots", AutomationProperties.GetName(list));
                    Assert.Equal(SelectionMode.Single, list.SelectionMode);
                    Assert.Equal(KeyboardNavigationMode.Continue, KeyboardNavigation.GetDirectionalNavigation(list));
                    Assert.Contains("Enter", AutomationProperties.GetHelpText(list));
                    list.SelectedIndex = 0;
                    RaiseKey(list, Key.Right);
                    Assert.Equal(1, list.SelectedIndex);
                    RaiseKey(list, Key.Left);
                    Assert.Equal(0, list.SelectedIndex);
                    RaiseKey(list, Key.Enter);
                    Assert.Equal(1, service.Calls);
                }
                finally
                {
                    host.Close();
                }
            });
        }

        [Fact]
        public void TimelineView_ExposesEveryRowStateAndSelectionFailureAsAccessibleText()
        {
            WpfTestHost.Run(() =>
            {
                TimelineViewModel viewModel = NewTimelineViewModel();
                TimelineView view = new TimelineView { DataContext = viewModel };
                Window host = new Window { Content = view, Width = 1024, Height = 720 };

                host.Show();
                try
                {
                    Layout(view);

                    ListBox list = Assert.IsType<ListBox>(view.FindName("TimelineEventList"));
                    for (int index = 0; index < list.Items.Count; index++)
                    {
                        FrameworkElement row = FindElementWithAutomationName(
                            list.ItemContainerGenerator.ContainerFromIndex(index), viewModel.Events[index].AutomationName);
                        Assert.NotNull(row);
                    }

                    TextBlock selectionError = Assert.IsType<TextBlock>(view.FindName("SelectionErrorText"));
                    Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(selectionError));
                }
                finally
                {
                    host.Close();
                }
            });
        }

        private static TimelineViewModel NewTimelineViewModel(FakePreparationService service = null)
        {
            SnapshotEvent verified = NewEvent(SnapshotEventKind.Verified, @"C:\timeline\verified", "TEST-PC");
            SnapshotEvent failed = NewEvent(SnapshotEventKind.Failed, @"C:\timeline\failed", "TEST-PC", "disk full");
            TimelineViewModel viewModel = new TimelineViewModel(
                new FakeCatalog(verified, failed), service ?? new FakePreparationService(), new RecordingNavigator());
            viewModel.RefreshAsync().GetAwaiter().GetResult();
            return viewModel;
        }

        private static SnapshotEvent NewEvent(SnapshotEventKind kind, string path, string machine, string reason = null)
            => new SnapshotEvent(kind, new DateTime(2026, 8, 9, 12, 0, 0, DateTimeKind.Local),
                Path.GetFileName(path), Path.GetFullPath(path), reason, machine, 0, true, null);

        private static void RaiseKey(UIElement target, Key key)
        {
            PresentationSource inputSource = PresentationSource.FromVisual(target);
            Assert.NotNull(inputSource);
            target.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, inputSource, 0, key)
            {
                RoutedEvent = Keyboard.PreviewKeyDownEvent
            });
        }

        private static void Layout(FrameworkElement element)
        {
            element.Measure(new Size(1024, 720));
            element.Arrange(new Rect(0, 0, 1024, 720));
            element.UpdateLayout();
        }

        private static FrameworkElement FindElementWithAutomationName(DependencyObject root, string expectedName)
        {
            if (root is FrameworkElement element && AutomationProperties.GetName(element) == expectedName)
                return element;

            if (root == null)
                return null;

            for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            {
                FrameworkElement found = FindElementWithAutomationName(
                    VisualTreeHelper.GetChild(root, index), expectedName);
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
            internal int Calls { get; private set; }

            public Task<SnapshotPayloadPreparation> PrepareAsync(SnapshotEvent snapshot,
                CancellationToken cancellationToken)
            {
                Calls++;
                return Task.FromResult(new SnapshotPayloadPreparation(snapshot, null, "unexpected preparation"));
            }
        }

        private sealed class RecordingNavigator : ITimelineNavigator
        {
            public void OpenCompare(SnapshotPayloadPreparation preparation) => preparation.Dispose();

            public void ShowSnapshotDiagnostic(SnapshotEvent snapshot) { }
        }
    }
}
