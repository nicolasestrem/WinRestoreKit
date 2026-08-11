using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using WinRestoreKit.Wpf.ViewModels;
using WinRestoreKit.Wpf.Views;
using Xunit;

namespace WinRestoreKit.Tests
{
    public sealed class BackupWorkspaceViewTests
    {
        [Fact]
        public void View_ExposesScopeWarningsLabeledCompressionAndExactAutomationIds()
        {
            WpfTestHost.Run(() =>
            {
                var viewModel = new BackupWorkspaceViewModel(_ => Task.CompletedTask, @"C:\snapshots");
                foreach (BackupScopeItemViewModel scope in viewModel.Scopes)
                    scope.IsSelected = false;
                viewModel.StartAsync().GetAwaiter().GetResult();

                var view = new BackupWorkspaceView
                {
                    DataContext = viewModel
                };
                var host = new Window { Content = view, Width = 1024, Height = 720 };
                host.Show();
                host.UpdateLayout();
                Dispatcher.CurrentDispatcher.Invoke(
                    DispatcherPriority.ApplicationIdle,
                    new Action(() => { }));

                try
                {
                    Assert.Equal("BackupWorkspace", AutomationProperties.GetAutomationId(view));
                    Assert.NotNull(view.FindName("BackupScopeList"));
                    Assert.NotNull(view.FindName("CreateSnapshotButton"));
                    Assert.NotNull(view.FindName("CompressionComboBox"));
                    Assert.Equal("BackupDestinationTextBox", AutomationProperties.GetAutomationId(
                        Assert.IsType<TextBox>(view.FindName("DestinationTextBox"))));
                    Assert.Equal("CreateSnapshotButton", AutomationProperties.GetAutomationId(
                        Assert.IsType<Button>(view.FindName("CreateSnapshotButton"))));
                    Assert.Equal("CompressionComboBox", AutomationProperties.GetAutomationId(
                        Assert.IsType<ComboBox>(view.FindName("CompressionComboBox"))));

                    TextBlock validation = Assert.IsType<TextBlock>(view.FindName("BackupValidationText"));
                    Assert.Equal(viewModel.ValidationAutomationName, AutomationProperties.GetName(validation));
                    Assert.Equal(viewModel.ValidationMessage, AutomationProperties.GetHelpText(validation));
                }
                finally
                {
                    host.Close();
                }
            });
        }
    }
}
