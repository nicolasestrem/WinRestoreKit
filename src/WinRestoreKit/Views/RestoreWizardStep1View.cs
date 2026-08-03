using WinRestoreKit;
using DataHelper;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Views
{
    /// <summary>Restore wizard step 1: select the snapshot that supplies the restore source.</summary>
    internal sealed partial class RestoreWizardStep1View : UserControl, IRefreshableView
    {
        private readonly NavigationService navigation;
        private readonly Action<BackupFolder> onPicked;
        private readonly Action goToBackup;
        private readonly FlowLayoutPanel cards;
        private readonly Label selectionLabel;
        private readonly Button btnContinue;
        private readonly List<CardEntry> cardEntries = new List<CardEntry>();

        private BackupFolder selectedFolder;

        private sealed class CardEntry
        {
            internal BackupFolder Folder { get; }
            internal BlueprintFrame Frame { get; }

            internal CardEntry(BackupFolder folder, BlueprintFrame frame)
            {
                Folder = folder;
                Frame = frame;
            }
        }

        public RestoreWizardStep1View(NavigationService navigation, Action<BackupFolder> onPicked, Action goToBackup)
        {
            this.navigation = navigation;
            this.onPicked = onPicked;
            this.goToBackup = goToBackup;

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
                Margin = new Padding(0, 0, 0, Ui.SpaceS),
                Text = "PICK A SNAPSHOT",
            };

            Label detail = new Label
            {
                AutoSize = true,
                Font = Ui.Body(),
                ForeColor = Theme.Current.TextMuted,
                Margin = new Padding(0, 0, 0, Ui.SpaceM),
                Text = "Choose the snapshot whose saved settings you want to restore.",
            };

            cards = new FlowLayoutPanel
            {
                AutoScroll = true,
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                Padding = new Padding(6, 6, 12, 6),
            };

            btnContinue = CreatePrimaryButton("CONTINUE");
            btnContinue.Enabled = false;
            btnContinue.Click += Continue_Click;

            Button btnBack = CreateSecondaryButton("BACK");
            btnBack.Click += (s, e) => navigation.Pop();

            selectionLabel = new Label
            {
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Font = Ui.MonoSmall(),
                ForeColor = Theme.Current.TextMuted,
                Text = "No snapshot selected",
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
            footer.Controls.Add(btnContinue, 2, 0);

            TableLayoutPanel layout = new TableLayoutPanel
            {
                ColumnCount = 1,
                Dock = DockStyle.Fill,
                Padding = new Padding(30, 26, 30, 26),
                RowCount = 6,
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(stepIndicator, 0, 0);
            layout.Controls.Add(kicker, 0, 1);
            layout.Controls.Add(title, 0, 2);
            layout.Controls.Add(detail, 0, 3);
            layout.Controls.Add(cards, 0, 4);
            layout.Controls.Add(footer, 0, 5);
            Controls.Add(layout);
        }

        void IRefreshableView.RefreshView(ViewEntry entry)
        {
            selectedFolder = null;
            cardEntries.Clear();
            cards.Controls.Clear();

            BackupFolders folders = BackupFolders.Read();
            if (folders.UnreadableReason != null)
            {
                cards.Controls.Add(MakeState(
                    "SNAPSHOT LIST UNAVAILABLE",
                    folders.UnreadableReason,
                    null));
                UpdateSelection();
                Theme.Apply(this);
                return;
            }

            if (folders.Backups.Count == 0 && folders.Snapshots.Count == 0)
            {
                cards.Controls.Add(MakeState(
                    "NO SNAPSHOTS YET",
                    "Create a backup before restoring settings.",
                    "GO TO BACK UP"));
                UpdateSelection();
                Theme.Apply(this);
                return;
            }

            AddSection("SAVED SNAPSHOTS", folders.Backups, false);
            AddSection("PRE-RESTORE SNAPSHOTS", folders.Snapshots, true);
            UpdateSelection();
            Theme.Apply(this);
        }

        private void AddSection(string title, IReadOnlyList<BackupFolder> folders, bool undoPoint)
        {
            if (folders.Count == 0)
                return;

            cards.Controls.Add(new Label
            {
                AutoSize = true,
                Font = Ui.Kicker(),
                ForeColor = Theme.Current.TextMuted,
                Margin = new Padding(0, Ui.SpaceS, 0, Ui.SpaceXs),
                Text = title,
            });

            foreach (BackupFolder folder in folders)
                cards.Controls.Add(MakeCard(folder, undoPoint));
        }

        private Control MakeState(string heading, string detail, string action)
        {
            BlueprintFrame frame = new BlueprintFrame
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0, 0, 0, Ui.SpaceM),
                MinimumSize = new Size(0, 92),
                Width = Math.Max(460, Width - 72),
            };

            Label headingLabel = new Label
            {
                AutoSize = true,
                Font = Ui.Heading2(),
                ForeColor = Theme.Current.Text,
                Text = heading,
            };
            Label detailLabel = new Label
            {
                AutoSize = true,
                Font = Ui.Body(),
                ForeColor = Theme.Current.TextMuted,
                Margin = new Padding(0, Ui.SpaceXs, 0, 0),
                Text = detail,
            };

            FlowLayoutPanel content = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Top,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
            };
            content.Controls.Add(headingLabel);
            content.Controls.Add(detailLabel);

            if (action != null)
            {
                Button button = CreateSecondaryButton(action);
                button.Margin = new Padding(0, Ui.SpaceS, 0, 0);
                button.Click += (s, e) => goToBackup?.Invoke();
                content.Controls.Add(button);
            }

            frame.Controls.Add(content);
            return frame;
        }

        private Control MakeCard(BackupFolder folder, bool undoPoint)
        {
            ManifestData manifest = folder.ReadManifest();
            BlueprintFrame frame = new BlueprintFrame
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0, 0, 0, Ui.SpaceS),
                MinimumSize = new Size(0, 100),
                Width = Math.Max(460, Width - 72),
                Cursor = Cursors.Hand,
                Tag = folder.Name,
            };

            Label name = new Label
            {
                AutoSize = true,
                Font = Ui.Heading2(),
                ForeColor = Theme.Current.Text,
                Text = folder.DisplayName,
            };
            Label date = new Label
            {
                AutoSize = true,
                Font = Ui.MonoSmall(),
                ForeColor = Theme.Current.TextMuted,
                Margin = new Padding(0, Ui.SpaceXs, 0, 0),
                Text = folder.Created == DateTime.MinValue
                    ? "DATE UNKNOWN"
                    : folder.Created.ToString("yyyy-MM-dd HH.mm"),
            };
            Label summary = new Label
            {
                AutoSize = true,
                Font = Ui.Body(),
                ForeColor = Theme.Current.TextMuted,
                Margin = new Padding(0, Ui.SpaceXs, 0, 0),
                MaximumSize = new Size(760, 0),
                Text = undoPoint
                    ? "Pre-restore snapshot. Use this to roll back the preceding restore."
                    : DescribeManifest(manifest),
            };

            TagChip sizeTag = new TagChip
            {
                Text = undoPoint ? "UNDO POINT" : "SNAPSHOT",
                Variant = undoPoint ? TagVariant.Accent2 : TagVariant.Neutral,
                Margin = new Padding(Ui.SpaceM, 0, 0, 0),
            };

            TableLayoutPanel top = new TableLayoutPanel
            {
                AutoSize = true,
                ColumnCount = 2,
                Dock = DockStyle.Top,
            };
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            top.Controls.Add(name, 0, 0);
            top.Controls.Add(sizeTag, 1, 0);

            FlowLayoutPanel content = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Top,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
            };
            content.Controls.Add(top);
            content.Controls.Add(date);
            content.Controls.Add(summary);
            frame.Controls.Add(content);

            CardEntry entry = new CardEntry(folder, frame);
            cardEntries.Add(entry);
            AttachSelection(frame, entry);
            AttachSelection(name, entry);
            AttachSelection(date, entry);
            AttachSelection(summary, entry);
            AttachSelection(sizeTag, entry);
            return frame;
        }

        private void AttachSelection(Control control, CardEntry entry)
            => control.Click += (s, e) => SelectFolder(entry.Folder);

        private void SelectFolder(BackupFolder selected)
        {
            selectedFolder = selected;
            UpdateSelection();
        }

        private void UpdateSelection()
        {
            btnContinue.Enabled = selectedFolder != null;
            selectionLabel.Text = selectedFolder == null
                ? "No snapshot selected"
                : selectedFolder.DisplayName;

            foreach (CardEntry entry in cardEntries)
            {
                bool selected = entry.Folder.Name == selectedFolder?.Name;
                entry.Frame.Paint -= PaintCardSelection;
                if (selected)
                    entry.Frame.Paint += PaintCardSelection;

                entry.Frame.Invalidate();
            }
        }

        private void PaintCardSelection(object sender, PaintEventArgs e)
        {
            Control card = (Control)sender;
            using (Pen accent = new Pen(Theme.Current.Accent, 2f))
            {
                e.Graphics.DrawRectangle(accent, 2, 2, card.ClientSize.Width - 5, card.ClientSize.Height - 5);
            }
        }

        private void Continue_Click(object sender, EventArgs e)
        {
            if (selectedFolder != null)
                onPicked(selectedFolder);
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

        private static string DescribeManifest(ManifestData manifest)
        {
            if (manifest == null)
                return "Details unavailable for this legacy snapshot.";

            int items = manifest.Modules == null ? 0 : manifest.Modules.Count;
            return items + (items == 1 ? " setting group" : " setting groups")
                + " from " + (string.IsNullOrEmpty(manifest.MachineName) ? "an unknown machine" : manifest.MachineName);
        }
    }
}
