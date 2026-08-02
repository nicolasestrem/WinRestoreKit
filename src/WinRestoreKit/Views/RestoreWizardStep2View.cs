using WinRestoreKit;
using Conf;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Views
{
    /// <summary>
    /// Restore wizard step 2: contents &amp; portability. One row per module showing whether the
    /// backup holds it and what the manifest recorded, then the consent dialog and the in-page result.
    /// </summary>
    /// <remarks>
    /// Implements <see cref="IRunUi"/> exactly as the backup page does - same modal consent, same
    /// default-No snapshot override, results into its own <see cref="RunResultsPanel"/> - and owns a
    /// <see cref="BackupRestoreOrchestrator"/> so a restore runs here rather than back on the backup
    /// page. Default ticks every module the folder actually holds (restore-what's-in-it).
    /// </remarks>
    internal sealed partial class RestoreWizardStep2View : UserControl, IRunUi
    {
        private readonly NavigationService navigation;
        private readonly Action<bool> runStateChanged;
        private readonly BackupRestoreOrchestrator runner;

        private readonly Label headerLabel;
        private readonly Label provenanceBanner;
        private readonly FlowLayoutPanel rows;
        private readonly Label progressLabel;
        private readonly FlowLayoutPanel actionRow;
        private readonly Button btnBack;
        private readonly Button btnNext;
        private readonly RunResultsPanel resultsPanel;

        private BackupFolder folder;
        /// <summary>
        /// One step-2 row: its checkbox, its module, and whether the folder actually holds anything
        /// for it.
        /// </summary>
        /// <remarks>
        /// <see cref="Eligible"/> is tracked here rather than read back off <c>CheckBox.Enabled</c>
        /// because those are two different facts that only happened to coincide. Eligibility is a
        /// statement about the BACKUP; Enabled is a statement about the control, and the control's
        /// state is also driven by the run - the whole page is disabled while a restore is in
        /// flight. Deriving one from the other means any future change to how an inert row is
        /// painted silently changes which modules get restored.
        /// </remarks>
        private sealed class RowCheck
        {
            internal CheckBox Box { get; }
            internal BackupBase Module { get; }
            internal bool Eligible { get; }

            internal RowCheck(CheckBox box, BackupBase module, bool eligible)
            {
                Box = box;
                Module = module;
                Eligible = eligible;
            }
        }

        private readonly List<RowCheck> rowChecks = new List<RowCheck>();

        public RestoreWizardStep2View(NavigationService navigation, Action<bool> runStateChanged)
        {
            this.navigation = navigation;
            this.runStateChanged = runStateChanged;
            runner = new BackupRestoreOrchestrator(this);

            BackColor = Ui.Surface;
            AutoScroll = true;

            headerLabel = new Label
            {
                AutoSize = true,
                Dock = DockStyle.Top,
                Font = Ui.Title(),
                ForeColor = Ui.TextPrimary,
                Margin = new Padding(Ui.SpaceM, Ui.SpaceM, Ui.SpaceM, Ui.SpaceXs),
                Text = "Restore contents",
            };

            provenanceBanner = new Label
            {
                AutoSize = true,
                Dock = DockStyle.Top,
                Font = Ui.BodyBold(),
                ForeColor = Ui.Caution,
                Margin = new Padding(Ui.SpaceM, 0, Ui.SpaceM, Ui.SpaceS),
                Visible = false,
            };

            progressLabel = new Label
            {
                AutoSize = true,
                Dock = DockStyle.Top,
                Font = Ui.Body(),
                ForeColor = Ui.Muted,
                Margin = new Padding(Ui.SpaceM, 0, Ui.SpaceM, Ui.SpaceS),
                Text = "Choose what to restore",
            };

            rows = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true,
                Dock = DockStyle.Fill,
                Padding = new Padding(Ui.SpaceM),
            };

            btnBack = new Button { AutoSize = true, Font = Ui.Body(), Text = "\u2190 Back", UseVisualStyleBackColor = true };
            btnBack.Click += (s, e) => navigation.Pop();

            btnNext = new Button
            {
                AutoSize = true,
                Enabled = false,
                Font = Ui.BodyBold(),
                Text = "Next",
                UseVisualStyleBackColor = true,
            };
            btnNext.Click += btnNext_Click;

            // FlowLayoutPanel, not Panel. A plain Panel lays nothing out, so both buttons kept their
            // default (0,0) and Next - added first, so topmost in z-order - covered Back completely,
            // leaving no way back to the picker except the rail. Back is added first here so it
            // reads left-to-right.
            actionRow = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Top,
                FlowDirection = FlowDirection.LeftToRight,
                Padding = new Padding(Ui.SpaceM),
                WrapContents = false,
            };
            actionRow.Controls.Add(btnBack);
            actionRow.Controls.Add(btnNext);

            resultsPanel = new RunResultsPanel { Dock = DockStyle.Top };

            Controls.Add(rows);
            Controls.Add(actionRow);
            Controls.Add(progressLabel);
            Controls.Add(provenanceBanner);
            Controls.Add(headerLabel);
            // resultsPanel is shown on top of the stack once a run reports (docked top, hidden until then).
            Controls.Add(resultsPanel);
        }

        internal void LoadFolder(BackupFolder folder)
        {
            this.folder = folder;
            rows.Controls.Clear();
            rowChecks.Clear();
            resultsPanel.Clear();
            resultsPanel.Visible = false;
            btnNext.Enabled = false;

            headerLabel.Text = "Restore from " + folder.Name;

            string provenance = RestoreContents.DescribeProvenance(
                folder.ReadManifest(), Environment.MachineName, Environment.UserName);
            provenanceBanner.Visible = provenance != null;
            provenanceBanner.Text = provenance ?? "";

            IReadOnlyList<BackupBase> modules = ModuleCatalog.CreateAll().Select(r => r.Module).ToList();
            IReadOnlyList<RestoreContentsRow> contents = RestoreContents.For(modules, folder.Path, folder.ReadManifest());

            foreach (RestoreContentsRow row in contents)
                rows.Controls.Add(MakeRow(row));

            RefreshNextEnabled();

            // These rows did not exist when MainForm themed this page, and WinForms defaults are
            // LIGHT - a TextBox nobody assigns a BackColor to is white. That is why the inline
            // warning rendered as a white slab in dark mode. Anything built after startup has to be
            // re-walked; the walker steps over the chips, so their colours survive.
            Theme.Apply(this);
        }

        private Control MakeRow(RestoreContentsRow row)
        {
            // The glyph only - the text lives on the Label beside it. A DISABLED CheckBox paints its
            // own Text with the system grey and ignores ForeColor entirely, so the muted colour set
            // here in the first version did nothing: every "(nothing in this backup)" row rendered at
            // about 3:1 against the dark surface, which is where "can't read the greyed rows" came
            // from. A Label is not disabled, so its colour is ours and lands at 7:1.
            // Splitting glyph from words costs the checkbox its accessible name - a WinForms Label
            // is NOT automatically associated with the control beside it, so a screen reader would
            // announce "checked" with nothing to say WHAT is checked. Named explicitly instead, with
            // the same words the label shows.
            string caption = row.HasBackup
                ? row.Module.Title
                : row.Module.Title + " (nothing in this backup)";

            CheckBox check = new CheckBox
            {
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                AccessibleName = caption,
                AccessibleRole = AccessibleRole.CheckButton,
                Checked = row.HasBackup,
                Enabled = row.HasBackup,
                Margin = new Padding(0, 0, Ui.SpaceXs, 0),
                Tag = row.Module,
                Text = "",
            };
            check.CheckedChanged += (s, e) => RefreshNextEnabled();

            Label title = new Label
            {
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Font = Ui.Body(),
                ForeColor = row.HasBackup ? Ui.TextPrimary : Ui.Muted,
                Margin = new Padding(0, 3, Ui.SpaceS, 0),
                // Not announced: the checkbox beside it already carries these words as its
                // accessible name, so exposing them twice would make Narrator read every row twice.
                AccessibleRole = AccessibleRole.StaticText,
                Text = row.HasBackup
                    ? row.Module.Title
                    : row.Module.Title + "   (nothing in this backup)",
            };

            if (row.HasBackup)
            {
                // The label now carries the words, so it has to carry the click that used to land on
                // the checkbox's own text.
                title.Cursor = Cursors.Hand;
                title.Click += (s, e) => check.Checked = !check.Checked;
            }

            rowChecks.Add(new RowCheck(check, row.Module, row.HasBackup));

            Control chip = MakeStateChip(row.ManifestState);

            TableLayoutPanel content = new TableLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = chip == null ? 2 : 3,
                RowCount = 1,
                Dock = DockStyle.Fill,
                Margin = new Padding(0),
            };
            content.Controls.Add(check, 0, 0);
            content.Controls.Add(title, 1, 0);

            // ColumnStyles are positional - index 0 is the first column, not the first Add. Order
            // here is checkbox, title, chip, and it must be added in exactly that order or the title
            // takes the chip's fixed 120px and the chip takes the stretch.
            content.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));   // 0 - checkbox glyph
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F)); // 1 - title

            if (chip != null)
            {
                content.Controls.Add(chip, 2, 0);
                content.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120F)); // 2 - chip
            }

            // Only for rows that can actually run. A warning describes what restoring this WOULD do,
            // which is noise under a row the folder holds nothing for - and expensive noise: before
            // the presence check was fixed nothing was ever greyed, so this never showed, and now
            // twelve inert warnings would push the one restorable row off the bottom of the screen.
            if (row.HasBackup && !string.IsNullOrEmpty(row.Warning))
            {
                // Height MEASURED, not left to default. A multiline TextBox does not auto-size to
                // its content, so the default height showed roughly one and a half lines and cut
                // the rest off mid-word - visible in every screenshot once the text was legible
                // enough to read. Same approach RunResultsPanel uses for failure reasons.
                const int WarningWidth = 620;
                string warningText = "\u26A0 " + row.Warning;
                Font warningFont = Ui.Body();
                Size measured = TextRenderer.MeasureText(
                    warningText, warningFont,
                    new Size(WarningWidth, int.MaxValue),
                    TextFormatFlags.WordBreak);

                TextBox warning = new TextBox
                {
                    BorderStyle = BorderStyle.None,
                    Font = warningFont,
                    ForeColor = Ui.Caution,
                    Margin = new Padding(24, Ui.SpaceXs, 0, 0),
                    Multiline = true,
                    ReadOnly = true,
                    ScrollBars = ScrollBars.None,
                    Text = warningText,
                    Width = WarningWidth,
                    Height = measured.Height + Ui.SpaceXs,
                    WordWrap = true,
                };
                TableLayoutPanel wrap = new TableLayoutPanel
                {
                    AutoSize = true,
                    AutoSizeMode = AutoSizeMode.GrowAndShrink,
                    ColumnCount = 1,
                    RowCount = 2,
                    Dock = DockStyle.Top,
                    Margin = new Padding(0, 0, 0, Ui.SpaceS),
                };
                wrap.Controls.Add(content, 0, 0);
                wrap.Controls.Add(warning, 0, 1);
                wrap.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
                wrap.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                wrap.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                return wrap;
            }

            content.Margin = new Padding(0, 0, 0, Ui.SpaceS);
            return content;
        }

        private static Control MakeStateChip(string state)
        {
            Color back;
            Color fore;
            string text;

            switch (state)
            {
                case BackupManifest.StateSucceeded:
                    back = Ui.ChipSucceededBack;
                    fore = Ui.ChipSucceededFore;
                    text = "OK in backup";
                    break;
                case BackupManifest.StateFailed:
                    back = Ui.ChipFailedBack;
                    fore = Ui.ChipFailedFore;
                    text = "failed in backup";
                    break;
                case BackupManifest.StateSkipped:
                    // Amber, never green.
                    back = Ui.ChipSkippedBack;
                    fore = Ui.ChipSkippedFore;
                    text = "skipped";
                    break;
                default:
                    // Unknown (no manifest, retired type, "unknown" state) shows no chip.
                    return null;
            }

            // AccentLabel so the theme walker steps over the chip instead of flattening it.
            return new AccentLabel
            {
                AutoSize = false,
                BackColor = back,
                BorderStyle = BorderStyle.None,
                Font = Ui.BodyBold(),
                ForeColor = fore,
                Margin = new Padding(0, 2, 0, 2),
                Size = new Size(112, 22),
                Text = text,
                TextAlign = ContentAlignment.MiddleCenter,
            };
        }

        private void RefreshNextEnabled()
        {
            foreach (RowCheck row in rowChecks)
            {
                if (row.Eligible && row.Box.Checked)
                {
                    btnNext.Enabled = true;
                    return;
                }
            }

            btnNext.Enabled = false;
        }

        /// <summary>
        /// The modules to restore: ticked AND backed by something in this folder.
        /// </summary>
        /// <remarks>
        /// The eligibility half is not redundant. This used to test <c>Checked</c> alone and was
        /// correct only by accident - an ineligible box was disabled, so nothing could tick it. That
        /// made the guarantee a property of how the row is PAINTED, one restyle away from letting a
        /// module the backup has nothing for into a real restore. It is now a property of the row.
        /// </remarks>
        private IReadOnlyList<BackupBase> SelectedModules()
        {
            List<BackupBase> selected = new List<BackupBase>();

            foreach (RowCheck row in rowChecks)
            {
                if (row.Eligible && row.Box.Checked)
                    selected.Add(row.Module);
            }

            return selected;
        }

        private async void btnNext_Click(object sender, EventArgs e)
        {
            // Deleted between step 1 and Next: fail back to the picker rather than into a restore.
            if (folder == null || !Directory.Exists(folder.Path))
            {
                MessageBox.Show(this, "This backup folder no longer exists.", "Restore",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                navigation.Pop();
                return;
            }

            IReadOnlyList<BackupBase> selection = SelectedModules();
            if (selection.Count == 0)
                return;

            btnNext.Enabled = false;
            Enabled = false;
            runStateChanged?.Invoke(true);

            try
            {
                await runner.RunRestore(selection, folder.Path + "\\");
            }
            finally
            {
                Enabled = true;
                // Next has to come back too - the backup page already does this for its own button.
                // Cancelling the consent dialog returns here normally, and without this the user
                // lands back on their still-valid ticked list with the only way forward greyed out,
                // recoverable only by leaving the wizard and re-picking the folder. Via
                // RefreshNextEnabled rather than a bare true so an empty selection stays disabled.
                RefreshNextEnabled();
                runStateChanged?.Invoke(false);
            }
        }

        // ---------------------------------------------------------------------------------------------
        //  IRunUi - identical contract to the backup page: consent stays modal and Cancel-focused,
        //  the snapshot override defaults to No, results render in-page.
        // ---------------------------------------------------------------------------------------------

        void IRunUi.SetProgressText(string text) => progressLabel.Text = text;

        IWin32Window IRunUi.Owner => FindForm();

        void IRunUi.ShowSummary(RunSummary summary, string caption, IReadOnlyList<ModuleOutcome> outcomes)
        {
            LogHelper.Instance.LogMessage(summary.Headline);
            LogHelper.Instance.LogMessage(summary.Detail);
            resultsPanel.ShowRun(summary, caption, outcomes);
        }

        IReadOnlyList<string> IRunUi.ShowConsentDialog(RestorePlan plan)
        {
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
    }
}
