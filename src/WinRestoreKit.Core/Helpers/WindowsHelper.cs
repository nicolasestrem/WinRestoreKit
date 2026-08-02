using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace WinRestoreKit
{
    public static class Utils
    {
        private static readonly LogHelper logger = LogHelper.Instance;

        internal static async Task<CopyResult> CopyFolder(string source, string destination)
        {
            CopyResult result = new CopyResult();
            await CopyFolderInto(source, destination, result, isRoot: true).ConfigureAwait(false);
            return result;
        }

        /// <summary>
        /// Copies exactly one file, creating the destination directory if it is missing.
        /// </summary>
        /// <remarks>
        /// Returns the same tally type as <see cref="CopyFolder"/> so both directions fold through
        /// one Skipped-vs-Failed ladder. It does not throw: a copy that fails comes back as
        /// FilesFailed=1 with FirstError set, because a module that cannot distinguish "copied" from
        /// "threw and was caught" is the failure mode Phase 2a exists to remove.
        ///
        /// Creating the destination directory is load-bearing rather than convenience: a machine
        /// being restored onto may never have run ssh, so %USERPROFILE%\.ssh does not exist, and
        /// failing there would report "could not be copied" for a restore that is simply first.
        /// </remarks>
        internal static async Task<CopyResult> CopyFile(string source, string destination)
        {
            CopyResult result = new CopyResult();

            FileStream sourceStream;

            // Absence is decided by the exception the OPEN raises, never by File.Exists - the rule
            // RestAppsForm.AppExport.Read spells out, applied here. File.Exists answers a question
            // about metadata, and a false from it folds "there is nothing here" together with "I
            // was not allowed to find out". Only the first is normal, and it maps to Skipped for
            // four of the five modules, so believing the second would report a settings file that
            // was never captured as one the machine does not have.
            //
            // Measured on Windows 11, 2026-07-21, which is why the open is split from the copy
            // rather than wrapped with it:
            //   existing dir + missing file             -> FileNotFoundException
            //   missing dir                             -> DirectoryNotFoundException
            //   file used as a directory component      -> DirectoryNotFoundException
            //   source path that is a directory         -> UnauthorizedAccessException
            //   parent denied ListDirectory+ReadData    -> File.Exists TRUE, open throws
            //   parent icacls /inheritance:r /deny (RX) -> File.Exists FALSE, open throws
            //                                              UnauthorizedAccessException
            //
            // The last two lines are the whole reason this is not an Exists probe, and the gap
            // between them is a trap worth naming: the first ACL leaves Traverse and
            // ReadAttributes intact, so Exists still answers true and an Exists-based probe looks
            // correct. Deny ReadAndExecute - which is what `icacls /inheritance:r /deny (RX)`
            // does, and what the standard OpenSSH "permissions are too open" remedy does to
            // %USERPROFILE%\.ssh - and Exists answers FALSE for a file that is sitting right
            // there. An Exists probe would then set SourceMissing, and with AbsenceIsNormal true
            // (ESsh, ETerminal, EVSCode) the user gets a green "not present on this system" over
            // a file that exists and was never copied, with no backup directory written at all.
            //
            // That was this method's behaviour as first written, and the first measurement taken
            // here used the narrower ACL and wrongly cleared it. Recorded because the measurement
            // that exonerates a probe can be the one that did not deny enough.
            try
            {
                sourceStream = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096, useAsync: true);
            }
            catch (FileNotFoundException)
            {
                result.SourceMissing = true;
                logger.LogMessage("Source file does not exist: " + source);
                return result;
            }
            catch (DirectoryNotFoundException)
            {
                // The folder the file would live in is not there - the app was never installed.
                // Deliberately an absence here, unlike in RestAppsForm, where the folder had just
                // been enumerated off disk and its disappearance meant the world moved underneath
                // us. Nothing enumerated anything here: these paths are composed from Data.* roots
                // for software that may simply not be present.
                result.SourceMissing = true;
                logger.LogMessage("Source folder does not exist: " + source);
                return result;
            }
            catch (Exception ex)
            {
                // Locked, access-denied, malformed, too long. We did not establish absence, we
                // failed to look, and that is a failure rather than a reassuring skip.
                result.FilesFailed++;
                result.FirstError = source + ": " + ex.Message;
                logger.LogMessage("Could not read source file " + source + ": " + ex.Message);
                return result;
            }

            using (sourceStream)
            {
                // Written STRAIGHT INTO the destination with FileMode.Create. This method briefly
                // wrote to a temporary file and renamed it into place, for atomicity, and that was
                // reverted on 2026-07-21 after the trade was examined against the pre-restore
                // snapshot. The reasoning is recorded because the atomic version looks like the
                // more careful one and someone will want it back.
                //
                // A rename makes the content swap atomic: the live file is the old contents or the
                // new ones, never a prefix. What it costs is the DIRECTORY ENTRY. Renaming replaces
                // the entry, so a destination that is a link stops being one - measured on Windows
                // 11, 2026-07-21, with a hard link: afterwards the link held the new content and
                // the file it was linked to still held the old, no longer the same file.
                //
                // Line those two up against what the restore path already guarantees:
                //
                //                        | torn file (what atomicity prevents) | broken link (what it costs)
                //   reported to the user | yes* - the step is Failed           | NO - every row green
                //   undone by a snapshot | yes - old contents are in it        | NO - it captures
                //                        |                                     | contents, not links
                //
                // * with one exception, because the claim has to be exact if the trade rests on it.
                //   A torn file has three triggers: disk full, a source read error, and the app
                //   being killed mid-restore. The first two throw, are caught below, and become a
                //   Failed step the user sees. A KILL reports nothing at all - PerformRestoration
                //   never returns, so no summary is shown and restore_log.txt is never written.
                //   The snapshot half still holds in that case (it is taken before the restore
                //   begins), so the file is recoverable, but the user is not told it needs
                //   recovering. So: two of three triggers are loud, one is silent-but-recoverable,
                //   and a broken link is silent-and-permanent. The trade still runs the same way,
                //   and now it says so accurately rather than by rounding the kill case up to
                //   "reported".
                //
                // Atomicity was buying protection from a failure that is loud and recoverable, and
                // paying with one that is silent and permanent. Both columns point the same way, so
                // the swap goes and Create comes back.
                //
                // Who the link case reaches: developers who symlink .ssh\config or VS Code's
                // settings.json into a git-managed dotfiles repo - a common arrangement among
                // exactly the people these modules are for. Create follows the link and updates the
                // file behind it, which is what every editor saving that path already does.
                //
                // The rejected middle option was to detect a link and write through it while
                // keeping the rename otherwise. That needs a branch this environment CANNOT test:
                // creating a symbolic link requires elevation or Developer Mode, so the symlink arm
                // would ship unexercised. An untested branch guarding a silent failure is worse
                // than not having the branch, and writing directly gets link-preservation
                // structurally instead - the same reason ESsh excludes private keys by never
                // enumerating the directory rather than by filtering it.
                //
                // Residual, stated rather than buried: Create truncates before the first byte, so a
                // copy that dies part-way (disk full, source read error, the app killed
                // mid-restore) leaves the destination empty or half-written. That is the failure in
                // the left column above - Failed step plus snapshot for the first two, snapshot
                // only for the kill, per the footnote - and for the files these modules carry, all
                // a few kilobytes, the window is microseconds. A real cost, accepted knowingly.
                //
                // Create also PRESERVES the destination's security descriptor, because it truncates
                // an existing file rather than replacing it. That matters on .ssh\config, whose
                // recommended permissions are an explicit non-inheriting ACE, and on hosts, which
                // can carry an anti-tamper ACE from EDR or GPO. The temp-file version had to reach
                // for File.Replace to hold this property (File.Move(overwrite) handed the
                // destination the temp file's freshly inherited descriptor - measured: protected
                // True -> False, 1 explicit rule -> 7 inherited). Writing in place gets it for
                // free, and CopyFileTests pins it so a future rewrite cannot quietly lose it.
                try
                {
                    string destinationDir = Path.GetDirectoryName(destination);

                    if (!string.IsNullOrEmpty(destinationDir) && !Directory.Exists(destinationDir))
                        Directory.CreateDirectory(destinationDir);

                    long length = sourceStream.Length;

                    using (FileStream destinationStream = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 4096, useAsync: true))
                    {
                        // The open has already truncated the destination. Recorded HERE - after the
                        // constructor returned, before a single byte is written - because that is
                        // the exact instant the live file stops being intact, and ToFileStep needs
                        // to know it to avoid telling the user their file was left alone.
                        result.DestinationTruncated = true;

                        await sourceStream.CopyToAsync(destinationStream).ConfigureAwait(false);
                    }

                    result.FilesCopied++;
                    result.BytesCopied += length;
                }
                catch (Exception ex)
                {
                    result.FilesFailed++;
                    if (result.FirstError == null)
                        result.FirstError = source + ": " + ex.Message;

                    logger.LogMessage("Error copying file " + source + " to " + destination + ": " + ex.Message);
                }
            }

            return result;
        }

        private static async Task CopyFolderInto(string source, string destination,
                                                 CopyResult result, bool isRoot)
        {
            try
            {
                DirectoryInfo sourceDir = new DirectoryInfo(source);

                if (!sourceDir.Exists)
                {
                    if (isRoot)
                    {
                        result.SourceMissing = true;
                        logger.LogMessage("Source directory does not exist: " + source);
                        return;
                    }

                    // A subdirectory that vanished between enumeration and this visit. Browsers
                    // delete cache folders constantly, so this is ordinary, not exotic. It is a
                    // folder we failed to copy - NOT evidence that the backup source was absent.
                    // Setting SourceMissing here would make ToStep discard a copy that had already
                    // moved hundreds of files and report "not present on this system".
                    result.FoldersFailed++;
                    if (result.FirstError == null)
                        result.FirstError = source + ": the folder disappeared during the copy";

                    logger.LogMessage("Subdirectory vanished during copy: " + source);
                    return;
                }

                DirectoryInfo destinationDir = new DirectoryInfo(destination);

                if (!destinationDir.Exists)
                    destinationDir.Create();

                foreach (FileInfo file in sourceDir.GetFiles())
                {
                    string destinationFilePath = Path.Combine(destinationDir.FullName, file.Name);

                    try
                    {
                        using (FileStream sourceStream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096, useAsync: true))
                        using (FileStream destinationStream = new FileStream(destinationFilePath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 4096, useAsync: true))
                        {
                            // ConfigureAwait(false) so an aggregating caller is not marshalled back
                            // to the UI thread once per file across a browser profile.
                            await sourceStream.CopyToAsync(destinationStream).ConfigureAwait(false);
                        }

                        result.FilesCopied++;
                        result.BytesCopied += file.Length;
                    }
                    catch (Exception ex)
                    {
                        result.FilesFailed++;
                        if (result.FirstError == null)
                            result.FirstError = file.Name + ": " + ex.Message;

                        logger.LogMessage("Error copying file " + file.FullName + ": " + ex.Message);
                    }
                }

                foreach (DirectoryInfo subDirectory in sourceDir.GetDirectories())
                {
                    string newDestinationPath = Path.Combine(destinationDir.FullName, subDirectory.Name);
                    await CopyFolderInto(subDirectory.FullName, newDestinationPath, result, isRoot: false)
                        .ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                // Enumeration or directory creation failed, so this folder's whole subtree was
                // never attempted. Counted as a FOLDER failure, not a file one: incrementing
                // FilesFailed here would produce "1 of 1 files could not be copied" having tried
                // exactly zero files.
                result.FoldersFailed++;
                if (result.FirstError == null)
                    result.FirstError = source + ": " + ex.Message;

                logger.LogMessage("Error copying folder " + source + " to " + destination + ": " + ex.Message);
            }
        }

        private static readonly IRegistryTool DefaultRegistryTool = new RegeditTool();

        /// <summary>
        /// Exports one registry key and verifies the artifact it was supposed to produce.
        /// </summary>
        /// <remarks>
        /// The verification is not belt-and-braces. Measured on Windows 11, 2026-07-20: regedit /e
        /// against a key that does not exist returns exit code 0 and writes no file. Checking the
        /// exit code alone would report success for a backup containing nothing.
        /// </remarks>
        internal static StepResult ExportRegistryKey(string filePath, string registryPath,
                                                     bool absenceIsNormal, IRegistryTool tool = null)
        {
            tool = tool ?? DefaultRegistryTool;

            // Clear any file already at the target path FIRST - before the probe, not just before
            // the export. Two separate reasons, and the second one bites in normal use:
            //
            // 1. Provenance. The verification below could otherwise be satisfied by a file this run
            //    did not write, making this method's promise to verify what it produced false.
            //
            // 2. Stale artifacts across runs. ConfPageView reuses one timestamped folder for every
            //    Backup click in an app session, so clicking Backup twice writes into the same
            //    place. If the key existed on the first click and is gone on the second, returning
            //    early on Absent leaves the FIRST run's .reg file sitting there while the log says
            //    the item was skipped. A later restore then imports registry state the user was
            //    told had not been captured - the same landmine as a part-written export, arriving
            //    by a different route.
            //
            // A delete failure fails the step even when absence is normal: a file we cannot remove
            // is a file we cannot vouch for, and "skipped" would imply the folder holds nothing.
            string clearError = TryDeleteExport(filePath);

            if (clearError != null)
                return StepResult.Failed(registryPath, "could not clear the previous export at " + filePath + ": " + clearError);

            KeyProbe probe = ProbeKey(registryPath);

            if (probe == KeyProbe.Indeterminate)
                return StepResult.Failed(registryPath, "could not read " + registryPath + " to check whether it exists");

            if (probe == KeyProbe.Absent)
            {
                return absenceIsNormal
                    ? StepResult.Skipped(registryPath, "not present on this system")
                    : StepResult.Failed(registryPath, "expected " + registryPath + " is missing");
            }

            ProcessOutcome outcome = tool.Export(filePath, registryPath);

            if (outcome == null)
                return StepResult.Failed(registryPath, "the registry tool returned no outcome");

            if (!outcome.Started)
                return StepResult.Failed(registryPath, "could not start regedit: " + outcome.Error);

            // The three branches below abandon an export that regedit may have been part-way through
            // writing. See AbandonExport for why deleting the fragment is the fix, not tidiness.
            if (outcome.TimedOut)
                return AbandonExport(filePath, registryPath, "regedit did not exit - it may be showing an error dialog");

            if (outcome.Error != null)
                return AbandonExport(filePath, registryPath, "regedit ran but its outcome could not be determined: " + outcome.Error);

            if (outcome.ExitCode != 0)
                return AbandonExport(filePath, registryPath, "regedit exited with code " + outcome.ExitCode);

            string readError;

            switch (RegFile.Validate(filePath, out readError))
            {
                case RegFileCheck.Valid:
                    return StepResult.Succeeded(registryPath, "exported " + registryPath);
                case RegFileCheck.Missing:
                    return StepResult.Failed(registryPath, "regedit reported success but wrote no file");
                case RegFileCheck.Empty:
                    return StepResult.Failed(registryPath, "regedit wrote an empty file");
                case RegFileCheck.BadHeader:
                    return StepResult.Failed(registryPath, "the exported file is not a valid .reg file");
                case RegFileCheck.Unreadable:
                    return StepResult.Failed(registryPath, "could not read back the exported file: " + readError);
                default:
                    // Fail closed. A RegFileCheck member added later must not silently pass here.
                    return StepResult.Failed(registryPath, "the exported file could not be classified");
            }
        }

        /// <summary>
        /// Removes the file at <paramref name="filePath"/> if it is there, returning why if it could
        /// not. Never throws.
        /// </summary>
        private static string TryDeleteExport(string filePath)
        {
            try
            {
                if (File.Exists(filePath))
                    File.Delete(filePath);

                return null;
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        /// <summary>
        /// Fails an export and removes whatever regedit may have been part-way through writing.
        /// </summary>
        /// <remarks>
        /// The delete is the point, not tidiness. RegFile.Validate is header-only by design, so a
        /// truncated export with an intact header later passes ImportRegistryKey's pre-flight,
        /// reaches regedit /s, exits 0 and is reported as applied. The user would be told the backup
        /// failed and then told the restore of that same known-bad artifact worked.
        ///
        /// A delete failure is logged and then dropped rather than returned: it must never displace
        /// the real reason the export failed, which is what the user has to act on.
        /// </remarks>
        private static StepResult AbandonExport(string filePath, string registryPath, string reason)
        {
            string deleteError = TryDeleteExport(filePath);

            if (deleteError != null)
                logger.LogMessage("Could not remove the incomplete export at " + filePath + ": " + deleteError);

            return StepResult.Failed(registryPath, reason);
        }

        /// <summary>
        /// Imports one .reg file, having first checked it is worth importing.
        /// </summary>
        /// <remarks>
        /// The pre-flight matters more than the exit code here. regedit /s returns 0 on a file it
        /// only partially applied, so refusing a malformed file BEFORE launching regedit is the one
        /// strong guarantee available on this path.
        ///
        /// The read-back afterwards is deliberately weaker than the export path's. It proves the key
        /// EXISTS after the import; it does not prove any value under it matches the backup, and a
        /// partially-applied file leaves the key present. So the wording stays "applied" and never
        /// becomes "verified".
        ///
        /// The tri-state maps in the opposite direction to ExportRegistryKey, and that asymmetry is
        /// the whole point. Absence is affirmative evidence the import did not take, so it fails.
        /// Indeterminate is no evidence at all and must NOT overturn exit code 0 - the concrete case
        /// is an unelevated probe of an HKLM key that regedit has just written under elevation, and
        /// failing there would report a false failure on every such import.
        /// </remarks>
        internal static StepResult ImportRegistryKey(string filePath, string registryPath,
                                                     IRegistryTool tool = null,
                                                     Func<string, KeyProbe> probe = null)
        {
            tool = tool ?? DefaultRegistryTool;
            probe = probe ?? ProbeKey;

            string readError;

            switch (RegFile.Validate(filePath, out readError))
            {
                case RegFileCheck.Valid:
                    break;   // the only case that may proceed to the registry
                case RegFileCheck.Missing:
                    return StepResult.Skipped(registryPath, "nothing was backed up for this item");
                case RegFileCheck.Empty:
                    return StepResult.Failed(registryPath, "the backed-up file is empty - not importing it");
                case RegFileCheck.BadHeader:
                    return StepResult.Failed(registryPath, "the backed-up file is not a valid .reg file - not importing it");
                case RegFileCheck.Unreadable:
                    // Deliberately NOT worded as "invalid". We could not read it, so we do not know
                    // whether it is valid - and a locked or ACL-denied file is a different problem
                    // for the user to fix than a corrupt one.
                    return StepResult.Failed(registryPath, "could not read the backed-up file: " + readError);
                default:
                    // Fail CLOSED. Without this, a RegFileCheck member added later falls through to
                    // regedit /s unexamined, which would invert this method's one real guarantee:
                    // that a file we cannot vouch for never reaches the registry.
                    return StepResult.Failed(registryPath, "the backed-up file could not be classified - not importing it");
            }

            ProcessOutcome outcome = tool.Import(filePath);

            if (outcome == null)
                return StepResult.Failed(registryPath, "the registry tool returned no outcome");

            if (!outcome.Started)
                return StepResult.Failed(registryPath, "could not start regedit: " + outcome.Error);

            if (outcome.TimedOut)
                return StepResult.Failed(registryPath, "regedit did not exit - it may be showing an error dialog");

            if (outcome.Error != null)
            {
                // regedit started, so the registry may already have been written to. Saying it
                // could not start would be a false claim about whether the machine changed.
                return StepResult.Failed(registryPath,
                    "regedit ran but its outcome could not be determined, so the registry may have been partly changed: " + outcome.Error);
            }

            if (outcome.ExitCode != 0)
                return StepResult.Failed(registryPath, "regedit exited with code " + outcome.ExitCode);

            // A key whose hive ProbeKey does not understand always probes Absent, which would turn
            // this method's strongest branch into a reported failure on the strength of a lookup
            // that was never performed. Such a key gets the plain claim instead.
            if (!IsProbeableKeyPath(registryPath))
                return StepResult.Applied(registryPath, registryPath);

            switch (probe(registryPath))
            {
                case KeyProbe.Present:
                    return StepResult.Succeeded(registryPath,
                        "applied " + registryPath + "; the key is present after the import");
                case KeyProbe.Absent:
                    return StepResult.Failed(registryPath,
                        "regedit reported success but " + registryPath + " is not present after the import");
                case KeyProbe.Indeterminate:
                    return StepResult.Succeeded(registryPath,
                        "applied " + registryPath + "; could not confirm the key is present afterwards");
                default:
                    // Fail OPEN here, unlike the export path. A KeyProbe member added later carries
                    // no evidence about this key, and exit code 0 already supports "applied" - so
                    // the claim is narrowed rather than inverted into a failure.
                    return StepResult.Applied(registryPath, registryPath);
            }
        }

        /// <summary>
        /// Whether <see cref="ProbeKey"/> can actually look this path up.
        /// </summary>
        /// <remarks>
        /// ProbeKey understands HKEY_CURRENT_USER and HKEY_LOCAL_MACHINE only, and reports anything
        /// else as Absent - indistinguishable from a key it looked for and did not find. Callers that
        /// treat Absent as evidence have to ask this first.
        /// </remarks>
        internal static bool IsProbeableKeyPath(string key)
            => key != null
               && (key.StartsWith(Registry.CurrentUser.Name + "\\", StringComparison.OrdinalIgnoreCase)
                   || key.StartsWith(Registry.LocalMachine.Name + "\\", StringComparison.OrdinalIgnoreCase));

        // Reg operations

        /// <summary>
        /// Whether a registry key is present, absent, or could not be determined.
        /// </summary>
        /// <remarks>
        /// The third state is the point. This method is the Skipped-vs-Failed discriminator for the
        /// whole backup path, and "I could not tell" is a failure of the tool, not an absence of the
        /// data - reporting a permission-denied probe as Absent would silently downgrade a real
        /// failure into a reassuring "not present on this system".
        /// </remarks>
        public static KeyProbe ProbeKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return KeyProbe.Absent;

            KeyProbe hkcu = ProbeUnder(key, Registry.CurrentUser);
            if (hkcu != KeyProbe.Absent)
                return hkcu;

            return ProbeUnder(key, Registry.LocalMachine);
        }

        /// <summary>
        /// Convenience wrapper for callers that only need a yes/no and must never throw.
        /// </summary>
        /// <remarks>
        /// Indeterminate deliberately maps to FALSE here, the opposite of the backup path's mapping.
        /// The only caller is the IsInstalled() tree-build (ConfPageView.SelectInstalled), and
        /// auto-checking a module whose keys could not be probed would manufacture a Failed row in
        /// the very summary this phase exists to make trustworthy.
        /// </remarks>
        public static bool KeyExists(string key)
            => ProbeKey(key) == KeyProbe.Present;

        private static KeyProbe ProbeUnder(string key, RegistryKey baseKey)
        {
            string prefix = baseKey.Name + "\\";

            // Only probe under this hive if the path actually names it. The previous implementation
            // stripped only the matching prefix and then probed the remainder under BOTH hives, so
            // an HKCU path was also looked up under HKLM with "HKEY_CURRENT_USER\" still attached -
            // always null, so the HKLM half of the check was dead for every HKCU input.
            if (!key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return KeyProbe.Absent;

            string subKey = key.Substring(prefix.Length);

            try
            {
                using (RegistryKey opened = baseKey.OpenSubKey(subKey))
                {
                    return opened != null ? KeyProbe.Present : KeyProbe.Absent;
                }
            }
            catch (System.Security.SecurityException ex)
            {
                return Undetermined(key, ex);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Undetermined(key, ex);
            }
            catch (Exception ex)
            {
                // A malformed path or an unexpected provider error. Not knowing is the honest answer.
                return Undetermined(key, ex);
            }
        }

        /// <summary>
        /// Records why a key could not be probed, then reports that we could not tell.
        /// </summary>
        /// <remarks>
        /// The logging is the point of the helper. Returning Indeterminate without recording the
        /// cause would leave the user with "could not read this key" and no way to learn whether
        /// that was a permission problem, a malformed path, or a provider fault - which is the same
        /// silent discard this whole phase exists to remove.
        /// </remarks>
        private static KeyProbe Undetermined(string key, Exception ex)
        {
            logger.LogMessage("Could not probe " + key + ": " + ex.Message);
            return KeyProbe.Indeterminate;
        }

        /// <summary>
        /// Closes the Windows shell and brings back exactly one, reporting what happened.
        /// </summary>
        /// <remarks>
        /// The previous implementation started a shell once per KILLED process, so a user with six
        /// File Explorer windows open got six shells. Killing is delegated to CloseProcess, which
        /// already bounds its wait and survives processes that vanish mid-kill.
        ///
        /// The probe before starting is not optimisation. explorer.exe launched while a shell is
        /// already running opens a stray file-browser window instead of becoming the shell, and
        /// Windows' AutoRestartShell usually beats us to it - so the N-shells bug would come straight
        /// back at N=2. Carries a 5s close budget, so callers must not run it on the UI thread.
        /// </remarks>
        public static ExplorerRestartResult RestartExplorer()
        {
            CloseResult close = CloseProcess("explorer");

            bool shellAlreadyRunning = IsProcessRunning("explorer");
            string startError = null;

            if (ExplorerRestartResult.ShouldStartShell(close, shellAlreadyRunning))
                startError = TryStartShell();

            ExplorerRestartResult result = new ExplorerRestartResult(
                close,
                ExplorerRestartResult.DecideShellOutcome(close, shellAlreadyRunning, startError),
                startError);

            logger.LogMessage(result.Describe());
            return result;
        }

        /// <summary>
        /// Starts one explorer.exe, returning why it could not rather than throwing.
        /// </summary>
        private static string TryStartShell()
        {
            try
            {
                using (Process started = Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = false }))
                {
                    return started == null ? "Windows did not start explorer.exe." : null;
                }
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        // Show disk space info on ConfPage
        public static string GetSystemPartitionDiskSpaceInfo()
        {
            try
            {
                string systemDrive = Path.GetPathRoot(Environment.SystemDirectory);
                DriveInfo driveInfo = new DriveInfo(systemDrive);

                long availableSpace = driveInfo.AvailableFreeSpace;
                long totalSpace = driveInfo.TotalSize;

                string info = $"Available Space: {FormatBytes(availableSpace)} | Total Space: {FormatBytes(totalSpace)}";
                return info;
            }
            catch (Exception ex)
            {
                return $"Error retrieving disk space information: {ex.Message}";
            }
        }

        private static string FormatBytes(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB", "TB", "PB", "EB" };
            int suffixIndex = 0;

            while (bytes >= 1024 && suffixIndex < suffixes.Length - 1)
            {
                bytes /= 1024;
                suffixIndex++;
            }

            return $"{bytes} {suffixes[suffixIndex]}";
        }

        // Check for running processes in Confs
        public static bool IsProcessRunning(string processName)
        {
            try
            {
                Process[] processes = Process.GetProcessesByName(processName);

                try
                {
                    return processes.Length > 0;
                }
                finally
                {
                    foreach (Process p in processes)
                        p.Dispose();
                }
            }
            catch (Exception)
            {
                // A false negative here only means the user is not prompted to close the app.
                return false;
            }
        }

        /// <summary>
        /// The shared budget for the wait pass in <see cref="CloseProcess"/>, in milliseconds.
        /// </summary>
        /// <remarks>
        /// Shared across every process in the tree, not five seconds each - a bounded per-process
        /// wait is still unbounded in aggregate when Chrome yields 10-30 chrome.exe entries, and that
        /// unbounded wait runs synchronously under an async void click handler with no dispatch off
        /// the UI thread, so it froze the window instead of the process it was meant to bound.
        /// </remarks>
        private const int CloseTimeoutMs = 5000;

        /// <summary>
        /// Asks every instance of a process to terminate.
        /// </summary>
        /// <remarks>
        /// Two passes over the same process list, sharing one deadline, rather than kill-then-wait
        /// per process. The guard is still the point: Kill() throws InvalidOperationException when
        /// the process exited between enumeration and the call - likely, not exotic, because Chrome
        /// is a whole tree of child processes that come and go - and Win32Exception when access is
        /// denied. The browser modules reach this from an async void click handler, so an escape
        /// here took down the entire run and every result collected with it.
        ///
        /// Pass 1 kills every process with no waiting. Pass 2 waits on each against the remaining
        /// shared budget. Kill() is asynchronous, so without waiting at all the caller starts copying
        /// while the process is still flushing and releasing file handles - a just-killed Chrome
        /// still holds its SQLite files, so the copy that follows hits locked files. See
        /// <see cref="CloseTimeoutMs"/> for why the budget is shared instead of per process.
        /// </remarks>
        public static CloseResult CloseProcess(string processName)
        {
            Process[] processes;

            try
            {
                processes = Process.GetProcessesByName(processName);
            }
            catch (Exception)
            {
                return CloseResult.AccessDenied;
            }

            if (processes.Length == 0)
                return CloseResult.NotRunning;

            CloseResult worst = CloseResult.Exited;

            // Pass 1: kill everything, no waiting. Handles stay open - pass 2 needs them.
            foreach (Process process in processes)
            {
                try
                {
                    process.Kill();
                }
                catch (System.ComponentModel.Win32Exception ex)
                {
                    logger.LogMessage("Could not close " + processName + ": " + ex.Message);
                    worst = Worse(worst, CloseResult.AccessDenied);
                }
                catch (InvalidOperationException)
                {
                    // Already gone between enumeration and Kill. Nothing to report.
                }
                catch (Exception ex)
                {
                    logger.LogMessage("Could not close " + processName + ": " + ex.Message);
                    worst = Worse(worst, CloseResult.StillRunning);
                }
            }

            // Pass 2: wait on each against the remaining shared budget, then dispose.
            Stopwatch waited = Stopwatch.StartNew();

            foreach (Process process in processes)
            {
                try
                {
                    int remaining = CloseTimeoutMs - (int)waited.ElapsedMilliseconds;

                    // When the budget is spent, still ASK whether it exited rather than assuming it
                    // did not. Killing 30 Chrome children can exhaust the budget while every one of
                    // them died, and reporting those as still-running manufactures a failed backup
                    // out of a completely successful close.
                    bool exited = remaining > 0
                        ? process.WaitForExit(remaining)
                        : process.HasExited;

                    if (!exited)
                        worst = Worse(worst, CloseResult.StillRunning);
                }
                catch (Exception)
                {
                    // Deliberately silent. The likely trigger here is a process that was already
                    // gone before pass 1 could kill it - pass 1 treats that as nothing to report,
                    // and reaching the opposite conclusion about the same process would turn a
                    // clean browser close into a failed backup.
                }
                finally
                {
                    process.Dispose();
                }
            }

            return worst;
        }

        /// <summary>
        /// Keeps the more severe of two close outcomes.
        /// </summary>
        /// <remarks>
        /// Plain assignment would be last-write-wins, which quietly downgrades. A browser is a tree
        /// of child processes and mixed outcomes across them are ordinary: if one child is
        /// access-denied and a later one merely fails to die, straight assignment reports the
        /// milder result and the caller decides it may safely copy files that are still locked.
        /// </remarks>
        /// <remarks>
        /// Internal rather than private so tests can call THIS function. A test that reimplements
        /// the comparison locally and asserts on its own copy passes even when the production code
        /// reverts to plain assignment - it verifies the reimplementation, not the shipped
        /// behaviour, which is precisely the bug this method exists to prevent.
        /// </remarks>
        internal static CloseResult Worse(CloseResult a, CloseResult b)
            => Severity(a) >= Severity(b) ? a : b;

        internal static int Severity(CloseResult r)
        {
            switch (r)
            {
                case CloseResult.NotRunning: return 0;
                case CloseResult.Exited: return 1;
                case CloseResult.StillRunning: return 2;
                case CloseResult.AccessDenied: return 3;
                default: return 3;
            }
        }

        /// <summary>
        /// Opens an http/https URL in the user's default browser at their normal privilege level.
        /// </summary>
        /// <remarks>
        /// Routed through explorer.exe deliberately. WinRestoreKit's manifest requests highestAvailable
        /// because registry export needs it, and ShellExecute hands the parent's elevated token to
        /// the child - so launching the browser directly opens it as Administrator. That leaves
        /// admin-owned files in the browser profile (which can stop it starting normally afterwards)
        /// and silently grants admin rights to anything downloaded and run from that window.
        /// explorer.exe hands the request to the already-running shell, which runs as the user.
        ///
        /// This is also why the URL is checked first: with a shell launch, a non-web string is not
        /// an invalid argument, it is an arbitrary file or program to execute - at admin integrity.
        /// Callers pass constants today, so the check is here to keep that true.
        ///
        /// The remaining failure modes are environmental rather than programming errors - no default
        /// browser registered, a broken protocol association, a locked-down machine - so they are
        /// surfaced to the user and logged, not rethrown.
        /// </remarks>
        internal static void OpenUrl(string url)
        {
            if (!IsWebUrl(url))
            {
                logger.Log("Refused to open {0}: not an http/https URL.", url ?? "(null)");
                return;
            }

            try
            {
                // ArgumentList quotes the value properly rather than pasting it into a command line.
                var startInfo = new ProcessStartInfo("explorer.exe") { UseShellExecute = false };
                startInfo.ArgumentList.Add(url);

                Process.Start(startInfo);
            }
            catch (Exception ex)
            {
                ReportUrlFailure(url, ex);
            }
        }

        /// <summary>
        /// True only for absolute http/https URLs. See <see cref="OpenUrl"/> for why this matters.
        /// </summary>
        internal static bool IsWebUrl(string url)
            => Uri.TryCreate(url, UriKind.Absolute, out Uri parsed)
               && (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps);

        /// <summary>
        /// Logs a message without ever throwing, for the timer and thread-pool paths where an
        /// escaping exception would terminate the process.
        /// </summary>
        /// <remarks>
        /// Passing the message as a format ARGUMENT rather than as the format string matters: the
        /// underlying logger runs string.Format, and an exception message containing braces would
        /// otherwise throw inside the very handler meant to contain a failure.
        /// </remarks>
        internal static void LogQuietly(string message)
        {
            try
            {
                logger.Log("{0}", message);
            }
            catch
            {
            }
        }

        /// <summary>
        /// Tells the user that a link could not be opened. Registered by the app at startup; null in
        /// any process that has no UI, where the log line below is the whole report.
        /// </summary>
        /// <remarks>
        /// This exists because OpenUrl lives in engine code that cannot reference WinForms, and the
        /// failure it reports is one only a human can act on. Registration happens as the first
        /// statement of Program.Main, before any form, timer or worker thread exists, so the window
        /// in which this is unexpectedly null is not reachable from a running app.
        /// </remarks>
        internal static Action<string, Exception> UrlFailureUi;

        private static void ReportUrlFailure(string url, Exception ex)
        {
            // OpenUrl is called from a System.Timers.Timer thread, and .NET 8 no longer swallows
            // exceptions thrown by Elapsed handlers the way .NET Framework did - anything that
            // escapes takes the process down. So the reporting needs containing too: showing a
            // dialog can itself fail on exactly the locked-down machines this catch exists for.
            // Swallowing is the last resort here, not a shortcut; the alternative is a crash whose
            // only cause was that we could not display a warning about a link.
            //
            // The catch also contains the registered handler: it runs arbitrary app code, and this
            // frame is still the one standing between a link failure and process termination.
            try
            {
                logger.Log("Failed to open {0}: {1}", url, ex.Message);

                UrlFailureUi?.Invoke(url, ex);
            }
            catch
            {
            }
        }

        /// <summary>
        /// How long to wait for an ordinary command-line tool before giving up on it.
        /// </summary>
        /// <remarks>
        /// Matches RegeditTool's budget rather than winget's: netsh finishes in seconds, so a
        /// minute is a backstop for a wedged process, not a progress budget.
        /// </remarks>
        private const int ToolTimeoutMs = 60000;

        /// <summary>
        /// Runs a command-line tool with both output streams captured, and waits for it to finish.
        /// </summary>
        /// <remarks>
        /// Replaces the two private ExecuteNetshCommand copies that WNetworkConf and CWiFiConf
        /// each carried - one redirected stderr and one did not, and the one that drained stdout
        /// line-by-line could not safely redirect stderr at all: with nobody draining it, a
        /// chatty tool fills the pipe and blocks, which is the orphan-process hazard the old
        /// WNetworkConf comment documents. Both streams are therefore drained CONCURRENTLY, from
        /// the moment the process starts, with the exit wait running against neither.
        ///
        /// <paramref name="stdoutFile"/> lands the captured stdout on disk, and only for a run
        /// that exited 0. The path is CLEARED before the tool starts, for the same two reasons
        /// ExportRegistryKey documents: the artifact check that must follow any export (exit
        /// codes are not evidence) could otherwise be satisfied by a file this run did not write,
        /// and the backup folder is reused across Backup clicks in one app session - so a failed
        /// re-export would otherwise leave the previous run's file restorable while the row says
        /// Failed. On a non-zero exit nothing is written either: the captured text is the tool's
        /// error banner, and a restore would happily "apply" it. A path that cannot be cleared
        /// fails the run before the tool starts - a file we cannot remove is a file we cannot
        /// vouch for. A write that fails is logged and any partial file it left is removed - or,
        /// when a lock blocks the delete, renamed aside out of the restore side's view - so the
        /// artifact check then reports the missing file, which is the user-visible fact; a partial
        /// that can neither be removed nor moved fails the run instead, because a truncated dump
        /// passes the ladder's non-empty check and would restore as if it were whole.
        ///
        /// winget deliberately does NOT go through here: RestAppsForm shows winget's own console
        /// window as its only progress reporting, which is incompatible with redirected streams,
        /// and its ten-minute budget reflects measured installs. See RunWingetAsync.
        /// </remarks>
        internal static async Task<ProcessOutcome> RunToolAsync(string fileName, string[] args,
                                                                string stdoutFile = null,
                                                                int timeoutMs = ToolTimeoutMs)
        {
            return await Task.Run(() =>
            {
                if (stdoutFile != null)
                {
                    string clearError = TryDeleteExport(stdoutFile);

                    if (clearError != null)
                        return ProcessOutcome.NeverStarted(
                            "could not clear the previous export at " + stdoutFile + ": " + clearError);
                }

                // Mirrors RunWingetAsync: once Start() has returned the tool may already be doing
                // its work, so a later exception must not be reported as "never started".
                bool started = false;

                try
                {
                    ProcessStartInfo startInfo = new ProcessStartInfo
                    {
                        FileName = fileName,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };

                    foreach (string arg in args)
                        startInfo.ArgumentList.Add(arg);

                    using (Process proc = Process.Start(startInfo))
                    {
                        if (proc == null)
                            return ProcessOutcome.NeverStarted(fileName + " did not start");

                        started = true;

                        Task<string> stdout = proc.StandardOutput.ReadToEndAsync();
                        Task<string> stderr = proc.StandardError.ReadToEndAsync();

                        if (!proc.WaitForExit(timeoutMs))
                        {
                            try
                            {
                                proc.Kill(entireProcessTree: true);
                                proc.WaitForExit(5000);
                            }
                            catch (Exception)
                            {
                                // A leaked process is the better trade than losing the timeout signal.
                            }

                            return ProcessOutcome.Timeout();
                        }

                        // The drains complete when the pipes close. Bounded, so a grandchild that
                        // inherited the handles and outlived the tool cannot hold the exit code
                        // hostage; an unfinished drain then just means less captured text.
                        Task.WaitAll(new Task[] { stdout, stderr }, 5000);

                        string output = stdout.IsCompletedSuccessfully ? stdout.Result : "";
                        string error = stderr.IsCompletedSuccessfully ? stderr.Result : "";

                        if (output.Length > 0)
                            logger.LogMessage(fileName + ": " + output);

                        if (error.Length > 0)
                            logger.LogMessage(fileName + " (stderr): " + error);

                        if (stdoutFile != null && proc.ExitCode == 0)
                        {
                            try
                            {
                                File.WriteAllText(stdoutFile, output);
                            }
                            catch (Exception ex)
                            {
                                logger.LogMessage("Could not write the captured output to " + stdoutFile + ": " + ex.Message);

                                // The write can throw part-way through (a full disk being the
                                // classic), having already produced bytes - and a truncated dump
                                // passes the artifact ladder's non-empty check, so it must go.
                                // Once it is gone, returning the exit code is truthful: the
                                // artifact check that must follow any export sees the missing
                                // file. A partial that cannot be removed gets a second chance -
                                // a lock that blocks deletion does not always block a rename,
                                // and consumers of the backup folder find artifacts by their
                                // expected name or extension, so moving the partial aside takes
                                // it out of the restore side's view even when it cannot be
                                // destroyed. Only when neither works does the run fail: a file
                                // we can neither remove nor hide is a file we cannot vouch for,
                                // and the only honest outcome left names it.
                                string writeClearError = TryDeleteExport(stdoutFile);

                                if (writeClearError != null)
                                {
                                    try
                                    {
                                        File.Move(stdoutFile, stdoutFile + ".partial", overwrite: true);
                                    }
                                    catch (Exception moveEx)
                                    {
                                        return ProcessOutcome.OutcomeUnknown(
                                            "its output could not be fully written to " + stdoutFile +
                                            ", and the partial file could not be removed (" + writeClearError +
                                            ") or moved aside (" + moveEx.Message + ")");
                                    }
                                }
                            }
                        }

                        return ProcessOutcome.Ran(proc.ExitCode);
                    }
                }
                catch (Exception ex)
                {
                    return started
                        ? ProcessOutcome.OutcomeUnknown(ex.Message)
                        : ProcessOutcome.NeverStarted(ex.Message);
                }
            }).ConfigureAwait(false);
        }

        /// <summary>
        /// Checks that a command-line export actually produced its file.
        /// </summary>
        /// <remarks>
        /// The export-side half of "an exit code is not evidence", for tools other than regedit
        /// (whose .reg files get the stronger RegFile.Validate). Measured examples that make the
        /// check mandatory: regedit /e on a nonexistent key exits 0 and writes nothing, and netsh
        /// wlan export printed "saved successfully" with exit code 0 while writing nothing.
        /// Content checks stay with the caller - only it knows what a usable file looks like.
        /// </remarks>
        internal static StepResult ValidateExportArtifact(string filePath, string target,
                                                          string toolName, string successReason)
        {
            if (!File.Exists(filePath))
                return StepResult.Failed(target, toolName + " reported success but wrote no file");

            try
            {
                if (new FileInfo(filePath).Length == 0)
                    return StepResult.Failed(target, toolName + " wrote an empty file");
            }
            catch (Exception ex)
            {
                return StepResult.Failed(target, "could not read back the exported file: " + ex.Message);
            }

            return StepResult.Succeeded(target, successReason);
        }

        /// <summary>
        /// How long to wait for winget before giving up on it.
        /// </summary>
        /// <remarks>
        /// Much longer than RegeditTool's 60s, on purpose. winget can spend minutes updating its
        /// sources or downloading a package on a slow link - a measured install here pulled 146 MB -
        /// and killing a legitimately slow run would be a false failure report, the exact thing this
        /// phase exists to remove.
        ///
        /// The timeout is a backstop for a wedged process, not a progress budget. It has to exist:
        /// AStoreApps.BackupAsync awaits this from ConfPageView.RunBackup with the window disabled,
        /// so an unbounded wait left the whole app frozen with no way out but Task Manager.
        /// </remarks>
        private const int WingetTimeoutMs = 600000;

        /// <summary>
        /// Runs winget and waits for it to finish, reporting how it went.
        /// </summary>
        /// <remarks>
        /// Runs winget.exe DIRECTLY. This used to launch it through Windows Terminal, and waiting on
        /// wt.exe is waiting on the wrong process: wt is a launcher that hands the command off and
        /// exits immediately. Measured 2026-07-20 on a real backup - the export was reported as
        /// "winget reported success but wrote no file" at 07:35:54, and winget wrote a complete,
        /// valid 113-package export to that same path at 07:36:23, 29 seconds after the app had
        /// already written its log. A backup that worked was reported as failed.
        ///
        /// That is the cry-wolf direction of this phase's failure mode, and no amount of care in the
        /// reporting layer could fix it: the module was being asked a question about a file that was
        /// not finished being written. The exit code being checked belonged to a process that had
        /// done nothing but forward an argument.
        ///
        /// Arguments go through ArgumentList rather than one pasted string, for the reason given in
        /// RegeditTool.Run: the export path contains spaces and the backup folder name contains a
        /// hyphen, and manual quoting is the kind of thing that works until it silently does not.
        /// </remarks>
        internal static async Task<ProcessOutcome> RunWingetAsync(bool showWindow, params string[] args)
        {
            string winget = DataHelper.Data.ShellWinget;

            if (!File.Exists(winget))
                return ProcessOutcome.NeverStarted("winget is not installed (looked in " + winget + ")");

            return await Task.Run(() =>
            {
                // Tracks whether Process.Start succeeded, mirroring RegeditTool.Run in
                // IRegistryTool.cs: once Start() has returned, winget may already be installing, so
                // an exception from WaitForExit must not be reported as "never started".
                bool started = false;

                try
                {
                    ProcessStartInfo startInfo = new ProcessStartInfo
                    {
                        FileName = winget,
                        // The old WorkingDirectory was Data.DataRootDir, which may not exist yet -
                        // Process.Start then threw Win32Exception onto the sync context.
                        UseShellExecute = false,
                        CreateNoWindow = !showWindow
                    };

                    foreach (string arg in args)
                        startInfo.ArgumentList.Add(arg);

                    using (Process proc = Process.Start(startInfo))
                    {
                        if (proc == null)
                            return ProcessOutcome.NeverStarted("winget did not start");

                        started = true;

                        if (!proc.WaitForExit(WingetTimeoutMs))
                        {
                            try
                            {
                                proc.Kill(entireProcessTree: true);
                                // Kill is asynchronous; without this the using block disposes while
                                // the process may still be terminating.
                                proc.WaitForExit(5000);
                            }
                            catch (Exception)
                            {
                                // A leaked process is the better trade than losing the timeout signal.
                            }

                            return ProcessOutcome.Timeout();
                        }

                        return ProcessOutcome.Ran(proc.ExitCode);
                    }
                }
                catch (Exception ex)
                {
                    return started
                        ? ProcessOutcome.OutcomeUnknown(ex.Message)
                        : ProcessOutcome.NeverStarted(ex.Message);
                }
            }).ConfigureAwait(false);
        }
    }
}