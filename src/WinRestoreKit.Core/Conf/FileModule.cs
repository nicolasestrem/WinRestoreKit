using System;
using WinRestoreKit;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Conf
{
    /// <summary>
    /// A module that backs up a named list of individual files into <c>{Title}\</c> in the backup.
    /// </summary>
    /// <remarks>
    /// The file-level sibling of <see cref="FolderModule"/>. It exists because the developer-tooling
    /// modules copy NAMED FILES out of directories they must otherwise leave alone: backing up
    /// %USERPROFILE%\.ssh as a folder would sweep up private keys, which are deliberately excluded
    /// from a plaintext backup (user decision, 2026-07-21). A whitelist expresses that by
    /// construction - this base copies what <see cref="Files"/> lists and never enumerates a
    /// directory - rather than by an exclusion filter someone has to keep correct.
    ///
    /// Shape borrowed from <see cref="MultiKeyRegistryModule"/> (N items, per-item names, fields
    /// read at access time) and its decisions from <see cref="FolderModule"/> (sealed async pair,
    /// restore-side absence wording). The async pair is sealed for the same reason it is there:
    /// the Skipped-vs-Failed rule and the restore-side wording are the things that must not drift
    /// between modules that are supposed to behave identically.
    /// </remarks>
    public abstract class FileModule : BackupBase
    {
        /// <summary>The files this module copies out on backup and overwrites on restore.</summary>
        /// <remarks>
        /// A public mutable field, not a property, and populated by subclass constructors - the
        /// <see cref="MultiKeyRegistryModule.Keys"/> arrangement, for the same two reasons. The
        /// tests discover file modules through <c>GetField("Files")</c>, and the naming test
        /// appends a synthetic file after construction and observes the name the async pair
        /// actually computes. That only works because everything below reads this list at access
        /// time instead of capturing it at construction.
        /// </remarks>
        public List<string> Files = new List<string>();

        /// <summary>
        /// Whether <paramref name="file"/> can legitimately be missing on a healthy machine.
        /// </summary>
        /// <remarks>
        /// Abstract and per file, following <see cref="MultiKeyRegistryModule"/> rather than
        /// FolderModule's virtual-default-true, because the consumers genuinely disagree: a
        /// Windows Terminal Preview settings file is absent on most machines, and an absent
        /// %WINDIR%\System32\drivers\etc\hosts means something is wrong with the install. A default
        /// would be right for one of them and silently wrong for the other, and this is the flag
        /// whose two failure directions are cry-wolf and hidden-problem.
        /// </remarks>
        protected abstract bool AbsenceIsNormal(string file);

        /// <summary>
        /// The name this module writes <paramref name="file"/> under, inside <c>{Title}\</c>.
        /// </summary>
        /// <remarks>
        /// The file-side counterpart of <see cref="BackupBase.RegFileNameFor"/>, and the same rule
        /// applies: a loop over N files must produce N distinct names, or one export silently
        /// overwrites another while both steps report success.
        ///
        /// The default is the BASE NAME, deliberately not derived from the full path. The paths in
        /// Files are composed from Data.* roots at runtime, so a path-derived name would carry the
        /// backing-up machine's user name into the artifact name and stop resolving on restore
        /// under any other account - the same class of defect as the WThemes wallpaper pointer,
        /// except this one would break the restore rather than merely the result. Base names
        /// (hosts, config, settings.json) are stable across machines and accounts.
        ///
        /// A module whose files share a base name MUST override this. ETerminal does: three
        /// installs of Windows Terminal all call their file settings.json. DeveloperModuleTests
        /// pins that the names a module produces are distinct.
        /// </remarks>
        protected virtual string BackupFileNameFor(string file) => Path.GetFileName(file);

        /// <summary>Where in the backup this module's artifacts live.</summary>
        /// <remarks>
        /// One subfolder per module rather than loose files at the root: it keeps N artifacts
        /// grouped, keeps base names like "config" from colliding across modules, and gives
        /// <see cref="HasBackupIn"/> a single directory to probe.
        /// </remarks>
        private string BackupDirFor(string path) => Path.Combine(path, Title);

        public override bool IsInstalled()
        {
            foreach (string f in Files)
            {
                if (File.Exists(f))
                    return true;
            }

            return false;
        }

        // Read from Files on every access rather than captured once: Files is a mutable public
        // field filled by subclass constructors, so a snapshot taken at construction would
        // describe a different set of files than the one Restore actually overwrites.
        public override IReadOnlyList<RestoreTarget> RestoreTargets
        {
            get
            {
                List<RestoreTarget> targets = new List<RestoreTarget>();

                foreach (string f in Files)
                    targets.Add(RestoreTarget.File(f));

                return targets;
            }
        }

        /// <remarks>
        /// The real check is earned only by modules that close a process - the FolderModule rule,
        /// and the reasoning is the same: answering honestly here is what stops the orchestrator
        /// closing Windows Terminal, and every shell session running in it, for a restore that had
        /// nothing to copy. A module that closes nothing keeps the base default of true.
        ///
        /// It asks for an ARTIFACT, not for the directory. FolderModule probes the directory and
        /// that is right for it, because its restore copies the directory wholesale - if it is
        /// there, there is something to do. Here the restore resolves each file by name, so the
        /// directory existing proves nothing about whether any of THIS machine's files are in it.
        /// Two ways that goes wrong, both found in review rather than in use:
        ///
        ///  - Utils.CopyFile creates the destination directory before it knows the copy will
        ///    succeed, so a backup where every file failed still leaves an empty {Title}\ behind.
        ///  - A backup taken on a machine with only Windows Terminal Preview holds only
        ///    "settings (preview).json". Restored onto a machine with only the Store build, a
        ///    directory probe says yes, Terminal is closed - ending every tab and every command
        ///    running in them - and then all three files report "nothing was backed up".
        ///
        /// Both spend the thing the check exists to protect. Note this can only ever say no when
        /// the module names every file it would read, which is the shape of every FileModule.
        /// </remarks>
        public override bool HasBackupIn(string restorePath)
        {
            if (ProcessesToCloseBeforeRestore.Count == 0)
                return true;

            if (string.IsNullOrWhiteSpace(restorePath))
                return false;

            string backupDir = BackupDirFor(restorePath);

            foreach (string f in Files)
            {
                // File.Exists rather than an open attempt, and the cost of that is worth stating
                // correctly, because an earlier version of this comment stated it BACKWARDS.
                //
                // A false here does NOT merely cost an unnecessary close - that is what a false
                // POSITIVE costs. A false NEGATIVE makes RestoreScope block the module as
                // NothingToRestore, so the user is told "nothing was backed up for this item" over
                // a backup that is sitting right there, and the restore they asked for silently
                // does not happen. And File.Exists answers false, without throwing, for a file
                // whose parent directory denies access - the same ACL shape Utils.CopyFile was
                // just corrected for - so RestoreScope's catch, which exists to fall towards
                // restoring when the answer is unreliable, can never engage here.
                //
                // Kept anyway, on the balance of the two: the backup folder is one this app wrote
                // and normally reads back under the same account, whereas opening every candidate
                // file to answer a yes/no question costs a handle per file on every restore. The
                // exposure is a misreport, not data loss, and nothing is overwritten. Revisit if
                // restoring from a folder carried off another machine turns out to be common.
                if (File.Exists(Path.Combine(backupDir, BackupFileNameFor(f))))
                    return true;
            }

            return false;
        }

        /// <remarks>
        /// The same file-by-file probe <see cref="HasBackupIn"/> runs, minus the short-circuit that
        /// makes it answer true unconditionally when no process would be killed. Every FileModule
        /// names every file it would read - the shape the remarks above rely on - so this can always
        /// give a real answer, and the trade-off argued there (File.Exists over an open attempt)
        /// applies here for the same reason.
        /// </remarks>
        public override bool? HasArtifactIn(string backupPath)
        {
            if (string.IsNullOrWhiteSpace(backupPath))
                return false;

            string backupDir = BackupDirFor(backupPath);

            foreach (string f in Files)
            {
                if (File.Exists(Path.Combine(backupDir, BackupFileNameFor(f))))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Removes a previous backup artifact before this run decides whether the source exists.
        /// </summary>
        private static string TryClear(string filePath)
        {
            try
            {
                if (File.Exists(filePath))
                    File.Delete(filePath);

                return null;
            }
            catch (Exception ex)
            {
                LogHelper.Instance.LogMessage(
                    "Could not clear the previous backup file at " + filePath + ": " + ex.Message);
                return ex.Message;
            }
        }

        public sealed override async Task<ModuleResult> BackupAsync(string path)
        {
            List<StepResult> steps = new List<StepResult>();
            string backupDir = BackupDirFor(path);

            foreach (string f in Files)
            {
                string destination = Path.Combine(backupDir, BackupFileNameFor(f));
                string clearError = TryClear(destination);

                if (clearError != null)
                {
                    steps.Add(StepResult.Failed(f,
                        "could not clear the previous backup file at " + destination + ": " + clearError));
                    continue;
                }

                CopyResult copy = await Utils.CopyFile(f, destination).ConfigureAwait(true);

                // The live path as the step target, not the Title: these modules carry several
                // files each, so a per-file row saying only "Windows Terminal" would leave the
                // user unable to tell which of three settings.json files the reason is about.
                steps.Add(copy.ToFileStep(f, AbsenceIsNormal(f)));
            }

            return ModuleResult.Aggregate(steps);
        }

        public sealed override async Task<ModuleResult> RestoreAsync(string path)
        {
            List<StepResult> steps = new List<StepResult>();
            string backupDir = BackupDirFor(path);

            foreach (string f in Files)
            {
                CopyResult copy = await Utils.CopyFile(Path.Combine(backupDir, BackupFileNameFor(f)), f)
                    .ConfigureAwait(true);

                // Absence is ALWAYS normal on this side, whatever the module's flag says: the flag
                // describes the live machine, and the source here is the backup folder - a backup
                // taken before this module existed legitimately holds nothing for it. Passing the
                // flag through would let an AbsenceIsNormal=false module (EHosts) fail that
                // restore with "expected file ... is missing", which is backup-side wording about
                // the wrong machine entirely.
                steps.Add(copy.ToFileStep(f, true, NothingBackedUp));
            }

            return ModuleResult.Aggregate(steps);
        }
    }
}
