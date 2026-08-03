using System;
using System.Windows.Forms;
using Views;
using WinRestoreKit;
using Xunit;

namespace WinRestoreKit.Tests
{
    public class ProgressPageViewTests
    {
        [Fact]
        public void Constructor_DoesNotStartBackupBeforeViewIsShown()
        {
            RunCoordinator.SetRunning(false);

            using (Panel host = new Panel())
            using (ProgressPageView view = new ProgressPageView(
                       new NavigationService(host),
                       Array.Empty<BackupBase>(),
                       "nightly",
                       SnapshotCompression.Fast,
                       @"C:\backup"))
            {
                Assert.IsAssignableFrom<IRunUi>(view);
                Assert.False(RunCoordinator.IsRunning);
            }
        }
    }
}
