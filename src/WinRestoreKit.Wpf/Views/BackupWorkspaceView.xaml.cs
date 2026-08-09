using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using WinRestoreKit.Wpf.ViewModels;

namespace WinRestoreKit.Wpf.Views
{
    public partial class BackupWorkspaceView : UserControl
    {
        public BackupWorkspaceView()
        {
            InitializeComponent();
        }

        private void BrowseDestination_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not BackupWorkspaceViewModel workspace)
                return;

            var dialog = new OpenFolderDialog();
            if (dialog.ShowDialog() == true)
                workspace.Destination = dialog.FolderName;
        }
    }
}
