using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using WinRestoreKit;
using WinRestoreKit.Wpf.Views.Dialogs;
using Xunit;

namespace WinRestoreKit.Tests
{
    public sealed class RestoreConsentDialogTests
    {
        [Fact]
        public void RestoreConsentDialog_IsOwnerBoundAndDefaultsToNoConsentedProcesses()
        {
            WpfTestHost.Run(() =>
            {
                Window owner = new Window();
                owner.Show();
                RestoreConsentDialog dialog = RestoreConsentDialog.Create(owner, PlanWithOneConsentEntry());

                Assert.Same(owner, dialog.Owner);
                Assert.Empty(dialog.ConsentedProcessNames);
                Assert.False(dialog.DialogResult == true);
                dialog.Close();
                owner.Close();
            });
        }

        [Fact]
        public void RestoreConsentDialog_IsResizableScrollableAndWrapsLongConsentText()
        {
            WpfTestHost.Run(() =>
            {
                Window owner = new Window();
                owner.Show();
                RestorePlan plan = PlanWithOneConsentEntry();
                RestoreConsentDialog dialog = RestoreConsentDialog.Create(owner, plan);
                dialog.Show();
                dialog.UpdateLayout();

                Assert.Equal(ResizeMode.CanResizeWithGrip, dialog.ResizeMode);
                Assert.Equal(SizeToContent.Manual, dialog.SizeToContent);
                Assert.True(dialog.Width <= SystemParameters.WorkArea.Width);
                Assert.True(dialog.Height <= SystemParameters.WorkArea.Height);
                Assert.NotNull(dialog.FindName("ConsentScrollViewer"));
                TextBlock confirmation = Assert.IsType<TextBlock>(dialog.FindName("ConsentConfirmationText"));
                TextBlock notice = Assert.IsType<TextBlock>(dialog.FindName("ConsentSnapshotNotice"));
                Assert.Equal(TextWrapping.Wrap, confirmation.TextWrapping);
                Assert.Equal(TextWrapping.Wrap, notice.TextWrapping);
                Assert.Equal(plan.ConfirmationText, AutomationProperties.GetHelpText(confirmation));
                Assert.Equal(plan.SnapshotNotice, AutomationProperties.GetHelpText(notice));

                dialog.Close();
                owner.Close();
            });
        }

        private static RestorePlan PlanWithOneConsentEntry()
        {
            ConsentModule module = new ConsentModule();
            return new RestorePlan(new BackupBase[] { module }, Path.GetTempPath(), Path.Combine(Path.GetTempPath(), "snapshot"));
        }

        private sealed class ConsentModule : BackupBase
        {
            public ConsentModule() => Title = "Code";
            public override IReadOnlyList<RestoreTarget> RestoreTargets
                => new[]
                {
                    RestoreTarget.File(Path.Combine(Path.GetTempPath(), new string('x', 220), "settings.json"))
                };
            public override IReadOnlyList<RestoreCloseRequirement> ProcessesToCloseBeforeRestore
                => new[] { new RestoreCloseRequirement("code", "Visual Studio Code", true) };
        }
    }
}
