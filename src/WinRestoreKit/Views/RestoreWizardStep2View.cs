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
    /// <summary>Restore wizard step 2: select eligible components, then open the safe confirmation modal.</summary>
    internal sealed partial class RestoreWizardStep2View : UserControl
    {
        private readonly NavigationService navigation;
        private readonly FlowLayoutPanel rows;
        private readonly Label sourceName;
        private readonly Label sourceDetail;
        private readonly Label warningText;
        private readonly Label selectionLabel;
        private readonly Button btnBack;
        private readonly Button btnNext;
        private readonly List<RowCheck> rowChecks = new List<RowCheck>();

        private BackupFolder folder;

        internal Action<IReadOnlyList<BackupBase>, BackupFolder> StartRestoreRequested { get; set; }
        private sealed class RowCheck
        {
            internal CustomCheckbox Box { get; }
            internal BackupBase Module { get; }
            internal bool Eligible { get; }

            internal RowCheck(CustomCheckbox box, BackupBase module, bool eligible)
            {
                Box = box;
                Module = module;
                Eligible = eligible;
            }
        }

        public RestoreWizardStep2View(NavigationService navigation)
        {
            this.navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
            AutoScroll = true;
            BackColor = Theme.Current.Bg;

            Label stepIndicator = new Label
            {
                AutoSize = true,
                Font = Ui.Kicker(),
                ForeColor = Theme.Current.Accent700,
                Text = "1. SNAPSHOT   ->   2. COMPONENTS   ->   3. CONFIRM",
            };

            Label kicker = new Label
            {
                AutoSize = true,
                Font = Ui.Kicker(),
                ForeColor = Theme.Current.Accent700,
                Margin = new Padding(0, Ui.SpaceM, 0, Ui.SpaceXs),
                Text = "RESTORE",
            };

            Label title = new Label
            {
                AutoSize = true,
                Font = Ui.Heading(),
                ForeColor = Theme.Current.Text,
                Margin = new Padding(0, 0, 0, Ui.SpaceM),
                Text = "PICK WHAT COMES BACK",
            };

            sourceName = new Label
            {
                AutoSize = true,
                Font = Ui.Heading2(),
                ForeColor = Theme.Current.Text,
            };
            sourceDetail = new Label
            {
                AutoSize = true,
                Font = Ui.MonoSmall(),
                ForeColor = Theme.Current.TextMuted,
                Margin = new Padding(0, Ui.SpaceXs, 0, 0),
            };

            BlueprintFrame sourceFrame = new BlueprintFrame
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Top,
                Margin = new Padding(0, 0, Ui.SpaceM, Ui.SpaceM),
            };
            Label sourceKicker = new Label
            {
                AutoSize = true,
                Font = Ui.Kicker(),
                ForeColor = Theme.Current.Accent700,
                Margin = new Padding(0, 0, 0, Ui.SpaceS),
                Text = "SOURCE SNAPSHOT",
            };
            FlowLayoutPanel sourceContent = CreateVerticalFlow();
            sourceContent.Controls.Add(sourceKicker);
            sourceContent.Controls.Add(sourceName);
            sourceContent.Controls.Add(sourceDetail);
            sourceFrame.Controls.Add(sourceContent);

            warningText = new AccentLabel
            {
                AutoSize = true,
                Font = Ui.Body(),
                ForeColor = Theme.Current.Accent2_700,
                MaximumSize = new Size(360, 0),
            };
            AccentPanel warningPanel = new AccentPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = Theme.Current.Accent2_100,
                Dock = DockStyle.Top,
                Margin = new Padding(0, 0, Ui.SpaceM, 0),
                Padding = new Padding(Ui.SpaceM),
            };
            AccentLabel cautionGlyph = new AccentLabel
            {
                AutoSize = true,
                Font = Ui.Heading2(),
                ForeColor = Theme.Current.Accent2_700,
                Text = "\u26A0",
            };
            TableLayoutPanel warningContent = new TableLayoutPanel
            {
                AutoSize = true,
                ColumnCount = 2,
                Dock = DockStyle.Top,
            };
            warningContent.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            warningContent.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            warningContent.Controls.Add(cautionGlyph, 0, 0);
            warningContent.Controls.Add(warningText, 1, 0);
            warningPanel.Controls.Add(warningContent);

            FlowLayoutPanel leftColumn = CreateVerticalFlow();
            leftColumn.Dock = DockStyle.Fill;
            leftColumn.Controls.Add(sourceFrame);
            leftColumn.Controls.Add(warningPanel);

            rows = CreateVerticalFlow();
            rows.AutoScroll = true;
            rows.Dock = DockStyle.Fill;
            rows.Padding = new Padding(0, 0, Ui.SpaceXs, 0);

            BlueprintFrame componentsFrame = new BlueprintFrame
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(Ui.SpaceM, 0, 0, 0),
            };
            Label componentsKicker = new Label
            {
                AutoSize = true,
                Dock = DockStyle.Top,
                Font = Ui.Kicker(),
                ForeColor = Theme.Current.Accent700,
                Margin = new Padding(0, 0, 0, Ui.SpaceS),
                Text = "COMPONENTS",
            };
            componentsFrame.Controls.Add(rows);
            componentsFrame.Controls.Add(componentsKicker);

            TableLayoutPanel body = new TableLayoutPanel
            {
                ColumnCount = 2,
                Dock = DockStyle.Fill,
                Margin = new Padding(0),
            };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40F));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60F));
            body.Controls.Add(leftColumn, 0, 0);
            body.Controls.Add(componentsFrame, 1, 0);

            btnBack = CreateSecondaryButton("BACK");
            btnBack.Click += (s, e) => navigation.Pop();
            btnNext = CreatePrimaryButton("CONTINUE");
            btnNext.Enabled = false;
            btnNext.Click += btnNext_Click;

            selectionLabel = new Label
            {
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Font = Ui.MonoSmall(),
                ForeColor = Theme.Current.TextMuted,
                Text = "0 components selected",
            };

            TableLayoutPanel footer = new TableLayoutPanel
            {
                AutoSize = true,
                ColumnCount = 3,
                Dock = DockStyle.Top,
                Margin = new Padding(0, Ui.SpaceM, 0, 0),
                Padding = new Padding(0, Ui.SpaceS, 0, 0),
            };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            footer.Controls.Add(btnBack, 0, 0);
            footer.Controls.Add(selectionLabel, 1, 0);
            footer.Controls.Add(btnNext, 2, 0);


            TableLayoutPanel layout = new TableLayoutPanel
            {
                ColumnCount = 1,
                Dock = DockStyle.Fill,
                Padding = new Padding(30, 26, 30, 26),
                RowCount = 5,
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(stepIndicator, 0, 0);
            layout.Controls.Add(kicker, 0, 1);
            layout.Controls.Add(title, 0, 2);
            layout.Controls.Add(body, 0, 3);
            layout.Controls.Add(footer, 0, 4);
            Controls.Add(layout);
        }

        internal void LoadFolder(BackupFolder folder)
        {
            this.folder = folder;
            rows.Controls.Clear();
            rowChecks.Clear();

            sourceName.Text = folder.DisplayName;
            sourceDetail.Text = folder.Created == DateTime.MinValue
                ? "DATE UNKNOWN"
                : folder.Created.ToString("yyyy-MM-dd HH.mm");

            string provenance = RestoreContents.DescribeProvenance(
                folder.ReadManifest(), Environment.MachineName, Environment.UserName);
            warningText.Text = provenance ?? "Restoring replaces the selected settings with values saved in this snapshot. Continue only if this is the source you intend to use.";

            IReadOnlyList<BackupBase> modules = ModuleCatalog.CreateAll().Select(registration => registration.Module).ToList();
            IReadOnlyList<RestoreContentsRow> contents = RestoreContents.For(modules, folder.Path, folder.ReadManifest());
            foreach (RestoreContentsRow row in contents)
                rows.Controls.Add(MakeRow(row));

            RefreshSelectionState();
            Theme.Apply(this);
        }

        private Control MakeRow(RestoreContentsRow row)
        {
            string caption = row.HasBackup
                ? row.Module.Title
                : row.Module.Title + " (nothing in this snapshot)";

            CustomCheckbox checkbox = new CustomCheckbox
            {
                AccessibleName = caption,
                Checked = row.HasBackup,
                Enabled = row.HasBackup,
                LabelText = string.Empty,
                Margin = new Padding(0, 2, Ui.SpaceS, 0),
                Tag = row.Module,
            };
            checkbox.CheckedChanged += (s, e) => RefreshSelectionState();

            Label name = new Label
            {
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Font = Ui.BodyBold(),
                ForeColor = row.HasBackup ? Theme.Current.Text : Theme.Current.TextMuted,
                Margin = new Padding(0, 0, Ui.SpaceS, 0),
                Text = caption,
            };
            if (row.HasBackup)
            {
                name.Cursor = Cursors.Hand;
                name.Click += (s, e) =>
                {
                    if (checkbox.Enabled)
                        checkbox.Checked = !checkbox.Checked;
                };
            }

            Label itemCount = new Label
            {
                AutoSize = true,
                Anchor = AnchorStyles.Right,
                Font = Ui.MonoSmall(),
                ForeColor = Theme.Current.TextMuted,
                Margin = new Padding(Ui.SpaceS, 0, 0, 0),
                Text = DescribeItemCount(row.Module),
            };

            TagChip restartChip = row.Module.RequiresExplorerRestart
                ? new TagChip
                {
                    Text = "RESTART REQUIRED",
                    Variant = TagVariant.Accent2,
                    Margin = new Padding(Ui.SpaceS, 0, 0, 0),
                }
                : null;

            TableLayoutPanel rowTop = new TableLayoutPanel
            {
                AutoSize = true,
                ColumnCount = restartChip == null ? 3 : 4,
                Dock = DockStyle.Top,
                Margin = new Padding(0),
            };
            rowTop.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            rowTop.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            if (restartChip != null)
                rowTop.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            rowTop.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            rowTop.Controls.Add(checkbox, 0, 0);
            rowTop.Controls.Add(name, 1, 0);
            if (restartChip != null)
                rowTop.Controls.Add(restartChip, 2, 0);
            rowTop.Controls.Add(itemCount, restartChip == null ? 2 : 3, 0);

            FlowLayoutPanel content = CreateVerticalFlow();
            content.Controls.Add(rowTop);

            if (row.HasBackup && !string.IsNullOrWhiteSpace(row.Warning))
            {
                AccentLabel rowWarning = new AccentLabel
                {
                    AutoSize = true,
                    Font = Ui.MonoSmall(),
                    ForeColor = Theme.Current.Accent2_700,
                    Margin = new Padding(24, Ui.SpaceXs, 0, 0),
                    MaximumSize = new Size(680, 0),
                    Text = "\u26A0 " + row.Warning,
                };
                content.Controls.Add(rowWarning);
            }

            BlueprintFrame frame = new BlueprintFrame
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0, 0, 0, Ui.SpaceS),
                MinimumSize = new Size(0, 48),
                Width = 640,
            };
            frame.Controls.Add(content);
            rowChecks.Add(new RowCheck(checkbox, row.Module, row.HasBackup));
            return frame;
        }

        private static string DescribeItemCount(BackupBase module)
        {
            int count = module.RestoreTargets.Count;
            return count + (count == 1 ? " ITEM" : " ITEMS");
        }

        private void RefreshSelectionState()
        {
            int selected = SelectedModules().Count;
            selectionLabel.Text = selected + (selected == 1 ? " component selected" : " components selected");
            btnNext.Enabled = selected > 0;
        }

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

        private void btnNext_Click(object sender, EventArgs e)
        {
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

            StartRestoreRequested?.Invoke(selection, folder);
        }

        private static FlowLayoutPanel CreateVerticalFlow()
        {
            return new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
            };
        }

        private static Button CreatePrimaryButton(string text)
        {
            return new AccentButton
            {
                AutoSize = true,
                BackColor = Theme.Current.Accent,
                FlatStyle = FlatStyle.Flat,
                Font = Ui.Kicker(),
                ForeColor = Theme.Current.Bg,
                Padding = new Padding(16, 8, 16, 8),
                Text = text,
                UseVisualStyleBackColor = false,
            };
        }

        private static Button CreateSecondaryButton(string text)
        {
            return new Button
            {
                AutoSize = true,
                BackColor = Theme.Current.Surface,
                FlatStyle = FlatStyle.Flat,
                Font = Ui.Kicker(),
                ForeColor = Theme.Current.Text,
                Padding = new Padding(16, 8, 16, 8),
                Text = text,
                UseVisualStyleBackColor = false,
            };
        }
    }
}
