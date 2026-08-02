using WinRestoreKit;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Conf
{
    /// <summary>
    /// One power scheme as powercfg reported it.
    /// </summary>
    /// <remarks>
    /// <see cref="Name"/> is DISPLAY ONLY. powercfg localises the friendly name ("Balanced" is
    /// "Ausbalanciert" on a German install, and a user-created plan can be called anything at all),
    /// so no decision in this module may be taken on it. The GUID is the identity.
    /// </remarks>
    internal sealed class PowerSchemeEntry
    {
        internal PowerSchemeEntry(string guid, string name, bool isActive)
        {
            Guid = guid;
            Name = name;
            IsActive = isActive;
        }

        internal string Guid { get; }

        internal string Name { get; }

        internal bool IsActive { get; }
    }

    /// <summary>
    /// Backs up the machine's power schemes and which one is active; restores the active choice.
    /// </summary>
    /// <remarks>
    /// SNAPSHOT CLOSURE - why this restore is activate-only.
    ///
    /// The pre-restore snapshot is this module's own Backup, which records the currently-active
    /// scheme in the manifest. Restore's ONLY write is the active-scheme selection, so everything
    /// restore changes is inside the snapshot, and restoring the snapshot re-activates the plan that
    /// was live beforehand. That is full closure, and it is the whole reason the restore is shaped
    /// this way.
    ///
    /// powercfg /import was rejected for precisely that reason (user decision, 2026-07-21). Importing
    /// creates NEW GUID-keyed scheme objects on the machine, and this app has no mechanism to delete
    /// a scheme - so the snapshot could not undo them, while SnapshotGate would still report the
    /// restore as fully undoable. That asymmetry is the same one that pushed WTelemetry's
    /// legacy-filename fallback out of Phase 2c: a caveat buried in a step reason does not correct a
    /// verdict the user reads as "this can be undone".
    ///
    /// The .pow files are still exported, because they are the only way to carry a custom plan to
    /// another machine - but importing them is a deliberate manual act by the user, stated in
    /// <see cref="BackupBase.WarningMessage"/>, not something this restore does behind the snapshot's
    /// back.
    /// </remarks>
    public class WPowerPlans : BackupBase
    {
        /// <summary>The one spelling of this module's name, shared by the files it writes.</summary>
        public const string ModuleTitle = "Power plans";

        /// <summary>
        /// The manifest this module writes, and the only name any reader may look for.
        /// </summary>
        /// <remarks>
        /// A const on the class that WRITES the file, following AppStoreApps.ExportFileName: a name
        /// kept away from its producer drifts, and that one ended up spelled four ways at once.
        /// BackupBase.RegFileNameFor is not the seam for this - it derives .reg names from a registry
        /// key, and this artifact has no key.
        /// </remarks>
        public const string ManifestFileName = ModuleTitle + ".json";

        /// <summary>
        /// Where this module's manifest lives inside <paramref name="backupFolder"/>.
        /// </summary>
        /// <remarks>
        /// Backup and restore both compose their path through here, so producer and reader cannot
        /// disagree about the filename.
        /// </remarks>
        public static string ManifestPathIn(string backupFolder)
            => Path.Combine(backupFolder, ManifestFileName);

        /// <inheritdoc/>
        /// <remarks>
        /// The manifest, not the .pow files. Restore is driven entirely from it - it is what maps a
        /// scheme GUID to its exported file and records which plan was active - so a folder holding
        /// .pow files without it has nothing this module can act on.
        /// </remarks>
        public override bool? HasArtifactIn(string backupPath)
            => !string.IsNullOrWhiteSpace(backupPath) && File.Exists(ManifestPathIn(backupPath));

        /// <summary>
        /// The exported file for one power scheme.
        /// </summary>
        /// <remarks>
        /// Keyed by GUID, never by the scheme's friendly name and never by anything derived from the
        /// current account. The GUID is stable across machines and accounts, it is unique per scheme
        /// - so a loop over N schemes produces N distinct filenames, the rule
        /// BackupBase.RegFileNameFor exists to enforce for .reg exports - and no user name ever
        /// enters an artifact name. The GUID reaching here was matched by <see cref="GuidPattern"/>,
        /// so it contains only hex digits and hyphens and cannot carry a path separator.
        /// </remarks>
        internal static string PowFileNameFor(string guid)
            => ModuleTitle + "_" + guid + ".pow";

        public WPowerPlans()
        {
            Title = ModuleTitle;

            Info = "This will back up your Windows power plans and remember which one is active.\n" +
                   "Each plan is exported as a .pow file, and a manifest records the plan that was " +
                   "active at the time.\n" +
                   "Restoring re-activates that plan; it does not import the exported plan files.";

            WarningMessage =
                "Restoring only re-activates the power plan that was active when the backup was taken. " +
                "If that plan no longer exists on this PC the restore fails rather than recreating it, " +
                "and any changes made to a plan's own settings since the backup are not reverted. " +
                "The exported .pow files are left for you to import by hand with powercfg /import if " +
                "you want a plan back.";
        }

        // IsInstalled is deliberately NOT overridden, matching WNetworkConf and CWiFiConf: the base
        // returns false, so command-driven modules are not auto-ticked by "select installed", which
        // probes for something present on this machine rather than for a tool Windows always ships.

        /// <remarks>
        /// A command rather than a registry key or a folder: what changes is the machine's active
        /// power scheme, which powercfg owns. The wording states what the restore does NOT do,
        /// because "power plans" reads as though the plans themselves come back - and the gap between
        /// that expectation and the activate-only behaviour is exactly what a user needs to know
        /// before consenting.
        /// </remarks>
        public override IReadOnlyList<RestoreTarget> RestoreTargets
            => new[]
            {
                RestoreTarget.Command(
                    "runs powercfg to re-activate the power plan that was active when this backup " +
                    "was taken; the exported plan files are NOT imported, so no plan is created, " +
                    "changed or removed on this PC")
            };

        // --- parsing -----------------------------------------------------------------------

        /// <remarks>
        /// Identity is the GUID and nothing else. powercfg's own labels ("Power Scheme GUID:",
        /// "Existing Power Schemes") are LOCALISED, so a parser that keys off them works on the
        /// machine it was written on and silently finds zero plans everywhere else - the same
        /// class of defect as CWiFiConf's "WLAN*.xml" glob, which matched 0 of 19 real exports.
        /// </remarks>
        private const string GuidPattern =
            @"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}";

        private static readonly Regex GuidRegex = new Regex(GuidPattern, RegexOptions.Compiled);

        /// <summary>
        /// The parenthesised friendly name at the end of a line, for display only.
        /// </summary>
        private static readonly Regex TrailingNameRegex =
            new Regex(@"\(([^()]*)\)\s*\*?\s*$", RegexOptions.Compiled);

        /// <summary>
        /// Reads the schemes out of <c>powercfg /list</c> output.
        /// </summary>
        /// <remarks>
        /// Measured on Windows 11, 2026-07-21, the output is one line per scheme carrying a GUID,
        /// the friendly name in brackets, and a trailing " *" on the active one. Lines with no GUID
        /// (the banner and its rule) are skipped rather than parsed, which is what makes this survive
        /// a translated banner.
        /// </remarks>
        internal static IReadOnlyList<PowerSchemeEntry> ParseSchemeList(string text)
        {
            List<PowerSchemeEntry> found = new List<PowerSchemeEntry>();

            if (string.IsNullOrEmpty(text))
                return found;

            foreach (string rawLine in text.Split('\n'))
            {
                string line = rawLine.Trim();

                Match guid = GuidRegex.Match(line);

                if (!guid.Success)
                    continue;

                // The active marker is the trailing asterisk. Deliberately checked on the trimmed
                // line's last character rather than by searching for '*' anywhere: a user-created
                // plan may legitimately have an asterisk in its name.
                bool isActive = line.EndsWith("*", StringComparison.Ordinal);

                Match name = TrailingNameRegex.Match(line);

                found.Add(new PowerSchemeEntry(
                    guid.Value,
                    name.Success ? name.Groups[1].Value.Trim() : "",
                    isActive));
            }

            return found;
        }

        /// <summary>
        /// Reads the GUID out of <c>powercfg /getactivescheme</c> output, or null if there is none.
        /// </summary>
        internal static string ParseActiveSchemeGuid(string text)
        {
            if (string.IsNullOrEmpty(text))
                return null;

            Match guid = GuidRegex.Match(text);

            return guid.Success ? guid.Value : null;
        }

        // --- running powercfg --------------------------------------------------------------

        /// <summary>
        /// A powercfg run together with whatever it printed.
        /// </summary>
        private sealed class ToolCapture
        {
            internal ProcessOutcome Outcome;

            /// <summary>What powercfg printed, or null when it could not be read back.</summary>
            internal string Output;

            /// <summary>Why <see cref="Output"/> is null. Null when it is not.</summary>
            internal string ReadError;
        }

        /// <summary>
        /// Runs powercfg and hands back what it printed.
        /// </summary>
        /// <remarks>
        /// ProcessOutcome carries no stdout and RunToolAsync only lands captured output on disk, so
        /// reading powercfg's listing means giving it a file and reading that file back. The file is
        /// created in the SYSTEM TEMP directory, not in the backup folder, for two reasons: the
        /// restore path also needs a capture (the /getactivescheme read-back) and must not write into
        /// the backup it is restoring from, and the backup folder is enumerated by other modules and
        /// by the History timeline, so a scratch file there is a stray artifact somebody has to reason about.
        ///
        /// It is removed in a finally. A removal that fails is logged and dropped: it leaves a few
        /// hundred bytes in %TEMP% and must never displace the real result of the run.
        /// </remarks>
        private static async Task<ToolCapture> CaptureAsync(params string[] args)
        {
            ToolCapture capture = new ToolCapture();

            string scratch = Path.Combine(
                Path.GetTempPath(), "appcopier_powercfg_" + Guid.NewGuid().ToString("N") + ".txt");

            try
            {
                capture.Outcome = await Utils.RunToolAsync("powercfg", args, stdoutFile: scratch);

                try
                {
                    capture.Output = File.ReadAllText(scratch);
                }
                catch (Exception ex)
                {
                    // RunToolAsync writes the file only on exit 0, so a missing file here is the
                    // ordinary companion of a failed run - the ladder above will already have a
                    // better reason. When the run DID succeed this is the honest "I could not tell",
                    // and the callers treat it as a failure rather than as an empty listing.
                    capture.ReadError = ex.Message;
                    LogHelper.Instance.LogMessage(
                        "Could not read back powercfg output from " + scratch + ": " + ex.Message);
                }
            }
            finally
            {
                try
                {
                    if (File.Exists(scratch))
                        File.Delete(scratch);
                }
                catch (Exception ex)
                {
                    LogHelper.Instance.LogMessage(
                        "Could not remove the temporary powercfg output at " + scratch + ": " + ex.Message);
                }
            }

            return capture;
        }

        /// <summary>
        /// Why a powercfg run cannot be believed, or null when it can.
        /// </summary>
        /// <remarks>
        /// The same ladder WNetworkConf.Verify walks, returning the REASON rather than a StepResult
        /// so each caller can label its own step - this module makes several distinct powercfg calls
        /// per backup and they must not all report themselves as the same target. An exit code alone
        /// is never the end of it: every caller here goes on to check the artifact or the output the
        /// command was supposed to produce.
        /// </remarks>
        private static string ProblemWith(ProcessOutcome outcome)
        {
            if (outcome == null)
                return "the powercfg run returned no outcome";

            if (!outcome.Started)
                return "could not run powercfg: " + outcome.Error;

            if (outcome.TimedOut)
                return "powercfg did not finish";

            if (outcome.Error != null)
                return "powercfg ran but its outcome could not be determined: " + outcome.Error;

            if (outcome.ExitCode != 0)
                return "powercfg exited with code " + outcome.ExitCode;

            return null;
        }

        /// <summary>What to call one scheme in a step the user reads.</summary>
        private static string LabelFor(PowerSchemeEntry scheme)
            => string.IsNullOrWhiteSpace(scheme.Name) ? scheme.Guid : scheme.Name;

        /// <summary>
        /// How to put a missing plan back by hand, for the restore that could not find it.
        /// </summary>
        /// <remarks>
        /// THE GUID IN THIS COMMAND IS LOAD-BEARING, and the PR review caught its absence. Measured
        /// from powercfg's own help, 2026-07-21:
        ///
        ///     POWERCFG /IMPORT &lt;FILENAME&gt; [&lt;GUID&gt;]
        ///     &lt;GUID&gt;  Specifies the GUID for the imported scheme. If no GUID is specified,
        ///             a new GUID will be created.
        ///
        /// The earlier wording said `powercfg /import "&lt;file&gt;"` with no GUID. A user following it
        /// would get the plan back under a NEW identity the manifest does not name, so the very next
        /// restore would fail in the same branch and hand them the same non-working instruction.
        /// Advice that silently fails to fix the thing it is offered for is this project's failure
        /// mode arriving through text instead of through code.
        ///
        /// Separated from the step that reports it so a test can read the sentence without launching
        /// powercfg - this suite deliberately shells out to nothing, and driving RestoreAsync to
        /// reach this text would have broken that.
        /// </remarks>
        internal static string ImportByHandAdvice(string guid)
            => "the backed-up power plan is probably not on this PC. The exported "
             + PowFileNameFor(guid) + " in this backup folder can be added by hand with: "
             + "powercfg /import \"<file>\" " + guid + " - include that GUID, or Windows imports the "
             + "plan under a new one and this item will not find it next time either";

        /// <summary>
        /// Removes a file if it is there, returning why it could not. Never throws.
        /// </summary>
        private static string TryClear(string filePath)
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

        // --- backup ------------------------------------------------------------------------

        public override async Task<ModuleResult> BackupAsync(string path)
        {
            List<StepResult> steps = new List<StepResult>();

            try
            {
                ToolCapture list = await CaptureAsync("/list");

                string problem = ProblemWith(list.Outcome);

                if (problem != null)
                {
                    steps.Add(StepResult.Failed(Title, "could not list the power plans: " + problem));
                    return ModuleResult.Aggregate(steps);
                }

                if (list.Output == null)
                {
                    steps.Add(StepResult.Failed(Title,
                        "powercfg listed the power plans but its output could not be read back: " + list.ReadError));
                    return ModuleResult.Aggregate(steps);
                }

                IReadOnlyList<PowerSchemeEntry> schemes = ParseSchemeList(list.Output);

                if (schemes.Count == 0)
                {
                    // NOT a skip. Windows always has at least one power scheme, so powercfg cannot
                    // honestly report none - finding no GUIDs in a successful run means the output
                    // was not understood, and "I could not tell" is a tool failure, not an absence.
                    steps.Add(StepResult.Failed(Title,
                        "powercfg succeeded but no power plans could be read out of its output"));
                    return ModuleResult.Aggregate(steps);
                }

                foreach (PowerSchemeEntry scheme in schemes)
                    steps.Add(await ExportSchemeAsync(path, scheme));

                steps.Add(await WriteManifestAsync(path, schemes));
            }
            catch (Exception ex)
            {
                steps.Add(StepResult.Failed(Title, ex.GetType().Name + ": " + ex.Message));
            }

            return ModuleResult.Aggregate(steps);
        }

        private async Task<StepResult> ExportSchemeAsync(string path, PowerSchemeEntry scheme)
        {
            string label = LabelFor(scheme);
            string filePath = Path.Combine(path, PowFileNameFor(scheme.Guid));

            // Cleared before the export, for the two reasons ExportRegistryKey documents: the
            // artifact check below could otherwise be satisfied by a file this run did not write,
            // and ConfPageView reuses one timestamped folder for every Backup click in an app
            // session - so a failed re-export would leave the previous run's .pow sitting there,
            // restorable, while the row says Failed.
            string clearError = TryClear(filePath);

            if (clearError != null)
            {
                return StepResult.Failed(label,
                    "could not clear the previous export at " + filePath + ": " + clearError);
            }

            ProcessOutcome outcome = await Utils.RunToolAsync(
                "powercfg", new[] { "/export", filePath, scheme.Guid });

            string problem = ProblemWith(outcome);

            if (problem != null)
                return AbandonExport(filePath, label, problem);

            // An exit code is not evidence. Whatever powercfg said, the file it was told to write
            // has to be there and non-empty before this claims the plan was captured.
            StepResult step = Utils.ValidateExportArtifact(
                filePath, label, "powercfg", "exported the power plan " + label);

            // And a file that FAILED that check is abandoned too, not just one from a failed run.
            // The reachable case is exit code 0 over an empty file, which is not hypothetical: the
            // measurement that made ValidateExportArtifact mandatory in the first place was netsh
            // wlan export printing "saved successfully" with exit code 0 while writing nothing.
            //
            // This is deliberately STRICTER than Utils.ExportRegistryKey, whose RegFile.Validate
            // failure branches leave the file alone, and the difference is not an inconsistency to
            // reconcile. A bad .reg left on disk is caught downstream - ImportRegistryKey validates
            // before it will hand anything to regedit, so the restore refuses it. A bad .pow has no
            // downstream at all: this restore never reads .pow files, so the only consumer is a
            // human following this module's own instruction to import it by hand. Nothing else will
            // ever check it, which is exactly why it must not survive the failure that produced it.
            return AbandonIfFailed(filePath, step);
        }

        /// <summary>
        /// Removes the artifact behind a step that failed, and leaves a successful one alone.
        /// </summary>
        /// <remarks>
        /// One rule - a .pow this module will not vouch for does not stay in the backup folder -
        /// expressed once so both failure routes obey it, and internal so a test drives THIS
        /// function rather than a copy of it.
        ///
        /// It cannot pin the whole path: reaching <see cref="ExportSchemeAsync"/> means running
        /// powercfg, and coverage stops where elevation begins. What it does pin is the decision,
        /// leaving exactly one unpinned link - that the call site routes through here - instead of
        /// the rule itself being unwritten anywhere a test can see it.
        /// </remarks>
        internal static StepResult AbandonIfFailed(string filePath, StepResult step)
            => step != null && step.State == ResultState.Failed
                ? AbandonExport(filePath, step.Target, step.Reason)
                : step;

        /// <summary>
        /// Fails an export and removes whatever powercfg left at the target path.
        /// </summary>
        /// <remarks>
        /// The delete is the point, not tidiness, and it is here because of a measurement rather
        /// than a hunch. Measured on Windows 11, 2026-07-21, unelevated: powercfg /export CREATES
        /// THE TARGET FILE AND THEN FAILS - "A required privilege is not held by the client", exit
        /// code 1, with a zero-byte .pow left sitting at the path. (The app itself runs elevated, so
        /// this is the failure a user hits when elevation was declined, not the normal path.)
        ///
        /// Leaving that file behind is the CWiFiConf partial-export problem in a new place: the row
        /// says Failed while the backup folder still holds an artifact, and this module's warning
        /// text actively tells the user those .pow files can be imported by hand with
        /// powercfg /import. A zero-byte one cannot, so it must not survive the failure that
        /// produced it.
        ///
        /// A delete that fails is logged and then DROPPED rather than returned: it must never
        /// displace the real reason the export failed, which is what the user has to act on. The
        /// path is pre-cleared before the run, so anything here was written by this run.
        ///
        /// Internal rather than private so a test can call THIS function, following Utils.Worse: a
        /// test that reimplements the cleanup locally passes even when the production call site
        /// reverts to a bare StepResult.Failed, which is precisely the regression this guards.
        /// </remarks>
        internal static StepResult AbandonExport(string filePath, string label, string reason)
        {
            string deleteError = TryClear(filePath);

            if (deleteError != null)
            {
                LogHelper.Instance.LogMessage(
                    "Could not remove the incomplete power plan export at " + filePath + ": " + deleteError);
            }

            return StepResult.Failed(label, reason);
        }

        /// <summary>
        /// Records which plan is active. This is the file the restore actually runs on.
        /// </summary>
        private async Task<StepResult> WriteManifestAsync(string path, IReadOnlyList<PowerSchemeEntry> schemes)
        {
            // Asked of powercfg directly rather than taken from the "*" in the listing:
            // /getactivescheme is the command whose entire job is this one answer, and the manifest
            // is what restore acts on, so it gets the stronger source.
            //
            // To be exact about what this does NOT do, because an earlier version of this comment
            // claimed otherwise and both Phase 3c reviewers caught it: the listing's "*" marker is
            // parsed into PowerSchemeEntry.IsActive, and nothing here compares the two. There is no
            // cross-check and no fallback - a disagreement between the marker and /getactivescheme
            // would be discarded silently, and /getactivescheme would win by default rather than by
            // decision. IsActive is kept because it makes the parse observable to the tests that
            // pin locale-independence, not because any production decision reads it. If a
            // cross-check is ever wanted, this is the place, and it does not exist yet.
            ToolCapture active = await CaptureAsync("/getactivescheme");

            string problem = ProblemWith(active.Outcome);

            if (problem != null)
            {
                return StepResult.Failed(Title,
                    "could not determine which power plan is active: " + problem);
            }

            if (active.Output == null)
            {
                return StepResult.Failed(Title,
                    "powercfg reported the active power plan but its output could not be read back: " + active.ReadError);
            }

            string activeGuid = ParseActiveSchemeGuid(active.Output);

            if (activeGuid == null)
            {
                return StepResult.Failed(Title,
                    "powercfg succeeded but no active power plan could be read out of its output");
            }

            string manifestPath = ManifestPathIn(path);

            try
            {
                JArray entries = new JArray();

                foreach (PowerSchemeEntry scheme in schemes)
                {
                    entries.Add(new JObject
                    {
                        ["guid"] = scheme.Guid,
                        ["name"] = scheme.Name
                    });
                }

                JObject manifest = new JObject
                {
                    ["activeScheme"] = activeGuid,
                    ["schemes"] = entries
                };

                File.WriteAllText(manifestPath, manifest.ToString());
            }
            catch (Exception ex)
            {
                LogHelper.Instance.LogMessage(
                    "Could not write the power plan manifest to " + manifestPath + ": " + ex.Message);

                return StepResult.Failed(Title,
                    "could not write the power plan manifest to " + manifestPath + ": " + ex.Message);
            }

            return StepResult.Succeeded(Title,
                "recorded the active power plan and " + schemes.Count + " plan(s)");
        }

        // --- restore -----------------------------------------------------------------------

        public override async Task<ModuleResult> RestoreAsync(string path)
        {
            List<StepResult> steps = new List<StepResult>();

            try
            {
                steps.Add(await ActivateBackedUpSchemeAsync(path));
            }
            catch (Exception ex)
            {
                steps.Add(StepResult.Failed(Title, ex.GetType().Name + ": " + ex.Message));
            }

            return ModuleResult.Aggregate(steps);
        }

        private async Task<StepResult> ActivateBackedUpSchemeAsync(string path)
        {
            string manifestPath = ManifestPathIn(path);

            if (!File.Exists(manifestPath))
                return StepResult.Skipped(Title, NothingBackedUp);

            string json;

            try
            {
                json = File.ReadAllText(manifestPath);
            }
            catch (Exception ex)
            {
                // Deliberately not worded as "invalid": we could not read it, so we do not know
                // whether it is valid, and a locked file is a different problem for the user to fix
                // than a corrupt one.
                LogHelper.Instance.LogMessage(
                    "Could not read the power plan manifest at " + manifestPath + ": " + ex.Message);

                return StepResult.Failed(Title,
                    "could not read the backed-up power plan manifest: " + ex.Message);
            }

            string guid;

            try
            {
                guid = ParseActiveSchemeGuid((string)JObject.Parse(json)["activeScheme"]);
            }
            catch (Exception ex)
            {
                LogHelper.Instance.LogMessage(
                    "The power plan manifest at " + manifestPath + " is not valid JSON: " + ex.Message);

                return StepResult.Failed(Title,
                    "the backed-up power plan manifest is not valid JSON: " + ex.Message);
            }

            if (guid == null)
            {
                return StepResult.Failed(Title,
                    "the backed-up power plan manifest does not record which plan was active");
            }

            ProcessOutcome outcome = await Utils.RunToolAsync(
                "powercfg", new[] { "/setactive", guid });

            if (outcome == null)
                return StepResult.Failed(Title, "the powercfg run returned no outcome");

            if (!outcome.Started)
                return StepResult.Failed(Title, "could not run powercfg: " + outcome.Error);

            if (outcome.TimedOut)
                return StepResult.Failed(Title, "powercfg did not finish");

            if (outcome.Error != null)
            {
                // powercfg started, so the active plan may already have changed. Saying it could not
                // run would be a false claim about whether this machine was modified.
                return StepResult.Failed(Title,
                    "powercfg ran but its outcome could not be determined, so the active power plan may " +
                    "already have changed: " + outcome.Error);
            }

            if (outcome.ExitCode != 0)
            {
                // The expected cause: the plan does not exist on THIS machine, which is the ordinary
                // cross-machine case. The .pow files are named because importing one by hand is the
                // user's route out, and this restore deliberately will not do it for them - see the
                // snapshot-closure note on this class.
                //
                // THE GUID IN THAT COMMAND IS LOAD-BEARING, and the PR review caught its absence.
                // Measured from powercfg's own help, 2026-07-21:
                //
                //     POWERCFG /IMPORT <FILENAME> [<GUID>]
                //     <GUID>  Specifies the GUID for the imported scheme. If no GUID is specified,
                //             a new GUID will be created.
                //
                // So the advice this message used to give - import the file, no GUID - would have
                // created the plan under a NEW identity that the manifest does not name, and the
                // very next restore would fail in exactly this branch again. Advice that quietly
                // does not fix the thing it is offered to fix is the failure mode this project is
                // organised against, arriving through the text rather than through the code.
                return StepResult.Failed(Title,
                    "powercfg exited with code " + outcome.ExitCode + "; " + ImportByHandAdvice(guid));
            }

            return await ConfirmActiveSchemeAsync(guid);
        }

        /// <summary>
        /// Reads the active scheme back after setting it.
        /// </summary>
        /// <remarks>
        /// The tri-state maps the same way ImportRegistryKey's post-import probe does, and for the
        /// same reason. Exit code 0 already supports "applied", so a read-back that cannot be
        /// PERFORMED narrows the wording rather than inverting the verdict - failing there would cry
        /// wolf every time the read-back itself is what broke. Only positive evidence of the wrong
        /// state, a different GUID actually reported as active, is a failure.
        /// </remarks>
        private async Task<StepResult> ConfirmActiveSchemeAsync(string guid)
        {
            const string what = "the power plan that was active when this backup was taken";

            ToolCapture active = await CaptureAsync("/getactivescheme");

            string problem = ProblemWith(active.Outcome);

            if (problem != null || active.Output == null)
            {
                return StepResult.Succeeded(Title,
                    "applied " + what + "; could not confirm which plan is active afterwards");
            }

            string now = ParseActiveSchemeGuid(active.Output);

            if (now == null)
            {
                return StepResult.Succeeded(Title,
                    "applied " + what + "; could not confirm which plan is active afterwards");
            }

            if (!string.Equals(now, guid, StringComparison.OrdinalIgnoreCase))
            {
                return StepResult.Failed(Title,
                    "powercfg reported success but " + now + " is active afterwards, not the backed-up " + guid);
            }

            return StepResult.Applied(Title, what);
        }
    }
}
