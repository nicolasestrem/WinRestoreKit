using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using WinRestoreKit;
using WinRestoreKit.Wpf.Services;
using WinRestoreKit.Wpf.ViewModels;
using WinRestoreKit.Wpf.Views;
using Xunit;

namespace WinRestoreKit.Tests
{
    public sealed class WpfSurfaceAccessibilityTests
    {
        [Fact]
        public void ComparisonWorkspace_StacksAtMinimumWidthAndRestoresWideLayout()
        {
            WpfTestHost.Run(() =>
            {
                ComparisonWorkspaceViewModel viewModel = ComparisonViewModel();
                ComparisonWorkspaceView view = new ComparisonWorkspaceView { DataContext = viewModel };
                Window host = new Window { Content = view, Width = 1280, Height = 800 };

                host.Show();
                try
                {
                    Layout(view, 1280, 800);
                    Border evidence = Require<Border>(view, "EvidenceCard");
                    Border restoreSet = Require<Border>(view, "RestoreSetCard");
                    Border detail = Require<Border>(view, "DetailCard");
                    ScrollViewer scroller = Require<ScrollViewer>(view, "ComparisonPaneScroller");

                    Assert.False(view.IsNarrowLayout);
                    Assert.Equal(0, Grid.GetColumn(evidence));
                    Assert.Equal(3, Grid.GetRowSpan(evidence));
                    Assert.Equal(1, Grid.GetColumn(restoreSet));
                    Assert.Equal(1, Grid.GetColumn(detail));
                    Assert.Equal(ScrollBarVisibility.Disabled, scroller.VerticalScrollBarVisibility);

                    host.Width = 900;
                    Layout(view, 900, 800);

                    Assert.True(view.IsNarrowLayout);
                    Assert.Equal(0, Grid.GetColumn(evidence));
                    Assert.Equal(0, Grid.GetRow(evidence));
                    Assert.Equal(1, Grid.GetRowSpan(evidence));
                    Assert.Equal(0, Grid.GetColumn(restoreSet));
                    Assert.Equal(1, Grid.GetRow(restoreSet));
                    Assert.Equal(0, Grid.GetColumn(detail));
                    Assert.Equal(2, Grid.GetRow(detail));
                    Assert.Equal(ScrollBarVisibility.Auto, scroller.VerticalScrollBarVisibility);

                    host.Width = 1280;
                    Layout(view, 1280, 800);

                    Assert.False(view.IsNarrowLayout);
                    Assert.Equal(1, Grid.GetColumn(restoreSet));
                    Assert.Equal(1, Grid.GetColumn(detail));
                }
                finally
                {
                    host.Close();
                }
            });
        }

        [Fact]
        public void ComparisonWorkspace_ExposesOnlyTheActiveDetailAlternative()
        {
            WpfTestHost.Run(() =>
            {
                ComparisonWorkspaceViewModel viewModel = ComparisonViewModel();
                ComparisonWorkspaceView view = new ComparisonWorkspaceView { DataContext = viewModel };
                Window host = new Window { Content = view, Width = 1280, Height = 800 };

                host.Show();
                try
                {
                    Layout(view, 1280, 800);
                    Border detail = Require<Border>(view, "DetailCard");
                    Border placeholder = Require<Border>(view, "DetailPlaceholder");

                    Assert.Equal(Visibility.Collapsed, detail.Visibility);
                    Assert.Equal(IsOffscreenBehavior.Offscreen,
                        AutomationProperties.GetIsOffscreenBehavior(detail));
                    Assert.Equal(Visibility.Visible, placeholder.Visibility);
                    Assert.Equal(IsOffscreenBehavior.Onscreen,
                        AutomationProperties.GetIsOffscreenBehavior(placeholder));

                    BackupModuleRegistration registration = new BackupModuleRegistration(
                        new TestModule("Mouse"), "Input");
                    viewModel.SelectedRow = new ModuleComparisonRowViewModel(
                        registration, viewModel.RestoreSet);
                    Layout(view, 1280, 800);

                    Assert.Equal(Visibility.Visible, detail.Visibility);
                    Assert.Equal(IsOffscreenBehavior.Onscreen,
                        AutomationProperties.GetIsOffscreenBehavior(detail));
                    Assert.Equal(Visibility.Collapsed, placeholder.Visibility);
                    Assert.Equal(IsOffscreenBehavior.Offscreen,
                        AutomationProperties.GetIsOffscreenBehavior(placeholder));
                }
                finally
                {
                    host.Close();
                }
            });
        }

        [Fact]
        public void ConfirmWorkspace_HidesInactiveSectionsFromTheAccessibilityContentView()
        {
            WpfTestHost.Run(() =>
            {
                ConfirmViewModel viewModel = new ConfirmViewModel(
                    Snapshot(), new BackupBase[] { new TestModule("Mouse") });
                ConfirmView view = new ConfirmView { DataContext = viewModel };
                Window host = new Window { Content = view, Width = 1100, Height = 800 };

                host.Show();
                try
                {
                    Layout(view, 1100, 800);
                    AssertHiddenFromContent(view, "ConfirmPartialWarning");
                    AssertHiddenFromContent(view, "ConfirmEmptyModules");
                    AssertHiddenFromContent(view, "ConfirmApplicationsSection");
                    AssertHiddenFromContent(view, "ConfirmConsentProcesses");
                    AssertHiddenFromContent(view, "ConfirmInformationalProcesses");
                    AssertHiddenFromContent(view, "ConfirmExplorerSection");
                    AssertHiddenFromContent(view, "ConfirmWarningsSection");
                    AssertHiddenFromContent(view, "ConfirmRestoreStatusSection");
                    AssertHiddenFromContent(view, "ConfirmProgressText");
                    AssertHiddenFromContent(view, "ConfirmSummary");
                }
                finally
                {
                    host.Close();
                }
            });
        }

        [Fact]
        public void SettingsThemeOptionsExposeStableAutomationIds()
        {
            WpfTestHost.Run(() =>
            {
                SettingsView view = new SettingsView
                {
                    DataContext = new SettingsViewModel(new FakeThemeService())
                };
                Window host = new Window { Content = view, Width = 900, Height = 600 };

                host.Show();
                try
                {
                    Layout(view, 900, 600);
                    ComboBox selector = Require<ComboBox>(view, "themeSelector");
                    Assert.Equal("SettingsThemeSelector", AutomationProperties.GetAutomationId(selector));
                    selector.IsDropDownOpen = true;
                    Layout(view, 900, 600);

                    Assert.Equal("SettingsThemeFollowSystem", ItemAutomationId(selector, 0));
                    Assert.Equal("SettingsThemeLight", ItemAutomationId(selector, 1));
                    Assert.Equal("SettingsThemeDark", ItemAutomationId(selector, 2));
                }
                finally
                {
                    host.Close();
                }
            });
        }

        private static ComparisonWorkspaceViewModel ComparisonViewModel()
            => new ComparisonWorkspaceViewModel(
                Snapshot(),
                Array.Empty<BackupModuleRegistration>(),
                new SnapshotComparisonService(),
                (_, __) => { });

        private static SnapshotEvent Snapshot()
            => new SnapshotEvent(SnapshotEventKind.Verified,
                new DateTime(2026, 8, 10, 0, 0, 0, DateTimeKind.Local),
                "snapshot", @"C:\snapshot", string.Empty, "TEST-PC", 0, true, null);

        private static void AssertHiddenFromContent(FrameworkElement view, string name)
        {
            FrameworkElement element = Require<FrameworkElement>(view, name);
            Assert.Equal(Visibility.Collapsed, element.Visibility);
            Assert.Equal(IsOffscreenBehavior.Offscreen,
                AutomationProperties.GetIsOffscreenBehavior(element));
        }

        private static string ItemAutomationId(ComboBox selector, int index)
        {
            ComboBoxItem item = Assert.IsType<ComboBoxItem>(
                selector.ItemContainerGenerator.ContainerFromIndex(index));
            return AutomationProperties.GetAutomationId(item);
        }

        private static T Require<T>(FrameworkElement root, string name) where T : FrameworkElement
            => Assert.IsAssignableFrom<T>(root.FindName(name));

        private static void Layout(FrameworkElement element, double width, double height)
        {
            element.Measure(new Size(width, height));
            element.Arrange(new Rect(0, 0, width, height));
            element.UpdateLayout();
        }

        private sealed class FakeThemeService : IThemeService
        {
            public ThemeMode Mode { get; private set; } = ThemeMode.FollowSystem;
            public ThemeMode EffectiveMode { get; private set; } = ThemeMode.Light;
            public event EventHandler ThemeChanged;

            public void SetMode(ThemeMode mode)
            {
                Mode = mode;
                EffectiveMode = mode == ThemeMode.FollowSystem ? ThemeMode.Light : mode;
                ThemeChanged?.Invoke(this, EventArgs.Empty);
            }

            public void Dispose() { }
        }

        private sealed class TestModule : BackupBase
        {
            internal TestModule(string title) => Title = title;

            public override IReadOnlyList<RestoreTarget> RestoreTargets
                => Array.Empty<RestoreTarget>();

            public override IReadOnlyList<RestoreCloseRequirement> ProcessesToCloseBeforeRestore
                => Array.Empty<RestoreCloseRequirement>();

            public override bool? HasArtifactIn(string backupPath) => false;

            public override bool? HasDriftedFrom(string backupPath) => false;
        }
    }
}
