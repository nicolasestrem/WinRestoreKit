using WinRestoreKit;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Conf
{
    /// <summary>
    /// A module that backs up exactly one folder by copying it under <c>{Title}</c> in the backup.
    /// </summary>
    /// <remarks>
    /// The folder-copy modules shared byte-identical IsInstalled/RestoreTargets/HasBackupIn
    /// bodies and async pairs; this writes them once. The subclass supplies the folder and its
    /// metadata and inherits the decisions, so the Skipped-vs-Failed rule and the restore-side
    /// wording cannot drift between modules that are supposed to behave identically.
    ///
    /// Deliberately NO close-before-backup consent block, although the retired browser modules
    /// carried one: its only surviving would-be consumer, APinnedApps, deliberately closes
    /// nothing on backup (the Start menu database copies cleanly while its process runs). A
    /// module that must close an app before backing it up is a different shape and should say so
    /// by hand-rolling from BackupBase, where the omission is visible, rather than by flipping a
    /// flag here that nothing exercises.
    /// </remarks>
    public abstract class FolderModule : BackupBase
    {
        /// <summary>The one folder this module copies out on backup and overwrites on restore.</summary>
        /// <remarks>
        /// A public readonly field, set through the constructor: RestoreDeclarationTests reads it
        /// with <c>GetField("Folder")</c> to hold declaration and target in agreement, and
        /// readonly keeps the folder the declaration names identical to the one Restore writes.
        /// </remarks>
        public readonly string Folder;

        protected FolderModule(string folder)
        {
            Folder = folder;
        }

        /// <summary>
        /// Whether this folder can legitimately be missing on a healthy machine.
        /// </summary>
        /// <remarks>
        /// Defaults to true because that is what every existing folder module declares: a profile
        /// folder that is not there means the owning feature has never been used on this account,
        /// not that something broke. Override with false for a folder whose absence is a fault.
        /// </remarks>
        protected virtual bool AbsenceIsNormal => true;

        /// <summary>
        /// Maps the backup-side copy tally onto a step. Override for bespoke backup-side wording.
        /// </summary>
        /// <remarks>
        /// Backup-side only, on purpose. The restore side below always reports an absent source
        /// as <see cref="BackupBase.NothingBackedUp"/>, because there the missing thing is the
        /// backup - a fact no per-module wording may restate as a claim about the live machine.
        ///
        /// private protected because CopyResult is internal: every module lives in this assembly,
        /// so the seam loses nothing, and widening CopyResult instead would leak the tally type
        /// into the public surface for the sake of one override.
        /// </remarks>
        private protected virtual StepResult BackupStepFor(CopyResult copy)
            => copy.ToStep(Title, AbsenceIsNormal);

        public override bool IsInstalled() => Directory.Exists(Folder);

        public override IReadOnlyList<RestoreTarget> RestoreTargets
            => new[] { RestoreTarget.Folder(Folder) };

        /// <remarks>
        /// The real check is earned only by modules that close a process: answering "no" there is
        /// what stops the orchestrator killing an app for a restore that has nothing to copy.
        /// A module that closes nothing keeps the base default of true - the documented invariant
        /// (BackupBase.HasBackupIn) that a module not taught to check must never be quietly
        /// skipped, and the shape RestoreDeclarationTests holds for every close-nothing module.
        /// </remarks>
        public override bool HasBackupIn(string restorePath)
        {
            if (ProcessesToCloseBeforeRestore.Count == 0)
                return true;

            return !string.IsNullOrWhiteSpace(restorePath)
                   && Directory.Exists(Path.Combine(restorePath, Title));
        }

        /// <remarks>
        /// Unconditional, unlike <see cref="HasBackupIn"/> above: that one only earns a real probe
        /// when a process would be killed, because there its false negative cancels a restore. Here
        /// a wrong answer only mis-paints a checkbox, so every folder module can afford to look.
        ///
        /// The directory must also be NON-EMPTY. Utils.CopyFolder creates its destination before
        /// copying, so a backup where every file failed still leaves an empty {Title}\ behind -
        /// exactly the shape FileModule's remarks warn about, and existence alone would call it a
        /// backup.
        /// </remarks>
        public override bool? HasArtifactIn(string backupPath)
        {
            if (string.IsNullOrWhiteSpace(backupPath))
                return false;

            string dir = Path.Combine(backupPath, Title);

            if (!Directory.Exists(dir))
                return false;

            // foreach, not GetEnumerator().MoveNext() - the enumerator holds a directory handle and
            // has to be disposed. Stops after the first entry either way.
            foreach (string unused in Directory.EnumerateFileSystemEntries(dir))
                return true;

            return false;
        }

        public sealed override async Task<ModuleResult> BackupAsync(string path)
        {
            CopyResult copy = await Utils.CopyFolder(Folder, Path.Combine(path, Title));

            return ModuleResult.Aggregate(new[] { BackupStepFor(copy) });
        }

        public sealed override async Task<ModuleResult> RestoreAsync(string path)
        {
            CopyResult copy = await Utils.CopyFolder(Path.Combine(path, Title), Folder);

            // Absence is ALWAYS normal on this side, whatever the module's flag says: the flag
            // describes the live machine, and the source here is the backup folder - a backup
            // that predates the module legitimately holds nothing for it. Passing the flag
            // through would let an AbsenceIsNormal=false subclass fail that restore with
            // "expected folder is missing", backup-side wording about the wrong machine.
            return ModuleResult.Aggregate(new[] { copy.ToStep(Title, true, NothingBackedUp) });
        }
    }
}
