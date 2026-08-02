using WinRestoreKit;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Views
{
    /// <summary>
    /// The in-page results surface: a run's headline plus one row per module, replacing the old
    /// summary MessageBox. Failed rows pin first; every reason is a selectable TextBox, never a
    /// Label, so a user can copy failure text straight into an issue.
    /// </summary>
    /// <remarks>
    /// Built with TableLayoutPanel/Dock/AutoSize only - no absolute positioning - so PR 9's
    /// PerMonitorV2 DPI flip rescales it cleanly. Colours are inline for now; PR 9's Theme pass
    /// replaces them with tokens (Skipped is amber and never green by construction here too).
    /// </remarks>
    internal sealed partial class RunResultsPanel : UserControl
    {
        private const int ChipWidth = 88;

        private readonly TableLayoutPanel stack;
        private readonly Label headlineLabel;
        private readonly Label detailLabel;
        private readonly TableLayoutPanel outcomesFlow;
        private readonly TableLayoutPanel explorerRow;
        private readonly Button explorerButton;

        // Remembered so a resize re-wraps the reason boxes to the new width.
        private RunSummary pendingSummary;
        private IReadOnlyList<ModuleOutcome> pendingOutcomes;
        private bool explorerRestartNeeded;

        public RunResultsPanel()
        {
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            BackColor = Ui.Surface;
            Padding = new Padding(Ui.SpaceM);
            Visible = false;

            headlineLabel = new Label
            {
                AutoSize = true,
                Font = Ui.Heading(),
                ForeColor = Ui.TextPrimary,
                Margin = new Padding(0, 0, 0, Ui.SpaceXs),
            };

            detailLabel = new Label
            {
                AutoSize = true,
                Font = Ui.Body(),
                ForeColor = Ui.Muted,
                Margin = new Padding(0, 0, 0, Ui.SpaceM),
            };

            outcomesFlow = new TableLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                RowCount = 0,
                Dock = DockStyle.Top,
                Margin = new Padding(0),
            };
            outcomesFlow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

            explorerButton = new Button
            {
                AutoSize = true,
                Text = "Restart File Explorer",
                UseVisualStyleBackColor = true,
                Margin = new Padding(0, Ui.SpaceS, 0, 0),
            };
            explorerButton.Click += explorerButton_Click;

            // AccentPanel: the highlighted row keeps its caution tint through a theme change rather
            // than being flattened to the surface colour by the walker.
            explorerRow = new AccentPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                RowCount = 2,
                Dock = DockStyle.Top,
                Visible = false,
                Margin = new Padding(0, Ui.SpaceM, 0, 0),
                Padding = new Padding(Ui.SpaceS),
                BackColor = Ui.CardSurface,
            };
            explorerRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            Label explorerPrompt = new Label
            {
                AutoSize = true,
                Dock = DockStyle.Top,
                Font = Ui.BodyBold(),
                ForeColor = Ui.Caution,
                Text = "To apply some changes, the explorer.exe needs to be restarted. Click here to finish.",
            };
            explorerRow.Controls.Add(explorerPrompt, 0, 0);
            explorerRow.Controls.Add(explorerButton, 0, 1);
            explorerRow.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            explorerRow.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            // Top-down stack: headline, detail, outcomes, explorer. Hidden items collapse.
            stack = new TableLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                RowCount = 4,
                Dock = DockStyle.Top,
                Margin = new Padding(0),
            };
            stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            stack.Controls.Add(headlineLabel, 0, 0);
            stack.Controls.Add(detailLabel, 0, 1);
            stack.Controls.Add(outcomesFlow, 0, 2);
            stack.Controls.Add(explorerRow, 0, 3);
            stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            Controls.Add(stack);
        }

        /// <summary>Renders a run. Empty outcomes show the header only. Makes the panel visible.</summary>
        internal void ShowRun(RunSummary summary, string caption, IReadOnlyList<ModuleOutcome> outcomes)
        {
            pendingSummary = summary;
            pendingOutcomes = outcomes ?? Array.Empty<ModuleOutcome>();
            Render();
            Visible = true;
        }

        internal void SetExplorerRestartVisible(bool visible)
        {
            explorerRestartNeeded = visible;
            explorerRow.Visible = visible;
        }

        internal void Clear()
        {
            pendingSummary = null;
            pendingOutcomes = null;
            explorerRestartNeeded = false;
            headlineLabel.Text = "";
            detailLabel.Text = "";
            outcomesFlow.Controls.Clear();
            outcomesFlow.RowCount = 0;
            explorerRow.Visible = false;
            Visible = false;
        }

        private void Render()
        {
            headlineLabel.Text = pendingSummary?.Headline ?? "";
            detailLabel.Text = pendingSummary?.Detail ?? "";

            outcomesFlow.Controls.Clear();
            outcomesFlow.RowCount = 0;
            outcomesFlow.RowStyles.Clear();

            if (pendingOutcomes == null || pendingOutcomes.Count == 0)
            {
                explorerRow.Visible = explorerRestartNeeded;
                return;
            }

            int contentWidth = ReasonContentWidth();

            // Failed first, then the original order; stable partition preserves each run's sequence.
            IEnumerable<ModuleOutcome> ordered = pendingOutcomes
                .Where(o => o != null && o.Result != null && o.Result.State == ResultState.Failed)
                .Concat(pendingOutcomes.Where(o => o != null && o.Result != null && o.Result.State != ResultState.Failed));

            foreach (ModuleOutcome outcome in ordered)
            {
                Control row = CreateOutcomeRow(outcome, contentWidth);
                int index = outcomesFlow.RowCount;
                outcomesFlow.RowCount++;
                outcomesFlow.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                outcomesFlow.Controls.Add(row, 0, index);
            }

            explorerRow.Visible = explorerRestartNeeded;

            // Result rows are built per run, long after MainForm themed this control, so without a
            // re-walk the reason text boxes come up on WinForms' default white. The walker steps
            // over AccentLabel/AccentPanel, so the state chips and the caution row keep their own
            // colours. See the note in RestoreWizardStep2View.LoadFolder.
            Theme.Apply(this);
        }

        private int ReasonContentWidth()
        {
            int width = outcomesFlow.ClientSize.Width - ChipWidth - Ui.SpaceS;
            return Math.Max(120, width);
        }

        private Control CreateOutcomeRow(ModuleOutcome outcome, int contentWidth)
        {
            Label chip = MakeChip(outcome.Result.State);

            Label title = new Label
            {
                AutoSize = true,
                Dock = DockStyle.Top,
                Font = Ui.BodyBold(),
                ForeColor = Ui.TextPrimary,
                Text = outcome.Title,
                Margin = new Padding(0),
            };

            TextBox reason = new TextBox
            {
                ReadOnly = true,
                Multiline = true,
                WordWrap = true,
                BorderStyle = BorderStyle.None,
                BackColor = Ui.Surface,
                ForeColor = Ui.Muted,
                Font = Ui.Body(),
                Text = outcome.Result.Reason ?? "",
                Dock = DockStyle.Top,
                TabStop = false,
                Margin = new Padding(0, Ui.SpaceXs, 0, 0),
            };
            // TextBox does not auto-size its multiline height; measure it against the content width.
            Size measured = TextRenderer.MeasureText(reason.Text, reason.Font,
                new Size(contentWidth, int.MaxValue),
                TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);
            reason.Width = contentWidth;
            reason.Height = measured.Height + 6;

            TableLayoutPanel content = new TableLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                RowCount = 2,
                Dock = DockStyle.Fill,
                Margin = new Padding(0),
            };
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            content.Controls.Add(title, 0, 0);
            content.Controls.Add(reason, 0, 1);
            content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, reason.Height));

            TableLayoutPanel row = new TableLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                RowCount = 1,
                Dock = DockStyle.Top,
                Margin = new Padding(0, 0, 0, Ui.SpaceS),
            };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ChipWidth));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            row.Controls.Add(chip, 0, 0);
            row.Controls.Add(content, 1, 0);
            row.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            return row;
        }

        private static Label MakeChip(ResultState state)
        {
            Color back;
            Color fore;
            string text;
            switch (state)
            {
                case ResultState.Succeeded:
                    back = Ui.ChipSucceededBack;
                    fore = Ui.ChipSucceededFore;
                    text = "Done";
                    break;
                case ResultState.Skipped:
                    // Amber, never green - keeps the distinction the three-state result fought for.
                    back = Ui.ChipSkippedBack;
                    fore = Ui.ChipSkippedFore;
                    text = "Skipped";
                    break;
                case ResultState.Failed:
                    back = Ui.ChipFailedBack;
                    fore = Ui.ChipFailedFore;
                    text = "Failed";
                    break;
                default:
                    back = Ui.Muted;
                    fore = Ui.ChipSucceededFore;
                    text = "—";
                    break;
            }

            // AccentLabel: paints itself, so the theme walker steps over it rather than flattening
            // the chip to a transparent label.
            return new AccentLabel
            {
                AutoSize = false,
                BackColor = back,
                BorderStyle = BorderStyle.None,
                Font = Ui.BodyBold(),
                ForeColor = fore,
                Margin = new Padding(0, 2, Ui.SpaceS, 2),
                Size = new Size(ChipWidth - Ui.SpaceS, 22),
                Text = text,
                TextAlign = ContentAlignment.MiddleCenter,
            };
        }

        // A restart hides the row only when a shell actually came back: hiding unconditionally removed
        // the user's only way to retry precisely when the retry was needed. Body moved verbatim from
        // ConfPageView.btnRestartExplorer_Click; the row plays the role the button used to.
        private async void explorerButton_Click(object sender, EventArgs e)
        {
            explorerButton.Enabled = false;

            try
            {
                ExplorerRestartResult outcome = await Task.Run(() => Utils.RestartExplorer());

                if (outcome.Shell == ShellOutcome.Restarted || outcome.Shell == ShellOutcome.RestartedByWindows)
                    explorerRow.Visible = false;
                else
                    MessageBox.Show(this, outcome.Describe(), "Restart File Explorer",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                explorerButton.Enabled = true;
            }
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);

            // First show: the host has now assigned a width, so wrap the reasons to it.
            if (Visible && pendingOutcomes != null && pendingOutcomes.Count > 0)
                Render();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);

            if (Visible && pendingOutcomes != null && pendingOutcomes.Count > 0)
                Render();
        }
    }
}
