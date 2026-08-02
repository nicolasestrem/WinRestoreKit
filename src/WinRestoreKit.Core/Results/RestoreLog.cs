using System;
using System.Collections.Generic;
using System.Globalization;

namespace WinRestoreKit
{
    /// <summary>
    /// Composes restore_log.txt: what a restore changed, and what could undo it.
    /// </summary>
    /// <remarks>
    /// Pure composition. It returns a string and never touches the file system, mirroring BackupLog:
    /// the view owns the write, so the text a user will read is assertable in the suite instead of
    /// only on real hardware.
    ///
    /// The per-module section is delegated to BackupLog.Compose rather than reimplemented. That
    /// composer is already direction-neutral - it takes modules, results and a "when" - so a second
    /// renderer here would be a second place for the skipped-vs-failed labels to drift.
    /// </remarks>
    internal static class RestoreLog
    {
        // Older backups on disk still carry the Appcopier header, which is fine because readers never parse it.
        internal const string VersionHeader = "# WinRestoreKit restore log v1";

        /// <summary>The name used when the log goes into the snapshot folder, beside the rollback artifact.</summary>
        internal const string FileName = "restore_log.txt";

        /// <summary>
        /// The line for a restore that the user allowed to run past a snapshot that did not complete.
        /// </summary>
        /// <remarks>
        /// This is the whole reason the file exists. Someone reading it after a bad restore is
        /// looking for the folder that undoes it; if there is none, that has to be the first thing
        /// they see, not a detail below a list of modules.
        /// </remarks>
        internal const string NoSnapshotWarning =
            "# !! THIS RESTORE RAN WITHOUT A COMPLETE PRE-RESTORE SNAPSHOT. The snapshot did not " +
            "complete and you chose to continue anyway, so the changes below may not be undoable.";

        internal const string SnapshotTakenLine = "# A pre-restore snapshot completed before this restore.";

        private const string SnapshotUnrecordedLine =
            "# !! The pre-restore snapshot outcome was not recorded, so whether this restore can be undone is unknown.";

        /// <summary>Matches the snapshot folder format, and contains no character Windows rejects in a name.</summary>
        private const string FileNameTimestampFormat = "yyyy-MM-dd - HH.mm.ss";

        /// <param name="restoredFrom">The backup folder this restore read from.</param>
        /// <param name="when">Rendered by BackupLog as the log's timestamp line.</param>
        /// <param name="snapshot">
        /// The gate's verdict. Null is reported rather than assumed benign - a missing verdict is not
        /// evidence that the snapshot worked.
        /// </param>
        /// <param name="snapshotPath">Where the snapshot went, or null/empty when no folder exists.</param>
        internal static string Compose(IReadOnlyList<BackupBase> modules,
                                       IReadOnlyList<ModuleResult> results,
                                       string when,
                                       string restoredFrom,
                                       SnapshotDecision snapshot,
                                       string snapshotPath)
        {
            return BackupLog.Compose(modules, results, when,
                Header(restoredFrom, snapshot, snapshotPath));
        }

        /// <summary>
        /// The name to write the log under when it cannot go in the snapshot folder, because the
        /// gate was overridden after the folder could not be created.
        /// </summary>
        /// <remarks>
        /// Timestamped because this file lands in the restored-from folder, which - unlike a snapshot
        /// folder - is reused by every restore made from that backup. A fixed name would let the
        /// second restore erase the record of the first.
        /// </remarks>
        internal static string FallbackFileName(DateTime when)
            => "restore_log " + when.ToString(FileNameTimestampFormat, CultureInfo.InvariantCulture) + ".txt";

        private static IEnumerable<string> Header(string restoredFrom, SnapshotDecision snapshot,
                                                  string snapshotPath)
        {
            yield return VersionHeader;

            yield return "# Restored from:";
            yield return "#   " + (string.IsNullOrWhiteSpace(restoredFrom)
                ? "(the source folder was not recorded)"
                : restoredFrom.Trim());

            yield return "#";

            foreach (string line in SnapshotLines(snapshot, snapshotPath))
                yield return line;

            yield return "#";
            yield return "# " + RestorePlan.FidelityCaveat;
            yield return "#";
        }

        private static IEnumerable<string> SnapshotLines(SnapshotDecision snapshot, string snapshotPath)
        {
            if (snapshot == null)
            {
                yield return SnapshotUnrecordedLine;
                yield break;
            }

            // RequiresOverride means the gate stopped the restore and the user pushed past it: the
            // restore happened DESPITE the snapshot, not on the strength of it.
            //
            // Everything else defers to the verdict, and ONLY Complete may say the snapshot
            // completed. Stated as an allowlist rather than by excluding the states that must not
            // claim it, because excluding them is what went wrong: PartiallyCaptured was added to
            // the gate and inherited the completion line by default, so a restore that overwrote
            // items the snapshot had nothing to save for was recorded as fully protected. A verdict
            // added later must fall back to its own summary, not to a claim nobody checked.
            bool claimsCompletion =
                !snapshot.RequiresOverride && snapshot.Verdict == SnapshotVerdict.Complete;

            if (snapshot.RequiresOverride)
                yield return NoSnapshotWarning;
            else if (claimsCompletion)
                yield return SnapshotTakenLine;
            else
                yield return "# " + snapshot.Summary;

            if (claimsCompletion || snapshot.RequiresOverride)
                yield return "# " + snapshot.Summary;

            for (int i = 0; i < snapshot.Failures.Count; i++)
                yield return "#   " + snapshot.Failures[i];

            if (!string.IsNullOrWhiteSpace(snapshotPath))
            {
                yield return "# Snapshot folder:";
                yield return "#   " + snapshotPath.Trim();
            }
        }
    }
}
