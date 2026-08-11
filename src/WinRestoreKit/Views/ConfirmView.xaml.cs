using System;
using System.Windows;
using System.Windows.Controls;
using WinRestoreKit.Wpf.Services;
using WinRestoreKit.Wpf.ViewModels;

namespace WinRestoreKit.Wpf.Views
{
    public partial class ConfirmView : UserControl
    {
        public ConfirmView()
        {
            InitializeComponent();
        }

        private void ConfirmView_Loaded(object sender, RoutedEventArgs e)
        {
            ConfirmViewModel viewModel = DataContext as ConfirmViewModel;
            if (viewModel == null)
                return;

            Window owner = Window.GetWindow(this) ?? throw new InvalidOperationException("Confirm must be hosted in a Window.");
            viewModel.AttachRunSurfaces(Dispatcher, () => Window.GetWindow(this), new RestoreRunDialogService(owner));
        }
    }
}
