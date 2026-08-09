using System;
using System.Windows;
using System.Windows.Threading;

namespace WinRestoreKit.Wpf.Services
{
    internal sealed class WpfDialogService : IWpfDialogService
    {
        private readonly Dispatcher dispatcher;
        private readonly Func<Window> ownerProvider;

        internal WpfDialogService(Dispatcher dispatcher, Func<Window> ownerProvider)
        {
            this.dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            this.ownerProvider = ownerProvider ?? throw new ArgumentNullException(nameof(ownerProvider));
        }

        public void ShowInformation(string text, string caption)
            => Show(text, caption, MessageBoxButton.OK, MessageBoxImage.Information);

        public void ShowWarning(string text, string caption)
            => Show(text, caption, MessageBoxButton.OK, MessageBoxImage.Warning);

        public void ShowError(string text, string caption)
            => Show(text, caption, MessageBoxButton.OK, MessageBoxImage.Error);

        public bool Confirm(string text, string caption)
        {
            return dispatcher.CheckAccess()
                ? ConfirmOnDispatcher(text, caption)
                : dispatcher.Invoke(() => ConfirmOnDispatcher(text, caption));
        }

        private void Show(string text, string caption, MessageBoxButton buttons, MessageBoxImage image)
        {
            if (dispatcher.CheckAccess())
                ShowOnDispatcher(text, caption, buttons, image);
            else
                dispatcher.Invoke(() => ShowOnDispatcher(text, caption, buttons, image));
        }

        private void ShowOnDispatcher(string text, string caption, MessageBoxButton buttons,
                                      MessageBoxImage image)
        {
            Window owner = ownerProvider();
            if (owner == null || !owner.IsLoaded)
                return;

            MessageBox.Show(owner, text, caption, buttons, image);
        }

        private bool ConfirmOnDispatcher(string text, string caption)
        {
            Window owner = ownerProvider();
            return owner != null && owner.IsLoaded &&
                   MessageBox.Show(owner, text, caption, MessageBoxButton.YesNo,
                                   MessageBoxImage.Information) == MessageBoxResult.Yes;
        }
    }
}
