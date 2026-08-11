using Conf;
using System;
using System.Windows;
using WinRestoreKit;
using WinRestoreKit.Wpf.Views;

namespace WinRestoreKit.Wpf.Services
{
    internal sealed class WpfAppRestoreDialog
    {
        private readonly SnapshotEventCatalog catalog;

        internal WpfAppRestoreDialog(SnapshotEventCatalog catalog)
            => this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));

        internal static void Register(SnapshotEventCatalog catalog)
        {
            var dialog = new WpfAppRestoreDialog(catalog);
            AppStoreApps.RestoreDialog = CreateCallback(dialog.Show);
        }

        internal static Action<string, object> CreateCallback(Action<Window, string> show)
        {
            if (show == null)
                throw new ArgumentNullException(nameof(show));

            return (path, owner) =>
            {
                if (owner is not Window window || !window.IsLoaded || !window.IsVisible
                    || window.Dispatcher.HasShutdownStarted || window.Dispatcher.HasShutdownFinished)
                    return;

                Action open = () => show(window, path);
                if (window.Dispatcher.CheckAccess())
                    open();
                else
                    window.Dispatcher.Invoke(open);
            };
        }

        internal void Show(Window owner, string payloadPath)
        {
            var dialog = new AppRestoreDialog(payloadPath, catalog.Read()) { Owner = owner };
            dialog.ShowDialog();
        }
    }
}
