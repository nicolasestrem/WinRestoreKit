using System.Windows;
using System.Windows.Controls;
using WinRestoreKit.Wpf.ViewModels.History;

namespace WinRestoreKit.Wpf.Views
{
    public partial class AdvancedHistoryView : UserControl
    {
        public AdvancedHistoryView()
        {
            InitializeComponent();
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is AdvancedHistoryViewModel viewModel)
                await viewModel.RefreshAsync();
        }
    }
}
