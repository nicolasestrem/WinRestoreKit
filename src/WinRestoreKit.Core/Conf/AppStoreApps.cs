using WinRestoreKit;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Conf
{
    public class AppStoreApps : BackupBase
    {
        /// <summary>
        /// The one spelling of this module's name, shared by the file it writes.
        /// </summary>
        public const string ModuleTitle = "Remember installed apps";

        /// <summary>
        /// The file this module writes, and the only name any reader may look for.
        /// </summary>
        /// <remarks>
        /// This name existed in four spellings at once: the producer wrote ".json", one reader
        /// looked for ".JSON", another for ".json", and the Info text shown to the user said
        /// ".JSON". On a case-insensitive filesystem three of those happen to agree, which is
        /// exactly why the disagreement survived - it is a latent bug that becomes real the moment
        /// a backup folder is read from anything that respects case. The extension is lowercase
        /// because that is what winget itself writes.
        ///
        /// It lives here, on the class that WRITES the file, rather than on BackupBase.RegFileNameFor:
        /// that seam derives .reg names from a registry key, and this artifact has no key.
        /// </remarks>
        public const string ExportFileName = ModuleTitle + ".json";

        /// <summary>
        /// Where this module's export lives inside <paramref name="backupFolder"/>.
        /// </summary>
        /// <remarks>
        /// The restore dialog composes its path through this method rather than repeating the
        /// Path.Combine, so producer and reader cannot disagree about the filename again.
        /// </remarks>
        public static string ExportPathIn(string backupFolder)
            => Path.Combine(backupFolder, ExportFileName);

        public AppStoreApps()
        {
            Title = ModuleTitle;
            Info = "This will export all installed winget package identifiers as a .json file.\nThe import process allows you to restore specific apps themselves based on this file.";
        }

        // HasBackupIn keeps the base fail-open restore behavior because this module closes no
        // processes and writes no settings itself. HasArtifactIn serves a different purpose: it
        // decides whether Compare may claim that the selected snapshot contains a usable app list.
        // That answer must describe this snapshot, not another source the later dialog might offer.
        public override bool? HasArtifactIn(string backupPath)
            => !string.IsNullOrWhiteSpace(backupPath) && File.Exists(ExportPathIn(backupPath));

        public override IReadOnlyList<RestoreTarget> RestoreTargets
            => new[]
            {
                RestoreTarget.Command(
                    "opens the app reinstall dialog; this item changes nothing by itself, and any " +
                    "installs happen only from choices made inside that dialog")
            };

        /// <remarks>
        /// The one module that opts out, and the reason is Restore returning Skipped: it writes
        /// nothing, so snapshotting it would spend a full winget export - measured at ~29 s, and
        /// allowed up to ten minutes - protecting a restore that cannot change anything.
        /// </remarks>
        public override bool RestoreMakesChanges => false;

        public override async Task<ModuleResult> BackupAsync(string path)
        {
            // Execute winget command to list installed apps
            string outputFilePath = ExportPathIn(path);

            // Clear the target before running winget. A direct-path caller or a folder from an older
            // build can be reused, so a later run can find a valid export from the first still
            // sitting there. winget can exit 0 while writing nothing when no source is configured.
            // Without this clear, Verify would accept the previous package list as the new one.
            try
            {
                if (File.Exists(outputFilePath))
                    File.Delete(outputFilePath);
            }
            catch (Exception ex)
            {
                return ModuleResult.Aggregate(new[]
                {
                    StepResult.Failed(Title, "could not clear the previous export at " + outputFilePath + ": " + ex.Message)
                });
            }

            ProcessOutcome outcome = await Utils.RunWingetAsync(false, "export", "-o", outputFilePath);

            return ModuleResult.Aggregate(new[] { Verify(outcome, outputFilePath) });
        }

        /// <summary>
        /// Checks that winget produced a file RestAppsForm can actually read back.
        /// </summary>
        /// <remarks>
        /// The artifact is verified, not just the exit code. The previous version awaited nothing -
        /// RunWT was async void - and logged "Backup successful" before winget had started, so the
        /// message was written before the fact it described could be known. Even with the exit code
        /// now available it is not sufficient: winget exits 0 having written nothing when it has no
        /// source configured, and a file with no Packages array restores nothing.
        ///
        /// internal static rather than private so the ladder can be exercised without a real winget,
        /// following the precedent of RestAppsForm.Describe. It reads ModuleTitle instead of the
        /// Title instance property, which is the same string by construction.
        /// </remarks>
        internal static StepResult Verify(ProcessOutcome outcome, string outputFilePath)
        {
            if (outcome == null)
                return StepResult.Failed(ModuleTitle, "the winget export returned no outcome");

            if (!outcome.Started)
                return StepResult.Failed(ModuleTitle, "could not run the winget export: " + outcome.Error);

            if (outcome.TimedOut)
                return StepResult.Failed(ModuleTitle, "the winget export did not finish");

            if (outcome.Error != null)
                return StepResult.Failed(ModuleTitle, "winget ran but its outcome could not be determined: " + outcome.Error);

            if (outcome.ExitCode != 0)
                return StepResult.Failed(ModuleTitle, "winget exited with code " + outcome.ExitCode);

            if (!File.Exists(outputFilePath))
                return StepResult.Failed(ModuleTitle, "winget reported success but wrote no file");

            string json;

            try
            {
                json = File.ReadAllText(outputFilePath);
            }
            catch (Exception ex)
            {
                // Could not read it, so nothing is known about its contents - deliberately not
                // reported as an invalid file.
                return StepResult.Failed(ModuleTitle, "could not read back the exported file: " + ex.Message);
            }

            if (string.IsNullOrWhiteSpace(json))
                return StepResult.Failed(ModuleTitle, "winget wrote an empty file");

            try
            {
                // Sources[0].Packages is exactly the shape RestAppsForm reads. Anything else is a
                // file the restore dialog will show as empty, so accepting it would put a green
                // tick on a backup that restores nothing.
                JArray packages = JObject.Parse(json)["Sources"]?.FirstOrDefault()?["Packages"] as JArray;

                if (packages == null)
                    return StepResult.Failed(ModuleTitle, "the exported file has no list of packages in it, so nothing could be restored from it");

                return StepResult.Succeeded(ModuleTitle, $"exported {packages.Count} package identifier(s)");
            }
            catch (Exception ex)
            {
                return StepResult.Failed(ModuleTitle, "the exported file is not valid JSON: " + ex.Message);
            }
        }

        /// <summary>
        /// Runs the restore on the caller's thread instead of a thread-pool thread.
        /// </summary>
        /// <remarks>
        /// The base RestoreAsync wraps Restore in Task.Run, and this is the one module whose Restore
        /// opens a window. Thread-pool threads are MTA; Windows Forms requires the application's STA
        /// UI thread. The orchestrator therefore calls the owner-aware overload below and this
        /// completed task keeps the dialog on the thread that owns that window.
        ///
        /// Not marked async on purpose: there is nothing to await, and async here would move the
        /// body back off the caller's thread in every case but the first.
        /// </remarks>
        public override Task<ModuleResult> RestoreAsync(string path)
            => Task.FromResult(Restore(path));

        /// <summary>Runs the dialog on the caller's thread with its modal owner.</summary>
        internal Task<ModuleResult> RestoreAsync(string path, object owner)
            => Task.FromResult(Restore(path, owner));

        /// <summary>
        /// Opens the app reinstall dialog for <paramref name="path"/> with its modal owner.
        /// Registered by the app at startup; null in any process that has no UI to open it with.
        /// </summary>
        /// <remarks>
        /// The owner remains <see cref="object"/> here so Core stays WinForms-free. The app passes
        /// its <c>IWin32Window</c> through this seam and casts it only at the ShowDialog call site.
        /// A delegate rather than a constructor argument because this module is constructed by
        /// Activator.CreateInstance(type) with no arguments in nine test sites, and every module in
        /// the app is enumerated that way. A parameterless constructor is not negotiable here.
        ///
        /// Registration happens in Program.Main before the message pump starts, so the unregistered
        /// path below is not reachable from the running app. It exists for the test suite and for
        /// any future headless host, where failing closed is the point.
        /// </remarks>
        internal static Action<string, object> RestoreDialog;

        /// <remarks>
        /// This module restores nothing itself. It opens the app restore dialog, and the installs
        /// happen later from inside it, so Skipped is the only honest answer available here -
        /// claiming a result it does not have would be a new lie in a phase built to remove them.
        ///
        /// When no dialog is registered or no owner is supplied the answer is Failed, NOT Skipped.
        /// Skipped already means something specific and true on this module - "the dialog took it
        /// from here" - so reusing it for a total no-op would make a missing dialog or unowned
        /// dialog indistinguishable from the ordinary interactive path.
        ///
        /// Call this only from the UI thread - see RestoreAsync above.
        /// </remarks>
        public override ModuleResult Restore(string path)
            => Restore(path, null);

        /// <summary>Opens the registered dialog with an application-owned modal window.</summary>
        internal ModuleResult Restore(string path, object owner)
        {
            // Read the delegate once: it is static and mutable, and a null check against one read
            // followed by an invoke of another is a race with whatever cleared it.
            Action<string, object> dialog = RestoreDialog;

            if (dialog == null)
            {
                return ModuleResult.Aggregate(new[]
                {
                    StepResult.Failed(Title,
                        "the app restore dialog is not available in this process, so nothing " +
                        "could be offered for reinstall")
                });
            }

            if (owner == null)
            {
                return ModuleResult.Aggregate(new[]
                {
                    StepResult.Failed(Title,
                        "the app restore dialog requires an owning application window, so nothing " +
                        "could be offered for reinstall")
                });
            }

            dialog(path, owner);

            return ModuleResult.Aggregate(new[]
            {
                StepResult.Skipped(Title, "handled interactively in the app restore dialog")
            });
        }
    }
}
