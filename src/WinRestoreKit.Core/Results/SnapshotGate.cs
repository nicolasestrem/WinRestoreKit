using System.Collections.Generic;
using System.Text;

namespace WinRestoreKit
{
    /// <summary>
    /// What the pre-restore snapshot turned out to be.
    /// </summary>
    public enum SnapshotVerdict
    {
        /// <summary>Every item that needed snapshotting was captured.</summary>
        Complete,

        /// <summary>
        /// Some items were captured and others had nothing to save, so part of what this restore
        /// overwrites has no fallback.
        /// </summary>
        /// <remarks>
        /// Distinct from <see cref="Complete"/> on purpose. These used to fold together, and a run
        /// that captured four of five items reported the same verdict as one that captured all five.
        /// </remarks>
        PartiallyCaptured,

        /// <summary>Nothing was captured, and nothing failed: an empty set, or every item skipped.</summary>
        NothingCaptured,

        /// <summary>At least one item failed to be captured.</summary>
        ModulesFailed,

        /// <summary>The snapshot folder itself could not be created, so nothing was attempted.</summary>
        FolderNotCreated
    }

    /// <summary>
    /// Whether a restore may proceed on the strength of the snapshot that was just taken.
    /// </summary>
    /// <remarks>
    /// Carries the failure text as data rather than showing anything. The dialog belongs to the
    /// caller on the UI thread; a decision type that raised its own MessageBox would be the
    /// worker-thread dialog pattern this phase exists to remove.
    ///
    /// <see cref="Summary"/> is written to restore_log.txt in every branch, including the ones that
    /// proceed, so the record says which path was taken rather than only that a restore happened.
    /// </remarks>
    internal sealed class SnapshotDecision
    {
        public SnapshotVerdict Verdict { get; }

        /// <summary>
        /// True when the user must confirm a second time before anything is overwritten.
        /// </summary>
        /// <remarks>
        /// Proceeding silently is never an option here: a restore with no working snapshot is the
        /// unsafe behaviour this phase removes, and doing it after OFFERING a snapshot is worse than
        /// never offering one, because the user believes they have a fallback.
        /// </remarks>
        public bool RequiresOverride { get; }

        /// <summary>One line per thing that went wrong. Empty when nothing did.</summary>
        public IReadOnlyList<string> Failures { get; }

        /// <summary>One line for the log and for the top of the override prompt.</summary>
        public string Summary { get; }

        private SnapshotDecision(SnapshotVerdict verdict, bool requiresOverride,
                                 string summary, IReadOnlyList<string> failures)
        {
            Verdict = verdict;
            RequiresOverride = requiresOverride;
            Summary = summary;
            Failures = failures;
        }

        /// <summary>The summary followed by one line per failure, ready to drop into a dialog.</summary>
        public string Describe()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(Summary);

            for (int i = 0; i < Failures.Count; i++)
                sb.AppendLine("  " + Failures[i]);

            return sb.ToString();
        }

        internal static SnapshotDecision Create(SnapshotVerdict verdict, bool requiresOverride,
                                                string summary, IReadOnlyList<string> failures)
            => new SnapshotDecision(verdict, requiresOverride, summary,
                   failures ?? (IReadOnlyList<string>)new string[0]);
    }

    /// <summary>
    /// The pure decision Phase 2b's snapshot exists to make possible: given what the snapshot
    /// actually produced, may the restore go ahead?
    /// </summary>
    /// <remarks>
    /// A snapshot whose failure could not be DETECTED would be decoration. This is the detection;
    /// it reads the same ModuleResults the snapshot's own backup_log.txt is composed from, so there
    /// is no second notion of what "the snapshot worked" means.
    /// </remarks>
    internal static class SnapshotGate
    {
        /// <summary>
        /// The row for a snapshot that never started, because its folder could not be created.
        /// </summary>
        internal static SnapshotDecision FolderNotCreated(string error)
        {
            string detail = string.IsNullOrWhiteSpace(error)
                ? "the reason was not reported"
                : error.Trim();

            return SnapshotDecision.Create(
                SnapshotVerdict.FolderNotCreated,
                true,
                "The pre-restore snapshot folder could not be created, so nothing was backed up before this restore.",
                new[] { detail });
        }

        /// <summary>
        /// The row for a snapshot that ran. <paramref name="outcomes"/> holds one entry per module
        /// that was snapshotted, which is the selection minus the modules whose restore changes
        /// nothing - so an empty list is a legitimate state, not a missing argument.
        /// </summary>
        /// <param name="blockedCount">
        /// How many selected modules the restore is refusing before it starts. An empty
        /// <paramref name="outcomes"/> means something different depending on this: nothing needed
        /// capturing, or everything that did was blocked. Saying the first when the second is true
        /// tells the user their selection changes nothing, which is false.
        ///
        /// Only the EMPTY-outcome call site in ConfPageView.TakeSnapshot passes it. The one site
        /// that carries non-empty outcomes - the ModuleOutcome.Pair call at the end of TakeSnapshot -
        /// calls Evaluate without it, so on that path blockedCount is always 0 and the branches below
        /// that read it are unreachable whenever any module was actually considered.
        /// </param>
        internal static SnapshotDecision Evaluate(IReadOnlyList<ModuleOutcome> outcomes, int blockedCount = 0)
        {
            List<string> failures = new List<string>();
            List<string> notCaptured = new List<string>();
            int considered = 0;
            int captured = 0;

            int count = outcomes == null ? 0 : outcomes.Count;

            for (int i = 0; i < count; i++)
            {
                ModuleOutcome outcome = outcomes[i];

                // Counted BEFORE the null check, and a null entry is a failure rather than a skip.
                // Skipping it before counting made an all-null list indistinguishable from an empty
                // one, which then reported "none of the selected items change anything when
                // restored" - the exact false sentence the blockedCount branch below was added to
                // remove, reached through a different door. ModuleOutcome.Pair emits no nulls
                // today; folding them in here makes that invariant structural rather than
                // coincidental. A null entry adds a line of its own, so it can never displace or
                // hide a real, named failure.
                considered++;

                // A module that produced no result did not report success - it reported nothing,
                // and an unrun module is exactly the case the override prompt exists for. An entry
                // that is itself null cannot even name which module it was, so it says so.
                if (outcome == null)
                {
                    failures.Add(Describe(null, "no module and no result were recorded"));
                    continue;
                }

                if (outcome.Result == null)
                {
                    failures.Add(Describe(outcome.Title, "no result was recorded"));
                    continue;
                }

                switch (outcome.Result.State)
                {
                    case ResultState.Failed:
                        failures.Add(Describe(outcome.Title, outcome.Result.Reason));
                        break;
                    case ResultState.Succeeded:
                        captured++;
                        break;

                    // Reported, never dropped. Every module reaching this gate is one the restore is
                    // about to overwrite, so a skip means that particular item has no fallback -
                    // restoring it will write state the snapshot cannot put back. It does not force
                    // an override, because on this path a skip means the item had nothing to capture
                    // rather than that the capture refused: the snapshot runs with prompts
                    // suppressed, so a module cannot decline it. Silently dropping it was how a
                    // module could be listed as snapshotted while nothing of it was saved.
                    default:
                        notCaptured.Add(Describe(outcome.Title, outcome.Result.Reason));
                        break;
                }
            }

            if (failures.Count > 0)
            {
                string summary = string.Format(
                    "The pre-restore snapshot could not capture {0} of {1} item(s), so this restore cannot be fully undone:",
                    failures.Count, considered);

                return SnapshotDecision.Create(SnapshotVerdict.ModulesFailed, true, summary, failures);
            }

            // Nothing failed and nothing was captured. This proceeds, but it must SAY so: a user
            // told "snapshot taken" who then needs to roll back would find an empty folder.
            if (captured == 0)
            {
                string summary;

                if (considered > 0)
                {
                    summary = string.Format(
                        "The pre-restore snapshot captured nothing: none of the {0} item(s) had anything to save.",
                        considered);
                }
                else if (blockedCount > 0)
                {
                    summary = string.Format(
                        "No pre-restore snapshot was taken: {0} item(s) are not being restored, and nothing else needed capturing.",
                        blockedCount);
                }
                else
                {
                    summary = "No pre-restore snapshot was taken: none of the selected items change anything when restored.";
                }

                return SnapshotDecision.Create(SnapshotVerdict.NothingCaptured, false, summary, notCaptured);
            }

            if (notCaptured.Count > 0)
            {
                // Deliberately NOT the Complete verdict. Some of what is about to be overwritten has
                // no fallback, and a summary reading "captured 4 of 5" under a heading that says the
                // snapshot completed is the partial-reported-as-whole failure this project exists to
                // remove.
                string summary = string.Format(
                    "The pre-restore snapshot captured {0} of {1} item(s). The rest had nothing to save, so restoring them cannot be undone:",
                    captured, considered);

                return SnapshotDecision.Create(SnapshotVerdict.PartiallyCaptured, false, summary, notCaptured);
            }

            return SnapshotDecision.Create(
                SnapshotVerdict.Complete,
                false,
                string.Format("The pre-restore snapshot captured all {0} item(s).", captured),
                null);
        }

        private static string Describe(string title, string reason)
        {
            string named = string.IsNullOrWhiteSpace(title) ? "(unnamed item)" : title;

            return string.IsNullOrWhiteSpace(reason) ? named : named + ": " + reason;
        }
    }
}
