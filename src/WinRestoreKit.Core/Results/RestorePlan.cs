using System;
using System.Collections.Generic;
using System.Text;

namespace WinRestoreKit
{
    /// <summary>
    /// One process the user is asked to agree to closing before the restore starts.
    /// </summary>
    /// <remarks>
    /// Keyed by process name rather than by module: two modules naming the same process are one
    /// question, and asking it twice is how a dialog trains people to tick without reading.
    /// </remarks>
    public sealed class RestoreConsentEntry
    {
        /// <summary>The process name as Process.GetProcessesByName takes it: no ".exe".</summary>
        public string ProcessName { get; }

        public string DisplayName { get; }

        /// <summary>The checkbox caption.</summary>
        public string Label { get; }

        internal RestoreConsentEntry(string processName, string displayName)
        {
            ProcessName = processName;
            DisplayName = displayName;
            Label = "Close " + displayName + " before restoring";
        }
    }

    /// <summary>
    /// Everything the confirmation dialog shows, composed from what the selected modules declare.
    /// </summary>
    /// <remarks>
    /// Pure by design: no WinForms types, no file system, no OS calls. The dialog is the only place
    /// the user is told what a restore will overwrite, so this text is the deliverable and has to be
    /// assertable in the test suite rather than only visible on real hardware.
    /// </remarks>
    public sealed class RestorePlan
    {
        /// <summary>
        /// The limits of the pre-restore snapshot, stated in full.
        /// </summary>
        /// <remarks>
        /// The single source of truth: the dialog, the snapshot's backup_log.txt header, and
        /// restore_log.txt all reference this constant, so the guarantee cannot be worded more
        /// strongly in one place than in another. Both restore mechanisms are additive - regedit /s
        /// merges, and CopyFolder leaves destination files absent from the source in place - so
        /// offering "rollback" without this sentence would claim a guarantee the app does not hold.
        ///
        /// Written with an escaped dash so the sentence survives being compiled from a source file
        /// with no byte order mark.
        /// </remarks>
        public const string FidelityCaveat =
            "The snapshot can put back settings this restore overwrites. It cannot remove registry " +
            "values or files that this restore adds \u2014 restoring the snapshot merges it over the " +
            "current state rather than resetting to it.";

        public IReadOnlyList<BackupBase> Modules { get; }

        /// <summary>The folder the restore reads from.</summary>
        public string RestoreSourcePath { get; }

        /// <summary>Where the pre-restore snapshot will be written, rendered verbatim.</summary>
        public string SnapshotDestination { get; }

        /// <summary>The module-by-module text: title, declared targets, and any warning.</summary>
        public string ConfirmationText { get; }

        /// <summary>One entry per distinct process that may only be closed with consent.</summary>
        public IReadOnlyList<RestoreConsentEntry> ConsentEntries { get; }

        /// <summary>
        /// Closes the user is told about but not asked about.
        /// </summary>
        /// <remarks>
        /// Separate from <see cref="ConsentEntries"/> because a checkbox is a question, and these
        /// processes come straight back on their own - there is nothing to decline.
        /// </remarks>
        public IReadOnlyList<string> InformationalCloseLines { get; }

        public RestorePlan(IReadOnlyList<BackupBase> modules, string restoreSourcePath,
                           string snapshotDestination)
        {
            if (string.IsNullOrWhiteSpace(restoreSourcePath))
                throw new ArgumentException("A restore plan must name where it restores from.", nameof(restoreSourcePath));

            if (string.IsNullOrWhiteSpace(snapshotDestination))
                throw new ArgumentException("A restore plan must name where the snapshot goes.", nameof(snapshotDestination));

            Modules = Compact(modules);
            RestoreSourcePath = restoreSourcePath;
            SnapshotDestination = snapshotDestination;

            List<RestoreConsentEntry> consent = new List<RestoreConsentEntry>();
            List<string> informational = new List<string>();
            CollectCloses(Modules, consent, informational);

            ConsentEntries = consent;
            InformationalCloseLines = informational;
            ConfirmationText = Compose(Modules, restoreSourcePath, snapshotDestination);
        }

        private static IReadOnlyList<BackupBase> Compact(IReadOnlyList<BackupBase> modules)
        {
            List<BackupBase> kept = new List<BackupBase>();

            if (modules != null)
            {
                foreach (BackupBase module in modules)
                {
                    if (module != null)
                        kept.Add(module);
                }
            }

            return kept;
        }

        private static void CollectCloses(IReadOnlyList<BackupBase> modules,
                                          List<RestoreConsentEntry> consent,
                                          List<string> informational)
        {
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (BackupBase module in modules)
            {
                IReadOnlyList<RestoreCloseRequirement> requirements = module.ProcessesToCloseBeforeRestore;

                if (requirements == null)
                    continue;

                foreach (RestoreCloseRequirement requirement in requirements)
                {
                    if (requirement == null || !seen.Add(requirement.ProcessName))
                        continue;

                    if (requirement.NeedsConsent)
                        consent.Add(new RestoreConsentEntry(requirement.ProcessName, requirement.DisplayName));
                    else
                        informational.Add(requirement.DisplayName + " will be briefly closed and will restart itself.");
                }
            }
        }

        private static string Compose(IReadOnlyList<BackupBase> modules, string restoreSourcePath,
                                      string snapshotDestination)
        {
            StringBuilder sb = new StringBuilder();

            sb.AppendLine("Restoring from:");
            sb.AppendLine("  " + restoreSourcePath);
            sb.AppendLine();
            sb.AppendLine("These items will be overwritten with what was backed up:");
            sb.AppendLine();

            foreach (BackupBase module in modules)
            {
                sb.AppendLine(module.Title);

                foreach (string line in TargetLines(module))
                    sb.AppendLine("  " + line);

                if (!string.IsNullOrWhiteSpace(module.WarningMessage))
                    sb.AppendLine("  Note: " + module.WarningMessage);

                sb.AppendLine();
            }

            sb.AppendLine("The current settings will first be copied to:");
            sb.AppendLine("  " + snapshotDestination);

            return sb.ToString();
        }

        /// <remarks>
        /// A module whose declaration is null or empty falls back to the marker rather than
        /// contributing nothing. Contributing nothing reads exactly like a module that touches
        /// nothing, which is the one thing the marker exists to keep from happening silently.
        ///
        /// A null ENTRY inside a non-empty declaration gets its own, different marker. It is not
        /// the same fact as an absent declaration - the module's other lines are real - and it is
        /// not something to skip: a dropped line understates what the restore overwrites in the
        /// one piece of text the user consents against. Modules are null-filtered in
        /// <see cref="Compact"/> and close requirements in <see cref="CollectCloses"/>; this makes
        /// the third list consistent with them instead of throwing an NRE.
        ///
        /// ConfPageView now wraps this ctor in a catch that abandons the restore, so a throw here
        /// is no longer the unhandled-dialog-mid-restore it once was. That catch is the backstop,
        /// not the fix: reaching it abandons a restore the user asked for, whereas rendering a
        /// marker lets the run continue while still showing that one line of the declaration is
        /// broken. Both exist on purpose, in that order.
        /// </remarks>
        private static IEnumerable<string> TargetLines(BackupBase module)
        {
            IReadOnlyList<RestoreTarget> targets = module.RestoreTargets;

            if (targets == null || targets.Count == 0)
            {
                yield return RestoreTarget.UndeclaredMarker;
                yield break;
            }

            foreach (RestoreTarget target in targets)
                yield return Render(target);
        }

        private static string Render(RestoreTarget target)
        {
            if (target == null)
                return RestoreTarget.UnnamedTargetMarker;

            switch (target.Kind)
            {
                case RestoreTargetKind.RegistryKey: return "registry key: " + target.Path;
                case RestoreTargetKind.Folder: return "folder: " + target.Path;
                case RestoreTargetKind.File: return "file: " + target.Path;

                // Command falls here on purpose: its Path is already a sentence written for this
                // dialog ("runs netsh to re-apply..."), so prefixing it would read as a label on a
                // path. Any kind added without an arm here also lands on it and renders bare -
                // RestorePlanTests asserts a prefix per kind so that shows up as a failing test
                // rather than as an unlabelled line in the text the user consents against.
                default: return target.Path;
            }
        }
    }
}
