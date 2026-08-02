using System.Collections.Generic;
using System.Windows.Forms;

namespace WinRestoreKit
{
    /// <summary>
    /// The UI surface a <see cref="BackupRestoreOrchestrator"/> run talks back through.
    /// </summary>
    /// <remarks>
    /// Extracted in Phase 4 PR 5 so the backup/restore orchestration can move out of
    /// <c>ConfPageView</c> without taking WinForms control references with it. Every member is a
    /// direct replacement for something the view used to do inline; see the per-member remarks for
    /// the exact line each replaces. The orchestrator is app-side (not Core) precisely because
    /// <see cref="RunSummary"/> carries a <see cref="MessageBoxIcon"/> and stays app-side with its
    /// test - that decision is recorded in the Phase 4 design spec.
    /// </remarks>
    internal interface IRunUi
    {
        /// <summary>Replaces linkSubHeader.Text progress updates.</summary>
        void SetProgressText(string text);

        /// <summary>Owner for modal dialogs. Replaces FindForm().</summary>
        IWin32Window Owner { get; }

        /// <summary>Renders a run result. Replaces ShowSummary's log+MessageBox pair.</summary>
        void ShowSummary(RunSummary summary, string caption, IReadOnlyList<ModuleOutcome> outcomes);

        /// <summary>
        /// Modal consent. Returns the consented process names, or null when the user cancelled.
        /// </summary>
        IReadOnlyList<string> ShowConsentDialog(RestorePlan plan);

        /// <summary>Snapshot-override prompt. Implementations MUST default to false (No).</summary>
        bool ConfirmSnapshotOverride(string text, string caption);

        /// <summary>
        /// Replaces the MessageBox at ConfPageView.cs:820 (fail-closed plan composition).
        /// </summary>
        void ShowPlanCompositionError(string text, string caption);

        /// <summary>Replaces btnRestartExplorer.Visible toggling in stage 8.</summary>
        void SetExplorerRestartVisible(bool visible);
    }
}
