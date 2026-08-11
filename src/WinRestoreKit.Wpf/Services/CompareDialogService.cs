using System;
using System.Windows;
using WinRestoreKit;

namespace WinRestoreKit.Wpf.Services
{
    internal sealed class CompareDialogService : ICompareDialogService
    {
        public bool ConfirmDiscardRestoreSet(Window owner, SnapshotEvent current, SnapshotEvent incoming)
            => MessageBox.Show(owner,
                "Changing from \"" + current.DisplayName + "\" to \"" + incoming.DisplayName +
                "\" clears the selected restore modules. Change snapshot?",
                "Change snapshot", MessageBoxButton.YesNo, MessageBoxImage.Warning,
                MessageBoxResult.No) == MessageBoxResult.Yes;

        public void ShowSnapshotDiagnostic(Window owner, SnapshotEvent snapshot)
            => MessageBox.Show(owner, DiagnosticTextFor(snapshot), "Snapshot diagnostic",
                MessageBoxButton.OK, MessageBoxImage.Error);

        internal static string DiagnosticTextFor(SnapshotEvent snapshot)
        {
            if (snapshot == null)
                throw new ArgumentNullException(nameof(snapshot));

            return string.IsNullOrWhiteSpace(snapshot.DiagnosticReason)
                ? "No additional diagnostic details were recorded for this snapshot."
                : snapshot.DiagnosticReason;
        }
    }
}
