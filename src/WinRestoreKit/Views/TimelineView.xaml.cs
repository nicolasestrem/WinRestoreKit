using System;
using System.Windows;
using System.Windows.Controls;
using WinRestoreKit.Wpf.ViewModels.Timeline;

namespace WinRestoreKit.Wpf.Views
{
    public partial class TimelineView : UserControl
    {
        public TimelineView()
        {
            InitializeComponent();
            RegisterName("TimelineEventList", SnapshotEventListControl.EventList);
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is TimelineViewModel viewModel)
                await viewModel.RefreshAsync();
        }
    }
}
