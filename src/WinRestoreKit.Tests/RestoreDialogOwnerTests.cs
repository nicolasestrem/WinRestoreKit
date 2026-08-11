using Conf;
using System;
using System.IO;
using System.Windows.Forms;
using Xunit;

namespace WinRestoreKit.Tests
{
    /// <summary>
    /// Pins the opaque Core-to-shell dialog seam. WinForms supplies an <see cref="IWin32Window"/>;
    /// the WPF bridge independently admits only a visible <see cref="System.Windows.Window"/>.
    /// </summary>
    public class RestoreDialogOwnerTests
    {
        private sealed class TestWindowOwner : IWin32Window
        {
            public IntPtr Handle => IntPtr.Zero;
        }

        private sealed class DialogHook : IDisposable
        {
            private readonly Action<string, object> previous;

            public DialogHook(Action<string, object> replacement)
            {
                previous = AppStoreApps.RestoreDialog;
                AppStoreApps.RestoreDialog = replacement;
            }

            public void Dispose() => AppStoreApps.RestoreDialog = previous;
        }

        [Fact]
        public void Restore_WithAnOwner_PassesThatOwnerToTheRegisteredDialog()
        {
            string source = Path.Combine(Path.GetTempPath(), "app-restore-" + Guid.NewGuid().ToString("N"));
            IWin32Window owner = new TestWindowOwner();
            object receivedOwner = null;
            string openedPath = null;

            try
            {
                Directory.CreateDirectory(source);

                using (new DialogHook((path, suppliedOwner) =>
                {
                    openedPath = path;
                    receivedOwner = suppliedOwner;
                }))
                {
                    ModuleResult result = new AppStoreApps().Restore(source, owner);

                    Assert.Equal(ResultState.Skipped, result.State);
                    Assert.Same(owner, receivedOwner);
                    Assert.Equal(Path.GetFullPath(source), Path.GetFullPath(openedPath), StringComparer.OrdinalIgnoreCase);
                }
            }
            finally
            {
                try { Directory.Delete(source, true); } catch { }
            }
        }
    }
}
