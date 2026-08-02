using WinRestoreKit;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Conf
{
    public class CWiFiConf : BackupBase
    {
        public CWiFiConf()
        {
            Title = "Wi-Fi networks & passwords";
            Info = "This will back up and restore credentials of Wi-Fi networks.";
            WarningMessage = "Restoring this backup adds every saved network in it back to this machine, for all accounts, not just yours. This includes networks you may have since forgotten.";
        }

        // A command, not a file list: what gets added is one Wi-Fi profile per XML found in the
        // backup, and netsh installs each machine-wide. The wording says "for all accounts" because
        // that is the part of this restore a user would not otherwise expect.
        public override IReadOnlyList<RestoreTarget> RestoreTargets
            => new[]
            {
                RestoreTarget.Command(
                    "runs netsh to add every saved Wi-Fi network in the backup, with its password, " +
                    "to this machine for all accounts")
            };

        public override async Task<ModuleResult> BackupAsync(string path)
        {
            List<StepResult> steps = new List<StepResult>();

            try
            {
                // ConfPageView passes the shared backup root, and other modules write their own
                // files into the same folder, so a bare "how many .xml files are there now" count
                // is meaningless. Snapshot before and after the export and count only what netsh
                // itself wrote this run.
                //
                // Snapshotting by last-write-time, not just presence: CurrentBackupPath is the same
                // folder for every Backup click within one app session (ConfPageView.cs builds it
                // from Data.NowShort, a static field stamped once at process start), so a second
                // Backup click re-exports into files that already exist from the first. A pure
                // "did this filename exist before" check would then see zero new files and report a
                // real, successful re-export as Failed.
                Dictionary<string, DateTime> before = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
                foreach (string file in Directory.GetFiles(path, "*.xml"))
                    before[file] = File.GetLastWriteTimeUtc(file);

                ProcessOutcome outcome = await Utils.RunToolAsync(
                    "netsh",
                    new[] { "wlan", "export", "profile", "key=clear", "folder=" + path.TrimEnd('\\') });

                List<string> changed = new List<string>();
                foreach (string file in Directory.GetFiles(path, "*.xml"))
                {
                    DateTime previousWrite;
                    bool existedBefore = before.TryGetValue(file, out previousWrite);

                    if (!existedBefore || File.GetLastWriteTimeUtc(file) > previousWrite)
                        changed.Add(file);
                }

                bool succeeded = outcome != null && outcome.Started && !outcome.TimedOut
                                 && outcome.Error == null && outcome.ExitCode == 0;

                if (succeeded && changed.Count > 0)
                {
                    steps.Add(StepResult.Succeeded(Title, $"exported {changed.Count} Wi-Fi profile(s)"));
                }
                else if (succeeded)
                {
                    // netsh has been measured printing "saved successfully" with exit code 0 while
                    // writing nothing (the export path was too long for it). A zero exit code alone
                    // is not evidence of success.
                    steps.Add(StepResult.Failed(Title, "netsh reported success but wrote no Wi-Fi profile files"));
                }
                else
                {
                    string reason;

                    if (outcome == null || !outcome.Started)
                        reason = "could not run netsh: " + (outcome == null ? "no outcome" : outcome.Error);
                    else if (outcome.TimedOut)
                        reason = "netsh did not finish";
                    else if (outcome.Error != null)
                        reason = "netsh ran but its outcome could not be determined: " + outcome.Error;
                    else
                        reason = $"netsh exited with code {outcome.ExitCode}";

                    // A bounded export can die part-way through having already written some of the
                    // profiles, into a folder the restore side reads back by CONTENT - so whatever
                    // this run wrote or overwrote must go with the failure, or a backup the row
                    // reports as Failed would still restore a partial profile set. Same rule as
                    // the runner's stdoutFile pre-clear, applied to files netsh names itself.
                    string stuck = TryRemovePartialExports(changed);

                    if (stuck != null)
                        reason += "; the partial profile file(s) it left could not be removed and would still restore: " + stuck;

                    // A timed-out netsh is killed, but the kill is deliberately not escalated when
                    // it fails - a process that outlives the timeout can keep writing profiles
                    // after this cleanup ran, and a complete profile file always restores.
                    if (outcome != null && outcome.TimedOut)
                        reason += "; if netsh is in fact still running, profile files it writes after this point would also restore";

                    steps.Add(StepResult.Failed(Title, reason));
                }
            }
            catch (Exception ex)
            {
                steps.Add(StepResult.Failed(Title, $"{ex.GetType().Name}: {ex.Message}"));
            }

            return ModuleResult.Aggregate(steps);
        }

        /// <summary>
        /// Best-effort removal of the files a failed export wrote or overwrote. Returns the names
        /// it could not remove (with why), or null when everything (or nothing) was cleaned up.
        /// </summary>
        /// <remarks>
        /// Scoped to what the restore side would actually find, with the same predicate it uses
        /// (WlanProfile.IsWlanProfile), so "what a failed backup removes" and "what a restore
        /// discovers" cannot drift apart: a truncated file fails the XML parse and cannot restore
        /// anyway, and a foreign .xml some other writer put in the shared folder is not this
        /// module's to delete.
        /// </remarks>
        private static string TryRemovePartialExports(List<string> files)
        {
            List<string> stuck = new List<string>();

            foreach (string file in files)
            {
                if (!WlanProfile.IsWlanProfile(file))
                    continue;

                try
                {
                    File.Delete(file);
                }
                catch (Exception ex)
                {
                    stuck.Add(Path.GetFileName(file) + " (" + ex.Message + ")");
                }
            }

            return stuck.Count == 0 ? null : string.Join(", ", stuck);
        }

        public override async Task<ModuleResult> RestoreAsync(string path)
        {
            List<StepResult> steps = new List<StepResult>();

            try
            {
                // Selection is by content, not filename: netsh names exports
                // "<interface name>-<SSID>.xml", and the interface name is machine-specific and
                // localised, so no filename pattern can be relied on. WlanProfile.FindIn parses
                // each *.xml and keeps only the ones whose root element is WLANProfile.
                string[] xmlFiles = WlanProfile.FindIn(path);

                if (xmlFiles.Length == 0)
                {
                    // The user selected this module for restore; there being nothing usable in the
                    // backup is a failure to deliver what they asked for, not something to skip.
                    steps.Add(StepResult.Failed(Title, "no Wi-Fi profile files were found in this backup"));
                }
                else
                {
                    // Import every matching profile, not just the first - a backup folder holds one
                    // XML per network, and stopping at the first entry discarded all the others.
                    foreach (string xmlFile in xmlFiles)
                    {
                        ProcessOutcome outcome = await Utils.RunToolAsync(
                            "netsh", new[] { "wlan", "add", "profile", "filename=" + xmlFile });

                        string name = Path.GetFileName(xmlFile);

                        if (outcome != null && outcome.Started && !outcome.TimedOut
                            && outcome.Error == null && outcome.ExitCode == 0)
                        {
                            steps.Add(StepResult.Applied(Title, name));
                        }
                        else if (outcome == null || !outcome.Started)
                        {
                            steps.Add(StepResult.Failed(name, "could not run netsh: " + (outcome == null ? "no outcome" : outcome.Error)));
                        }
                        else if (outcome.TimedOut)
                        {
                            steps.Add(StepResult.Failed(name, "netsh did not finish"));
                        }
                        else if (outcome.Error != null)
                        {
                            steps.Add(StepResult.Failed(name, "netsh ran but its outcome could not be determined: " + outcome.Error));
                        }
                        else
                        {
                            steps.Add(StepResult.Failed(name, $"netsh exited with code {outcome.ExitCode}"));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                steps.Add(StepResult.Failed(Title, $"{ex.GetType().Name}: {ex.Message}"));
            }

            return ModuleResult.Aggregate(steps);
        }

        /// <inheritdoc/>
        /// <remarks>
        /// The same content-based selection the restore uses, and it has to be: netsh names exports
        /// "&lt;interface&gt;-&lt;SSID&gt;.xml" with a machine-specific localised interface name, so
        /// no filename pattern identifies them, and these land in the SHARED backup root beside every
        /// other module's files. A bare "any *.xml here" probe would answer yes for a folder whose
        /// only XML belongs to something else.
        ///
        /// FindIn is already total - blank path, missing directory and any IO fault all return an
        /// empty array rather than throwing - so this cannot escape into the wizard's load.
        /// </remarks>
        public override bool? HasArtifactIn(string backupPath)
            => WlanProfile.FindIn(backupPath).Length > 0;
    }
}
