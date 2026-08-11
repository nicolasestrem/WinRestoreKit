using System;
using System.Collections.Generic;
using System.Windows;
using WinRestoreKit;
using WinRestoreKit.Wpf.Views.Dialogs;

namespace WinRestoreKit.Wpf.Services
{
    internal sealed class RestoreRunDialogService : IRunDialogService
    {
        private readonly Window owner;

        internal RestoreRunDialogService(Window owner)
        {
            this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
        }

        public IReadOnlyList<string> ShowRestoreConsent(RestorePlan plan)
        {
            RestoreConsentDialog dialog = RestoreConsentDialog.Create(owner, plan);
            return dialog.ShowDialog() == true ? dialog.ConsentedProcessNames : null;
        }

        public bool ConfirmSnapshotOverride(string text, string caption)
            => MessageBox.Show(owner, text, caption, MessageBoxButton.YesNo,
                MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;

        public void ShowPlanCompositionError(string text, string caption)
            => MessageBox.Show(owner, text, caption, MessageBoxButton.OK, MessageBoxImage.Error);
    }
}
