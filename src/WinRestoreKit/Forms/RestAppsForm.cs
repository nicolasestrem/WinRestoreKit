using WinRestoreKit;
using Conf;
using DataHelper;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Views
{
    public partial class RestAppsForm : Form
    {
        private static readonly LogHelper logger = LogHelper.Instance;

        /// <summary>
        /// True from the moment the install loop starts until it has finished or been stopped.
        /// </summary>
        /// <remarks>
        /// The loop used to be async void and was called un-awaited, so nothing disabled the Restore
        /// button while it ran. A second click therefore started a SECOND concurrent loop over the
        /// same ticked packages - and this app runs elevated, so that is two elevated winget install
        /// loops fighting over the same package store.
        /// </remarks>
        private bool installing;

        private readonly string restoreSourcePath;

        private sealed class BackupSource
        {
            public BackupSource(string path, bool isRestoreSource)
            {
                Path = path;
                IsRestoreSource = isRestoreSource;
            }

            public string Path { get; }

            public bool IsRestoreSource { get; }

            public override string ToString()
            {
                if (IsRestoreSource)
                    return "Selected restore source";

                return System.IO.Path.GetFileName(Path.TrimEnd(
                    System.IO.Path.DirectorySeparatorChar,
                    System.IO.Path.AltDirectorySeparatorChar));
            }
        }

        public RestAppsForm() : this(null)
        {
        }

        internal RestAppsForm(string restoreSourcePath)
        {
            this.restoreSourcePath = restoreSourcePath;

            InitializeComponent();

            // Load back ups. This reaches comboBackups.SelectedIndex = 0, which fires the
            // Designer-wired SelectedIndexChanged and so populates the list.
            //
            // There is deliberately no second subscription of that handler to comboBackups.Click.
            // The combo is a DropDownList, so ANY click on it fired Click; the handler begins by
            // clearing listApps, which is a CheckedListBox. Opening the dropdown therefore wiped
            // every box the user had ticked, inside a dialog whose only purpose is ticking a subset.
            LoadBackups();

            // Load styling
            SetStyle();
        }

        /// <summary>Set by Cancel or by closing the window: stop after the current package.</summary>
        /// <remarks>
        /// Deliberately "after the current one" rather than an abort. A winget install that is
        /// half-way through has already changed this machine; killing it would leave a partially
        /// installed package that no summary could describe honestly.
        /// </remarks>
        private bool stopRequested;

        /// <summary>The user closed the window mid-install; close for real once the loop is idle.</summary>
        private bool closeWhenIdle;

        /// <summary>True once OnShown has run, i.e. once this window can actually own a dialog.</summary>
        private bool hasBeenShown;

        /// <summary>
        /// A problem found before the window existed, still owed to the user. Null once reported.
        /// </summary>
        private string pendingProblemMessage;

        /// <summary>
        /// Adds the selected restore source before the legacy backup folders without duplicates.
        /// </summary>
        internal static IReadOnlyList<string> BackupSources(
            string restoreSourcePath,
            IEnumerable<string> legacyBackupPaths)
        {
            List<string> sources = new List<string>();

            AddSource(sources, restoreSourcePath);

            if (legacyBackupPaths != null)
            {
                foreach (string legacyBackupPath in legacyBackupPaths)
                    AddSource(sources, legacyBackupPath);
            }

            return sources;
        }

        private static void AddSource(ICollection<string> sources, string path)
        {
            if (string.IsNullOrWhiteSpace(path)
                || sources.Any(source => string.Equals(source, path, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            sources.Add(path);
        }

        internal void LoadBackups()
        {
            comboBackups.Items.Clear();

            IEnumerable<string> legacyBackups = Directory.Exists(Data.DataRootDir)
                ? Directory.GetDirectories(Data.DataRootDir)
                : Enumerable.Empty<string>();

            IReadOnlyList<string> sources = BackupSources(restoreSourcePath, legacyBackups);

            for (int i = 0; i < sources.Count; i++)
            {
                comboBackups.Items.Add(new BackupSource(
                    sources[i],
                    i == 0 && !string.IsNullOrWhiteSpace(restoreSourcePath)));
            }

            if (comboBackups.Items.Count > 0)
                comboBackups.SelectedIndex = 0;
        }

        // Some UI nicety
        private void SetStyle()
        {
            // Built after startup, so it is not in the shell's tree and themes itself.
            Theme.Apply(this);

            BackColor =
            listApps.BackColor =
                Ui.RailSurface;
        }


        /// <summary>
        /// Where the app export lives for the backup folder at <paramref name="backupFolder"/>.
        /// </summary>
        /// <remarks>
        /// Goes through AppStoreApps.ExportPathIn so this reader cannot disagree with the writer
        /// about the filename. It previously did, in two different ways at once: one call site
        /// looked for ".JSON" and built its path by concatenating a separator, the other for
        /// ".json" via Path.Combine.
        /// </remarks>
        internal static string ExportPathFor(string backupFolder)
            => AppStoreApps.ExportPathIn(backupFolder ?? string.Empty);

        /// <summary>
        /// Whether an app export was read, was simply not there, or could not be used.
        /// </summary>
        /// <remarks>
        /// Three states, not two. An export that is ABSENT is not a problem: this dialog is
        /// reachable standalone from the module tree, and most backup folders legitimately contain
        /// no app export because the user never ticked that module. Collapsing Absent into
        /// Unreadable would raise an error dialog on the ordinary case - the absence-is-normal flag
        /// collapse this codebase keeps having to un-collapse.
        /// </remarks>
        internal enum AppExportState
        {
            /// <summary>The file was read and had a Packages array (possibly an empty one).</summary>
            Ok,

            /// <summary>No file there. Normal, and never a dialog.</summary>
            Absent,

            /// <summary>There was something there, and it could not be used.</summary>
            Unreadable
        }

        /// <summary>
        /// The result of looking for one backup's app export. Never null, never carries null ids.
        /// </summary>
        internal sealed class AppExport
        {
            private AppExport(AppExportState state, IReadOnlyList<string> ids, string message)
            {
                State = state;
                PackageIdentifiers = ids ?? new string[0];
                Message = message;
            }

            internal AppExportState State { get; }

            /// <summary>Package ids in file order. Empty rather than null in every state.</summary>
            internal IReadOnlyList<string> PackageIdentifiers { get; }

            /// <summary>Human-readable text for the log, and for the dialog when this is a problem.</summary>
            internal string Message { get; }

            /// <summary>Whether the user needs to be told with a dialog rather than only a log line.</summary>
            internal bool IsProblem => State == AppExportState.Unreadable;

            internal static AppExport Ok(IReadOnlyList<string> ids, string message)
                => new AppExport(AppExportState.Ok, ids, message);

            internal static AppExport Absent(string message)
                => new AppExport(AppExportState.Absent, null, message);

            internal static AppExport Unreadable(string message)
                => new AppExport(AppExportState.Unreadable, null, message);

            /// <summary>
            /// Reads the export at <paramref name="path"/>. Total: it never throws.
            /// </summary>
            /// <remarks>
            /// Totality is the point, not tidiness. This runs during CONSTRUCTION - LoadBackups sets
            /// SelectedIndex, which fires the handler that calls this - so it executes before any
            /// window exists, on a path where .NET 8 turns an escaping exception into process
            /// termination with no window ever having been shown. Everything that touches the disk
            /// or the parser is therefore inside a catch: ReadAllText throws on a locked file, a
            /// directory, a malformed path or a permissions failure, and JObject.Parse throws on
            /// anything that is not JSON.
            ///
            /// Absence is decided by the exception the READ raises, not by File.Exists. File.Exists
            /// answers false for a file it was not allowed to look at, so it folds "there is nothing
            /// here" together with "I was not allowed to find out" - and only the first of those is
            /// normal. Believing the second one listed no apps while telling the user, in the one
            /// line they would read, that this backup CONTAINS no app export: a claim about what is
            /// in the folder, made without having been able to see it. Same rule the modules follow,
            /// where a target that could not be probed is never reported as an absence.
            /// </remarks>
            internal static AppExport Read(string path)
            {
                if (string.IsNullOrWhiteSpace(path))
                    return Absent("No app export path could be worked out, so no apps are listed.");

                string json;

                try
                {
                    json = File.ReadAllText(path);
                }
                catch (FileNotFoundException)
                {
                    // The one exception that is genuinely an absence: the folder opened, and the
                    // export is not in it. Not a problem, and not a dialog. The path is named so the
                    // log says which folder was looked in.
                    return Absent("This backup contains no app export (" + path +
                                  "), so there are no apps to list for it.");
                }
                catch (Exception ex)
                {
                    // Something is there, or something stopped us finding out - either way nothing
                    // is known about its contents, which is deliberately not the same as reporting
                    // it as an invalid file.
                    //
                    // DirectoryNotFoundException lands HERE rather than in the absence above, and
                    // the distinction is the whole point. LoadBackups enumerated this folder off
                    // disk moments ago, so the folder existing is not in question - it going missing
                    // between then and now means the drive was pulled, the network path dropped or
                    // the letter stopped resolving. That is the world changing underneath us, not a
                    // backup that happens to contain no apps, and reporting it as the latter would
                    // be the same confident claim about unseen contents that removing File.Exists
                    // was meant to end.
                    return Unreadable("Could not read the app export at " + path + ": " + ex.Message);
                }

                try
                {
                    return Parse(json, path);
                }
                catch (Exception ex)
                {
                    // Parse states its own problems, so reaching here means one it did not
                    // anticipate - a JSON shape that makes a token accessor throw. Still no escape
                    // route: this whole method runs before any window exists.
                    return Unreadable("Could not make sense of the app export at " + path + ": " + ex.Message);
                }
            }

            /// <summary>
            /// Turns the text of an export into package identifiers, or into a stated problem.
            /// </summary>
            /// <remarks>
            /// Mirrors AppStoreApps.Verify: the same shape decides the same way on both sides, so a
            /// file the backup called good is a file this dialog can list. An EMPTY Packages array
            /// is Ok - a machine with no winget packages is a fact, not a fault - while a MISSING
            /// Packages array is not, because that file cannot restore anything.
            /// </remarks>
            internal static AppExport Parse(string json, string path)
            {
                if (string.IsNullOrWhiteSpace(json))
                    return Unreadable("The app export at " + path + " is empty.");

                JArray packages;

                try
                {
                    packages = JObject.Parse(json)["Sources"]?.FirstOrDefault()?["Packages"] as JArray;
                }
                catch (Exception ex)
                {
                    return Unreadable("The app export at " + path + " is not valid JSON: " + ex.Message);
                }

                if (packages == null)
                {
                    return Unreadable("The app export at " + path +
                                      " has no list of packages in it, so nothing could be restored from it.");
                }

                // Entries with no PackageIdentifier are dropped rather than listed. A blank row is
                // tickable, and a ticked blank row reaches winget install --id "".
                List<string> ids = new List<string>();

                foreach (JToken package in packages)
                {
                    string id = package?.Value<string>("PackageIdentifier");

                    if (!string.IsNullOrWhiteSpace(id))
                        ids.Add(id);
                }

                return Ok(ids, "Read " + ids.Count + " app(s) from " + path + ".");
            }
        }

        /// <summary>
        /// What the list and the Restore button should show for a given export.
        /// </summary>
        internal sealed class ListState
        {
            internal ListState(IReadOnlyList<string> items, bool restoreEnabled)
            {
                Items = items ?? new string[0];
                RestoreEnabled = restoreEnabled;
            }

            internal IReadOnlyList<string> Items { get; }

            internal bool RestoreEnabled { get; }
        }

        /// <remarks>
        /// Pure, so the rule can be tested without a message loop. Restore is enabled only when
        /// there is at least one app to install: an absent export, an unreadable one, and a
        /// genuinely empty one all leave a button that would do nothing, and the Designer starts it
        /// disabled so this method can only ever turn it on.
        /// </remarks>
        internal static ListState ComposeListState(AppExport export)
        {
            if (export == null)
                return new ListState(null, false);

            return new ListState(export.PackageIdentifiers, export.PackageIdentifiers.Count > 0);
        }

        private void comboBackups_SelectedIndexChanged(object sender, EventArgs e)
        {
            BackupSource selectedBackup = comboBackups.SelectedItem as BackupSource;

            if (selectedBackup == null)
            {
                ApplyExport(AppExport.Absent(null));
                return;
            }

            ApplyExport(AppExport.Read(ExportPathFor(selectedBackup.Path)));
        }

        private void ApplyExport(AppExport export)
        {
            ListState state = ComposeListState(export);

            listApps.Items.Clear();

            foreach (string id in state.Items)
                listApps.Items.Add(id);

            btnRestore.Enabled = state.RestoreEnabled;

            if (export.Message != null)
            {
                // LogMessage, not Log: this text carries a file path and can carry a Newtonsoft
                // parse error, and JSON is made of braces. Log treats its first argument as a format
                // string, so a single brace throws inside the logger and the line is routed to
                // Console.WriteLine - invisible in a WinForms app. The one message a user needs in
                // order to diagnose a broken export was precisely the one being swallowed.
                logger.LogMessage(export.Message);
            }

            // Absent is not a problem, so it never reaches a dialog - only Unreadable does.
            //
            // WHERE that dialog is raised depends on whether a window exists yet. This method runs
            // during construction (LoadBackups sets SelectedIndex, which fires the handler), which
            // is before AStoreApps has called ShowDialog. Showing it here passed `this` as owner,
            // forcing handle creation on an unshown form: the dialog got an effectively invisible
            // owner and could paint BEHIND the main window - which, during an orchestrated restore,
            // is disabled. The user saw a frozen app with no visible cause and no way forward.
            //
            // So the state above is still computed during construction exactly as before; only the
            // dialog is deferred to OnShown.
            switch (RouteProblem(export, hasBeenShown))
            {
                case ProblemRouting.ShowNow:
                    // Cleared first, so the message cannot also be sitting queued for OnShown.
                    pendingProblemMessage = null;
                    ShowProblem(export.Message);
                    break;

                case ProblemRouting.Defer:
                    pendingProblemMessage = export.Message;
                    break;

                default:
                    // A read that came back clean withdraws a problem queued but not yet reported.
                    // Unreachable today (one ApplyExport runs before Shown), but a stale queued
                    // message would otherwise surface a dialog about a backup the user moved off.
                    pendingProblemMessage = null;
                    break;
            }
        }

        /// <summary>Where a problem found by <see cref="ApplyExport"/> should be reported.</summary>
        internal enum ProblemRouting
        {
            /// <summary>Nothing to report. Absent exports land here, and never raise a dialog.</summary>
            None,

            /// <summary>A window exists: report it immediately, as picking a bad backup always did.</summary>
            ShowNow,

            /// <summary>No window yet: hold it until OnShown, where it can be owned and seen.</summary>
            Defer
        }

        /// <remarks>
        /// Pure, so the rule that cost a hung-looking app can be tested without a message loop.
        ///
        /// Absent maps to None, not Defer. It is the ORDINARY case - this dialog is reachable
        /// standalone and most backup folders hold no app export - so deferring it would just move a
        /// spurious dialog rather than remove it. Only Unreadable is ever routed anywhere.
        /// </remarks>
        internal static ProblemRouting RouteProblem(AppExport export, bool hasBeenShown)
        {
            if (export == null || !export.IsProblem)
                return ProblemRouting.None;

            return hasBeenShown ? ProblemRouting.ShowNow : ProblemRouting.Defer;
        }

        /// <remarks>
        /// The one place the problem dialog is raised, so "reported once" is a property of this
        /// method's single caller set rather than of two call sites agreeing.
        /// </remarks>
        private void ShowProblem(string message)
        {
            MessageBox.Show(this, message, "The app list could not be read",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        /// <remarks>
        /// Anything found while the form was being built is surfaced here, once the window is real
        /// and visible and can genuinely own a modal dialog. The pending message is cleared BEFORE
        /// it is shown, so a re-entrant Shown could not report the same problem twice.
        /// </remarks>
        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);

            hasBeenShown = true;

            string problem = pendingProblemMessage;
            pendingProblemMessage = null;

            if (problem != null)
                ShowProblem(problem);
        }

        /// <summary>
        /// What went wrong with one package, or null if it installed.
        /// </summary>
        /// <remarks>
        /// Mirrors AStoreApps.Verify branch for branch, and for the same reason: "did not start" and
        /// "started and we lost track of it" are different facts about whether this machine was
        /// changed. A package winget may have half-installed must not be reported as untouched.
        /// </remarks>
        internal static string Describe(ProcessOutcome outcome)
        {
            if (outcome == null)
                return "winget returned no outcome";

            if (!outcome.Started)
                return "could not run winget: " + outcome.Error;

            if (outcome.TimedOut)
                return "winget did not finish, and was stopped";

            if (outcome.Error != null)
                return "winget ran but its outcome could not be determined: " + outcome.Error;

            if (outcome.ExitCode != 0)
                return "winget exited with code " + outcome.ExitCode;

            return null;
        }

        /// <summary>The summary shown once the install loop has finished or been stopped.</summary>
        internal sealed class OutcomeMessage
        {
            internal OutcomeMessage(string caption, string text, MessageBoxIcon icon)
            {
                Caption = caption;
                Text = text;
                Icon = icon;
            }

            internal string Caption { get; }

            internal string Text { get; }

            internal MessageBoxIcon Icon { get; }
        }

        /// <summary>
        /// Composes the end-of-run summary from what was asked for, what was tried, and what failed.
        /// </summary>
        /// <remarks>
        /// Three numbers rather than two, because "failed" and "never started" are different things
        /// and neither may swallow the other. Stopping after five of twenty packages leaves fifteen
        /// that were never attempted: calling those fifteen failures would be a false alarm, and
        /// folding them into the five would hide a real one.
        ///
        /// Says "installed" rather than "restored": winget install on a package that is already
        /// present upgrades it to the current version, and nothing here pins the backed-up version,
        /// so the machine is not put back the way it was.
        /// </remarks>
        internal static OutcomeMessage ComposeOutcome(int requested, int attempted, IReadOnlyList<string> failures)
        {
            int failed = failures?.Count ?? 0;
            string failureList = failures == null ? string.Empty : string.Join("\n", failures);
            bool stoppedEarly = attempted < requested;
            int notStarted = requested - attempted;

            if (requested == 0)
            {
                return new OutcomeMessage("Nothing to do",
                    "No apps were ticked, so nothing was installed.", MessageBoxIcon.Information);
            }

            if (stoppedEarly)
            {
                string stopped = $"Stopped after {attempted} of {requested} app(s). " +
                                 $"The remaining {notStarted} were not started.";

                if (failed == 0)
                {
                    return new OutcomeMessage("Stopped",
                        stopped + $"\n\nwinget installed {attempted} app(s).",
                        MessageBoxIcon.Information);
                }

                // Both facts survive: what was stopped, and what failed among what was tried.
                return new OutcomeMessage("Stopped, and some apps failed",
                    stopped + $"\n\n{failed} of the {attempted} attempted failed:\n\n{failureList}",
                    MessageBoxIcon.Warning);
            }

            if (failed == 0)
            {
                return new OutcomeMessage("Done",
                    $"winget installed {attempted} app(s).\n\nAlready-installed apps are upgraded to the current version rather than put back at the backed-up one.",
                    MessageBoxIcon.Information);
            }

            return new OutcomeMessage("Some apps did not install",
                $"{failed} of {attempted} app(s) did not install:\n\n{failureList}",
                MessageBoxIcon.Warning);
        }

        /// <remarks>
        /// A Task, awaited by the click handler, rather than the async void it was. As async void it
        /// was called un-awaited inside the caller's try, so the caller's catch stopped covering it
        /// at the first await - an exception from the install loop escaped to the message loop
        /// instead of reaching the handler that was written to report it.
        /// </remarks>
        private async Task RestorePackagesAsync()
        {
            List<string> selectedPackages = listApps.CheckedItems
                .Cast<string>()
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToList();

            int requested = selectedPackages.Count;
            int attempted = 0;

            // Every package's outcome is inspected. This used to discard the returned
            // ProcessOutcome entirely, so with winget missing the loop completed instantly
            // having installed nothing and told the user nothing - a dialog that closed as
            // though every package had been installed.
            List<string> failures = new List<string>();

            foreach (string packageIdentifier in selectedPackages)
            {
                if (stopRequested)
                    break;

                attempted++;

                // RunWingetAsync already runs on a background thread, so no outer Task.Run is
                // needed. The window is shown: an install is long, and winget's own progress is
                // the only progress reporting this dialog has.
                ProcessOutcome outcome = await Utils.RunWingetAsync(
                    true,
                    "install",
                    "--id", packageIdentifier,
                    "--accept-source-agreements",
                    "--accept-package-agreements");

                string problem = Describe(outcome);

                if (problem != null)
                    failures.Add(packageIdentifier + ": " + problem);
            }

            OutcomeMessage summary = ComposeOutcome(requested, attempted, failures);

            Report(summary.Text, summary.Caption, summary.Icon);
        }

        /// <summary>
        /// Whether this form is still in a state where it can own a modal dialog.
        /// </summary>
        /// <remarks>
        /// Pure, so the rule can be tested without a message loop - the same reason ShouldDeferClose
        /// is pure.
        ///
        /// Visible is in here, and it is the condition that actually fires. Both callers open this
        /// form with ShowDialog and neither disposes it, and ShowDialog does not dispose on its own:
        /// Close on a modal form HIDES it. So after a close this dialog gets no vote on -
        /// Application.Exit, the owning form tearing down, Windows shutting down - IsDisposed and
        /// Disposing are both still false while the window is closed and invisible. A guard that
        /// tested only those would fall straight through and raise a modal owned by an invisible
        /// window, which Windows Forms serves by recreating a handle for it. That is the ownerless
        /// dialog that can paint behind a disabled main window, leaving an app that looks hung with
        /// no visible cause - the exact defect the OnShown deferral above exists to prevent,
        /// reintroduced on the summary path.
        /// </remarks>
        internal static bool CanOwnADialog(bool isDisposed, bool disposing, bool visible)
            => !isDisposed && !disposing && visible;

        /// <summary>
        /// Says something to the user, in a window while there still is one and in the log otherwise.
        /// </summary>
        /// <remarks>
        /// The window can be gone by the time the install loop finishes; see CanOwnADialog for how.
        /// Showing a MessageBox anyway is either a crash (disposed owner, from an async void handler,
        /// which ends the process) or a hidden modal, so neither is available and the text has to go
        /// somewhere else.
        ///
        /// KNOWN LIMIT, not a guarantee: the log is a RichTextBox on the main window and nothing else.
        /// When the form went away because the APP is going away, that control is being torn down too,
        /// so LogHelper's own guard catches the disposed target and routes the line to Console.Write -
        /// invisible in a Windows Forms app with no console. On that path the summary naming which
        /// packages failed is lost, and this fallback does not save it. It is still worth having,
        /// because the form can also be closed while the app lives on, and it is written down rather
        /// than assumed away: making it hold in every case needs a persistent log file, which is a
        /// filed item and not something to half-build here.
        /// </remarks>
        private void Report(string text, string caption, MessageBoxIcon icon)
        {
            if (!CanOwnADialog(IsDisposed, Disposing, Visible))
            {
                logger.LogMessage(caption + ": " + text);
                return;
            }

            MessageBox.Show(this, text, caption, MessageBoxButtons.OK, icon);
        }

        private async void btnRestore_Click(object sender, EventArgs e)
        {
            // Re-entrancy guard. The button is disabled for the duration below, but a guard on the
            // state itself is what makes that a rule rather than a side effect of the UI.
            if (installing)
                return;

            // Let us ensure backup is selected
            string selectedBackup = comboBackups.SelectedItem?.ToString();
            if (string.IsNullOrEmpty(selectedBackup))
            {
                MessageBox.Show(this, "Please select a backup to restore.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // The ticked boxes are the source of what gets installed. The old code re-read and
            // re-parsed the export here to build a list, passed it to RestorePackages - and
            // RestorePackages ignored the parameter and read the checkboxes anyway. The parse is
            // gone; what it incidentally provided, a check that the export still exists, is covered
            // by the fact that Restore is only ever enabled when that read produced apps to tick.
            installing = true;
            stopRequested = false;
            closeWhenIdle = false;
            SetInstallingUi(true);

            try
            {
                await RestorePackagesAsync();
            }
            catch (Exception ex)
            {
                Report("Restoration failed. Error: " + ex.Message, "Error", MessageBoxIcon.Error);
            }
            finally
            {
                // Every exit path, including the catch above: leaving the dialog permanently
                // disabled after one failure would strand the user in a window whose only working
                // control is the close box.
                installing = false;
                stopRequested = false;

                // Unless there is no window left to leave in any state. Touching the controls of a
                // disposed form throws, and this finally runs on an async void path where that ends
                // the process rather than surfacing anywhere. See CanOwnADialog for how the form can
                // be gone while the loop is still running.
                //
                // Disposal only, deliberately - not the Visible test Report uses. Re-enabling the
                // controls of a form that is merely hidden is harmless and leaves it correct if it
                // is ever shown again, and Close on an already-closed form is a no-op. It is owning
                // a MODAL that a hidden window cannot do, which is why only that call site is
                // stricter.
                if (!IsDisposed && !Disposing)
                {
                    SetInstallingUi(false);

                    if (closeWhenIdle)
                        Close();
                }
            }
        }

        /// <remarks>
        /// Cancel is repurposed rather than joined by a third button: adding one would change the
        /// Designer's layout and tab order, and "Cancel" during a multi-minute install is exactly
        /// the button a user reaches for when they want it to stop.
        /// </remarks>
        private void SetInstallingUi(bool busy)
        {
            comboBackups.Enabled = !busy;
            listApps.Enabled = !busy;
            btnRestore.Enabled = !busy && listApps.Items.Count > 0;

            btnCancel.Enabled = true;
            btnCancel.Text = busy ? "Stop after the current app" : "Cancel";
        }

        /// <summary>
        /// The Cancel button's caption once a stop has been asked for.
        /// </summary>
        /// <remarks>
        /// Says the wait ends either way. stopRequested is only read BETWEEN packages, so a wedged
        /// winget holds this dialog until RunWingetAsync's timeout kills it - a wait the user should
        /// know is bounded by something other than the install itself. No duration is quoted: the
        /// timeout is a private constant in WindowsHelper, and a number copied here would be a
        /// second source of truth for it, free to drift the moment the real one is tuned.
        ///
        /// The alternative - checking stopRequested mid-package - would mean killing winget
        /// part-way through, leaving a half-installed package no summary could describe honestly.
        /// Waiting is the lesser harm; saying so is the fix.
        /// </remarks>
        internal const string StoppingText = "Stopping after the current app (or its timeout)";

        private void btnCancel_Click(object sender, EventArgs e)
        {
            if (!installing)
            {
                this.Close();
                return;
            }

            stopRequested = true;
            btnCancel.Enabled = false;
            btnCancel.Text = StoppingText;
        }

        /// <summary>
        /// Whether a close must be held back until the install loop is idle.
        /// </summary>
        /// <remarks>
        /// Pure, so the rule can be tested without a message loop.
        ///
        /// UserClosing ONLY. The handler used to check nothing but the installing flag, so it
        /// vetoed every other CloseReason too: a mid-install Application.Exit, a close of the
        /// owning MainForm, and an MDI teardown were all silently refused - the app simply appeared
        /// to hang - and WindowsShutDown was refused until Windows overrode it, having first shown
        /// the user a blocked-shutdown screen naming WinRestoreKit. None of those closes is something
        /// this dialog gets a vote on; only a user closing THIS window is.
        /// </remarks>
        internal static bool ShouldDeferClose(bool installing, CloseReason reason)
            => installing && reason == CloseReason.UserClosing;

        /// <remarks>
        /// A user closing this window mid-install asks the loop to stop after the current package
        /// and defers the close until it has. Letting the form tear down underneath a running loop
        /// would leave an elevated winget install owned by a disposed window, and the summary -
        /// including any failures - would never be shown.
        /// </remarks>
        private void RestAppsForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (!installing)
                return;

            // The loop is asked to stop after the current package on ANY close - an app that is
            // going away should not keep installing. What the CloseReason decides is whether the
            // close WAITS for it.
            stopRequested = true;

            if (!ShouldDeferClose(installing, e.CloseReason))
                return;

            e.Cancel = true;
            closeWhenIdle = true;
            btnCancel.Enabled = false;
            btnCancel.Text = StoppingText;
        }
    }
}
