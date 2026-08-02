using WinRestoreKit;
using DataHelper;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Conf
{
    public class WThemes : BackupBase
    {
        public List<string> Folders = new List<string>();
        public List<string> Keys = new List<string>();

        public WThemes()
        {
            Title = "Themes";
            Info = "This will back up your custom theme settings, the desktop background image you are currently using, and the setting that points to it. That pointer is a full path containing your Windows user name, so it only resolves again on a PC where your account has the same name.";
            // Version = "This is compatible with all versions of Windows.";
            RequiresExplorerRestart = true;
            WarningMessage = "Restoring this overwrites display settings captured on the PC you backed up: system colours, font sizes and per-monitor scaling. Colours saved while High Contrast was on can leave parts of the classic Windows interface hard to read. Some of these only take effect after you sign out, not after the Explorer restart this app offers. The wallpaper is stored as a path containing your user name, so under a different account name the desktop can come back black even though this reports success.";

            LoadSettings();
        }

        private void LoadSettings()
        {
            // %Windir%\Web\Wallpaper is deliberately NOT backed up. It holds the wallpapers Windows
            // ships with - measured 2026-07-20 on this machine: 20 files, 20.0 MB, byte-identical on
            // every Windows 11 install - so copying it spent about 95% of this module's bytes moving
            // data the destination machine already had. It was also this module's only write to a
            // directory shared by every account on the PC, which is to say the most dangerous thing
            // it did was also the least useful.
            Folders.Add(Data.RoamingAppData + "\\Microsoft\\Windows\\Themes");

            Keys.Add(LegacyNamedKey);

            // The folder above holds the wallpaper's PIXELS (TranscodedWallpaper and the
            // per-monitor Transcoded_00N variants). This key holds the POINTER to them - measured
            // 2026-07-20: WallPaper (capital P), WallpaperStyle and TileWallpaper. Inference, not
            // a measured cause: before this key was added only the pixels were captured, and the
            // pixels alone cannot tell Windows which image to display, so a restore had nothing to
            // point the desktop at. That is consistent with restoring a theme never changing the
            // background, but it was never traced end to end.
            //
            // KNOWN LIMITATION, not repaired here: WallPaper holds an ABSOLUTE, user-specific path
            // (measured: C:\Users\<name>\AppData\Roaming\Microsoft\Windows\Themes\TranscodedWallpaper).
            // Restored under an account with a different user name that path does not resolve and
            // the desktop comes back black - yet ImportRegistryKey checks only the exit code and
            // that the key is present afterwards, so the row still reports Succeeded. Rewriting the
            // path on restore, or poking it with SystemParametersInfo, is out of scope for this
            // phase; the limitation is stated in Info and WarningMessage instead of being hidden
            // behind a green row.
            //
            // Measured 2026-07-20: this key also carries WindowMetrics (six LOGFONT blobs plus an
            // explicit AppliedDPI, which is what ties them to the capture-time DPI), a Colors
            // subkey holding the real system colours (Window, WindowText, Menu, Hilight, ...),
            // PerMonitorSettings, MaxMonitorDimension and DpiScalingVer. Those describe THIS
            // machine's displays and colour scheme, so cross-DPI or High-Contrast-to-normal the
            // effect exceeds cosmetic. regedit /e takes subkeys wholesale and regedit /s cannot
            // select individual values, so there is no narrower export available - the passengers
            // are disclosed in WarningMessage rather than papered over. Note also that
            // RequiresExplorerRestart does NOT apply DPI or system-colour changes; those need a
            // sign-out, which is why WarningMessage says so. Narrowing needs a different write
            // mechanism and is deliberately not built here.
            Keys.Add(@"HKEY_CURRENT_USER\Control Panel\Desktop");
        }

        public override bool IsInstalled()
        {
            bool b1 = false;
            bool b2 = false;

            foreach (string f in Folders)
            {
                if (Directory.Exists(f))
                {
                    b1 = true;
                    break;
                }
            }

            foreach (string k in Keys)
            {
                if (Utils.KeyExists(k))
                {
                    b2 = true;
                    break;
                }
            }

            return b1 || b2;
        }

        // Both halves, in the order RestoreAsync applies them. Read from the fields on every access:
        // see the matching note in WPersonalization.
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
        /// The one module with heterogeneous sub-operations: a folder copy and two registry
        /// exports, folded through a single Aggregate. Every source uses absenceIsNormal=false
        /// because each is created at first logon, so a missing one is a real fault rather than a
        /// machine that simply never had it.
        /// </remarks>
        public override async Task<ModuleResult> BackupAsync(string path)
        {
            List<StepResult> steps = new List<StepResult>();

            foreach (string folder in Folders)
            {
                string backupFolderPath = Path.Combine(path, BackupFolderNameFor(folder));

                // No try/catch here any more: CopyFolder does not throw, it returns counts. The
                // catch this replaced logged the failure and then let the module report success.
                CopyResult copy = await Utils.CopyFolder(folder, backupFolderPath).ConfigureAwait(true);
                // Title, not the full filesystem path: Aggregate renders the target into
                // user-facing text, and a path produces rows reading "captured C:\Windows\...".
                steps.Add(copy.ToStep(Title, false));
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

                // absenceIsNormal is true on this side: the folder being read is one this app wrote,
                // and a backup taken before this module existed legitimately does not contain it.
                CopyResult copy = await Utils.CopyFolder(backupFolderPath, folder).ConfigureAwait(true);
                // Title, not the full filesystem path: see the matching comment in BackupAsync.
                steps.Add(copy.ToStep(Title, true, NothingBackedUp));
            }

            foreach (string k in Keys)
            {
                steps.Add(Utils.ImportRegistryKey(Path.Combine(path, RegFileNameFor(k)), k));
            }

            return ModuleResult.Aggregate(steps);
        }

        // False for every key: the Themes key is written at first logon and Control Panel\Desktop
        // exists in every user profile, so an absent one is a real fault rather than a machine that
        // simply never had it. Same judgement the folder copies above make, stated per key so a key
        // added later has to answer the question rather than inherit an answer.
        private static bool AbsenceIsNormal(string key) => false;

        /// <remarks>
        /// The theme key keeps the name it has always been written under. Nothing about that key
        /// changed, so deriving a new name from it would orphan the file in every backup already on
        /// disk for no gain. Keys added since take the key-derived default, which is what makes them
        /// distinguishable from this one and from each other.
        ///
        /// Matched case-insensitively because registry key paths are: a key spelled with different
        /// casing is the SAME key to regedit, and matching it case-sensitively would silently write
        /// it to a second file while both spellings kept exporting the one live key.
        /// </remarks>
        protected override string RegFileNameFor(string key)
            => string.Equals(key, LegacyNamedKey, StringComparison.OrdinalIgnoreCase)
                ? Title + ".reg"
                : base.RegFileNameFor(key);

        /// <inheritdoc/>
        /// <remarks>
        /// Goes through HasFolderOrKeyArtifactIn, which composes key filenames via RegFileNameFor -
        /// so the legacy "Themes.reg" spelling overridden just above is honoured here too, and a
        /// backup written by an older build stays findable.
        /// </remarks>
        public override bool? HasArtifactIn(string backupPath)
            => HasFolderOrKeyArtifactIn(backupPath, Folders, Keys);

        private const string LegacyNamedKey =
            @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes";
    }
}
