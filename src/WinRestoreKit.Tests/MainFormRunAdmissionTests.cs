using System;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Views;
using WinRestoreKit;
using Xunit;

namespace WinRestoreKit.Tests
{
    public class MainFormRunAdmissionTests
    {
        [Fact]
        public void StartRequests_WhenRunIsAlreadyActive_DoNotReplaceTheCurrentProgressPage()
        {
            RunCoordinator.SetRunning(false);

            try
            {
                using (MainForm form = new MainForm())
                {
                    MethodInfo startBackup = typeof(MainForm).GetMethod("StartBackup",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                    MethodInfo startRestore = typeof(MainForm).GetMethod("StartRestore",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                    Panel content = form.Controls.Find("contentPanel", true).OfType<Panel>().Single();
                    object[] arguments =
                    {
                        Array.Empty<BackupBase>(),
                        "first-snapshot",
                        SnapshotCompression.Fast,
                        Environment.CurrentDirectory
                    };

                    startBackup.Invoke(form, arguments);
                    Control firstProgressPage = content.Controls.OfType<ProgressPageView>().Single();

                    Assert.True(RunCoordinator.IsRunning);

                    startBackup.Invoke(form, arguments);
                    startRestore.Invoke(form, new object[]
                    {
                        Array.Empty<BackupBase>(),
                        new BackupFolder(Environment.CurrentDirectory)
                    });
                    Assert.Same(firstProgressPage, content.Controls.OfType<ProgressPageView>().Single());
                    Assert.Single(content.Controls);
                }
            }
            finally
            {
                RunCoordinator.SetRunning(false);
            }
        }
    }
}
