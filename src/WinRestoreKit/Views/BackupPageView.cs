using Conf;
using DataHelper;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using WinRestoreKit;

namespace Views
{
    /// <summary>
    /// Collects the scope and storage choices for a new snapshot.
    /// </summary>
    /// <remarks>
    /// Backup execution belongs to <see cref="ProgressPageView"/>. The shell wires
    /// <see cref="StartBackupRequested"/> to navigate to that runner.
    /// </remarks>
    public partial class BackupPageView : UserControl
    {
        private readonly IReadOnlyList<ScopeGroupRow> scopeGroups;
        private readonly Dictionary<ScopeGroupRow, CustomCheckbox> scopeToggles = new();
        private readonly Label selectionSummary;
        private readonly Label estimateValue;
        private readonly Label validationMessage;
        private readonly TextBox snapshotNameInput;
        private readonly TextBox destinationInput;
        private readonly SegmentedControl compressionSelector;

        /// <summary>
        /// The shell handles this request by opening the progress view, which owns the backup runner.
        /// </summary>
        internal Action<IReadOnlyList<BackupBase>, string, SnapshotCompression, string> StartBackupRequested;

        /// <summary>
        /// Retained for callers that supply the restore-wizard navigation seam.
        /// </summary>
        internal Action ShowRestoreView = () => { };

        public BackupPageView()
        {
            scopeGroups = ScopeGroups.Build();

            BackColor = Theme.Current.Bg;
            Dock = DockStyle.Fill;
            MinimumSize = new Size(740, 0);

            TableLayoutPanel page = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Theme.Current.Bg,
                ColumnCount = 1,
                RowCount = 2,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };
            page.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            page.RowStyles.Add(new RowStyle(SizeType.Absolute, 82f));
            page.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            Panel heading = new Panel
            {
                Dock = DockStyle.Fill,
                Margin = Padding.Empty
            };
            heading.Controls.Add(CreateHeading());
            page.Controls.Add(heading, 0, 0);

            TableLayoutPanel content = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58.333f));
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 41.667f));
            content.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            Panel scopes = CreateScopesPanel(out selectionSummary);
            content.Controls.Add(scopes, 0, 0);

            BlueprintFrame options = CreateOptionsPanel(out snapshotNameInput, out destinationInput,
                out compressionSelector, out estimateValue, out validationMessage);
            options.Margin = new Padding(Ui.SpaceL, 0, 0, 0);
            content.Controls.Add(options, 1, 0);

            page.Controls.Add(content, 0, 1);
            Controls.Add(page);

            RefreshSelectionSummary();
        }

        private static Control CreateHeading()
        {
            Panel panel = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
            Label kicker = new Label
            {
                AutoSize = true,
                Text = "NEW SNAPSHOT",
                Font = Ui.Kicker(),
                ForeColor = Theme.Current.Accent700,
                Location = new Point(0, 0),
                Margin = Padding.Empty
            };
            Label title = new Label
            {
                AutoSize = true,
                Text = "CHOOSE WHAT TO CAPTURE",
                Font = Ui.Heading(),
                ForeColor = Theme.Current.Text,
                Location = new Point(0, 16),
                Margin = Padding.Empty
            };
            panel.Controls.Add(kicker);
            panel.Controls.Add(title);
            return panel;
        }

        private Panel CreateScopesPanel(out Label summary)
        {
            Panel panel = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
            Panel toolbar = new Panel { Dock = DockStyle.Top, Height = 36, Margin = Padding.Empty };

            summary = new Label
            {
                AutoSize = true,
                Font = Ui.MonoSmall(),
                ForeColor = Theme.Current.Text,
                Location = new Point(0, 8),
                Margin = Padding.Empty
            };
            toolbar.Controls.Add(summary);

            Button selectAll = CreateGhostButton("SELECT ALL", (sender, args) => SetAllScopes(true));
            selectAll.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            selectAll.Location = new Point(0, 0);
            toolbar.Controls.Add(selectAll);

            Button clear = CreateGhostButton("CLEAR", (sender, args) => SetAllScopes(false));
            clear.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            clear.Location = new Point(0, 0);
            toolbar.Controls.Add(clear);

            toolbar.SizeChanged += (sender, args) =>
            {
                clear.Left = toolbar.ClientSize.Width - clear.Width;
                selectAll.Left = clear.Left - selectAll.Width - Ui.SpaceXs;
            };

            FlowLayoutPanel rows = new FlowLayoutPanel
            {
                Name = "scopeGroups",
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true,
                Padding = new Padding(0, Ui.SpaceXs, Ui.SpaceS, 0),
                Margin = Padding.Empty
            };
            rows.SizeChanged += (sender, args) =>
            {
                foreach (Control row in rows.Controls)
                    row.Width = Math.Max(0, rows.ClientSize.Width - rows.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth);
            };

            for (int index = 0; index < scopeGroups.Count; index++)
            {
                ScopeGroupRow group = scopeGroups[index];
                Panel row = CreateScopeRow(group, index, out CustomCheckbox toggle);
                scopeToggles.Add(group, toggle);
                rows.Controls.Add(row);
            }

            panel.Controls.Add(rows);
            panel.Controls.Add(toolbar);
            return panel;
        }

        private Panel CreateScopeRow(ScopeGroupRow group, int index, out CustomCheckbox toggle)
        {
            Panel row = new Panel
            {
                Height = 68,
                Width = 480,
                BackColor = Theme.Current.Surface,
                Margin = new Padding(0, 0, 0, Ui.SpaceXs),
                Cursor = Cursors.Hand,
                AccessibleRole = AccessibleRole.CheckButton,
                AccessibleName = group.Name
            };

            CustomCheckbox rowToggle = new CustomCheckbox
            {
                Name = "scopeToggle" + index,
                Checked = group.DefaultChecked,
                Location = new Point(Ui.SpaceM, 26),
                TabIndex = index
            };
            rowToggle.CheckedChanged += (sender, args) => RefreshSelectionSummary();
            toggle = rowToggle;

            Label name = new Label
            {
                AutoEllipsis = true,
                Font = Ui.BodyBold(),
                ForeColor = Theme.Current.Text,
                Location = new Point(40, 8),
                Size = new Size(260, 22),
                Text = group.Name,
                UseMnemonic = false
            };
            Label detail = new Label
            {
                AutoEllipsis = true,
                Font = Ui.MonoSmall(),
                ForeColor = Theme.Current.TextMuted,
                Location = new Point(40, 33),
                Size = new Size(260, 20),
                Text = group.Detail,
                UseMnemonic = false
            };
            Label size = new Label
            {
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Font = Ui.MonoSmall(),
                ForeColor = Theme.Current.TextMuted,
                Location = new Point(0, 25),
                Size = new Size(74, 20),
                Text = group.SizeLabel,
                TextAlign = ContentAlignment.MiddleRight,
                UseMnemonic = false
            };
            row.SizeChanged += (sender, args) =>
            {
                int detailWidth = Math.Max(120, row.ClientSize.Width - 132);
                name.Width = detailWidth;
                detail.Width = detailWidth;
                size.Left = row.ClientSize.Width - size.Width - Ui.SpaceM;
            };

            EventHandler toggleRow = (sender, args) => rowToggle.Checked = !rowToggle.Checked;
            row.Click += toggleRow;
            name.Click += toggleRow;
            detail.Click += toggleRow;
            size.Click += toggleRow;

            row.Controls.Add(rowToggle);
            row.Controls.Add(name);
            row.Controls.Add(detail);
            row.Controls.Add(size);
            return row;
        }

        private BlueprintFrame CreateOptionsPanel(out TextBox snapshotName, out TextBox destination,
                                                  out SegmentedControl compression, out Label estimate,
                                                  out Label message)
        {
            BlueprintFrame frame = new BlueprintFrame
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(Ui.SpaceL)
            };

            TableLayoutPanel options = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 10,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };
            options.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            options.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            options.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            options.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));
            options.RowStyles.Add(new RowStyle(SizeType.Absolute, Ui.SpaceM));
            options.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            options.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));
            options.RowStyles.Add(new RowStyle(SizeType.Absolute, Ui.SpaceM));
            options.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            options.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));
            options.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            options.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            snapshotName = CreateInput(string.Empty, "YYYY-MM-DD HH:MM initial tag");
            destination = CreateInput(Data.DataRootDir, "Destination folder");
            compression = new SegmentedControl(new[] { "NONE", "FAST", "MAX" })
            {
                Dock = DockStyle.Fill,
                SelectedIndex = 1,
                Name = "compressionSelector"
            };
            compression.SelectedIndexChanged += (sender, args) => RefreshSelectionSummary();

            options.Controls.Add(CreateOptionLabel("SNAPSHOT NAME"), 0, 0);
            options.SetColumnSpan(snapshotName, 2);
            options.Controls.Add(snapshotName, 0, 1);
            options.Controls.Add(CreateOptionLabel("DESTINATION"), 0, 3);
            options.Controls.Add(destination, 0, 4);

            Button browse = CreateGhostButton("BROWSE", BrowseDestination);
            browse.Margin = new Padding(Ui.SpaceS, 0, 0, 0);
            options.Controls.Add(browse, 1, 4);

            options.Controls.Add(CreateOptionLabel("COMPRESSION"), 0, 6);
            options.SetColumnSpan(compression, 2);
            options.Controls.Add(compression, 0, 7);

            Panel footer = new Panel { Dock = DockStyle.Fill, Height = 78, Margin = new Padding(0, Ui.SpaceL, 0, 0) };
            Label estimateCaption = new Label
            {
                AutoSize = true,
                Text = "ESTIMATED TOTAL",
                Font = Ui.Kicker(),
                ForeColor = Theme.Current.TextMuted,
                Location = new Point(0, 0)
            };
            estimate = new Label
            {
                AutoSize = true,
                Font = Ui.Mono(),
                ForeColor = Theme.Current.Text,
                Location = new Point(0, 18)
            };
            Label messageLabel = new Label
            {
                AutoEllipsis = true,
                ForeColor = Theme.Current.Accent2_600,
                Font = Ui.MonoSmall(),
                Location = new Point(0, 40),
                Size = new Size(10, 19),
                Visible = false
            };
            message = messageLabel;
            footer.SizeChanged += (sender, args) => messageLabel.Width = footer.ClientSize.Width;
            footer.Controls.Add(estimateCaption);
            footer.Controls.Add(estimate);
            footer.Controls.Add(message);
            options.SetColumnSpan(footer, 2);
            options.Controls.Add(footer, 0, 8);

            AccentButton capture = new AccentButton
            {
                Name = "captureButton",
                Text = "CAPTURE",
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = Theme.Current.Accent,
                ForeColor = Theme.Current.Bg,
                Font = Ui.Kicker(),
                FlatStyle = FlatStyle.Flat,
                Padding = new Padding(Ui.SpaceL, Ui.SpaceS, Ui.SpaceL, Ui.SpaceS),
                Cursor = Cursors.Hand,
                Anchor = AnchorStyles.Left | AnchorStyles.Bottom,
                Margin = Padding.Empty,
                UseVisualStyleBackColor = false
            };
            capture.FlatAppearance.BorderColor = Theme.Current.Accent;
            capture.Click += RequestCapture;
            options.SetColumnSpan(capture, 2);
            options.Controls.Add(capture, 0, 9);

            frame.Controls.Add(options);
            return frame;
        }

        private static Label CreateOptionLabel(string text)
        {
            return new Label
            {
                AutoSize = true,
                Text = text,
                Font = Ui.Kicker(),
                ForeColor = Theme.Current.Accent700,
                Margin = new Padding(0, 0, 0, Ui.SpaceXs)
            };
        }

        private static TextBox CreateInput(string text, string placeholder)
        {
            return new TextBox
            {
                Text = text,
                PlaceholderText = placeholder,
                Dock = DockStyle.Fill,
                Font = Ui.Mono(),
                BackColor = Theme.Current.Surface,
                ForeColor = Theme.Current.Text,
                BorderStyle = BorderStyle.FixedSingle,
                Margin = Padding.Empty
            };
        }

        private static Button CreateGhostButton(string text, EventHandler onClick)
        {
            Button button = new Button
            {
                Text = text,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Font = Ui.Kicker(),
                BackColor = Theme.Current.Bg,
                ForeColor = Theme.Current.Text,
                FlatStyle = FlatStyle.Flat,
                Padding = new Padding(Ui.SpaceS, Ui.SpaceXs, Ui.SpaceS, Ui.SpaceXs),
                Cursor = Cursors.Hand,
                UseVisualStyleBackColor = false
            };
            button.FlatAppearance.BorderSize = 0;
            button.Click += onClick;
            return button;
        }

        private void BrowseDestination(object sender, EventArgs args)
        {
            using (FolderBrowserDialog dialog = new FolderBrowserDialog
            {
                Description = "Choose the folder where this snapshot will be written.",
                SelectedPath = destinationInput.Text.Trim(),
                UseDescriptionForTitle = true
            })
            {
                if (dialog.ShowDialog(FindForm()) == DialogResult.OK)
                    destinationInput.Text = dialog.SelectedPath;
            }
        }

        private void RequestCapture(object sender, EventArgs args)
        {
            IReadOnlyList<BackupBase> modules = SelectedModules();
            if (modules.Count == 0)
            {
                SetValidationMessage("Select at least one scope with supported items.");
                return;
            }

            string destination = destinationInput.Text.Trim();
            if (destination.Length == 0)
            {
                SetValidationMessage("Choose a destination folder before capturing.");
                return;
            }

            if (StartBackupRequested == null)
            {
                SetValidationMessage("Backup integration is not connected. Open this page from the main window.");
                return;
            }

            SetValidationMessage(null);
            StartBackupRequested(modules, snapshotNameInput.Text.Trim(), SelectedCompression(), destination);
        }

        private IReadOnlyList<BackupBase> SelectedModules()
        {
            return scopeGroups
                .Where(group => scopeToggles[group].Checked)
                .SelectMany(group => group.Modules)
                .Distinct()
                .ToList();
        }

        private SnapshotCompression SelectedCompression()
        {
            return compressionSelector.SelectedIndex switch
            {
                0 => SnapshotCompression.None,
                2 => SnapshotCompression.Max,
                _ => SnapshotCompression.Fast
            };
        }

        private void SetAllScopes(bool isChecked)
        {
            foreach (CustomCheckbox toggle in scopeToggles.Values)
                toggle.Checked = isChecked;
        }

        private void RefreshSelectionSummary()
        {
            int selected = scopeToggles.Values.Count(toggle => toggle.Checked);
            selectionSummary.Text = selected + " of " + scopeGroups.Count + " groups";
            estimateValue.Text = EstimateSelection();
        }

        private string EstimateSelection()
        {
            long totalBytes = 0;
            foreach (ScopeGroupRow group in scopeGroups)
            {
                if (!scopeToggles[group].Checked || !TryParseSize(group.SizeLabel, out long bytes))
                    return "--";

                totalBytes += bytes;
            }

            return FormatSize(totalBytes);
        }

        private static bool TryParseSize(string label, out long bytes)
        {
            bytes = 0;
            string[] parts = (label ?? string.Empty).Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2 || !double.TryParse(parts[0], NumberStyles.Number, CultureInfo.InvariantCulture, out double value))
                return false;

            double multiplier = parts[1].ToUpperInvariant() switch
            {
                "B" => 1d,
                "KB" => 1024d,
                "MB" => 1024d * 1024d,
                "GB" => 1024d * 1024d * 1024d,
                _ => 0d
            };
            if (multiplier == 0d || value < 0d || value > long.MaxValue / multiplier)
                return false;

            bytes = (long)Math.Round(value * multiplier, MidpointRounding.AwayFromZero);
            return true;
        }

        private static string FormatSize(long bytes)
        {
            const long Gigabyte = 1024L * 1024L * 1024L;
            const long Megabyte = 1024L * 1024L;
            const long Kilobyte = 1024L;

            if (bytes >= Gigabyte)
                return (bytes / (double)Gigabyte).ToString("0.0", CultureInfo.InvariantCulture) + " GB";
            if (bytes >= Megabyte)
                return (bytes / (double)Megabyte).ToString("0.0", CultureInfo.InvariantCulture) + " MB";
            if (bytes >= Kilobyte)
                return (bytes / (double)Kilobyte).ToString("0.0", CultureInfo.InvariantCulture) + " KB";

            return bytes + " B";
        }

        private void SetValidationMessage(string text)
        {
            validationMessage.Text = text ?? string.Empty;
            validationMessage.Visible = !string.IsNullOrEmpty(text);
        }

        /// <summary>
        /// Selects every scope containing at least one module named in a prior manifest.
        /// </summary>
        internal void SelectModulesByTypeName(IReadOnlyList<string> moduleTypeNames)
        {
            HashSet<string> wanted = new HashSet<string>(
                moduleTypeNames?.Where(name => !string.IsNullOrWhiteSpace(name)) ?? Array.Empty<string>(),
                StringComparer.Ordinal);

            foreach (ScopeGroupRow group in scopeGroups)
            {
                scopeToggles[group].Checked = group.Modules.Any(module =>
                    wanted.Contains(module.GetType().Name));
            }
        }
    }
}
