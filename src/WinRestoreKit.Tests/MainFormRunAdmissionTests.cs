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

            using (BackupRunIsolation isolation = new BackupRunIsolation())
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
                        isolation.DestinationRoot
                    };

                    startBackup.Invoke(form, arguments);
                    Control firstProgressPage = content.Controls.OfType<ProgressPageView>().Single();

                    Assert.True(RunCoordinator.IsRunning);

                    startBackup.Invoke(form, arguments);
                    startRestore.Invoke(form, new object[]
                    {
                        Array.Empty<BackupBase>(),
                        new BackupFolder(isolation.DestinationRoot)
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

        [Fact]
        public void CompletedRun_ProgressRailRemainsEnabledAndReopensItsPage()
        {
            RunCoordinator.SetRunning(false);

            using (BackupRunIsolation isolation = new BackupRunIsolation())
            try
            {
                using (MainForm form = new MainForm())
                {
                    form.CreateControl();
                    Panel content = form.Controls.Find("contentPanel", true).OfType<Panel>().Single();
                    StartBackup(form, "completed-snapshot", isolation.DestinationRoot);
                    Control completedProgressPage = content.Controls.OfType<ProgressPageView>().Single();

                    RunCoordinator.SetRunning(false);
                    Invoke(form, "ApplyRunningState", false);

                    NavButton progressButton = form.Controls.Find("btnProgress", true).OfType<NavButton>().Single();
                    Assert.True(progressButton.Enabled);

                    Invoke(form, "ShowHome");
                    Invoke(form, "btnProgress_Click", form, EventArgs.Empty);

                    Assert.Same(completedProgressPage, content.Controls.OfType<ProgressPageView>().SingleOrDefault());
                }
            }
            finally
            {
                RunCoordinator.SetRunning(false);
            }
        }

        [Fact]
        public void NewRun_ReplacesCompletedProgressPageAndClearsItsRetainedResult()
        {
            RunCoordinator.SetRunning(false);

            using (BackupRunIsolation isolation = new BackupRunIsolation())
            try
            {
                using (MainForm form = new MainForm())
                {
                    form.CreateControl();
                    Panel content = form.Controls.Find("contentPanel", true).OfType<Panel>().Single();
                    StartBackup(form, "completed-snapshot", isolation.DestinationRoot);
                    Control completedProgressPage = content.Controls.OfType<ProgressPageView>().Single();
                    RunCoordinator.SetRunning(false);
                    Invoke(form, "ApplyRunningState", false);

                    StartBackup(form, "replacement-snapshot", isolation.DestinationRoot);

                    Control replacementProgressPage = content.Controls.OfType<ProgressPageView>().Single();
                    Assert.NotSame(completedProgressPage, replacementProgressPage);
                    FieldInfo retainedResult = typeof(MainForm).GetField("hasCompletedProgressResult",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                    Assert.NotNull(retainedResult);
                    Assert.False((bool)retainedResult.GetValue(form));
                }
            }
            finally
            {
                RunCoordinator.SetRunning(false);
            }
        }

        private static void StartBackup(MainForm form, string snapshotName, string destinationRoot)
        {
            Invoke(form, "StartBackup", Array.Empty<BackupBase>(), snapshotName,
                SnapshotCompression.Fast, destinationRoot);
        }


        private static object Invoke(MainForm form, string methodName, params object[] arguments)
        {
            return typeof(MainForm).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(form, arguments);
        }
    }
}
