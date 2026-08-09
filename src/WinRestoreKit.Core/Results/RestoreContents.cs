using System;
using System.Collections.Generic;

namespace WinRestoreKit
{
    /// <summary>
    /// One module's view-model row for the restore wizard's "contents &amp; portability" step: whether
    /// the backup folder actually holds something for it, what the manifest recorded about the run,
    /// and the warning to show inline.
    /// </summary>
    internal sealed class RestoreContentsRow
    {
        internal BackupBase Module { get; }
        internal bool HasBackup { get; }
        internal string ManifestState { get; }
        internal string Warning { get; }
        internal bool CouldNotBeRead { get; }

        internal RestoreContentsRow(BackupBase module, bool hasBackup, string manifestState, string warning,
                                    bool couldNotBeRead = false)
        {
            Module = module;
            HasBackup = hasBackup;
            ManifestState = manifestState;
            Warning = warning;
            CouldNotBeRead = couldNotBeRead;
        }
    }

    /// <summary>
    /// Builds the wizard's step-2 rows and the provenance banner. The parsed manifest supplies run
    /// state, while <see cref="BackupBase.HasArtifactIn"/> verifies physical presence whenever a
    /// module can answer. A succeeded manifest remains the fallback for modules without a probe.
    /// </summary>
    /// <remarks>
    /// Unknown renders as unknown, never inferred: a module absent from the manifest (every backup
    /// taken before the manifest existed, or a type this build retired) gets a null state and the view
    /// shows no chip. Provenance is null unless something actually differs, so a same-machine backup
    /// shows no scary banner.
    /// </remarks>
    internal static class RestoreContents
    {
        internal static IReadOnlyList<RestoreContentsRow> For(IReadOnlyList<BackupBase> modules,
                                                              string restoreSourcePath, ManifestData manifest)
        {
            List<RestoreContentsRow> rows = new List<RestoreContentsRow>();

            if (modules == null)
                return rows;

            string probePath = restoreSourcePath;
            BackupPayload.ReadScope payload = null;
            bool payloadPrepared = false;
            string payloadError = null;

            try
            {
                foreach (BackupBase module in modules)
                {
                    if (module == null)
                        continue;

                    string state = ManifestStateFor(manifest, module.GetType().Name);
                    bool needsPayload = state != BackupManifest.StateSkipped
                        && state != BackupManifest.StateFailed;

                    if (!payloadPrepared && needsPayload)
                    {
                        payloadPrepared = true;
                        if (BackupPayload.TryPrepareForRead(restoreSourcePath, out payload, out string preparationError))
                        {
                            probePath = payload.Path;
                        }
                        else
                        {
                            payloadError = "The backup payload could not be read"
                                + (string.IsNullOrWhiteSpace(preparationError) ? "." : ": " + preparationError);
                        }
                    }

                    if (payloadError != null && needsPayload)
                    {
                        rows.Add(new RestoreContentsRow(module, false, state, payloadError, true));
                        continue;
                    }

                    rows.Add(new RestoreContentsRow(
                        module,
                        HoldsSomethingFor(module, probePath, manifest, state),
                        state,
                        module.WarningMessage ?? ""));
                }
            }
            finally
            {
                if (payload != null)
                    payload.Dispose();
            }

            return rows;
        }

        /// <summary>
        /// Whether the folder has anything this module could restore. Failed and skipped manifest
        /// rows refuse the restore. Otherwise the module's physical probe decides when available,
        /// with manifest success as the fallback for modules that cannot inspect their own shape.
        /// </summary>
        /// <remarks>
        /// This deliberately does NOT ask <see cref="RestoreScope.HasBackup"/>, which was the first
        /// version of this and was wrong. That path ends at <c>BackupBase.HasBackupIn</c>, whose
        /// documented default is TRUE and which two subclasses answer true from without probing
        /// whenever no process has to be closed. It is a restore-SAFETY gate and fails open on
        /// purpose. Used here it said yes for all thirty modules, so a backup holding one module's
        /// files rendered as thirty enabled, pre-ticked rows, and the run then snapshotted and
        /// attempted every one of them. The greying was decorative.
        ///
        /// Order matters. The manifest is the run's own record of what it wrote and the artifact
        /// every other reader is told to trust, so a module it calls skipped or failed is not
        /// offered even if stale files from an earlier run into the same folder are sitting there -
        /// which is reachable today, see the reused-folder note in the Phase 4 implementation notes.
        /// Only when the manifest is silent does the folder itself get a vote.
        /// </remarks>
        private static bool HoldsSomethingFor(BackupBase module, string backupPath,
                                              ManifestData manifest, string manifestState)
        {
            if (manifestState == BackupManifest.StateSkipped || manifestState == BackupManifest.StateFailed)
                return false;

            // A successful manifest says what the run intended to write. The module's physical
            // probe still gets the final word when it can answer, so deleted or stale artifacts are
            // not offered for restore merely because the manifest survived.
            bool? probe = Probe(module, backupPath);

            if (probe != null)
                return probe.Value;

            if (manifestState == BackupManifest.StateSucceeded)
                return true;

            // It cannot tell. With a manifest present, silence means it was not part of that run.
            // Without one there is nothing left to consult, and refusing a restore we cannot
            // disprove is the worse error - so offer it and let the run report honestly.
            return manifest == null;
        }

        /// <summary>
        /// <see cref="BackupBase.HasArtifactIn"/>, with a throwing module treated as one that cannot
        /// tell.
        /// </summary>
        /// <remarks>
        /// The same guard <see cref="RestoreScope.HasBackup"/> puts around the sibling seam, and
        /// needed more here: this one runs while the wizard is BUILDING its page, inside a click
        /// handler, so an escape reaches WinForms' unhandled-exception dialog and leaves the user on
        /// a half-drawn step 2 - header set, module list empty - with no way forward.
        ///
        /// Reachable without anything exotic. A folder probe is two syscalls, an existence check and
        /// an enumeration, and a user pruning old backups in Explorer while the wizard is open can
        /// land between them; a backup root on a share or a removable drive can vanish outright.
        ///
        /// Falls to NULL, not to true. RestoreScope answers true because refusing to close an app is
        /// its safe direction; here the honest answer is that the module did not manage to look, and
        /// the caller above already knows what silence means in each case.
        /// </remarks>
        private static bool? Probe(BackupBase module, string backupPath)
        {
            try
            {
                return module.HasArtifactIn(backupPath);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Matches on the module's CLR type name - what the manifest records - so a reworded Title
        /// never breaks the join. Null when the manifest is absent or the type is not in it.
        /// </summary>
        private static string ManifestStateFor(ManifestData manifest, string typeName)
        {
            if (manifest == null)
                return null;

            IReadOnlyList<ManifestModule> entries = manifest.Modules;
            if (entries == null)
                return null;

            foreach (ManifestModule entry in entries)
            {
                if (entry != null && string.Equals(entry.Type, typeName, StringComparison.Ordinal))
                    return entry.State;
            }

            return null;
        }

        /// <summary>
        /// One sentence when the backup's machine or user differs from the current one, else null.
        /// </summary>
        internal static string DescribeProvenance(ManifestData manifest, string machineName, string userName)
        {
            if (manifest == null)
                return null;

            bool machineDiffers = !string.Equals(manifest.MachineName, machineName, StringComparison.OrdinalIgnoreCase);
            bool userDiffers = !string.Equals(manifest.UserName, userName, StringComparison.OrdinalIgnoreCase);

            if (!machineDiffers && !userDiffers)
                return null;

            List<string> parts = new List<string>(2);
            if (machineDiffers)
                parts.Add("machine " + NameOrUnknown(manifest.MachineName));
            if (userDiffers)
                parts.Add("user " + NameOrUnknown(manifest.UserName));

            return "This backup was made on a different " + string.Join(" and ", parts) +
                   "; some paths may not resolve on this PC.";
        }

        private static string NameOrUnknown(string value)
            => string.IsNullOrEmpty(value) ? "(unknown)" : "\"" + value + "\"";
    }
}
