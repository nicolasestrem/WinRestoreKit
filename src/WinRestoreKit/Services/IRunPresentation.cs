using System.Collections.Generic;
using WinRestoreKit;

namespace WinRestoreKit.Wpf.Services
{
    internal interface IRunPresentation
    {
        void SetProgressText(string text);
        void SetProgressPercent(int percent);
        void SetProgressDetail(string groupInfo, string elapsed, string remaining, string throughput,
                               long bytesWritten, int errors, int warnings);
        void ShowSummary(RunSummary summary, string caption, IReadOnlyList<ModuleOutcome> outcomes);
        void SetExplorerRestartVisible(bool visible);
    }
}
