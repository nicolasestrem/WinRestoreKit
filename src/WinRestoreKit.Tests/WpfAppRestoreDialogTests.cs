using System;
using System.Windows;
using WinRestoreKit.Wpf.Services;
using Xunit;

namespace WinRestoreKit.Tests
{
    public class WpfAppRestoreDialogTests
    {
        [Fact]
        public void RestoreDialogCallback_PassesTheVisibleWpfWindowAndPreparedPath()
        {
            WpfTestHost.Run(() =>
            {
                var owner = new Window();
                owner.Show();
                Window receivedOwner = null;
                string receivedPath = null;
                Action<string, object> callback = WpfAppRestoreDialog.CreateCallback(
                    (actualOwner, path) => { receivedOwner = actualOwner; receivedPath = path; });

                try
                {
                    callback(@"C:\prepared", owner);
                    Assert.Same(owner, receivedOwner);
                    Assert.Equal(@"C:\prepared", receivedPath);
                }
                finally
                {
                    owner.Close();
                }
            });
        }

        [Fact]
        public void RestoreDialogCallback_WithoutAVisibleWpfOwner_DoesNotOpenDialog()
        {
            Action<string, object> callback = WpfAppRestoreDialog.CreateCallback((_, _) =>
                throw new Xunit.Sdk.XunitException("must not open"));

            callback(@"C:\prepared", new object());

            WpfTestHost.Run(() => callback(@"C:\prepared", new Window()));
        }
    }
}
