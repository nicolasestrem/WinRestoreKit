using System.Collections.Generic;
using WinRestoreKit;

namespace WinRestoreKit.Wpf.Services
{
    internal interface IRunDialogService
    {
        IReadOnlyList<string> ShowRestoreConsent(RestorePlan plan);
        bool ConfirmSnapshotOverride(string text, string caption);
        void ShowPlanCompositionError(string text, string caption);
    }
}
