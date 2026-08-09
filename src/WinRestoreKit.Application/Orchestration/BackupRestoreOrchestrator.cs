using DataHelper;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace WinRestoreKit
{
    /// <summary>
    /// Runs a backup or restore, reporting back through an <see cref="IRunUi"/>.
    /// </summary>
    /// <remarks>
    /// Moved verbatim out of <c>ConfPageView</c> in Phase 4 PR 5. Every method body is the view's
    /// former code with only the mechanical substitutions the PR lists: progress text, summary,
    /// consent, snapshot override, plan-composition error and Explorer-restart visibility are now
    /// routed through <see cref="IRunUi"/> instead of being read off controls and shown directly.
    /// The tree/selection reads stayed in the view; this class receives the final
    /// <see cref="BackupBase"/> selection and the paths as parameters.
    /// </remarks>
    internal sealed class BackupRestoreOrchestrator
    {
        private static readonly LogHelper logger = LogHelper.Instance;

        internal const string ArchiveProgressText = "Archiving backup payload";

        private readonly IRunUi ui;
        private readonly RunControl runControl;

        /// <summary>
        /// The restore-ready folder for the duration of a <see cref="RunRestore"/> call.
        /// </summary>
        /// <remarks>
        /// A compressed backup is extracted into a private temporary folder before module probes or
        /// restore operations run. Legacy backups keep their original folder as this value.
        /// </remarks>
        private string currentRestorePath;

        /// <summary>
        /// The original backup folder selected by the user.
        /// </summary>
        private string currentRestoreSourcePath;

        /// <summary>
        /// Whether the current restore's pre-restore snapshot folder was exclusively claimed by
        /// this run. Mirrors <c>exclusivelyOwned</c> in <see cref="RunBackupCore"/> for the same
        /// reason: <see cref="TakeSnapshot"/> is one of several places <see cref="TryCreateBackupFolder"/>
        /// runs, and the claim must happen there, immediately after creation, not at the unrelated
        /// point later where cancellation is discovered.
        /// </summary>
        private bool snapshotFolderExclusivelyOwned;

        /// <summary>
        /// The compression option selected for the most recent user backup request.
        /// </summary>
        internal SnapshotCompression SnapshotCompression { get; private set; } = SnapshotCompression.Fast;

        /// <summary>
        /// The exact backup path attempted by the most recent backup call.
        /// </summary>
        internal string BackupOutputPath { get; private set; }

        internal BackupRestoreOrchestrator(IRunUi ui, RunControl runControl = null)
        {
            this.ui = ui;
            this.runControl = runControl;
        }

        internal Task RunBackup(IReadOnlyList<BackupBase> selection, string backupPath)
        {
            BackupOutputPath = backupPath;
            return RunBackupCore(selection, backupPath, null, SnapshotCompression, null);
        }

        /// <summary>
        /// Runs a user backup below <paramref name="destinationPath"/> with an optional display name.
        /// </summary>
        /// <remarks>
        /// The physical folder always retains the frozen Data.NowShort shape. A present custom name
        /// is validated and stored only in the manifest for display after a backup folder is copied
        /// or renamed.
        /// </remarks>
        internal Task RunBackup(IReadOnlyList<BackupBase> selection, string destinationPath,
                                string snapshotName, SnapshotCompression compression)
        {
            if (!BackupNaming.TryValidateCustomName(snapshotName, out string safeSnapshotName))
            {
                ui.ShowSummary(RunSummary.For(new List<ModuleOutcome>(), false, RunVerb.Backup,
                    "the snapshot name is not a safe single folder name"), "Backup",
                    new List<ModuleOutcome>());
                return Task.CompletedTask;
            }

            if (!IsKnownCompression(compression))
            {
                ui.ShowSummary(RunSummary.For(new List<ModuleOutcome>(), false, RunVerb.Backup,
                    "the selected compression mode is not supported"), "Backup",
                    new List<ModuleOutcome>());
                return Task.CompletedTask;
            }

            if (string.IsNullOrWhiteSpace(destinationPath))
            {
                ui.ShowSummary(RunSummary.For(new List<ModuleOutcome>(), false, RunVerb.Backup,
                    "the backup destination is empty"), "Backup",
                    new List<ModuleOutcome>());
                return Task.CompletedTask;
            }

            SnapshotCompression = compression;
            string backupPath = Path.Combine(destinationPath, Data.NowShort);
            BackupOutputPath = backupPath;

            if (DestinationInsideSelectedSource(backupPath, selection, out string containingSource))
            {
                ui.ShowSummary(RunSummary.For(new List<ModuleOutcome>(), false, RunVerb.Backup,
                    "the chosen destination is inside a folder this backup would copy (" + containingSource
                    + "), which would copy the backup into itself; choose a destination outside the "
                    + "folders being backed up"), "Backup", new List<ModuleOutcome>());
                return Task.CompletedTask;
            }

            return RunBackupCore(selection, backupPath, safeSnapshotName, compression, destinationPath);
        }

        internal async Task RunRestore(IReadOnlyList<BackupBase> selection, string restorePath)
        {
            if (!BackupPayload.TryPrepareForRead(restorePath, out BackupPayload.ReadScope payload, out string error))
            {
                ui.ShowSummary(RunSummary.For(new List<ModuleOutcome>(), false, RunVerb.Restore,
                    "the compressed backup could not be prepared: " + error), "Restore",
                    new List<ModuleOutcome>());
                return;
            }

            using (payload)
            {
                currentRestoreSourcePath = restorePath;
                currentRestorePath = payload.Path;

                try
                {
                    await RunRestoreCore(selection);
                }
                finally
                {
                    currentRestorePath = null;
                    currentRestoreSourcePath = null;
                }
            }
        }

        /// <summary>
        /// Whether the backup destination lies at or beneath a folder that one of the selected
        /// modules copies wholesale, which would make the backup a descendant of its own source.
        /// </summary>
        /// <remarks>
        /// WindowsHelper.CopyFolderInto creates its destination and then enumerates the source's
        /// subdirectories, so a destination inside the source is discovered mid-copy and copied into
        /// itself, recursing until the path length limit or the disk is exhausted. Only folder
        /// targets are a hazard: registry, file and command targets do not recurse a directory tree.
        /// The check is a containment test on canonical full paths, and a source path that cannot be
        /// resolved is skipped rather than allowed to abort a backup over a formatting quirk.
        /// </remarks>
        private static bool DestinationInsideSelectedSource(string backupPath,
            IReadOnlyList<BackupBase> selection, out string containingSource)
        {
            containingSource = null;

            if (selection == null || string.IsNullOrWhiteSpace(backupPath))
                return false;

            string destination;

            try
            {
                destination = Path.GetFullPath(backupPath)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch (Exception)
            {
                return false;
            }

            foreach (BackupBase module in selection)
            {
                if (module == null)
                    continue;

                IReadOnlyList<RestoreTarget> targets;

                try
                {
                    targets = module.RestoreTargets;
                }
                catch (Exception)
                {
                    continue;
                }

                if (targets == null)
                    continue;

                foreach (RestoreTarget target in targets)
                {
                    if (target == null || target.Kind != RestoreTargetKind.Folder)
                        continue;

                    string source;

                    try
                    {
                        source = Path.GetFullPath(target.Path)
                            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    }
                    catch (Exception)
                    {
                        continue;
                    }

                    if (destination.Equals(source, StringComparison.OrdinalIgnoreCase)
                        || destination.StartsWith(source + Path.DirectorySeparatorChar,
                                                  StringComparison.OrdinalIgnoreCase))
                    {
                        containingSource = target.Path;
                        return true;
                    }
                }
            }

            return false;
        }

        // ---------------------------------------------------------------------------------------------
        //  Backup
        // ---------------------------------------------------------------------------------------------

        private async Task RunBackupCore(IReadOnlyList<BackupBase> selection, string backupPath,
                                         string snapshotName, SnapshotCompression compression,
                                         string destinationRoot)
        {
            bool folderExistedBeforeRun = Directory.Exists(backupPath);
            string createError;

            if (!TryCreateBackupFolder(backupPath, out createError))
            {
                // Reported as a run that DID NOT RUN, not as a crash and not as a silent
                // no-op: the user asked for a backup and got nothing, and they need to be
                // told which of those two it was.
                ui.ShowSummary(RunSummary.For(new List<ModuleOutcome>(), false, RunVerb.Backup,
                    "the backup folder could not be created: " + createError), "Backup",
                    new List<ModuleOutcome>());
                return;
            }

            // Claimed only when this run itself observed the folder as absent: a folder that
            // already existed is never this run's to claim, exclusively or otherwise, and is
            // handled by the folderExistedBeforeRun branch below regardless of this flag.
            bool exclusivelyOwned = !folderExistedBeforeRun && TryClaimExclusiveFolderOwnership(backupPath);

            if (destinationRoot != null)
                BackupRootRegistry.Remember(destinationRoot);

            // Before a single module writes anything. The backup path is built from Data.NowShort
            // by the caller, stamped once per process, so a second Backup click in the same session
            // runs into the SAME folder - and the first run's manifest would otherwise survive
            // alongside files the second run has already replaced. That is the confidently-green
            // failure the whole-and-last write exists to prevent, arriving by a different route: an
            // interrupted second run must read as unknown, not as the first run's verdict.
            if (InvalidateBackupManifest(backupPath))
            {
                ui.ShowSummary(RunSummary.For(new List<ModuleOutcome>(), false, RunVerb.Backup,
                    "a previous compressed backup in this folder is locked and could not be replaced. " +
                    "This backup was not started, so its manifest and payload cannot disagree."),
                    "Backup", new List<ModuleOutcome>());
                return;
            }

            // A private copy for the run. RunModulesBackup enumerates this list across awaits, and
            // the caller's selection list is mutable - so a re-entrant handler that rebuilt it while
            // the run is suspended would clear the collection mid-foreach and the enumerator would
            // throw out of an async void handler, killing the backup partway. The rail is shut for
            // the duration, which closes the known route; this closes the class. The results are
            // paired against the same copy, so the summary describes what actually ran.
            List<BackupBase> running = new List<BackupBase>(selection);

            List<ModuleResult> results =
                await RunModulesBackup(running, backupPath, "Backing up");

            await WaitForModuleBoundary();

            if (runControl != null && runControl.IsCancellationRequested)
            {
                List<BackupBase> completedModules = running.Take(results.Count).ToList();
                IReadOnlyList<ModuleOutcome> incompleteOutcomes = ModuleOutcome.Pair(completedModules, results);
                string detail;

                if (folderExistedBeforeRun)
                {
                    detail = "Cancellation was requested. No further group was started. Completed output " +
                             "remains without a trusted manifest.";
                }
                else if (!exclusivelyOwned)
                {
                    detail = "Cancellation was requested. No further group was started. Partial output " +
                             "created by this run remains without a trusted manifest, because another process " +
                             "may be writing to the same backup folder and it was left in place rather than risk " +
                             "deleting output that is not this run's own.";
                }
                else if (TryRemoveIncompleteBackupFolder(backupPath))
                {
                    detail = "Cancellation was requested. No further group was started. Partial output " +
                             "created by this run was removed.";
                }
                else
                {
                    detail = "Cancellation was requested. No further group was started. Partial output " +
                             "created by this run could not be removed and remains without a trusted manifest.";
                }

                ui.ShowSummary(RunSummary.Incomplete(incompleteOutcomes, RunVerb.Backup, detail), "Backup",
                    incompleteOutcomes);
                return;
            }
            ui.SetProgressText(ArchiveProgressText);
            ui.SetProgressPercent(100);
            string archiveError = null;
            bool archived = await Task.Run(() => BackupPayload.TryArchive(backupPath, compression, out archiveError));
            SnapshotCompression effectiveCompression = archived ? compression : SnapshotCompression.None;

            if (compression != SnapshotCompression.None && !archived)
                logger.LogMessage("Could not create compressed backup payload: " + archiveError);

            LogBackedUpElements(backupPath, running, results, new[]
            {
                "# Snapshot name: " + (snapshotName ?? Path.GetFileName(
                    backupPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))),
                "# Compression: " + CompressionStorageDescription(effectiveCompression, archiveError)
            });

            // Write backup_manifest.json - the machine-readable companion to the log above.
            //
            // From `running`, never from `selection`. This used to be called twice, the second time
            // with the caller's mutable list, which is the exact hazard the snapshot above exists to
            // remove: `selection` is a field the view rebuilds, read here AFTER the awaits. If the
            // two ever diverged the manifest would record a different module set than the folder
            // actually holds - and a different one than the RunSummary below reports, since that
            // pairs against `running`. The manifest is the artifact readers are told to trust, so it
            // has to agree with what ran. Flagged in review on PR #14 and fixed here.
            WriteBackupManifest(backupPath, running, results, snapshotName, effectiveCompression,
                archived ? BackupPayload.FileName : null);

            IReadOnlyList<ModuleOutcome> outcomes = ModuleOutcome.Pair(running, results);

            ui.ShowSummary(
                RunSummary.For(outcomes, true, RunVerb.Backup),
                "Backup", outcomes);
        }

        /// <summary>
        /// Creates the backup folder, reporting rather than throwing if it cannot.
        /// </summary>
        /// <remarks>
        /// Ordinary failures, not exotic ones: the exe under Program Files on a standard-user
        /// account, a full disk, a path over the length limit. This used to be a bare
        /// Directory.CreateDirectory outside any try, in an async void handler.
        ///
        /// Directory.CreateDirectory is called unconditionally, so this method does not split its
        /// own existence probe and creation into separate filesystem operations. A successful call
        /// means the folder exists and is usable by this process. It does not reserve ownership of
        /// an existing folder or provide cross-process mutual exclusion, so success must not be
        /// treated as exclusive access when another process selects the same path.
        /// </remarks>
        private bool TryCreateBackupFolder(string path, out string error)
        {
            error = null;

            try
            {
                Directory.CreateDirectory(path);

                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                logger.LogMessage("Could not create the backup folder " + path + ": " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Marker filename used to break the folder-creation race between two app instances.
        /// </summary>
        /// <remarks>
        /// Not a compatibility surface: nothing on the read side ever parses this file, it exists
        /// purely to answer one question at the moment a run first sees its own timestamp folder as
        /// absent, and it is safe to ignore or delete by hand.
        /// </remarks>
        private const string OwnershipMarkerFileName = ".run-owner";

        /// <summary>
        /// Atomically decides which run, of possibly several racing to the same folder, may treat
        /// this folder as its own to delete on cancellation.
        /// </summary>
        /// <remarks>
        /// Data.NowShort is minute-granularity, so two app instances started in the same minute and
        /// targeting the same destination compute the identical backup path. Both then observe
        /// Directory.Exists as false and both succeed at Directory.CreateDirectory, which is
        /// idempotent and tells neither of them anything about the other. Left unresolved, whichever
        /// one cancels first would delete a folder the other is actively writing into.
        ///
        /// FileMode.CreateNew is the one filesystem primitive here that is genuinely atomic: it
        /// throws if the file already exists, so at most one caller across any number of racing
        /// processes ever observes success. That caller, and only that caller, may later delete the
        /// folder on cancellation. A caller that loses the race must assume the folder might belong
        /// to someone else and leave it alone, exactly as it already would for a folder that existed
        /// before this run started.
        /// </remarks>
        internal static bool TryClaimExclusiveFolderOwnership(string backupPath)
        {
            try
            {
                using (File.Open(Path.Combine(backupPath, OwnershipMarkerFileName),
                    FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    return true;
                }
            }
            catch (IOException)
            {
                // Already claimed, by this run's own earlier attempt or by a racing process.
                return false;
            }
            catch (Exception ex)
            {
                // Cannot tell. Treat like losing the race: the safe direction is never deleting
                // output this run cannot prove is exclusively its own.
                logger.LogMessage("Could not establish backup folder ownership for " + backupPath + ": " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Backs up a set of modules into a folder, reporting one result per module.
        /// </summary>
        /// <remarks>
        /// Shared by the Backup button and by the pre-restore snapshot, which is the point: the
        /// snapshot is an ordinary backup, so it inherits the export verification and the honest
        /// per-module results rather than growing a second, less-tested capture path.
        /// </remarks>
        private async Task<List<ModuleResult>> RunModulesBackup(IReadOnlyList<BackupBase> modules,
                                                                string folder, string progressVerb)
        {
            List<ModuleResult> results = new List<ModuleResult>();
            int total = modules.Count;
            Stopwatch stopwatch = Stopwatch.StartNew();

            ui.SetProgressPercent(0);

            for (int index = 0; index < total; index++)
            {
                if (!await WaitForModuleBoundary())
                    break;

                BackupBase module = modules[index];
                ui.SetProgressText(progressVerb + ": " + module.Title);

                ModuleResult outcome;

                try
                {
                    outcome = await module.BackupAsync(folder);
                }
                catch (Exception ex)
                {
                    // Rule 6. Mandatory, not defensive style: this loop is driven by an async void
                    // click handler, so an escaping exception is unhandled and takes the process
                    // down along with every result gathered so far.
                    outcome = ModuleResult.Aggregate(new[]
                    {
                        StepResult.Failed(module.Title, "unhandled error: " + ex.GetType().Name + ": " + ex.Message)
                    });
                }

                results.Add(outcome);

                bool hasByteMeasurement = TryMeasureBackupArtifactBytes(folder, out long bytesWritten);
                ProgressMetricValues metrics = ProgressMetrics.Create(index + 1, total, stopwatch.Elapsed,
                    bytesWritten, hasByteMeasurement);

                ui.SetProgressPercent(metrics.Percent);
                ui.SetProgressDetail(
                    ProgressMetrics.FormatGroup(index + 1, total, module.Title),
                    metrics.Elapsed, metrics.Remaining, metrics.Throughput, metrics.BytesWritten,
                    CountSteps(results, ResultState.Failed), CountSteps(results, ResultState.Skipped));
                ui.SetProgressText("Choose settings");
            }

            if (runControl == null || !runControl.IsCancellationRequested)
                ui.SetProgressPercent(100);

            return results;
        }

        private static bool TryMeasureBackupArtifactBytes(string folder, out long bytesWritten)
        {
            bytesWritten = 0;

            try
            {
                foreach (string path in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
                {
                    string name = Path.GetFileName(path);

                    if (IsBackupMetadataFile(name, path, folder))
                        continue;

                    bytesWritten = checked(bytesWritten + new FileInfo(path).Length);
                }

                return true;
            }
            catch (Exception)
            {
                bytesWritten = 0;
                return false;
            }
        }

        private Task<bool> WaitForModuleBoundary()
        {
            return runControl == null
                ? Task.FromResult(true)
                : runControl.WaitIfPausedAsync();
        }

        private bool TryRemoveIncompleteBackupFolder(string backupPath)
        {
            try
            {
                Directory.Delete(backupPath, true);
                return true;
            }
            catch (Exception ex)
            {
                logger.LogMessage("Could not remove canceled backup output " + backupPath + ": " + ex.Message);
                return false;
            }
        }

        private static bool IsBackupMetadataFile(string name, string path, string folder)
        {
            if (!string.Equals(Path.GetDirectoryName(path), folder, StringComparison.OrdinalIgnoreCase))
                return false;

            return string.Equals(name, BackupManifest.FileName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "backup_log.txt", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, BackupPayload.FileName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, OwnershipMarkerFileName, StringComparison.OrdinalIgnoreCase)
                || name.StartsWith(".payload-", StringComparison.OrdinalIgnoreCase);
        }

        private static int CountSteps(IEnumerable<ModuleResult> results, ResultState state)
            => results.Sum(result => result.Steps.Count(step => step.State == state));

        private static bool IsKnownCompression(SnapshotCompression compression)
            => compression == SnapshotCompression.None
                || compression == SnapshotCompression.Fast
                || compression == SnapshotCompression.Max;

        private static string CompressionStorageDescription(SnapshotCompression compression, string archiveError)
        {
            switch (compression)
            {
                case SnapshotCompression.None:
                    return string.IsNullOrEmpty(archiveError)
                        ? "None selected. Files are stored in the restore-compatible folder layout."
                        : "Compression was not applied. Files remain in the restore-compatible folder layout: "
                          + archiveError;
                case SnapshotCompression.Fast:
                    return "Fast selected. Module artifacts are stored in " + BackupPayload.FileName + ".";
                case SnapshotCompression.Max:
                    return "Max selected. Module artifacts are stored in " + BackupPayload.FileName + ".";
                default:
                    return "Unknown.";
            }
        }

        // Write a backup_log.txt that records outcomes, not just the selection.
        private void LogBackedUpElements(string backupFolderPath, IReadOnlyList<BackupBase> configurations,
                                         IReadOnlyList<ModuleResult> results,
                                         IEnumerable<string> extraHeaderLines = null)
        {
            string logFilePath = Path.Combine(backupFolderPath, "backup_log.txt");

            try
            {
                string text = BackupLog.Compose(configurations, results, DateTime.Now.ToString(), extraHeaderLines);
                File.WriteAllText(logFilePath, text);
            }
            catch (Exception ex)
            {
                logger.LogMessage("Failed to create backup log file: " + ex.Message);
            }
        }

        /// <summary>
        /// Removes any manifest already in the folder, so a run that does not finish leaves none.
        /// </summary>
        /// <remarks>
        /// The whole-and-last write guarantees a manifest describes a COMPLETED run, but only for a
        /// folder that started empty. Backing up twice in one session reuses the folder, because
        /// the backup path is built from Data.NowShort and that is stamped once per process. Then
        /// the first run's manifest sits beside files the second run has already overwritten, and if
        /// the second run is interrupted the reader trusts a verdict for data that is no longer
        /// there - a stale green, which is the failure this file exists to make impossible.
        ///
        /// Deleting up front means the window between "modules started" and "manifest published" has
        /// no manifest in it at all, which is exactly the state the reader renders as unknown.
        ///
        /// A stray .tmp is cleared too: the writer removes its own on failure, but a process killed
        /// mid-write cannot.
        ///
        /// Deleting can itself fail - a read-only attribute, or a file held open by a backup tool or
        /// an editor - so there are two more attempts before giving up: clear the attribute and
        /// retry, then truncate the file to nothing. An empty file is not valid JSON, so TryParse
        /// refuses it and the reader says "details unavailable", which is the honest answer. A stale
        /// manifest can still be treated as unknown, but a stale payload could later be paired with
        /// a newly written manifest. That mismatch risks restoring a previous backup, so callers
        /// must not start a run when the old payload cannot be removed or emptied.
        /// </remarks>
        private bool InvalidateBackupManifest(string backupFolderPath)
        {
            string finalPath = Path.Combine(backupFolderPath, BackupManifest.FileName);
            string payloadPath = Path.Combine(backupFolderPath, BackupPayload.FileName);
            bool payloadExisted = File.Exists(payloadPath);
            bool payloadSurvived = false;

            foreach (string path in new[] { finalPath, TempManifestPath(finalPath), payloadPath })
            {
                if (!TryRemove(path) && File.Exists(path))
                {
                    logger.LogMessage(
                        "The previous backup metadata or payload at " + path + " could not be removed or emptied. "
                        + "If this run does not finish, that file still describes the PREVIOUS run.");

                    if (payloadExisted && path == payloadPath)
                        payloadSurvived = true;
                }
            }

            return payloadSurvived;
        }

        private bool TryRemove(string path)
        {
            try
            {
                if (!File.Exists(path))
                    return true;

                File.Delete(path);
                return true;
            }
            catch (Exception ex)
            {
                logger.LogMessage("Could not delete " + path + ": " + ex.Message);
            }

            // A read-only attribute is the common cause and is ours to clear.
            try
            {
                File.SetAttributes(path, FileAttributes.Normal);
                File.Delete(path);
                return true;
            }
            catch (Exception ex)
            {
                logger.LogMessage("Could not delete " + path + " after clearing its attributes: " + ex.Message);
            }

            // Last resort: make it unparseable. An empty file reads as unknown, which is true.
            try
            {
                File.WriteAllText(path, string.Empty);
                return true;
            }
            catch (Exception ex)
            {
                logger.LogMessage("Could not empty " + path + ": " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// The scratch path the manifest is written to before being moved into place.
        /// </summary>
        /// <remarks>
        /// Process-scoped. Data.NowShort has minute precision and there is no single-instance guard,
        /// so two copies of the app started in the same minute address the same backup folder. They
        /// would otherwise share one .tmp and overwrite each other's half-written document, which is
        /// the one way this write can publish a well-formed manifest describing neither run. Their
        /// racing over the FINAL file is a pre-existing hazard of the shared folder - two processes
        /// writing one backup was already unsound before this file existed - but the temp collision
        /// is created here, so it is closed here.
        /// </remarks>
        private static string TempManifestPath(string finalPath)
            => finalPath + "." + Environment.ProcessId.ToString(CultureInfo.InvariantCulture) + ".tmp";

        /// <summary>
        /// Writes backup_manifest.json beside the log, atomically and only once every module has
        /// reported.
        /// </summary>
        /// <remarks>
        /// Whole-and-last, which is the point of the temp-file dance rather than a plain
        /// WriteAllText. The reader treats an absent manifest as unknown and says so, and that
        /// already covers a run killed partway. What it does NOT cover is a half-written file that
        /// still parses: that presents a truncated run as a smaller successful one - not unknown,
        /// but confidently green and wrong. File.Move over the final name makes the file appear
        /// complete or not at all, so a crash mid-write leaves the folder manifest-less and honest.
        ///
        /// Called for user backups only, not for the pre-restore snapshot: snapshots are identified
        /// by SnapshotNaming and their presentation belongs to History. The composer takes the same
        /// arguments either way, so adding the snapshot call later costs one line.
        ///
        /// Failing to write is logged and swallowed. The backup itself succeeded; losing its index
        /// must not be reported as losing the data.
        /// </remarks>
        private void WriteBackupManifest(string backupFolderPath, IReadOnlyList<BackupBase> configurations,
                                         IReadOnlyList<ModuleResult> results, string snapshotName,
                                         SnapshotCompression compression, string payloadFile)
        {
            string finalPath = Path.Combine(backupFolderPath, BackupManifest.FileName);
            string tempPath = TempManifestPath(finalPath);

            try
            {
                string json = BackupManifest.Compose(
                    configurations,
                    results,
                    DateTime.Now,
                    Environment.MachineName,
                    Environment.UserName,
                    OsHelper.GetVersion(),
                    VersionInfo.GetCurrentVersion(Assembly.GetEntryAssembly()),
                    snapshotName,
                    compression,
                    payloadFile);

                File.WriteAllText(tempPath, json);

                // Overwrite rather than fail: a leftover manifest from an earlier run into the same
                // folder would otherwise outlive the run that replaced it.
                File.Move(tempPath, finalPath, true);
            }
            catch (Exception ex)
            {
                logger.LogMessage("Failed to create backup manifest file: " + ex.Message);

                // A .tmp left behind would be mistaken for backup content by the restore view.
                try
                {
                    if (File.Exists(tempPath))
                        File.Delete(tempPath);
                }
                catch (Exception cleanupEx)
                {
                    logger.LogMessage("Could not remove the partial manifest file: " + cleanupEx.Message);
                }
            }
        }

        // ---------------------------------------------------------------------------------------------
        //  Restore
        // ---------------------------------------------------------------------------------------------

        /// <summary>
        /// Runs one module's restore, having first dealt with the process that owns the files it is
        /// about to overwrite.
        /// </summary>
        /// <remarks>
        /// The process state is re-read here rather than reused from consent time. Consent persists
        /// for the run; whether Chrome is open does not, and the user can reopen it while the
        /// snapshot is still being taken.
        /// </remarks>
        private async Task<ModuleResult> RestoreOne(RestoreScopeEntry entry, IReadOnlyList<string> consented)
        {
            BackupBase config = entry.Module;

            try
            {
                // Refused on the same observation the snapshot set was chosen from, rather than on a
                // fresh one. RestoreScope holds the reasoning; the short version is that a module the
                // snapshot left out must not be restored, and re-reading the process state here is
                // exactly how the two halves came to disagree.
                if (!entry.WillBeRestored)
                    return ModuleResult.Aggregate(new[] { RestoreScope.DescribeBlock(entry) });

                IReadOnlyList<RestoreCloseRequirement> requirements =
                    config.ProcessesToCloseBeforeRestore ?? new RestoreCloseRequirement[0];

                List<StepResult> closeSteps = new List<StepResult>();
                List<RestoreCloseRequirement> justInTime = new List<RestoreCloseRequirement>();

                foreach (RestoreCloseRequirement requirement in requirements)
                {
                    // Skipped exactly as RestoreScope.Evaluate and RestorePlan.CollectCloses skip it.
                    // Those two treat a null entry as a supported degenerate declaration; this loop
                    // was the only one of the three reading the list without the guard, so a module
                    // they both passed over as harmless reached here and dereferenced it. The catch
                    // below would have caught the NullReferenceException, so the cost was not a crash
                    // but a worse lie than one: the module was scoped as unblocked, was snapshotted,
                    // had its process closed - and was then reported as an unhandled error.
                    if (requirement == null)
                        continue;

                    bool consentGiven = requirement.NeedsConsent
                        && RestoreScope.IsConsented(consented, requirement.ProcessName);
                    bool isRunning = false;
                    CloseResult closeResult = CloseResult.NotRunning;

                    if (requirement.NeedsConsent)
                    {
                        string processName = requirement.ProcessName;

                        isRunning = await Task.Run(() => Utils.IsProcessRunning(processName));

                        // Re-closed rather than trusted, because consent persists for the run and the
                        // process state does not: the user can reopen a browser while the snapshot is
                        // still being taken. Failing here is safe in a way that failing the other way
                        // round is not - this module WAS snapshotted, so refusing it now leaves a
                        // usable fallback on disk.
                        if (consentGiven && isRunning)
                            closeResult = await Task.Run(() => Utils.CloseProcess(processName));
                    }

                    RestoreDecision decision = RestoreDispatch.Decide(
                        config.Title, requirement, consentGiven, isRunning, closeResult);

                    if (decision.CloseStep != null)
                        closeSteps.Add(decision.CloseStep);

                    if (decision.JustInTimeClose != null)
                        justInTime.Add(decision.JustInTimeClose);

                    // Skip and Fail are both refusals to overwrite, so nothing after this point may
                    // run - including the remaining requirements, whose closes would be pointless.
                    if (decision.Action != RestoreAction.Run)
                        return ModuleResult.Aggregate(closeSteps);
                }

                // Closed here rather than up front: StartMenuExperienceHost respawns within seconds,
                // so a close performed at consent time is gone again before the copy starts.
                foreach (RestoreCloseRequirement requirement in justInTime)
                {
                    string processName = requirement.ProcessName;
                    CloseResult closed = await Task.Run(() => Utils.CloseProcess(processName));

                    closeSteps.Add(DescribeJustInTimeClose(config.Title, requirement, closed));
                }

                ModuleResult outcome = config is Conf.AppStoreApps appStoreApps
                    ? await appStoreApps.RestoreAsync(currentRestorePath, ui.DialogOwner)
                    : await config.RestoreAsync(currentRestorePath);

                foreach (StepResult closeStep in closeSteps)
                    outcome = RestoreDispatch.Fold(closeStep, outcome);

                return outcome;
            }
            catch (Exception ex)
            {
                // Rule 6, and it matters more here than on the backup path: this method is awaited
                // by the restore flow, which is itself awaited from an async void handler. A module
                // failure must therefore become a result rather than escape as an unhandled UI error.
                return ModuleResult.Aggregate(new[]
                {
                    StepResult.Failed(config.Title, "unhandled error: " + ex.GetType().Name + ": " + ex.Message)
                });
            }
        }

        /// <summary>
        /// The consented processes that some module actually about to be restored owns.
        /// </summary>
        /// <remarks>
        /// Consent is gathered from the tree selection, which says nothing about what the chosen
        /// backup folder contains. Closing on consent alone therefore kills a browser for a module
        /// whose restore will report "nothing was backed up for this item" - real, visible work
        /// destroyed for an operation knowable in advance to be a no-op.
        /// </remarks>
        private IEnumerable<string> ProcessesWorthClosing(IReadOnlyList<BackupBase> modules,
                                                          IReadOnlyList<string> consented)
        {
            HashSet<string> worth = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (BackupBase module in modules)
            {
                // RestoreScope's, deliberately not a second copy: this asks the same question of
                // the same modules that Evaluate asks moments later, and the two must not be able
                // to disagree. See the remarks on RestoreScope.HasBackup.
                if (!RestoreScope.HasBackup(module, currentRestorePath))
                    continue;

                foreach (RestoreCloseRequirement requirement in
                         module.ProcessesToCloseBeforeRestore ?? new RestoreCloseRequirement[0])
                {
                    if (requirement != null && requirement.NeedsConsent)
                        worth.Add(requirement.ProcessName);
                }
            }

            return consented.Where(worth.Contains);
        }

        /// <summary>
        /// The sentence naming processes this run has already closed, or nothing when it closed none.
        /// </summary>
        private static string DescribeAlreadyClosed(IDictionary<string, CloseResult> closedUpFront)
        {
            string[] closed = closedUpFront
                .Where(entry => entry.Value == CloseResult.Exited)
                .Select(entry => entry.Key)
                .ToArray();

            if (closed.Length == 0)
                return "";

            return " Note that " + string.Join(", ", closed) +
                   " had already been closed in order to take the snapshot.";
        }

        /// <summary>
        /// The just-in-time close, as a step so it survives into restore_log.txt.
        /// </summary>
        /// <remarks>
        /// A process that would not close is reported as Skipped rather than Failed on purpose. These
        /// are the processes Windows restarts by itself - StartMenuExperienceHost is back within
        /// seconds - so it is running again during the copy on a healthy machine, and failing the
        /// module for that would cry wolf on nearly every run.
        ///
        /// That is a judgement about noise, not a guarantee of correctness, and the step wording says
        /// only what is known: files it held open may not have been replaced. A locked file does
        /// surface, because the copy fails on it and Aggregate fails the module. A process that keeps
        /// its state in memory and flushes on exit does NOT - it can let every file copy cleanly and
        /// then write its own version back over the restore. The Start menu layout store behaves that
        /// way, which is why the reason is worded as a caveat rather than an all-clear.
        /// </remarks>
        private static StepResult DescribeJustInTimeClose(string moduleTitle,
                                                          RestoreCloseRequirement requirement,
                                                          CloseResult closed)
        {
            switch (closed)
            {
                case CloseResult.Exited:
                    return StepResult.Succeeded(moduleTitle,
                        "closed " + requirement.DisplayName + " before writing its files");

                case CloseResult.NotRunning:
                    return StepResult.Skipped(moduleTitle,
                        requirement.DisplayName + " was not running, so nothing had to be closed");

                default:
                    return StepResult.Skipped(moduleTitle,
                        requirement.DisplayName + " could not be closed first (" + closed +
                        "), so any files it was holding open may not have been replaced");
            }
        }

        // Restoration logic with selected configurations
        private async Task<List<ModuleResult>> PerformRestoration(IReadOnlyList<RestoreScopeEntry> scope,
                                                                  IReadOnlyList<string> consented)
        {
            List<ModuleResult> results = new List<ModuleResult>();
            int total = scope.Count;
            Stopwatch stopwatch = Stopwatch.StartNew();

            ui.SetProgressPercent(0);

            for (int index = 0; index < total; index++)
            {
                if (!await WaitForModuleBoundary())
                    break;

                RestoreScopeEntry entry = scope[index];
                ui.SetProgressText("Restoring: " + entry.Module.Title);

                results.Add(await RestoreOne(entry, consented));

                ProgressMetricValues metrics = ProgressMetrics.Create(index + 1, total, stopwatch.Elapsed, 0, false);

                ui.SetProgressPercent(metrics.Percent);
                ui.SetProgressDetail(
                    ProgressMetrics.FormatGroup(index + 1, total, entry.Module.Title),
                    metrics.Elapsed, metrics.Remaining, metrics.Throughput, metrics.BytesWritten,
                    CountSteps(results, ResultState.Failed), CountSteps(results, ResultState.Skipped));
                ui.SetProgressText("Choose settings");
            }

            if (runControl == null || !runControl.IsCancellationRequested)
                ui.SetProgressPercent(100);

            return results;
        }

        /// <summary>
        /// Takes the pre-restore snapshot and reports whether the restore may go ahead on it.
        /// </summary>
        private async Task<SnapshotDecision> TakeSnapshot(IReadOnlyList<BackupBase> snapshotSet,
                                                          string snapshotFolderPath, int blockedCount)
        {
            if (snapshotFolderPath == null)
                return SnapshotGate.FolderNotCreated("a snapshot folder name could not be chosen");

            if (snapshotSet.Count == 0)
                return SnapshotGate.Evaluate(new List<ModuleOutcome>(), blockedCount);

            string createError;

            if (!TryCreateBackupFolder(snapshotFolderPath, out createError))
                return SnapshotGate.FolderNotCreated(createError);

            // As early as possible after creation, exactly like the equivalent claim in
            // RunBackupCore, and for the identical reason: SnapshotNaming.Unique's own free-name
            // scan has the same TOCTOU gap between two processes racing the same restore.
            snapshotFolderExclusivelyOwned = TryClaimExclusiveFolderOwnership(snapshotFolderPath);

            List<ModuleResult> results =
                await RunModulesBackup(snapshotSet, snapshotFolderPath, "Snapshotting");

            LogBackedUpElements(snapshotFolderPath, snapshotSet, results, new[]
            {
                "# Pre-restore snapshot, taken before restoring from " + currentRestoreSourcePath,
                "# " + RestorePlan.FidelityCaveat
            });

            return SnapshotGate.Evaluate(ModuleOutcome.Pair(snapshotSet, results));
        }

        // Write a restore_log.txt recording what this restore changed and what could undo it.
        private void LogRestoredElements(IReadOnlyList<BackupBase> configurations,
                                         IReadOnlyList<ModuleResult> results,
                                         SnapshotDecision snapshot, string snapshotFolderPath)
        {
            bool haveSnapshotFolder = snapshotFolderPath != null && Directory.Exists(snapshotFolderPath);

            string text = RestoreLog.Compose(configurations, results, DateTime.Now.ToString(),
                currentRestoreSourcePath, snapshot, haveSnapshotFolder ? snapshotFolderPath : null);

            // Beside the rollback artifact when there is one. When the gate was overridden after the
            // folder could not be created there is nowhere else but the folder just restored from.
            string logFilePath = haveSnapshotFolder
                ? Path.Combine(snapshotFolderPath, RestoreLog.FileName)
                : Path.Combine(currentRestoreSourcePath, RestoreLog.FallbackFileName(DateTime.Now));

            try
            {
                File.WriteAllText(logFilePath, text);
            }
            catch (Exception ex)
            {
                logger.LogMessage("Failed to create restore log file: " + ex.Message);
            }
        }

        private async Task RunRestoreCore(IReadOnlyList<BackupBase> selection)
        {
            if (currentRestorePath == "" || !Directory.Exists(currentRestorePath))
            {
                ui.ShowSummary(RunSummary.For(new List<ModuleOutcome>(), false, RunVerb.Restore), "Restore",
                    new List<ModuleOutcome>());
                return;
            }

            // Stage 1: name the snapshot before asking, so the dialog can say where it will go.
            // A fresh timestamp, never Data.NowShort - that is stamped once per process.
            string snapshotFolderPath = null;
            snapshotFolderExclusivelyOwned = false;

            try
            {
                string name = SnapshotNaming.Unique(SnapshotNaming.NameFor(DateTime.Now),
                    n => Directory.Exists(Path.Combine(Data.DataRootDir, n)));

                snapshotFolderPath = Path.Combine(Data.DataRootDir, name);
            }
            catch (Exception ex)
            {
                logger.LogMessage("Could not choose a snapshot folder name: " + ex.Message);
            }

            // Composing the plan reads four virtual members off every selected module -
            // RestoreTargets, ProcessesToCloseBeforeRestore, Title and WarningMessage - and any of
            // the four can throw from a module written later. This stage sits between the try above
            // and the confirmation dialog, and the whole chain up to the async void click handler
            // has no catch, so an escaping exception here would surface as WinForms' unhandled
            // exception dialog mid-restore.
            //
            // Fail closed: the plan IS the description the user consents against, so no description
            // means no consent, and no consent means nothing is touched.
            RestorePlan plan;

            try
            {
                plan = new RestorePlan(selection, currentRestoreSourcePath,
                    snapshotFolderPath ?? "(no snapshot folder could be named)");
            }
            catch (Exception ex)
            {
                logger.LogMessage("Could not describe what this restore would overwrite: " + ex.Message);

                ui.ShowPlanCompositionError(
                    "What this restore would overwrite could not be described, so you were not asked " +
                    "to confirm it and nothing has been changed.\r\n\r\n" + ex.Message,
                    "Restore");

                return;
            }

            // Stage 2: informed consent, on the UI thread, before anything is created.
            IReadOnlyList<string> consented = ui.ShowConsentDialog(plan);

            if (consented == null)
            {
                logger.LogMessage("Restore canceled, nothing was changed.");
                ui.ShowSummary(RunSummary.Canceled(RunVerb.Restore), "Restore",
                    new List<ModuleOutcome>());
                return;
            }

            // Stage 3: close the consented processes once, up front, so the snapshot's own backup
            // does not prompt about the same browser the user has already answered for.
            //
            // Only for processes some module is actually going to be restored from. A module the
            // backup folder holds nothing for is refused before this point, so its browser is not
            // killed for a restore that was always going to write nothing - which cost the user
            // every open tab, and cost it in a way the pre-2b code did not, because that closed
            // nothing at all.
            Dictionary<string, CloseResult> closedUpFront =
                new Dictionary<string, CloseResult>(StringComparer.OrdinalIgnoreCase);

            foreach (string processName in ProcessesWorthClosing(selection, consented))
            {
                string name = processName;
                CloseResult closed = await Task.Run(() => Utils.CloseProcess(name));

                closedUpFront[name] = closed;
                logger.LogMessage("Closing " + name + " before the restore: " + closed);
            }

            // Stages 4 and 5: snapshot, then decide whether the restore may go ahead on it.
            //
            // Worked out ONCE, here, and used by both the snapshot and the dispatch loop. Deciding
            // twice from two readings of the process state is what previously let a module be left
            // out of the snapshot and then restored anyway.
            IReadOnlyList<RestoreScopeEntry> scope =
                RestoreScope.For(selection, consented, closedUpFront, currentRestorePath);

            List<BackupBase> snapshotSet = scope
                .Where(entry => entry.NeedsSnapshot)
                .Select(entry => entry.Module)
                .ToList();

            int blockedCount = scope.Count(entry => !entry.WillBeRestored);
            bool snapshotFolderExistedBeforeRun = snapshotFolderPath != null && Directory.Exists(snapshotFolderPath);

            SnapshotDecision snapshot = await TakeSnapshot(snapshotSet, snapshotFolderPath, blockedCount);

            logger.LogMessage(snapshot.Summary);

            if (runControl != null && runControl.IsCancellationRequested)
            {
                if (!snapshotFolderExistedBeforeRun && snapshotFolderExclusivelyOwned
                    && snapshotFolderPath != null && Directory.Exists(snapshotFolderPath))
                    TryRemoveIncompleteBackupFolder(snapshotFolderPath);

                ui.ShowSummary(RunSummary.Incomplete(new List<ModuleOutcome>(), RunVerb.Restore,
                    "Cancellation was requested during the pre-restore snapshot. No selected setting was restored."),
                    "Restore", new List<ModuleOutcome>());
                return;
            }

            if (snapshot.RequiresOverride)
            {
                bool proceed = ui.ConfirmSnapshotOverride(
                    snapshot.Describe() + "\r\n" + RestorePlan.FidelityCaveat +
                    "\r\n\r\nRestore anyway, without being able to undo it?",
                    "Pre-restore snapshot");

                if (!proceed)
                {
                    // Names the processes that were already closed. They were closed to take the
                    // snapshot, and the snapshot is what just failed - so the user gave up an open
                    // browser for a restore that then did not happen, and "nothing ran" on its own
                    // would be the misreport this phase exists to remove.
                    ui.ShowSummary(RunSummary.For(new List<ModuleOutcome>(), false, RunVerb.Restore,
                        "the pre-restore snapshot did not complete and you chose not to continue." +
                        DescribeAlreadyClosed(closedUpFront)), "Restore", new List<ModuleOutcome>());
                    return;
                }
            }

            // Stage 6.
            List<ModuleResult> results = await PerformRestoration(scope, consented);

            // Stage 7. Reported against the modules the restore actually walked, not against the
            // list it was asked to walk. `results` is built one-per-scope-entry, and RestoreScope.For
            // drops nulls - so pairing them with the selection pairs two lists that are only equal
            // in length by coincidence of upstream filtering. Whenever they were not, every outcome
            // after the dropped module would be attributed to the wrong one, in the summary and in
            // restore_log.txt both. Projecting from scope makes the alignment structural.
            List<BackupBase> restoredModules = scope.Take(results.Count).Select(entry => entry.Module).ToList();

            LogRestoredElements(restoredModules, results, snapshot, snapshotFolderPath);

            if (runControl != null && runControl.IsCancellationRequested)
            {
                ui.SetExplorerRestartVisible(ExplorerRestartPrompt.IsNeeded(restoredModules, results));
                IReadOnlyList<ModuleOutcome> incompleteOutcomes = ModuleOutcome.Pair(restoredModules, results);
                ui.ShowSummary(RunSummary.Incomplete(incompleteOutcomes, RunVerb.Restore,
                    "Cancellation was requested. No further group was started. Already restored settings were not rolled back."),
                    "Restore", incompleteOutcomes);
                return;
            }

            // Stage 8. Gated on a module that declares RequiresExplorerRestart having actually
            // WRITTEN something, not merely on the declaration and not on its folded verdict. The
            // decision moved to ExplorerRestartPrompt in Phase 3c, where it can be tested and where
            // the reason the folded verdict is the wrong input is written down: Aggregate lets one
            // failed step dominate, so a hybrid that restored the taskbar pins and then failed a
            // later step would hide the very button its own warning tells the user to press.
            ui.SetExplorerRestartVisible(ExplorerRestartPrompt.IsNeeded(restoredModules, results));

            IReadOnlyList<ModuleOutcome> outcomes = ModuleOutcome.Pair(restoredModules, results);

            ui.ShowSummary(
                RunSummary.For(outcomes, true, RunVerb.Restore),
                "Restore", outcomes);
        }
    }

    internal readonly struct ProgressMetricValues
    {
        internal ProgressMetricValues(int percent, string elapsed, string remaining, string bytes,
                                      string throughput, long bytesWritten)
        {
            Percent = percent;
            Elapsed = elapsed;
            Remaining = remaining;
            Bytes = bytes;
            Throughput = throughput;
            BytesWritten = bytesWritten;
        }

        internal int Percent { get; }
        internal string Elapsed { get; }
        internal string Remaining { get; }
        internal string Bytes { get; }
        internal string Throughput { get; }
        internal long BytesWritten { get; }
    }

    internal static class ProgressMetrics
    {
        internal const string NotAvailable = "N/A";

        internal static ProgressMetricValues Create(int completed, int total, TimeSpan elapsed,
                                                    long bytesWritten, bool hasByteMeasurement)
        {
            int safeTotal = Math.Max(0, total);
            int safeCompleted = Math.Max(0, Math.Min(completed, safeTotal));
            TimeSpan safeElapsed = elapsed < TimeSpan.Zero ? TimeSpan.Zero : elapsed;
            TimeSpan remaining = EstimateRemaining(safeCompleted, safeTotal, safeElapsed);
            long measuredBytes = hasByteMeasurement ? Math.Max(0, bytesWritten) : -1;
            string bytes = hasByteMeasurement ? FormatBytes(measuredBytes) : NotAvailable;
            string throughput = hasByteMeasurement && safeElapsed > TimeSpan.Zero
                ? FormatBytes(measuredBytes / safeElapsed.TotalSeconds) + "/s"
                : NotAvailable;

            return new ProgressMetricValues(
                Percent(safeCompleted, safeTotal),
                FormatDuration(safeElapsed),
                FormatDuration(remaining),
                bytes,
                throughput,
                measuredBytes);
        }

        internal static string FormatGroup(int completed, int total, string title)
            => "Group " + completed.ToString(CultureInfo.InvariantCulture) + " of " +
               total.ToString(CultureInfo.InvariantCulture) + ". " + (title ?? string.Empty);

        private static int Percent(int completed, int total)
            => total == 0 ? 0 : completed * 100 / total;

        private static TimeSpan EstimateRemaining(int completed, int total, TimeSpan elapsed)
        {
            if (completed == 0 || total <= completed)
                return TimeSpan.Zero;

            double remainingTicks = elapsed.Ticks * (double)(total - completed) / completed;

            return TimeSpan.FromTicks((long)Math.Min(remainingTicks, TimeSpan.MaxValue.Ticks));
        }

        private static string FormatDuration(TimeSpan value)
        {
            long hours = (long)value.TotalHours;

            return hours.ToString("D2", CultureInfo.InvariantCulture) + ":" +
                   value.Minutes.ToString("D2", CultureInfo.InvariantCulture) + ":" +
                   value.Seconds.ToString("D2", CultureInfo.InvariantCulture);
        }

        private static string FormatBytes(double bytes)
        {
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            int unit = 0;

            while (bytes >= 1024 && unit < units.Length - 1)
            {
                bytes /= 1024;
                unit++;
            }

            return unit == 0 && bytes == Math.Floor(bytes)
                ? bytes.ToString(CultureInfo.InvariantCulture) + " " + units[unit]
                : bytes.ToString("0.0", CultureInfo.InvariantCulture) + " " + units[unit];
        }
    }
}
