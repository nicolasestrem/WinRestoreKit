using Conf;
using DataHelper;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using WinRestoreKit;

namespace Views
{
    public partial class RestAppsForm : Form
    {
        private static readonly LogHelper logger = LogHelper.Instance;
        private readonly string restoreSourcePath;
        private bool installing;
        private bool stopRequested;
        private bool closeWhenIdle;
        private bool hasBeenShown;
        private string pendingProblemMessage;

        private sealed class BackupSource
        {
            internal BackupSource(AppRestoreSource source) => Source = source;
            internal AppRestoreSource Source { get; }
            public override string ToString() => Source.DisplayName;
        }

        public RestAppsForm() : this(null)
        {
        }

        internal RestAppsForm(string restoreSourcePath)
        {
            this.restoreSourcePath = restoreSourcePath;
            InitializeComponent();
            LoadBackups();
            SetStyle();
        }

        internal void LoadBackups()
        {
            comboBackups.Items.Clear();
            IReadOnlyList<AppRestoreSource> sources = AppRestoreService.BuildSources(restoreSourcePath,
                new SnapshotEventCatalog().Read());
            foreach (AppRestoreSource source in sources)
                comboBackups.Items.Add(new BackupSource(source));
            if (comboBackups.Items.Count > 0)
                comboBackups.SelectedIndex = 0;
        }

        private void SetStyle()
        {
            Theme.Apply(this);
            BackColor = listApps.BackColor = Ui.RailSurface;
        }

        private void comboBackups_SelectedIndexChanged(object sender, EventArgs e)
        {
            BackupSource selected = comboBackups.SelectedItem as BackupSource;
            ApplyExport(selected == null
                ? AppExport.Absent("No app backup source is selected.")
                : AppRestoreService.ReadFromSource(selected.Source.Path));
        }

        private void ApplyExport(AppExport export)
        {
            AppRestoreListState state = AppRestoreService.ComposeListState(export);
            listApps.Items.Clear();
            foreach (string identifier in state.Items)
                listApps.Items.Add(identifier);
            btnRestore.Enabled = state.InstallEnabled;
            logger.LogMessage(export.Message);

            switch (AppRestoreService.RouteProblem(export, hasBeenShown))
            {
                case AppRestoreProblemRouting.ShowNow:
                    pendingProblemMessage = null;
                    ShowProblem(export.Message);
                    break;
                case AppRestoreProblemRouting.Defer:
                    pendingProblemMessage = export.Message;
                    break;
                default:
                    pendingProblemMessage = null;
                    break;
            }
        }

        private void ShowProblem(string message)
            => MessageBox.Show(this, message, "The app list could not be read",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            hasBeenShown = true;
            string problem = pendingProblemMessage;
            pendingProblemMessage = null;
            if (problem != null)
                ShowProblem(problem);
        }

        private async Task RestorePackagesAsync()
        {
            string[] selectedPackages = listApps.CheckedItems.Cast<string>()
                .Where(identifier => !string.IsNullOrWhiteSpace(identifier)).ToArray();
            AppRestoreOutcome outcome = await AppRestoreService.InstallAsync(selectedPackages,
                () => stopRequested);
            Report(outcome.Text, outcome.Caption, ToIcon(outcome.Severity));
        }

        private static MessageBoxIcon ToIcon(RunSeverity severity) => severity switch
        {
            RunSeverity.Error => MessageBoxIcon.Error,
            RunSeverity.Warning => MessageBoxIcon.Warning,
            _ => MessageBoxIcon.Information
        };

        internal static bool CanOwnADialog(bool isDisposed, bool disposing, bool visible)
            => !isDisposed && !disposing && visible;

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
            if (installing)
                return;
            if (comboBackups.SelectedItem == null)
            {
                MessageBox.Show(this, "Please select a backup to restore.", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

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
                installing = false;
                stopRequested = false;
                if (!IsDisposed && !Disposing)
                {
                    SetInstallingUi(false);
                    if (closeWhenIdle)
                        Close();
                }
            }
        }

        private void SetInstallingUi(bool busy)
        {
            comboBackups.Enabled = !busy;
            listApps.Enabled = !busy;
            btnRestore.Enabled = !busy && listApps.Items.Count > 0;
            btnCancel.Enabled = true;
            btnCancel.Text = busy ? "Stop after the current app" : "Cancel";
        }

        internal const string StoppingText = "Stopping after the current app (or its timeout)";

        private void btnCancel_Click(object sender, EventArgs e)
        {
            if (!installing)
            {
                Close();
                return;
            }
            stopRequested = true;
            btnCancel.Enabled = false;
            btnCancel.Text = StoppingText;
        }

        internal static bool ShouldDeferClose(bool installing, CloseReason reason)
            => installing && reason == CloseReason.UserClosing;

        private void RestAppsForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (!installing)
                return;
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
