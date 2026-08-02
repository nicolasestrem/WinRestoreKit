using WinRestoreKit;
using DataHelper;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Conf
{
    /// <summary>
    /// Fonts the current user installed for themselves, rather than for the whole machine.
    /// </summary>
    /// <remarks>
    /// Hand-rolled from <see cref="BackupBase"/> rather than inheriting a base, for the reason
    /// WThemes is: this module has heterogeneous sub-operations - a folder of font binaries and a
    /// registry key of pointers to them - and no base covers both. The structure below is WThemes'
    /// deliberately, so the two hybrids stay legible as one shape.
    /// </remarks>
    public class WFonts : BackupBase
    {
        public List<string> Folders = new List<string>();
        public List<string> Keys = new List<string>();

        public WFonts()
        {
            Title = "User fonts";
            Info = "This will back up the fonts you installed just for yourself - the font files, " +
                   "and the settings that tell Windows where each one is. Those settings are full " +
                   "paths containing your Windows user name, so on a PC where your account has a " +
                   "different name the font files come back but Windows cannot find them.";

            WarningMessage = "The font files are copied back first, then the settings that point " +
                             "at them. Those pointers are stored as full paths containing your " +
                             "user name (for example C:\\Users\\<name>\\AppData\\Local\\Microsoft" +
                             "\\Windows\\Fonts\\example.ttf), so under a different account name " +
                             "they do not resolve: the fonts are on disk but do not appear, and " +
                             "this app cannot detect that - the row still reports success. " +
                             "Restored fonts also only show up inside an application after that " +
                             "application is restarted.";

            LoadSettings();
        }

        private void LoadSettings()
        {
            // The font BINARIES. Measured 2026-07-21 on this machine: absent on an account that has
            // installed no per-user fonts - Windows creates this folder with the first such install.
            Folders.Add(Data.LocalAppData + "\\Microsoft\\Windows\\Fonts");

            // The POINTERS: one value per user-installed font, mapping its display name to the
            // absolute path of the file in the folder above. Measured 2026-07-21: the key EXISTS
            // with 0 values on that same clean account, which is why the two halves answer the
            // absence question differently below.
            Keys.Add(@"HKEY_CURRENT_USER\Software\Microsoft\Windows NT\CurrentVersion\Fonts");
        }

        /// <remarks>
        /// The FOLDER alone, deliberately - not the folder or the key, which is the shape WThemes
        /// uses and which the PR review caught being wrong here.
        ///
        /// The key is measured present with zero values on an account that has never installed a
        /// per-user font, so <c>Utils.KeyExists</c> returns true on essentially every profile. Asking
        /// it would make this method a constant `true`, and IsInstalled drives "select installed" -
        /// a convenience whose only job is to distinguish machines that have something here from
        /// machines that do not. A module that always answers yes tells the user nothing and quietly
        /// widens every "select installed" backup by an item with nothing in it.
        ///
        /// The folder is the honest signal: Windows creates it with the first per-user font install,
        /// so its presence means this account actually used the feature. This deliberately does NOT
        /// mirror the AbsenceIsNormal flags, and the difference is the point - those answer "is a
        /// missing target a fault while backing up", which the key answers differently from the
        /// folder. This answers "is there anything here worth offering", which only the folder knows.
        /// </remarks>
        public override bool IsInstalled()
        {
            foreach (string f in Folders)
            {
                if (Directory.Exists(f))
                    return true;
            }

            return false;
        }

        // Folders before keys, which is the order BackupAsync and RestoreAsync apply them in. Read
        // from the fields on every access rather than captured once: both are public mutable fields
        // filled by the constructor, so a snapshot would risk describing a different set than the
        // one the restore actually writes.
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

        /// <remarks>
        /// Folder first, then key, and the same order on both sides. The files must be on disk
        /// before the registry values that name them, which is WThemes' pixels-before-pointer
        /// rationale in its other form: a pointer to a file that is not there yet is a pointer to
        /// nothing, and nothing in the import re-checks it afterwards.
        /// </remarks>
        public override async Task<ModuleResult> BackupAsync(string path)
        {
            List<StepResult> steps = new List<StepResult>();

            foreach (string folder in Folders)
            {
                string backupFolderPath = Path.Combine(path, BackupFolderNameFor(folder));

                CopyResult copy = await Utils.CopyFolder(folder, backupFolderPath).ConfigureAwait(true);
                // Title, not the full filesystem path: Aggregate renders the target into
                // user-facing text, and a path produces rows reading "captured C:\Users\...".
                steps.Add(copy.ToStep(Title, FolderAbsenceIsNormal(folder)));
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

            foreach (string folder in Folders)
            {
                string backupFolderPath = Path.Combine(path, BackupFolderNameFor(folder));

                // absenceIsNormal is true on this side regardless of the backup-side flag: the
                // source being read here is a folder this app wrote, and a backup taken before this
                // module existed legitimately does not contain it.
                CopyResult copy = await Utils.CopyFolder(backupFolderPath, folder).ConfigureAwait(true);
                steps.Add(copy.ToStep(Title, true, NothingBackedUp));
            }

            foreach (string k in Keys)
            {
                steps.Add(Utils.ImportRegistryKey(Path.Combine(path, RegFileNameFor(k)), k));
            }

            return ModuleResult.Aggregate(steps);
        }

        /// <summary>
        /// Whether the per-user font FOLDER can legitimately be missing.
        /// </summary>
        /// <remarks>
        /// True: measured absent 2026-07-21 on an account that had installed no per-user fonts.
        /// Windows creates it with the first such install, so a machine without it is a machine
        /// that never used the feature - marking that red would cry wolf on most installs.
        /// </remarks>
        protected virtual bool FolderAbsenceIsNormal(string folder) => true;

        /// <summary>
        /// Whether the per-user font registry KEY can legitimately be missing.
        /// </summary>
        /// <remarks>
        /// False, and deliberately the opposite answer from the folder's. Measured 2026-07-21: the
        /// key was PRESENT with zero values on the same clean account whose folder was absent, so
        /// it exists in a profile whether or not any font was ever installed. An absent one is
        /// therefore a fault - or a key that could not be probed, which is the same verdict for a
        /// different reason.
        /// </remarks>
        protected virtual bool AbsenceIsNormal(string key) => false;

        /// <inheritdoc/>
        public override bool? HasArtifactIn(string backupPath)
            => HasFolderOrKeyArtifactIn(backupPath, Folders, Keys);
    }
}
