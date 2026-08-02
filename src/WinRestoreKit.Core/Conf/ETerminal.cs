using WinRestoreKit;
using DataHelper;
using System;
using System.Collections.Generic;
using System.IO;

namespace Conf
{
    /// <summary>
    /// Windows Terminal's <c>settings.json</c>, for all three ways it gets installed.
    /// </summary>
    /// <remarks>
    /// Three files, because Windows Terminal has three homes and a machine can hold more than one
    /// at a time: the Store build, the Preview build (a separate package with its own settings),
    /// and the unpackaged build that scoop and choco install. Covering only the Store build would
    /// give Preview and portable users a green "Skipped" row over a settings file that was right
    /// there. All three are absent on most machines, so absence is normal for each.
    ///
    /// All three files are called settings.json, which is exactly the collision
    /// <see cref="FileModule.BackupFileNameFor"/> exists to make a module answer: without the
    /// override below, the second export would overwrite the first while both steps reported
    /// success, and the restore would write one file to all three destinations.
    /// </remarks>
    public class ETerminal : FileModule
    {
        public ETerminal()
        {
            Title = "Windows Terminal settings";
            Info = "This will back up your Windows Terminal settings.json: your profiles, colour schemes, key bindings, startup behaviour and appearance. The Store, Preview and portable installations keep separate settings files, and whichever of them exist on this PC are all backed up.";
            // Deliberately does NOT say "because it rewrites settings.json when it exits". WinRestoreKit
            // force-terminates the process (Utils.CloseProcess calls Process.Kill), so the graceful
            // exit that rationale describes never happens - the sentence would be describing a
            // mechanism the app removes. The real hazard is a Terminal left running, whether the
            // user declined the close or reopened it, flushing its in-memory settings over the
            // restored file some minutes later.
            WarningMessage = "Windows Terminal must be closed before restoring. It holds its settings in memory while running and writes them back on its own schedule, so a Terminal still open after the restore can overwrite the restored file later. Note that WinRestoreKit force-closes it rather than asking it to exit: every open tab, and any command still running in one, ends immediately and without prompting.";

            Files.Add(StablePath);
            Files.Add(PreviewPath);
            Files.Add(UnpackagedPath);
        }

        // Absent on most machines for at least two of the three, and an absent settings.json even
        // for an installed Terminal just means it has never been customised - Terminal writes the
        // file on first change, not on install. None of that is a fault.
        protected override bool AbsenceIsNormal(string file) => true;

        /// <remarks>
        /// Matched against the paths this class composed, case-insensitively because Windows paths
        /// are. Deliberately NOT a counter or an index-based suffix: a name derived from position
        /// in the list would change meaning the moment a fourth install location is added or the
        /// order is edited, silently orphaning artifacts in every existing backup. Matching the
        /// path keeps each file's name tied to the install it came from.
        ///
        /// Falls through to the base (the plain base name) for anything unrecognised, which is
        /// what a test-injected synthetic file gets.
        /// </remarks>
        protected override string BackupFileNameFor(string file)
        {
            if (string.Equals(file, StablePath, StringComparison.OrdinalIgnoreCase))
                return "settings.json";

            if (string.Equals(file, PreviewPath, StringComparison.OrdinalIgnoreCase))
                return "settings (preview).json";

            if (string.Equals(file, UnpackagedPath, StringComparison.OrdinalIgnoreCase))
                return "settings (unpackaged).json";

            return base.BackupFileNameFor(file);
        }

        /// <remarks>
        /// One requirement covers the Store and Preview builds: both run as WindowsTerminal.exe, so
        /// Process.GetProcessesByName("WindowsTerminal") returns them together and closing by name
        /// closes both. The unpackaged build uses the same image name.
        ///
        /// NeedsConsent because the cost is visible and unrecoverable: Terminal hosts shells, and
        /// Utils.CloseProcess KILLS rather than asking politely, so whatever is running in every
        /// tab dies without a save prompt. Leaving it open is not a cosmetic alternative either -
        /// a Terminal still running when the restore finishes can flush its in-memory settings
        /// over the restored file minutes later, and nothing in this app would report that.
        /// </remarks>
        public override IReadOnlyList<RestoreCloseRequirement> ProcessesToCloseBeforeRestore
            => new[]
            {
                new RestoreCloseRequirement("WindowsTerminal", "Windows Terminal", true)
            };

        private static readonly string StablePath = Path.Combine(Data.LocalAppData,
            @"Packages\Microsoft.WindowsTerminal_8wekyb3d8bbwe\LocalState\settings.json");

        private static readonly string PreviewPath = Path.Combine(Data.LocalAppData,
            @"Packages\Microsoft.WindowsTerminalPreview_8wekyb3d8bbwe\LocalState\settings.json");

        // The scoop/choco/portable install, which is not packaged and so writes outside Packages.
        private static readonly string UnpackagedPath = Path.Combine(Data.LocalAppData,
            @"Microsoft\Windows Terminal\settings.json");
    }
}
