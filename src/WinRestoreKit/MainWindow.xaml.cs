using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
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
            shell.SetBeforeUserNavigation(navigator.LeaveCompareAsync);
            shell.SetTimeline(new TimelineViewModel(shell.SnapshotEventCatalog, new SnapshotPayloadPreparationService(), navigator));
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (DataContext is ShellViewModel shell && !shell.RequestWindowClose())
                e.Cancel = true;

            base.OnClosing(e);
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape && ReturnToTimelineFromCompare())
                e.Handled = true;
        }

        internal bool ReturnToTimelineFromCompare()
        {
            if (DataContext is not ShellViewModel shell
                || shell.CurrentWorkspace is not ComparisonWorkspaceViewModel
                || !shell.ShowTimelineCommand.CanExecute(null))
            {
                return false;
            }

            shell.ShowTimelineCommand.Execute(null);
            return true;
        }
    }
}
