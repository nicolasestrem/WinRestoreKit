using System.Collections.Generic;
using System.IO;
using System.Windows;
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

        private static RestorePlan PlanWithOneConsentEntry()
        {
            ConsentModule module = new ConsentModule();
            return new RestorePlan(new BackupBase[] { module }, Path.GetTempPath(), Path.Combine(Path.GetTempPath(), "snapshot"));
        }

        private sealed class ConsentModule : BackupBase
        {
            public ConsentModule() => Title = "Code";
            public override IReadOnlyList<RestoreCloseRequirement> ProcessesToCloseBeforeRestore
                => new[] { new RestoreCloseRequirement("code", "Visual Studio Code", true) };
        }
    }
}
