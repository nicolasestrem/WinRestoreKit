using WinRestoreKit;
using Conf;
using DataHelper;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Views
{
    /// <summary>
    /// The Backup page: presets + the full module tree, and the backup run. Renamed from
    /// ConfPageView in Phase 4 PR 7 once the restore flow moved to the wizard and this page lost its
    /// restore role.
    /// </summary>
    public partial class BackupPageView : UserControl, IRunUi
    {
        private static readonly LogHelper logger = LogHelper.Instance;

        /// <summary>
        /// The greeting shown in the info pane. {0} is the OS build string from OsHelper.GetVersion.
        /// </summary>
        /// <remarks>
        /// A const rather than an inline interpolation so the composition is testable without
        /// constructing the control. GetVersion degrades to a self-describing token rather than to
        /// an empty string precisely so this sentence cannot come out with a double space or a
        /// stray " ." in it; OsVersionTests asserts that for every degraded shape.
        /// </remarks>
        internal const string IntroTemplate =
            "This app supports you in backing up, sharing, and restoring your key settings of your Windows 11 {0} on this or another system.";

        internal string CurrentBackupPath = Data.DataRootDir + Data.NowShort + "\\";

        internal List<BackupBase> selectedConfigs = new List<BackupBase>();

        /// <summary>
        /// Runs a backup, routing every progress update, result, consent prompt and Explorer-restart
        /// toggle back here through <see cref="IRunUi"/>. Constructed once with this control as the
        /// UI surface. (Restore runs in the wizard, which owns its own orchestrator.)
        /// </summary>
        private readonly BackupRestoreOrchestrator runner;

        // root TableLayoutPanel row indices for the collapsible sections.
        private const int ResultsRow = 4;
        private const int LogRow = 6;

        // leftColumn row that hosts the (collapsible) full module tree.
        private const int TreeRow = 1;

        private bool isSelectAll = false;

        // True while a preset is programmatically ticking the tree, so AfterCheck does not read those
        // ticks as the user choosing Custom.
        private bool applyingPreset;

        // Whether the full module tree is expanded under the "Advanced" toggle.
        private bool treeExpanded;

        public BackupPageView()
        {
            InitializeComponent();
            InitializeConfigurations();
            SetStyle();

            runner = new BackupRestoreOrchestrator(this);

            // A hidden docked/row control must not reserve space. The results panel, the activity log
            // and the (collapsed) full module tree each follow their own visibility, so their rows do.
            resultsPanel.VisibleChanged += (s, e) => SetRowCollapsed(root, ResultsRow, !resultsPanel.Visible);
            rtbLog.VisibleChanged += (s, e) => SetRowCollapsed(root, LogRow, !rtbLog.Visible);
            treeConfigurations.VisibleChanged += (s, e) => SetRowCollapsed(leftColumn, TreeRow, !treeConfigurations.Visible);
            rtbLog.Visible = false;
            SetRowCollapsed(root, ResultsRow, !resultsPanel.Visible);
            ExpandTree(false);

            // "Everything on this PC" is the default; its radio applies Select-available on check.
            rbEverything.Text = "Everything on this PC   (" + CountInstalled() + " items found)";
            rbEverything.Checked = true;
        }

        private void SetStyle()
        {
            BackColor = Ui.Surface;

            headerLabel.Font = Ui.Title();
            headerLabel.ForeColor = Ui.TextPrimary;

            linkSubHeader.Font = Ui.BodyBold();
            linkSubHeader.ForeColor = Ui.TextPrimary;

            treeConfigurations.Font = Ui.Body();

            txtInfo.Font = Ui.Body();
            txtInfo.BackColor = Ui.Surface;
            txtInfo.ForeColor = Ui.TextPrimary;

            logToggle.Font = Ui.Body();

            foreach (RadioButton rb in new[] { rbEverything, rbDeveloper, rbMinimal, rbCustom })
            {
                rb.Font = Ui.Body();
                rb.ForeColor = Ui.TextPrimary;
            }

            lblWarnings.Font = Ui.Body();
            lnkAdvanced.Font = Ui.Body();

            // Segoe MDL2 Assets glyphs on the icon buttons.
            btnMenuMore.Text = "\uE712";
            btnMenuRestore.Text = "\uE777";

            // The primary action is deliberately inverted against the surface, so it stays the
            // highest-contrast thing on the page in BOTH palettes rather than a black-on-black block.
            btnBackup.Font = Ui.BodyBold();
            btnBackup.BackColor = Ui.TextPrimary;
            btnBackup.ForeColor = Ui.Surface;

            // The info pane opens on the greeting so the right column is informative before any
            // selection; AfterSelect replaces it with the chosen item's details.
            txtInfo.Text = string.Format(IntroTemplate, OsHelper.GetVersion());

            rtbLog.BackColor = Ui.Surface;
            logger.SetTarget(rtbLog);
        }

        private void InitializeConfigurations()
        {
            foreach (ModuleRegistration registration in ModuleCatalog.CreateAll())
            {
                AddConfiguration(registration.Module, registration.Category);
            }
        }

        private void AddConfiguration(BackupBase configuration, string parentNodeText)
        {
            TreeNode parentNode = FindOrCreateNode(parentNodeText);
            TreeNode childNode = new TreeNode(configuration.Title);
            childNode.Tag = configuration;
            parentNode.Nodes.Add(childNode);
        }

        private TreeNode FindOrCreateNode(string text)
        {
            TreeNode parentNode = treeConfigurations.Nodes.Cast<TreeNode>()
                .FirstOrDefault(node => node.Text == text);

            if (parentNode == null)
            {
                parentNode = new TreeNode(text);
                treeConfigurations.Nodes.Add(parentNode);
            }

            return parentNode;
        }

        /// <summary>
        /// Raised with true while a backup is running, and false when it ends.
        /// </summary>
        /// <remarks>
        /// This page disables ITSELF for the duration, which was sufficient when every control that
        /// could touch its state lived on it. The navigation rail does not: it stays live on the
        /// form while this control is disabled, so its buttons could navigate away mid-run. The shell
        /// listens here and shuts the rail for the duration.
        /// </remarks>
        internal Action<bool> RunStateChanged = _ => { };

        private async void btnBackup_Click(object sender, EventArgs e)
        {
            btnBackup.Enabled = false;
            this.Enabled = false;
            RunStateChanged(true);

            // The whole body is wrapped so the window is re-enabled in a finally. This is an async
            // void handler that disables the form on its first two lines: anything escaping it is
            // unhandled AND leaves the main window permanently dead, with no way back short of
            // killing the process.
            try
            {
                await RunBackup();
            }
            finally
            {
                this.Enabled = true;
                btnBackup.Enabled = true;
                RunStateChanged(false);
            }
        }

        private async Task RunBackup()
        {
            selectedConfigs.Clear();

            bool isAtLeastOneChecked = treeConfigurations.Nodes
                .Cast<TreeNode>()
                .Any(parentNode => parentNode.Nodes.Cast<TreeNode>().Any(childNode => childNode.Checked));

            // At least one node is checked, then proceed!
            if (!isAtLeastOneChecked)
            {
                // Input validation, not a result: this stays a MessageBox.
                MessageBox.Show("Nothing has been selected for backup. Please choose your settings to be backed up beforehand.", "", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            foreach (TreeNode parentNode in treeConfigurations.Nodes)
            {
                foreach (TreeNode childNode in parentNode.Nodes)
                {
                    if (childNode.Checked)
                    {
                        BackupBase configuration = childNode.Tag as BackupBase;
                        if (configuration != null)
                        {
                            selectedConfigs.Add(configuration);
                        }
                    }
                }
            }

            await runner.RunBackup(selectedConfigs, CurrentBackupPath);
        }

        // ---------------------------------------------------------------------------------------------
        //  IRunUi - the UI surface the orchestrator talks back through. Results render in-page via
        //  resultsPanel; the summary MessageBox is gone. (No restore role here: that lives in the
        //  wizard, which has its own IRunUi. The consent members remain because IRunUi is one contract.)
        // ---------------------------------------------------------------------------------------------

        void IRunUi.SetProgressText(string text) => linkSubHeader.Text = text;

        IWin32Window IRunUi.Owner => FindForm();

        void IRunUi.ShowSummary(RunSummary summary, string caption, IReadOnlyList<ModuleOutcome> outcomes)
        {
            // Keep the log record (it is no longer the primary surface, but it is still the audit
            // trail), then render in-page. No MessageBox: a 24-ok/1-failed run must not read as green.
            logger.LogMessage(summary.Headline);
            logger.LogMessage(summary.Detail);

            resultsPanel.ShowRun(summary, caption, outcomes);
        }

        IReadOnlyList<string> IRunUi.ShowConsentDialog(RestorePlan plan)
        {
            // Only reached on a backup-time error that composes a RestorePlan; backup itself never
            // consents. Kept identical to the wizard's impl so the contract is one shape.
            Form owner = FindForm();

            using (RestoreConfirmForm confirm = new RestoreConfirmForm(plan))
            {
                if (confirm.ShowDialog(owner) != DialogResult.OK)
                    return null;

                return confirm.ConsentedProcessNames;
            }
        }

        bool IRunUi.ConfirmSnapshotOverride(string text, string caption)
            => MessageBox.Show(FindForm(), text, caption, MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) == DialogResult.Yes;

        void IRunUi.ShowPlanCompositionError(string text, string caption)
            => MessageBox.Show(FindForm(), text, caption, MessageBoxButtons.OK, MessageBoxIcon.Error);

        void IRunUi.SetExplorerRestartVisible(bool visible) => resultsPanel.SetExplorerRestartVisible(visible);

        // The backup page's Restore button opens the wizard (step 1). No module-set gate: the wizard
        // picks what to restore FROM the chosen backup, which is the whole point of inverting the flow.
        private void btnRestore_Click(object sender, EventArgs e) => ShowRestoreView();

        /// <summary>
        /// Navigates to the restore wizard. Supplied by the shell, which owns that view.
        /// </summary>
        /// <remarks>
        /// A delegate rather than a NavigationService reference, matching how the engine's other
        /// UI seams are wired: this page needs exactly one navigation, and handing it the whole
        /// service would let any future edit here reach every screen in the app.
        /// </remarks>
        internal Action ShowRestoreView = () => { };

        /// <summary>
        /// Ticks exactly the modules named by their CLR type name, unticking everything else.
        /// </summary>
        /// <remarks>
        /// Drives Home's "Back up again" from the names in a backup_manifest.json. Unknown names are
        /// ignored in silence and that is deliberate, not lax: a manifest written by an older build
        /// can name a module this one retired - the browser modules Phase 3a removed, for instance -
        /// and a warning about it would be an error message about someone else's past decision, on a
        /// button whose entire promise is "the same as last time".
        ///
        /// Exact, ordinal type-name match. Titles are user-facing prose and have been reworded
        /// between releases; type names are what the manifest records for precisely this reason.
        /// </remarks>
        internal void SelectModulesByTypeName(IReadOnlyList<string> moduleTypeNames)
        {
            // Home's "Back up again" is a bespoke selection, so it lands on Custom with the tree open.
            TickByTypeNames(moduleTypeNames);
            rbCustom.Checked = true;
            ExpandTree(true);
        }

        private void TickByTypeNames(IReadOnlyList<string> moduleTypeNames)
        {
            HashSet<string> wanted = new HashSet<string>(StringComparer.Ordinal);

            if (moduleTypeNames != null)
            {
                foreach (string name in moduleTypeNames)
                {
                    if (!string.IsNullOrEmpty(name))
                        wanted.Add(name);
                }
            }

            foreach (TreeNode parentNode in treeConfigurations.Nodes)
            {
                foreach (TreeNode childNode in parentNode.Nodes)
                {
                    BackupBase configuration = childNode.Tag as BackupBase;

                    childNode.Checked = configuration != null
                        && wanted.Contains(configuration.GetType().Name);
                }
            }
        }

        private void SelectInstalled()
        {
            foreach (TreeNode parentNode in treeConfigurations.Nodes)
            {
                foreach (TreeNode childNode in parentNode.Nodes)
                {
                    BackupBase configuration = childNode.Tag as BackupBase;
                    if (configuration != null)
                    {
                        bool isConfigInstalled = configuration.IsInstalled();
                        childNode.Checked = isConfigInstalled;
                    }
                }
            }
        }

        private void SelectAll(bool flag)
        {
            foreach (TreeNode parentNode in treeConfigurations.Nodes)
            {
                foreach (TreeNode childNode in parentNode.Nodes)
                {
                    childNode.Checked = flag;
                }
            }
        }

        // The info pane replaces the rtbLog dual-use for browsing: selecting a node shows its title,
        // info, version and - inline, no modal - its warning. Nothing consent-relevant is lost: the
        // consent dialog still re-carries every warning via RestorePlan.
        private void treeConfigurations_AfterSelect(object sender, TreeViewEventArgs e)
        {
            if (treeConfigurations.SelectedNode != null &&
                treeConfigurations.SelectedNode.Tag is BackupBase selected)
            {
                string warning = selected.WarningMessage;

                txtInfo.Text = selected.Title + "\r\n\r\n" +
                               selected.Info + "\r\n" +
                               selected.Version +
                               (string.IsNullOrEmpty(warning) ? "" : "\r\n\r\n\u26A0 " + warning);
            }
        }

        private void btnMenuMore_Click(object sender, EventArgs e)
            => this.contextMenu.Show(Cursor.Position.X, Cursor.Position.Y);

        // Select-available IS the Everything preset.
        private void menuSelectInstalled_Click(object sender, EventArgs e)
            => rbEverything.Checked = true;

        private void menuSelectAll_Click(object sender, EventArgs e)
        {
            isSelectAll = !isSelectAll;
            SelectAll(isSelectAll);
        }

        private void treeConfigurations_AfterCheck(object sender, TreeViewEventArgs e)
        {
            foreach (TreeNode child in e.Node.Nodes)
            {
                child.Checked = e.Node.Checked;
            }

            // A tick the preset did not make is the user editing the selection, so it is Custom now.
            if (!applyingPreset)
                rbCustom.Checked = true;

            RefreshWarnings();
        }

        private void logToggle_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            rtbLog.Visible = !rtbLog.Visible;
        }

        private void menuOpenAppBackups_Click(object sender, EventArgs e)
        {
            RestAppsForm f = new RestAppsForm();
            f.ShowDialog();
        }

        private void menuOpenBackupFolder_Click(object sender, EventArgs e)
           => Process.Start(new ProcessStartInfo("explorer.exe", DataHelper.Data.DataRootDir) { UseShellExecute = true });

        // ---------------------------------------------------------------------------------------------
        //  Presets - curated named selections driving the tree. "Everything on this PC" is today's
        //  Select-available; the others are module-name lists in BackupPresets. Any manual tick change
        //  flips the radio to Custom; the tree lives behind an "Advanced" toggle that Custom opens.
        // ---------------------------------------------------------------------------------------------

        private void rbPreset_CheckedChanged(object sender, EventArgs e)
        {
            if (!(sender is RadioButton rb) || !rb.Checked)
                return;

            if (rb == rbEverything)
                ApplyPreset(() => { SelectAll(false); SelectInstalled(); }, expandTree: false);
            else if (rb == rbDeveloper)
                ApplyPreset(() => TickByTypeNames(BackupPresets.DeveloperMachine), expandTree: false);
            else if (rb == rbMinimal)
                ApplyPreset(() =>
                {
                    SelectAll(false);
                    SelectInstalled();
                    UntickByTypeName(BackupPresets.MinimalPrivacySafeExclusions);
                }, expandTree: false);
            else // rbCustom - no automatic selection, just reveal the tree for hand-editing.
                ExpandTree(true);
        }

        private void ApplyPreset(Action apply, bool expandTree)
        {
            applyingPreset = true;
            try
            {
                apply();
            }
            finally
            {
                applyingPreset = false;
            }

            ExpandTree(expandTree);
            RefreshWarnings();
        }

        private void UntickByTypeName(IReadOnlyList<string> names)
        {
            HashSet<string> exclude = new HashSet<string>(StringComparer.Ordinal);

            if (names != null)
            {
                foreach (string name in names)
                {
                    if (!string.IsNullOrEmpty(name))
                        exclude.Add(name);
                }
            }

            foreach (TreeNode parentNode in treeConfigurations.Nodes)
            {
                foreach (TreeNode childNode in parentNode.Nodes)
                {
                    BackupBase configuration = childNode.Tag as BackupBase;
                    if (configuration != null && exclude.Contains(configuration.GetType().Name))
                        childNode.Checked = false;
                }
            }
        }

        // "N items in this selection carry warnings - shown on the item"; hidden when zero.
        private void RefreshWarnings()
        {
            int count = 0;

            foreach (TreeNode parentNode in treeConfigurations.Nodes)
            {
                foreach (TreeNode childNode in parentNode.Nodes)
                {
                    if (childNode.Checked)
                    {
                        BackupBase configuration = childNode.Tag as BackupBase;
                        if (configuration != null && !string.IsNullOrEmpty(configuration.WarningMessage))
                            count++;
                    }
                }
            }

            if (count == 0)
            {
                lblWarnings.Visible = false;
            }
            else
            {
                lblWarnings.Visible = true;
                lblWarnings.Text = count + " item(s) in this selection carry warnings - shown on the item.";
            }
        }

        private void ExpandTree(bool expand)
        {
            treeExpanded = expand;
            treeConfigurations.Visible = expand;
            lnkAdvanced.Text = (expand ? "\u25BE " : "\u25B8 ") + "Advanced: full module list";
        }

        private void lnkAdvanced_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
            => ExpandTree(!treeExpanded);

        private int CountInstalled()
        {
            int count = 0;

            foreach (TreeNode parentNode in treeConfigurations.Nodes)
            {
                foreach (TreeNode childNode in parentNode.Nodes)
                {
                    BackupBase configuration = childNode.Tag as BackupBase;
                    if (configuration != null && configuration.IsInstalled())
                        count++;
                }
            }

            return count;
        }

        /// <summary>
        /// Collapses (or expands) a row of a <see cref="TableLayoutPanel"/> by switching its row
        /// style, so a hidden results panel, activity log or full module tree does not reserve space.
        /// </summary>
        private static void SetRowCollapsed(TableLayoutPanel tlp, int row, bool collapsed)
        {
            tlp.RowStyles[row].SizeType = collapsed ? SizeType.Absolute : SizeType.AutoSize;
            if (collapsed)
                tlp.RowStyles[row].Height = 0;
        }
    }
}
