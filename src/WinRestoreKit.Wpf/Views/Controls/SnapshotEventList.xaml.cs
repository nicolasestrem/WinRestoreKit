using System;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WinRestoreKit.Wpf.ViewModels.Timeline;

namespace WinRestoreKit.Wpf.Views.Controls
{
    public partial class SnapshotEventList : UserControl
    {
        // Measured against this control, not the window. At the 1024 px minimum window width the
        // timeline page reserves 32 px of horizontal padding per side, so the rail stays visible
        // there and only collapses into a plain status list on genuinely narrow layouts.
        private const double WideTimelineThreshold = 880;

        public SnapshotEventList()
        {
            InitializeComponent();
            SizeChanged += OnSizeChanged;
            ((INotifyCollectionChanged)TimelineEventList.Items).CollectionChanged += OnItemsChanged;
        }

        internal ListBox EventList => TimelineEventList;

        private async void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Left || e.Key == Key.Right)
            {
                MoveSelection(e.Key == Key.Left ? -1 : 1);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Enter && DataContext is TimelineViewModel viewModel)
            {
                e.Handled = true;
                await viewModel.OpenSelectedAsync();
            }
        }

        private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (TimelineEventList.SelectedIndex >= 0)
                TimelineEventList.Focus();
        }

        private void OnSizeChanged(object sender, SizeChangedEventArgs e) => UpdateConnector(e.NewSize.Width);

        private void OnItemsChanged(object sender, NotifyCollectionChangedEventArgs e) => UpdateConnector(ActualWidth);

        private void UpdateConnector(double width)
        {
            // An axis with nothing on it is noise, so the rail only appears once events exist.
            TimelineConnector.Visibility = width >= WideTimelineThreshold && TimelineEventList.Items.Count > 0
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void MoveSelection(int offset)
        {
            if (TimelineEventList.Items.Count == 0)
                return;

            int selectedIndex = TimelineEventList.SelectedIndex + offset;
            selectedIndex = Math.Max(0, Math.Min(TimelineEventList.Items.Count - 1, selectedIndex));
            TimelineEventList.SelectedIndex = selectedIndex;
            TimelineEventList.ScrollIntoView(TimelineEventList.SelectedItem);
        }
    }
}
