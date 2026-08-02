using WinRestoreKit;
using DataHelper;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Conf
{
    /// <summary>
    /// Visual Studio Code's user profile: settings, key bindings and snippets.
    /// </summary>
    /// <remarks>
    /// Hand-rolled from BackupBase rather than inheriting <see cref="FileModule"/>, because two of
    /// its three targets are files and the third - snippets - is a directory of arbitrarily many
    /// user-named .json files. Teaching FileModule about folders to serve this one consumer is the
    /// mistake Phase 3a's critique caught with the dropped CommandModule: a base that fits one of
    /// its consumers is a worse seam than two honest ones. <see cref="WThemes"/> is the precedent
    /// for a heterogeneous module folding both kinds of sub-operation through one Aggregate.
    ///
    /// One asymmetry worth knowing before reading RestoreAsync: the two FILES are replaced
    /// wholesale, while the snippets FOLDER is merged, because Utils.CopyFolder overwrites what the
    /// backup holds and never deletes. So a snippet created after the backup survives a restore -
    /// and in the other direction, restoring the pre-restore snapshot afterwards puts the old
    /// snippets back but cannot remove the ones the restore introduced, while SnapshotGate still
    /// reports the restore undoable. That is the CLAUDE.md asymmetry arriving through merge
    /// semantics rather than through a path Backup does not read. It is inherent to CopyFolder and
    /// shared with APinnedApps, so it is disclosed in Info rather than fixed here.
    ///
    /// Stable VS Code only. Insiders (%APPDATA%\Code - Insiders) and VSCodium are deliberately out
    /// of scope for this phase - user decision, 2026-07-21 - as is the installed extension list,
    /// which the roadmap defers because reinstalling extensions needs an AStoreApps-style dialog
    /// and not a file copy. Per-profile settings under User\profiles\ are likewise not captured;
    /// this module carries the default profile, which is what the great majority of users have.
    /// </remarks>
    public class EVSCode : BackupBase
    {
        public List<string> Files = new List<string>();

        /// <remarks>
        /// Read-only from outside the assembly; the internal setter exists so the tests can point
        /// this module at a temp tree. That is not a convenience: without it, the only way to
        /// exercise this module's backup is to read the tester's REAL snippets folder, and the
        /// only way to exercise its restore is to WRITE into it. An untestable module here is the
        /// worse outcome, because this class hand-rolls the rules FileModule's sealed pair
        /// guarantees for everything else, so it is the one that can drift.
        ///
        /// What the previous `readonly` was defending is preserved: everything below reads this
        /// property at access time, so the folder named in the confirmation dialog is always the
        /// one RestoreAsync writes, whatever it has been set to. Nothing outside this assembly
        /// can move it.
        /// </remarks>
        public string SnippetsFolder { get; internal set; }

        public EVSCode()
        {
            Title = "VS Code settings";
            Info = "This will back up your Visual Studio Code user settings (settings.json), your custom key bindings (keybindings.json) and your user snippets folder.\n\nRestoring is not the same for both halves: settings.json and keybindings.json are replaced with the backed-up versions, so changes made since are lost, while the snippets folder is merged - backed-up snippets are written over, and any snippet you have added since is left in place. That also means restoring snippets cannot be fully undone.\n\nInstalled extensions are not included - those are reinstalled from the Marketplace rather than copied. VS Code Insiders and VSCodium are not covered.";
            WarningMessage = "VS Code must be closed before restoring. It rewrites settings.json while running, so an open window would overwrite the restored file - and closing it discards changes in any editor you have not saved.";

            string userDir = Path.Combine(Data.RoamingAppData, "Code", "User");

            Files.Add(Path.Combine(userDir, "settings.json"));
            Files.Add(Path.Combine(userDir, "keybindings.json"));

            SnippetsFolder = Path.Combine(userDir, "snippets");
        }

        // The profile directory, not the executable: this module backs up the profile, and a VS
        // Code that was installed and then uninstalled leaves settings behind that are still worth
        // capturing. It also keeps the probe working for portable installs whose exe is elsewhere.
        public override bool IsInstalled()
            => Directory.Exists(Path.Combine(Data.RoamingAppData, "Code", "User"));

        // Files first, then the folder - the order RestoreAsync applies them, read from the fields
        // on every access so the declaration cannot describe a different set than the restore
        // writes. DeveloperModuleTests pins both the order and the kinds.
        public override IReadOnlyList<RestoreTarget> RestoreTargets
        {
            get
            {
                List<RestoreTarget> targets = new List<RestoreTarget>();

                foreach (string f in Files)
                    targets.Add(RestoreTarget.File(f));

                targets.Add(RestoreTarget.Folder(SnippetsFolder));

                return targets;
            }
        }

        /// <remarks>
        /// The earned check, as on FolderModule and FileModule: this module closes VS Code, so it
        /// must answer honestly whether the chosen backup holds anything for it. Getting it wrong
        /// costs the user every unsaved editor buffer for a restore that then copies nothing.
        ///
        /// Asks for an ARTIFACT rather than the directory, for the reason FileModule.HasBackupIn
        /// gives at length. The cheap case here: Utils.CopyFolder creates its destination before
        /// enumerating, so a user whose snippets folder exists but is EMPTY, and who has never
        /// customised settings.json or keybindings.json, still produces a "{Title}\snippets\"
        /// directory holding nothing - enough for a directory probe to say yes and for VS Code,
        /// with whatever is unsaved in it, to be killed for a restore that writes nothing.
        /// </remarks>
        public override bool HasBackupIn(string restorePath)
        {
            if (string.IsNullOrWhiteSpace(restorePath))
                return false;

            string backupDir = Path.Combine(restorePath, Title);

            foreach (string f in Files)
            {
                if (File.Exists(Path.Combine(backupDir, BackupNameFor(f))))
                    return true;
            }

            // Any snippet at all, at any depth - the folder alone is not evidence.
            string snippets = SnippetsBackupDir(restorePath);

            return Directory.Exists(snippets)
                   && Directory.EnumerateFiles(snippets, "*", SearchOption.AllDirectories).Any();
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Delegates, because this module's HasBackupIn is already an unconditional artifact probe -
        /// it has no "true if nothing needs closing" short-circuit, having earned the real check by
        /// closing VS Code. The two questions genuinely have the same answer here, and writing it
        /// twice would let them drift.
        /// </remarks>
        public override bool? HasArtifactIn(string backupPath) => HasBackupIn(backupPath);

        /// <remarks>
        /// VS Code runs several processes all named Code (the window, the extension host, the
        /// renderers); closing by name closes all of them, which is the intent - the extension host
        /// is one of the things that writes settings.json.
        ///
        /// NeedsConsent: the cost is unsaved work, which the user is the only one who can weigh.
        /// </remarks>
        public override IReadOnlyList<RestoreCloseRequirement> ProcessesToCloseBeforeRestore
            => new[]
            {
                new RestoreCloseRequirement("Code", "Visual Studio Code", true)
            };

        public override async Task<ModuleResult> BackupAsync(string path)
        {
            List<StepResult> steps = new List<StepResult>();
            string backupDir = Path.Combine(path, Title);

            foreach (string f in Files)
            {
                CopyResult copy = await Utils.CopyFile(f, Path.Combine(backupDir, BackupNameFor(f)))
                    .ConfigureAwait(true);

                // True: VS Code writes settings.json on the first setting the user changes and
                // keybindings.json on the first binding, so a fresh install legitimately has
                // neither. Absent means "never customised", not "broken".
                steps.Add(copy.ToFileStep(f, true));
            }

            CopyResult snippets = await Utils.CopyFolder(SnippetsFolder, SnippetsBackupDir(path))
                .ConfigureAwait(true);
            steps.Add(snippets.ToStep(SnippetsStepName, true));

            return ModuleResult.Aggregate(steps);
        }

        public override async Task<ModuleResult> RestoreAsync(string path)
        {
            List<StepResult> steps = new List<StepResult>();
            string backupDir = Path.Combine(path, Title);

            foreach (string f in Files)
            {
                CopyResult copy = await Utils.CopyFile(Path.Combine(backupDir, BackupNameFor(f)), f)
                    .ConfigureAwait(true);

                // Absence is always normal on this side and the reason is always NothingBackedUp:
                // the source is the backup folder, so "not present on this system" would describe
                // the wrong machine. Same rule as FileModule.RestoreAsync.
                steps.Add(copy.ToFileStep(f, true, NothingBackedUp));
            }

            CopyResult snippets = await Utils.CopyFolder(SnippetsBackupDir(path), SnippetsFolder)
                .ConfigureAwait(true);
            steps.Add(snippets.ToStep(SnippetsStepName, true, NothingBackedUp));

            return ModuleResult.Aggregate(steps);
        }

        /// <summary>The name this module writes <paramref name="file"/> under, inside {Title}\.</summary>
        /// <remarks>
        /// A named seam rather than an inline Path.GetFileName at each call site, which is what
        /// backup and restore used to do independently. That is the "a name kept away from its
        /// producer drifts" rule: two copies of a naming expression can be edited apart, and the
        /// failure mode when they are is the WThemes one - the artifact is written under one name
        /// and looked for under another, so the restore reports "nothing was backed up" over a
        /// file that is sitting in the folder.
        ///
        /// It is deliberately NOT FileModule.BackupFileNameFor: this module does not inherit that
        /// base. The rule it has to satisfy is the same one - N files, N distinct names - and
        /// DeveloperModuleTests pins it here separately for exactly that reason. The two files
        /// have distinct base names today; a third that collided would need this overridden, and
        /// the test is what would say so.
        /// </remarks>
        private static string BackupNameFor(string file) => Path.GetFileName(file);

        // Inside {Title}\ with the two files, so HasBackupIn's artifact probe covers the whole
        // module and the artifacts stay grouped.
        private string SnippetsBackupDir(string path) => Path.Combine(path, Title, "snippets");

        // A name for the step row rather than the full profile path, which would render as
        // "captured C:\Users\...\Roaming\Code\User\snippets" in the summary. The two file steps
        // carry their paths because they need distinguishing from each other; this one does not.
        private const string SnippetsStepName = "VS Code snippets";
    }
}
