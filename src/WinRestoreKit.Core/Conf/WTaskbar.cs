using WinRestoreKit;
using DataHelper;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Conf
{
    /// <summary>
    /// Taskbar behaviour settings plus the pinned apps, which live in three separate places.
    /// </summary>
    /// <remarks>
    /// This was a single-key <see cref="RegistryModule"/> capturing only Explorer\Advanced -
    /// alignment, size, Widgets - which is not where pinned apps live. Nothing about the pins was
    /// captured, and the module said nothing about that, so a user who restored a backup got the
    /// taskbar's shape back and an empty taskbar.
    ///
    /// The pins are two halves that only work together: the Taskband key holds an opaque binary
    /// list, and the .lnk shortcuts it references live in the Quick Launch profile folder. Either
    /// one alone restores nothing usable, which is why this became a hybrid rather than gaining a
    /// second key.
    ///
    /// The retarget is purely ADDITIVE. The Advanced key keeps the filename it has always been
    /// written under (see <see cref="RegFileNameFor"/>), so every backup already on disk stays
    /// readable and no file is orphaned.
    /// </remarks>
    public class WTaskbar : BackupBase
    {
        public List<string> Folders = new List<string>();
        public List<string> Keys = new List<string>();

        public WTaskbar()
        {
            Title = "Taskbar";
            Info = "This will back up your taskbar settings (alignment, size, layout, Widgets) and the "
                 + "apps pinned to your taskbar - both the list Windows keeps of them and the shortcut "
                 + "files it points at. That list records where each shortcut lives using a full path "
                 + "containing your Windows user name, so it comes back correctly on the same account "
                 + "it was saved from.";

            // Taskband is read by Explorer when it starts, so a restore changes nothing visible
            // until Explorer is restarted. Advanced alone did not need this, which is why the
            // module never set it.
            RequiresExplorerRestart = true;

            // Three disclosures, and the third was added by the Phase 3c review rather than written
            // with the module. The Favorites blob is a shell ItemID list, and dumping the live one
            // (29,531 bytes, measured 2026-07-21) shows it carries account-bearing absolute paths -
            // AppData\Roaming\Microsoft\Internet Explorer\Quick Launch\TaskBar\<App>.lnk - plus the
            // profile display name. Under a different account name, or a rebuilt profile, Explorer
            // cannot resolve those entries and prunes them, so the taskbar comes back empty while
            // every row reads Succeeded: the folder copy really did copy 32 files and both imports
            // really were applied.
            //
            // That is the undetectable-failure shape this app is built to disclose, and the same
            // commit disclosed it for WFonts ("this app cannot detect that - the row still reports
            // success") and APinnedApps while missing it here. Same fact, same voice.
            WarningMessage = "After restoring the taskbar, use the Restart Explorer button BEFORE you "
                           + "sign out. Explorer keeps the pinned-app list in memory and writes its own "
                           + "copy back when it exits normally, so signing out without restarting first "
                           + "can overwrite what was just restored.\n\n"
                           + "The pinned apps only come back on the same Windows account they were saved "
                           + "from. Windows stores that list as full paths containing your user name, so "
                           + "on a PC where your account is named differently - or on a rebuilt profile - "
                           + "Windows cannot find the shortcuts and quietly drops the pins, leaving the "
                           + "taskbar empty. This app cannot detect that: it will still report success.\n\n"
                           + "Restoring the pinned shortcuts also MERGES rather than replaces: shortcuts "
                           + "you pinned since the backup stay on disk, and what actually appears on the "
                           + "taskbar is decided by the restored pin list, not by which shortcut files "
                           + "are present.";

            LoadSettings();
        }

        private void LoadSettings()
        {
            // Folder first, and the same order is used everywhere below: the .lnk shortcuts have to
            // be on disk before the Taskband blob that references them is applied.
            //
            // Measured 2026-07-21 on this machine: 32 .lnk files, one per pinned app.
            Folders.Add(Data.RoamingAppData + "\\Microsoft\\Internet Explorer\\Quick Launch\\User Pinned\\TaskBar");

            Keys.Add(LegacyNamedKey);

            // The pinned-app list itself. Measured 2026-07-21: Favorites and FavoritesResolve
            // (REG_BINARY), plus the FavoritesChanges / FavoritesVersion / FavoritesRemovedChanges
            // DWORDs. Favorites is an opaque blob of shell item IDs naming the shortcuts in the
            // folder above - which is the whole reason this module needs both halves.
            Keys.Add(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\Taskband");
        }

        public override bool IsInstalled()
        {
            foreach (string f in Folders)
            {
                if (Directory.Exists(f))
                    return true;
            }

            foreach (string k in Keys)
            {
                if (Utils.KeyExists(k))
                    return true;
            }

            return false;
        }

        // Folders before keys, matching the order RestoreAsync applies them. Read from the fields on
        // every access rather than cached at construction, so the dialog cannot describe a different
        // set of targets from the one the restore writes.
        public override IReadOnlyList<RestoreTarget> RestoreTargets
        {
            get
            {
                List<RestoreTarget> targets = new List<RestoreTarget>();

                foreach (string f in Folders)
                    targets.Add(RestoreTarget.Folder(f));

                foreach (string k in Keys)
                    targets.Add(RestoreTarget.RegistryKey(k));

                return targets;
            }
        }

        // Deliberately NO ProcessesToCloseBeforeRestore entry for explorer, even though this restore
        // overwrites files and a key Explorer owns.
        //
        // Utils.RestartExplorer goes through Utils.CloseProcess, which calls Process.Kill - Explorer
        // is killed, never asked to exit - and a killed Explorer does not flush its in-memory pin
        // list back to Taskband. That is what makes restore-then-Restart-Explorer safe.
        //
        // Closing it BEFORE the restore would not be. Windows' AutoRestartShell relaunches the shell
        // within about a second, so a pre-restore kill would very likely have a fresh Explorer
        // running - holding the OLD Taskband it just read - before the import finished. That is the
        // same overwrite hazard, plus a blanked desktop while the restore runs. The restart button
        // AFTER the import is the ordering that actually works, and the residual case (a user who
        // restores and then signs out without restarting) is disclosed in WarningMessage rather than
        // engineered around with a kill that makes it worse.

        /// <remarks>
        /// Heterogeneous sub-operations - one folder copy and two registry exports - folded through
        /// a single Aggregate. The absence judgement differs per source and is stated per source
        /// below rather than shared, because the sources are not alike: the two keys are shell keys
        /// every profile has, while a profile that has never pinned anything need not have the
        /// folder at all.
        /// </remarks>
        public override async Task<ModuleResult> BackupAsync(string path)
        {
            List<StepResult> steps = new List<StepResult>();

            foreach (string folder in Folders)
            {
                CopyResult copy = await Utils.CopyFolder(folder, BackupFolderPath(path, folder)).ConfigureAwait(true);
                // Title, not the full filesystem path: Aggregate renders the target into
                // user-facing text, and a path produces rows naming a directory nobody recognises.
                steps.Add(copy.ToStep(Title, AbsenceIsNormalForFolder(folder)));
            }

            foreach (string k in Keys)
            {
                steps.Add(Utils.ExportRegistryKey(Path.Combine(path, RegFileNameFor(k)), k, AbsenceIsNormal(k)));
            }

            return ModuleResult.Aggregate(steps);
        }

        public override async Task<ModuleResult> RestoreAsync(string path)
        {
            List<StepResult> steps = new List<StepResult>();

            // Folders first, and this ordering is DEFENSIVE rather than causal - the PR review
            // corrected an earlier version of this comment that claimed otherwise, and the claim is
            // worth getting right because a false mechanism is what stops the next author reasoning
            // about the real one.
            //
            // What the earlier comment said: the shortcuts must exist before the Taskband blob that
            // names them, or Explorer resolves pins to missing files and prunes them. That mechanism
            // does not operate here. Explorer reads Taskband at STARTUP (which is why this module
            // sets RequiresExplorerRestart), and this restore deliberately does not restart it - the
            // user does, afterwards, via the button. So within one RestoreAsync nothing is reading
            // either half while the other is written, and on a normal run the order has no
            // functional effect at all.
            //
            // It is kept because the case where it DOES matter is real if rare: Explorer crashing
            // and AutoRestartShell relaunching it between the two steps, which would have it read a
            // Taskband naming shortcuts not yet on disk. Free insurance against a genuine
            // interleaving, not the reason the pins work.
            foreach (string folder in Folders)
            {
                // absenceIsNormal is true on this side whatever the backup-side flag says: the source
                // being read is a folder this app wrote, and a backup taken before this module
                // captured pins legitimately does not contain it.
                CopyResult copy = await Utils.CopyFolder(BackupFolderPath(path, folder), folder).ConfigureAwait(true);
                steps.Add(copy.ToStep(Title, true, NothingBackedUp));
            }

            foreach (string k in Keys)
            {
                steps.Add(Utils.ImportRegistryKey(Path.Combine(path, RegFileNameFor(k)), k));
            }

            return ModuleResult.Aggregate(steps);
        }

        private string BackupFolderPath(string path, string folder)
            => Path.Combine(path, BackupFolderNameFor(folder));

        /// <inheritdoc/>
        public override bool? HasArtifactIn(string backupPath)
            => HasFolderOrKeyArtifactIn(backupPath, Folders, Keys);

        // False for both keys: Explorer\Advanced is a core shell key whose absence means a broken
        // profile, and Taskband was measured present on this standard Windows 11 install. Stated as
        // a per-key decision rather than a constant so a key added later has to answer the question
        // instead of inheriting an answer that was only ever about these two.
        private static bool AbsenceIsNormal(string key) => false;

        // True: the Quick Launch pinned-taskbar folder is created when something is first pinned, so
        // an account that has never pinned anything legitimately has no folder. Marking that Failed
        // would be the cry-wolf direction - a red row on a machine with nothing wrong with it.
        private static bool AbsenceIsNormalForFolder(string folder) => true;

        /// <remarks>
        /// The Advanced key keeps the name it has always been written under. It is the key this
        /// module has captured since it existed, and every backup on disk holds its export as
        /// Taskbar.reg; deriving a new name from it would orphan all of them, and each would then
        /// restore as "nothing was backed up for this item" while reporting no error. Taskband is
        /// new, so it takes the key-derived default, which is what keeps the two distinguishable and
        /// stops the second export deleting and overwriting the first.
        ///
        /// Matched case-insensitively because registry key paths are: a key spelled with different
        /// casing is the SAME key to regedit, so matching case-sensitively would quietly write that
        /// spelling to a second file while both spellings kept exporting the one live key.
        /// </remarks>
        protected override string RegFileNameFor(string key)
            => string.Equals(key, LegacyNamedKey, StringComparison.OrdinalIgnoreCase)
                ? Title + ".reg"
                : base.RegFileNameFor(key);

        private const string LegacyNamedKey =
            @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
    }
}
