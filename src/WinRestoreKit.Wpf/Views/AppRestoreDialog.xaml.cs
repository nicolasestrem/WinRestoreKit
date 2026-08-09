using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using WinRestoreKit;
using WinRestoreKit.Wpf.ViewModels;

namespace WinRestoreKit.Wpf.Views
{
    internal partial class AppRestoreDialog : Window
    {
        private readonly AppRestoreDialogViewModel viewModel;
        private bool closeWhenIdle;

        internal AppRestoreDialog(string payloadPath, IReadOnlyList<SnapshotEvent> snapshots)
        {
            InitializeComponent();
            viewModel = new AppRestoreDialogViewModel(payloadPath, snapshots, Close);
            viewModel.PropertyChanged += ViewModelPropertyChanged;
            DataContext = viewModel;
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            bool dispatcherIsStopping = Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished;
            bool ownerIsTearingDown = Owner != null && !Owner.IsVisible;
            if (viewModel.IsInstalling && !dispatcherIsStopping && !ownerIsTearingDown)
            {
                closeWhenIdle = true;
                viewModel.RequestStop();
                e.Cancel = true;
            }

            base.OnClosing(e);
        }

        private void ViewModelPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(AppRestoreDialogViewModel.Outcome)
                && viewModel.Outcome != null && !IsVisible)
            {
                LogHelper.Instance.LogMessage(viewModel.Outcome.Caption + ": " + viewModel.Outcome.Text);
            }

            if (closeWhenIdle && e.PropertyName == nameof(AppRestoreDialogViewModel.IsInstalling)
                && !viewModel.IsInstalling)
            {
                closeWhenIdle = false;
                Close();
            }
        }
    }
}
