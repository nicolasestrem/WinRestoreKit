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
        public async Task TimelineView_ExposesEquivalentMouseAndKeyboardActivation()
        {
            await WpfTestHost.RunAsync(async () =>
            {
                FakePreparationService service = new FakePreparationService();
                TimelineViewModel viewModel = NewTimelineViewModel(service);
                TimelineView view = new TimelineView { DataContext = viewModel };
                Window host = new Window { Content = view, Width = 1024, Height = 720 };

                host.Show();
                try
                {
                    await viewModel.RefreshAsync();
                    Layout(view);

                    ListBox list = Assert.IsType<ListBox>(view.FindName("TimelineEventList"));
                    Assert.Equal("Snapshots", AutomationProperties.GetName(list));
                    Assert.Equal("TimelineEventList", AutomationProperties.GetAutomationId(list));
                    Assert.Equal(SelectionMode.Single, list.SelectionMode);
                    Assert.Equal(KeyboardNavigationMode.Continue, KeyboardNavigation.GetDirectionalNavigation(list));
                    Assert.Contains("Enter", AutomationProperties.GetHelpText(list));
                    Assert.Contains("Double-click", AutomationProperties.GetHelpText(list));
                    list.SelectedIndex = 0;
                    RaiseKey(list, Key.Right);
                    Assert.Equal(1, list.SelectedIndex);
                    RaiseKey(list, Key.Left);
                    Assert.Equal(0, list.SelectedIndex);
                    RaiseKey(list, Key.Enter);
                    Assert.Equal(1, service.Calls);

                    ListBoxItem selected = Assert.IsType<ListBoxItem>(
                        list.ItemContainerGenerator.ContainerFromIndex(0));
                    RaiseDoubleClick(selected);
                    Assert.Equal(2, service.Calls);
                }
                finally
                {
                    host.Close();
                }
            });
        }

        [Fact]
        public async Task TimelineView_ExposesOnlyTheActiveAlternateStateToAccessibility()
        {
            await WpfTestHost.RunAsync(async () =>
            {
                TimelineViewModel viewModel = NewTimelineViewModel();
                TimelineView view = new TimelineView { DataContext = viewModel };
                Window host = new Window { Content = view, Width = 1024, Height = 720 };

                host.Show();
                try
                {
                    await viewModel.RefreshAsync();
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

                    Border empty = Assert.IsType<Border>(view.FindName("TimelineEmptyState"));
                    Border loading = Assert.IsType<Border>(view.FindName("TimelineLoadingState"));
                    Border error = Assert.IsType<Border>(view.FindName("TimelineSelectionError"));
                    Assert.Equal(Visibility.Collapsed, empty.Visibility);
                    Assert.Equal(IsOffscreenBehavior.Offscreen,
                        AutomationProperties.GetIsOffscreenBehavior(empty));
                    Assert.Equal(Visibility.Collapsed, loading.Visibility);
                    Assert.Equal(IsOffscreenBehavior.Offscreen,
                        AutomationProperties.GetIsOffscreenBehavior(loading));
                    Assert.Equal(Visibility.Collapsed, error.Visibility);
                    Assert.Equal(IsOffscreenBehavior.Offscreen,
                        AutomationProperties.GetIsOffscreenBehavior(error));

                    viewModel.SelectedEvent = viewModel.Events[0];
                    await viewModel.OpenSelectedAsync();
                    Layout(view);

                    Assert.Equal(Visibility.Visible, error.Visibility);
                    Assert.Equal(IsOffscreenBehavior.Onscreen,
                        AutomationProperties.GetIsOffscreenBehavior(error));
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

        private static void RaiseDoubleClick(Control target)
        {
            PresentationSource inputSource = PresentationSource.FromVisual(target);
            Assert.NotNull(inputSource);
            target.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
            {
                RoutedEvent = Control.MouseDoubleClickEvent
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
