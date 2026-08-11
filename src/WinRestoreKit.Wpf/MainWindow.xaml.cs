using System;
using System.Windows;
using WinRestoreKit;
using WinRestoreKit.Wpf.Navigation;
using WinRestoreKit.Wpf.Services;
using WinRestoreKit.Wpf.ViewModels;
using WinRestoreKit.Wpf.ViewModels.Timeline;

namespace WinRestoreKit.Wpf
{
    public partial class MainWindow : Window
    {
        internal MainWindow(ShellViewModel shell)
        {
            if (shell == null)
                throw new ArgumentNullException(nameof(shell));

            InitializeComponent();
            DataContext = shell;
            CompareWorkflowNavigator navigator = new CompareWorkflowNavigator(shell, this, new CompareDialogService());
            shell.SetTimeline(new TimelineViewModel(shell.SnapshotEventCatalog, new SnapshotPayloadPreparationService(), navigator));
        }
    }
}
