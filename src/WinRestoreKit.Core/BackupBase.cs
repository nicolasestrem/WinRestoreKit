using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace WinRestoreKit
{
    public abstract class BackupBase
    {
        // Property to indicate whether a restart is required
        public virtual bool RequiresExplorerRestart { get; protected set; } = false;

        // Property to display Hints
        public virtual string WarningMessage { get; protected set; } = "";

        /// <summary>
        /// What an absent source means on the RESTORE side, where the source is the backup folder.
        /// </summary>
        /// <remarks>
        /// Shared so the restore side cannot drift back into backup-side wording. Saying "not
        /// present on this system" during a restore describes the wrong machine: the thing that is
        /// missing is the backup, and the live machine was never examined. Several modules already
        /// spelled this sentence out by hand; this is the same sentence, in one place.
        /// </remarks>
        protected const string NothingBackedUp = "nothing was backed up for this item";

        /// <summary>
        /// Whether <paramref name="restorePath"/> actually holds anything for this module to restore.
        /// </summary>
        /// <remarks>
        /// What the user ticks in the tree is independent of what the chosen backup folder contains,
        /// so a module can be selected for restore with nothing there to restore from. Answering
        /// honestly here is what stops the orchestrator closing an application for an operation that
        /// was always going to be a no-op: force-killing a browser costs the user every open tab, and
        /// before this phase nothing closed it at all, so getting this wrong is a regression rather
        /// than merely a missed improvement.
        ///
        /// The default is TRUE - "assume there is something" - because a module that has not been
        /// taught to check must not be quietly skipped. Being wrong that way costs an unnecessary
        /// close; being wrong the other way silently cancels a restore the user asked for.
        /// </remarks>
        public virtual bool HasBackupIn(string restorePath) => true;

        /// <summary>
        /// Whether <paramref name="backupPath"/> demonstrably holds this module's artifacts:
        /// TRUE when they are there, FALSE when they provably are not, and NULL when this module
        /// cannot tell.
        /// </summary>
        /// <remarks>
        /// Deliberately NOT <see cref="HasBackupIn"/>, despite asking a similar-sounding question.
        /// That one decides whether to CLOSE AN APPLICATION for a restore, so it fails open: its
        /// documented default is true and two subclasses answer true without probing whenever no
        /// process has to die. Correct there, and useless here - the restore wizard asks "what is
        /// actually in this folder", and a seam that says yes by default answers every module yes,
        /// which is the same as not asking.
        ///
        /// Hence three states rather than two. A module that has not been taught to look says NULL
        /// and the caller decides what silence means; it must never be forced to guess, because
        /// both guesses are wrong in a way the user pays for - false hides a restore they asked
        /// for, true offers one that was never there.
        /// </remarks>
        public virtual bool? HasArtifactIn(string backupPath) => null;

        /// <summary>
        /// Whether this module's live state differs from the state captured in
        /// <paramref name="backupPath"/>.
        /// </summary>
        /// <remarks>
        /// A module that cannot compare its live state with the backup returns null rather than
        /// guessing. Callers render only confirmed drift.
        /// </remarks>
        public virtual bool? HasDriftedFrom(string backupPath) => null;

        /// <summary>
        /// Compares a recorded registry export with a fresh, temporary export of the live key.
        /// </summary>
        /// <remarks>
        /// The fresh export is deliberately outside the backup directory. Drift reads must never
        /// clear, replace, or add to an existing snapshot. A missing or unreadable recorded artifact,
        /// an indeterminate probe, and a failed fresh export all return null because none establishes
        /// a comparison.
        /// </remarks>
        protected static bool? HasDriftedFromRegistryArtifact(string artifactPath, string registryPath)
        {
            if (RegFile.Validate(artifactPath) != RegFileCheck.Valid)
                return null;

            KeyProbe probe = Utils.ProbeKey(registryPath);

            if (probe == KeyProbe.Indeterminate)
                return null;

            // A valid export records a present key. The live key's confirmed absence is therefore
            // real drift, without relying on a file timestamp.
            if (probe == KeyProbe.Absent)
                return true;

            string currentArtifact = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".reg");

            if (IsInSameDirectory(currentArtifact, artifactPath))
                return null;

            try
            {
                StepResult export = Utils.ExportRegistryKey(currentArtifact, registryPath, absenceIsNormal: false);

                if (export.State != ResultState.Succeeded)
                    return null;

                bool? same = RegFile.HasSameCanonicalContent(artifactPath, currentArtifact);
                return same.HasValue ? !same.Value : null;
            }
            finally
            {
                try { File.Delete(currentArtifact); } catch { }
            }
        }

        private static bool IsInSameDirectory(string firstPath, string secondPath)
        {
            try
            {
                string firstDirectory = Path.GetDirectoryName(Path.GetFullPath(firstPath));
                string secondDirectory = Path.GetDirectoryName(Path.GetFullPath(secondPath));

                return string.Equals(firstDirectory, secondDirectory, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return true;
            }
        }

        /// <summary>
        /// The backup file this module writes for one registry key.
        /// </summary>
        /// <remarks>
        /// A loop over N keys must produce N distinct filenames. Six modules each carried their own
        /// copy of this line, and the seventh - WThemes - built the name from the Title alone inside
        /// a foreach over its keys, so every key resolved to one file. That was harmless only while
        /// it had a single key, and the fix for its other defect was to add a second one.
        ///
        /// What made it worth writing once rather than fixing in place: with two keys the second
        /// export deletes the first (Utils.TryDeleteExport) and writes over it while both steps
        /// report Succeeded, and the restore imports that one file once per key while the
        /// post-import probe finds every key present, because the keys exist on the live machine
        /// regardless of what the file contained. Every row green, one key never captured.
        /// </remarks>
        protected virtual string RegFileNameFor(string key)
            => $"{Title}_{GetSafeFileName(key)}.reg";

        /// <summary>
        /// The directory name a module writes one backed-up folder under: <c>{Title}_{leaf}</c>.
        /// </summary>
        /// <remarks>
        /// The folder-side twin of <see cref="RegFileNameFor"/>, and extracted for the same reason
        /// that one was: WFonts, WTaskbar and WThemes each built this expression inline, four sites
        /// between them plus a private helper, and <see cref="HasArtifactIn"/> needed a fifth. A
        /// probe that spells the name even slightly differently from the writer reports "nothing in
        /// this backup" over a folder that is full, and disables the checkbox saying so - a silent
        /// wrong answer of exactly the kind RegFileNameFor's remarks were written about.
        /// </remarks>
        protected string BackupFolderNameFor(string folder)
            => $"{Title}_{GetSafeFileName(Path.GetFileName(folder))}";

        /// <summary>
        /// <see cref="HasArtifactIn"/> for the modules that back up a list of folders plus a list of
        /// registry keys: true when any one of those artifacts is present.
        /// </summary>
        /// <remarks>
        /// ANY, not all. These modules aggregate independently restorable halves - WThemes' wallpaper
        /// folder and its Control Panel keys - and a machine that had one but not the other still
        /// produced a backup worth offering.
        ///
        /// A folder must be non-empty to count, because Utils.CopyFolder creates its destination
        /// before copying: a run where every file failed leaves the directory behind.
        /// </remarks>
        protected bool HasFolderOrKeyArtifactIn(string backupPath, List<string> folders, List<string> keys)
        {
            if (string.IsNullOrWhiteSpace(backupPath))
                return false;

            if (folders != null)
            {
                foreach (string folder in folders)
                {
                    string dir = Path.Combine(backupPath, BackupFolderNameFor(folder));

                    if (!Directory.Exists(dir))
                        continue;

                    // foreach so the enumerator - which holds a directory handle - is disposed.
                    foreach (string unused in Directory.EnumerateFileSystemEntries(dir))
                        return true;
                }
            }

            if (keys != null)
            {
                foreach (string key in keys)
                {
                    if (File.Exists(Path.Combine(backupPath, RegFileNameFor(key))))
                        return true;
                }
            }

            return false;
        }

        // There is deliberately no RegFileNamesToTryOnRestore companion to the above.
        //
        // One was written for this phase, to let a module whose key path changed offer the older
        // filename as a fallback on restore. It was removed before merge because nothing called it:
        // every module's restore composes its path from RegFileNameFor directly, so an override
        // would have been a silent no-op - a trap wearing the costume of a safety feature, and the
        // exact drift the writer half exists to prevent.
        //
        // The one candidate consumer, WTelemetry's control-set correction, turned out to need more
        // than a filename anyway: the older file's CONTENTS name ControlSet001, so applying it would
        // rewrite the stale hive the correction exists to stop using. A fallback there has to
        // rewrite the payload, not just find it, and it has to be covered by the pre-restore
        // snapshot before it can honestly claim to be undoable. That is a design problem, not a
        // naming one, and it is filed rather than half-built here.

        /// <summary>
        /// Turns a registry key path or folder name into something usable as a filename.
        /// </summary>
        /// <remarks>
        /// Hoisted from six identical private copies. They differed only in the order of the
        /// Replace calls, which cannot matter here: every replacement maps to the same character,
        /// and no replacement produces a character another one searches for.
        ///
        /// The six copies each listed the characters their author happened to think of, and all six
        /// missed the same ones - notably &lt; &gt; and | which Windows also rejects in a filename,
        /// and the control characters. Asking the framework which characters are invalid removes the
        /// list from this file entirely, so it cannot be short again. Registry key paths contain
        /// none of the newly covered characters, so every filename this already produced is
        /// unchanged; the pinned literals in BackupFileNamingTests are what holds that.
        /// </remarks>
        protected static string GetSafeFileName(string value)
        {
            if (value == null)
                return string.Empty;

            char[] invalid = Path.GetInvalidFileNameChars();
            StringBuilder safe = new StringBuilder(value.Length);

            foreach (char c in value)
                safe.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);

            return safe.ToString();
        }

        // An AllowPrompts flag lived here until Phase 3a: shared mutable state, set on the module
        // instance before each backup and reset in a finally, that told the retired browser
        // modules not to prompt during the pre-restore snapshot. It went with its last consumers -
        // no shipped module prompts during backup at all now. A future module that genuinely must
        // ask something at backup time should receive that permission as a PARAMETER on the call
        // that needs it, not as instance state a caller has to remember to reset; and it must
        // never prompt on the snapshot path, where a thread-pool MessageBox has no owner and a
        // declined prompt reads as "nothing to back up" at the snapshot gate while the restore
        // goes on to overwrite the uncaptured module.

        // Property to display Info
        public string Title { get; set; }

        public string Info { get; set; }
        public string Version { get; set; }

        public virtual bool IsInstalled()
        { return false; }

        /// <summary>
        /// What this module's restore overwrites, in the words the confirmation dialog shows.
        /// </summary>
        /// <remarks>
        /// The default is a loud marker for the same reason the Backup/Restore defaults below are a
        /// failure rather than a skip: a future author who forgets produces something visible in the
        /// text users read before consenting, not silence. Silence here is worse than a wrong entry,
        /// because it asks for consent to an operation whose scope was never stated.
        /// </remarks>
        public virtual IReadOnlyList<RestoreTarget> RestoreTargets
            => RestoreTarget.Undeclared(GetType().Name);

        /// <summary>
        /// Processes that must not be running while this module's restore writes their files.
        /// </summary>
        /// <remarks>
        /// A declaration, not an action. The orchestrator decides and closes; module signatures do
        /// not change, so no module opens a dialog from the thread it happens to be running on.
        /// </remarks>
        public virtual IReadOnlyList<RestoreCloseRequirement> ProcessesToCloseBeforeRestore
            => new RestoreCloseRequirement[0];

        /// <summary>
        /// Whether this module's restore writes anything, and so needs a pre-restore snapshot.
        /// </summary>
        /// <remarks>
        /// Defaults to true so a future module is snapshotted unless its author deliberately opts
        /// out. This is the same judgement call as absenceIsNormal and fails the same way in both
        /// directions: set wrong here, a module either pays for a snapshot it cannot use or is
        /// silently exempted from the one thing that would undo it.
        /// </remarks>
        public virtual bool RestoreMakesChanges => true;

        /// <remarks>
        /// The default is a FAILURE, not a Skip. It is unreachable for all 23 shipped modules -
        /// ConfPageView only ever calls the async pair, and every module implements one side or the
        /// other - so it fires only for a future module whose author forgot to implement backup.
        /// That is a bug, and a bug that announces itself beats one that returns a reassuring
        /// "nothing to do" and is never noticed.
        /// </remarks>
        public virtual ModuleResult Backup(string path)
            => ModuleResult.Aggregate(new[]
            {
                StepResult.Failed(GetType().Name, "this module does not implement backup")
            });

        public virtual ModuleResult Restore(string path)
            => ModuleResult.Aggregate(new[]
            {
                StepResult.Failed(GetType().Name, "this module does not implement restore")
            });

        public virtual async Task<ModuleResult> BackupAsync(string path)
        {
            return await Task.Run(() => Backup(path)).ConfigureAwait(true);
        }

        public virtual async Task<ModuleResult> RestoreAsync(string path)
        {
            return await Task.Run(() => Restore(path)).ConfigureAwait(true);
        }
    }
}
