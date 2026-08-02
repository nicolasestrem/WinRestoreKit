using System;
using System.Collections.Generic;

namespace WinRestoreKit
{
    /// <summary>
    /// Why a module will not be restored, or <see cref="None"/> when it will be.
    /// </summary>
    public enum RestoreBlock
    {
        /// <summary>Nothing stands in the way.</summary>
        None,

        /// <summary>The user did not agree to close the process that owns the files.</summary>
        ConsentWithheld,

        /// <summary>The process was asked to close before the snapshot and did not.</summary>
        CouldNotClose,

        /// <summary>
        /// The chosen backup folder holds nothing for this module, so there is nothing to overwrite.
        /// </summary>
        /// <remarks>
        /// Decided before anything is closed. What the user ticked in the tree is independent of what
        /// the backup folder contains, so this is reachable in ordinary use - and without it the
        /// orchestrator closes an application, copies its live profile into a snapshot nobody asked
        /// for, and only then reports that nothing was backed up for it. The user pays real, visible
        /// work for a no-op that was knowable in advance.
        /// </remarks>
        NothingToRestore
    }

    /// <summary>
    /// Which modules a restore is going to overwrite, and which of those the snapshot must capture.
    /// </summary>
    /// <remarks>
    /// Both halves of the pipeline ask THIS type, and that is the entire point of it existing.
    /// They used to decide separately from two readings of the same fact: the snapshot set was chosen
    /// from the up-front close result, and the dispatch loop re-read the process with a fresh
    /// IsProcessRunning tens of seconds later. Those disagree in a way that is not exotic -
    /// CloseProcess reports StillRunning whenever its shared five-second budget runs out, which a
    /// Chrome tree of twenty-odd processes does routinely, and every one of them is gone by the time
    /// the snapshot finishes. The module was therefore left out of the snapshot and then restored
    /// anyway, overwriting a live profile with nothing on disk to undo it, while restore_log.txt
    /// recorded that a snapshot had completed.
    ///
    /// The invariant that prevents that is one sentence, and <c>RestoreScopeTests</c> asserts it over
    /// every combination: <b>a module the snapshot leaves out is a module the restore refuses.</b>
    /// </remarks>
    public sealed class RestoreScopeEntry
    {
        public BackupBase Module { get; }

        /// <summary>Why this module will not be restored. <see cref="RestoreBlock.None"/> if it will.</summary>
        public RestoreBlock Block { get; }

        /// <summary>The requirement that blocked it, or null when nothing did.</summary>
        public RestoreCloseRequirement BlockedBy { get; }

        /// <summary>The close outcome behind a <see cref="RestoreBlock.CouldNotClose"/>.</summary>
        public CloseResult BlockedClose { get; }

        /// <summary>Whether the pre-restore snapshot needs to capture this module.</summary>
        public bool NeedsSnapshot { get; }

        internal RestoreScopeEntry(BackupBase module, RestoreBlock block,
                                   RestoreCloseRequirement blockedBy, CloseResult blockedClose,
                                   bool needsSnapshot)
        {
            Module = module;
            Block = block;
            BlockedBy = blockedBy;
            BlockedClose = blockedClose;
            NeedsSnapshot = needsSnapshot;
        }

        public bool WillBeRestored => Block == RestoreBlock.None;
    }

    public static class RestoreScope
    {
        /// <summary>
        /// Works out, once, what the restore is going to do to each module.
        /// </summary>
        /// <param name="closedUpFront">
        /// What happened when the consented processes were closed before the snapshot. This is the
        /// only reading of that fact anyone is allowed to take: re-observing it later is the defect
        /// this type exists to prevent.
        /// </param>
        /// <param name="restoreSourcePath">
        /// The backup folder being restored from, so a module with nothing there is refused before
        /// anything is closed on its behalf. Null skips that check.
        /// </param>
        public static IReadOnlyList<RestoreScopeEntry> For(IReadOnlyList<BackupBase> modules,
                                                           IReadOnlyList<string> consented,
                                                           IDictionary<string, CloseResult> closedUpFront,
                                                           string restoreSourcePath = null)
        {
            List<RestoreScopeEntry> entries = new List<RestoreScopeEntry>();

            int count = modules == null ? 0 : modules.Count;

            for (int i = 0; i < count; i++)
            {
                BackupBase module = modules[i];

                if (module != null)
                    entries.Add(Evaluate(module, consented, closedUpFront, restoreSourcePath));
            }

            return entries;
        }

        private static RestoreScopeEntry Evaluate(BackupBase module, IReadOnlyList<string> consented,
                                                  IDictionary<string, CloseResult> closedUpFront,
                                                  string restoreSourcePath)
        {
            // First, because it is the only refusal that costs the user nothing. Everything below
            // decides what to close and what to capture on behalf of a restore that, if there is no
            // artifact, is never going to write anything.
            if (restoreSourcePath != null && !HasBackup(module, restoreSourcePath))
                return Blocked(module, RestoreBlock.NothingToRestore, null, CloseResult.NotRunning);

            IReadOnlyList<RestoreCloseRequirement> requirements =
                module.ProcessesToCloseBeforeRestore ?? new RestoreCloseRequirement[0];

            for (int i = 0; i < requirements.Count; i++)
            {
                RestoreCloseRequirement requirement = requirements[i];

                if (requirement == null || !requirement.NeedsConsent)
                    continue;

                if (!IsConsented(consented, requirement.ProcessName))
                    return Blocked(module, RestoreBlock.ConsentWithheld, requirement, CloseResult.NotRunning);

                CloseResult closed;

                if (closedUpFront != null
                    && closedUpFront.TryGetValue(requirement.ProcessName, out closed)
                    && (closed == CloseResult.StillRunning || closed == CloseResult.AccessDenied))
                {
                    return Blocked(module, RestoreBlock.CouldNotClose, requirement, closed);
                }
            }

            // Snapshotted only if it is going to write something. AStoreApps opts out because its
            // restore opens a dialog and returns Skipped; without that, every restore including it
            // would pay a full winget export to protect a restore that writes nothing.
            return new RestoreScopeEntry(module, RestoreBlock.None, null, CloseResult.NotRunning,
                needsSnapshot: module.RestoreMakesChanges);
        }

        // A blocked module is never snapshotted: nothing is going to write to it, so a snapshot would
        // spend a browser-profile copy protecting files that are not at risk - and copying a profile
        // whose process refused to close would only manufacture a gate failure out of a restore that
        // was never going to touch it.
        private static RestoreScopeEntry Blocked(BackupBase module, RestoreBlock block,
                                                 RestoreCloseRequirement requirement, CloseResult closed)
            => new RestoreScopeEntry(module, block, requirement, closed, needsSnapshot: false);

        /// <summary>
        /// Asks the module whether the backup folder holds anything for it, treating a module that
        /// throws as having something.
        /// </summary>
        /// <remarks>
        /// The catch decides which way an unreliable answer falls, and it falls towards restoring.
        /// A module whose probe throws has told us nothing, and silently skipping a restore the user
        /// asked for is worse than an unnecessary close - one loses their intent, the other loses
        /// their tabs.
        ///
        /// INTERNAL, not private, because ConfPageView.ProcessesWorthClosing asks the same question
        /// of the same modules moments earlier and must get the same answer. It briefly had its own
        /// copy of this method - added in Phase 3b when the artifact-aware probes gained a throwing
        /// path and that call site turned out to have no guard at all - and two implementations of
        /// one rule is the bug class this type exists to remove: if either catch is ever narrowed
        /// or either fallback flipped, ProcessesWorthClosing closes an app that Evaluate then
        /// blocks as NothingToRestore, and the user pays their open tabs for a module that is never
        /// restored. One rule, one place, both callers.
        /// </remarks>
        internal static bool HasBackup(BackupBase module, string restoreSourcePath)
        {
            try
            {
                return module.HasBackupIn(restoreSourcePath);
            }
            catch (Exception)
            {
                return true;
            }
        }

        public static bool IsConsented(IReadOnlyList<string> consented, string processName)
        {
            if (consented == null)
                return false;

            for (int i = 0; i < consented.Count; i++)
            {
                if (string.Equals(consented[i], processName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// The step recording why a blocked module was not restored.
        /// </summary>
        /// <remarks>
        /// Worded to match <see cref="RestoreDispatch"/>'s own refusals, so a module refused here and
        /// one refused there read identically to the user - they are the same refusal, reached by
        /// different evidence.
        /// </remarks>
        public static StepResult DescribeBlock(RestoreScopeEntry entry)
        {
            if (entry == null || entry.Block == RestoreBlock.None)
                throw new ArgumentException("Only a blocked module has a block to describe.", nameof(entry));

            string title = entry.Module.Title;

            // Worded exactly as the module's own restore would have worded it, because that is what
            // the user saw before this check existed and it is still the true statement - the only
            // change is that we now find out before closing anything rather than after.
            if (entry.Block == RestoreBlock.NothingToRestore)
                return StepResult.Skipped(title, "nothing was backed up for this item");

            string name = entry.BlockedBy.DisplayName;

            return entry.Block == RestoreBlock.ConsentWithheld
                ? StepResult.Skipped(title, "you chose not to close " + name + ", so its profile was not restored")
                : StepResult.Failed(title, name + " could not be closed, so its profile was not restored");
        }
    }
}
