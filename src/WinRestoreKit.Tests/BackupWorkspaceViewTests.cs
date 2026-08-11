using System.Threading.Tasks;
using System.Windows.Automation;
using System.Windows.Controls;
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
                var view = new BackupWorkspaceView
                {
                    DataContext = new BackupWorkspaceViewModel(_ => Task.CompletedTask, @"C:\snapshots")
                };

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
            });
        }
    }
}
