using System.Collections.Generic;

namespace WinRestoreKit
{
    /// <summary>
    /// Whether a restore has left Explorer showing stale state, so the restart button is worth offering.
    /// </summary>
    /// <remarks>
    /// Extracted from ConfPageView in Phase 3c, for the reason SnapshotGate and RestoreDispatch were:
    /// this is a decision, it was wrong, and while it lived inline in a UserControl the suite could not
    /// instantiate, nothing could pin it.
    ///
    /// THE DEFECT THIS FIXES. The button used to be gated on the module's whole ModuleResult being
    /// Succeeded. That was correct while every module declaring RequiresExplorerRestart wrote exactly
    /// one thing - Failed then really did mean nothing was written, so offering a restart would have
    /// been a no-op dressed up as a fix.
    ///
    /// It stopped being correct the moment those modules became multi-step. ModuleResult.Aggregate
    /// lets any single failed step dominate the verdict, so a restore that copied 32 pinned shortcuts
    /// and imported the Taskband key successfully, then failed on a third step, reports Failed - and
    /// the button vanished. WTaskbar's own WarningMessage tells the user to press that button BEFORE
    /// signing out, because a live Explorer will otherwise write its in-memory pin list back over what
    /// was just restored. So the failure mode was: partially-failed restore hides the one control that
    /// makes the successful part of it stick, having just told the user to use it.
    ///
    /// WThemes has been a folder-plus-two-keys hybrid since Phase 2c and declares
    /// RequiresExplorerRestart too, so it carried the same defect; it is fixed here as well rather
    /// than only in the module that made it obvious.
    ///
    /// The rule is therefore "did this module write ANYTHING", not "did everything succeed". The
    /// original intent survives intact: a module that was skipped outright, or that failed every step,
    /// still offers nothing, because there is genuinely nothing for a restart to reveal.
    /// </remarks>
    internal static class ExplorerRestartPrompt
    {
        /// <summary>
        /// True when some module that needs an Explorer restart actually wrote something.
        /// </summary>
        /// <remarks>
        /// Pairs by index and tolerates a short or ragged results list for the reason
        /// <see cref="ModuleOutcome.Pair"/> does: a module that threw before producing a result must
        /// not take down the decision made about every other module in the run.
        /// </remarks>
        internal static bool IsNeeded(IReadOnlyList<BackupBase> modules, IReadOnlyList<ModuleResult> results)
        {
            if (modules == null || results == null)
                return false;

            for (int i = 0; i < modules.Count && i < results.Count; i++)
            {
                if (modules[i] == null || results[i] == null)
                    continue;

                if (modules[i].RequiresExplorerRestart && WroteSomething(results[i]))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Whether any single sub-operation of this module succeeded.
        /// </summary>
        /// <remarks>
        /// Reads the steps rather than the folded State, which is the whole point: the fold is lossy
        /// in exactly the direction that matters here. Restore-side successes arrive as
        /// StepResult.Applied, which is a Succeeded state, so both wordings are counted.
        ///
        /// A null step is not counted as a success. ModuleResult.Aggregate does not emit nulls, but
        /// the guard costs nothing and the alternative is a NullReferenceException thrown out of a
        /// restore's final UI update, after the destructive work is already done.
        /// </remarks>
        private static bool WroteSomething(ModuleResult result)
        {
            if (result.Steps == null)
                return false;

            foreach (StepResult step in result.Steps)
            {
                if (step != null && step.State == ResultState.Succeeded)
                    return true;
            }

            return false;
        }
    }
}
