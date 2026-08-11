using System.Windows;
using WinRestoreKit;

namespace WinRestoreKit.Wpf.Services
{
    internal interface ICompareDialogService
    {
        bool ConfirmDiscardRestoreSet(Window owner, SnapshotEvent current, SnapshotEvent incoming);
        void ShowSnapshotDiagnostic(Window owner, SnapshotEvent snapshot);
    }
}
