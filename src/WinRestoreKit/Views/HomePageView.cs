using Conf;
using DataHelper;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using WinRestoreKit;

namespace Views
{
    internal sealed class HomePageView : UserControl, IRefreshableView
    {
        private readonly Action<IReadOnlyList<string>> backUpAgain;
        private readonly Action<string> viewDetails;
        private readonly Action restoreFromSnapshot;
        private readonly TableLayoutPanel rows;

        internal HomePageView(
            Action<IReadOnlyList<string>> backUpAgain,
            Action<string> viewDetails,
            Action restoreFromSnapshot)
        {
            this.backUpAgain = backUpAgain;
            this.viewDetails = viewDetails;
            this.restoreFromSnapshot = restoreFromSnapshot;

            BackColor = Theme.Current.Bg;
            Dock = DockStyle.Fill;
            AutoScroll = true;
            Padding = new Padding(Ui.SpaceL, Ui.SpaceL, Ui.SpaceL, Ui.SpaceL);

            rows = new TableLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                Dock = DockStyle.Top,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };
            rows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            Controls.Add(rows);

            RefreshView(ViewEntry.Fresh);
        }

        public void RefreshView(ViewEntry entry)
        {
            rows.SuspendLayout();

            for (int i = rows.Controls.Count - 1; i >= 0; i--)
            {
                Control old = rows.Controls[i];
                rows.Controls.RemoveAt(i);
                old.Dispose();
            }

            try
            {
                Build();
            }
            catch (Exception ex)
            {
                BuildUnavailable(ex.Message);
            }

            rows.ResumeLayout(true);
            Theme.Apply(this);
        }

        private void Build()
        {
            BackupFolders folders = BackupFolders.Read();
            IReadOnlyList<ModuleRegistration> registrations = ModuleCatalog.CreateAll();
            IReadOnlyList<WatchedGroupSummary> watchedGroups = WatchedGroups.GetCurrent();

            if (folders.UnreadableReason != null)
            {
                BuildHeader(
                    "BACKUP STATUS UNAVAILABLE",
                    "The backup folder could not be read. Existing backups are untouched. Back up this PC after resolving access to " + Data.DataRootDir + ".");
                rows.Controls.Add(BuildStatStrip("Unknown", "Unavailable", "Unavailable", "Unavailable"));
                rows.Controls.Add(BuildActions());
                rows.Controls.Add(Spacer(Ui.SpaceL));
                rows.Controls.Add(BuildBottom(
                    null,
                    "Drift status is unavailable until backup history can be read.",
                    watchedGroups));
                rows.Controls.Add(Line(folders.UnreadableReason, Ui.MonoSmall(), Theme.Current.Accent2_600));
                return;
            }

            if (folders.Backups.Count == 0)
            {
                BuildHeader(
                    "SYSTEM AWAITS A SNAPSHOT",
                    "No readable user snapshot exists for " + Environment.MachineName + ". Back up this PC to begin protecting "
                        + registrations.Count + " settings modules across " + watchedGroups.Count + " watched groups.");
                rows.Controls.Add(BuildStatStrip("0", "Not yet", DescribeStorage(folders, out _), "Not measured"));
                rows.Controls.Add(BuildActions());
                rows.Controls.Add(Spacer(Ui.SpaceL));
                rows.Controls.Add(BuildBottom(
                    null,
                    "Take a backup to begin drift detection.",
                    watchedGroups));
                return;
            }

            BackupFolder latest = folders.Backups[0];
            IReadOnlyList<DriftItem> drifted = DetectDrift(latest, registrations, out string driftUnavailableReason);
            string driftCount = drifted == null ? "Unavailable" : drifted.Count.ToString();
            string summary = "Last snapshot \"" + latest.DisplayName + "\" taken " + Ago(latest.Created)
                + " from " + Environment.MachineName + ". " + registrations.Count + " settings modules across "
                + watchedGroups.Count + " watched groups are under watch; "
                + (drifted == null ? "drift could not be measured" : drifted.Count + " have drifted since the snapshot") + ".";

            BuildHeader("SYSTEM IS CAPTURED", summary);
            rows.Controls.Add(BuildStatStrip(
                folders.Backups.Count.ToString(),
                DescribeLastRun(latest),
                DescribeStorage(folders, out _),
                driftCount));
            rows.Controls.Add(BuildActions());
            rows.Controls.Add(Spacer(Ui.SpaceL));
            rows.Controls.Add(BuildBottom(drifted, driftUnavailableReason, watchedGroups));
        }

        private void BuildUnavailable(string reason)
        {
            BuildHeader(
                "BACKUP STATUS UNAVAILABLE",
                "The dashboard could not read its backup information. Back up this PC after resolving the reported problem.");
            rows.Controls.Add(BuildStatStrip("Unknown", "Unavailable", "Unavailable", "Unavailable"));
            rows.Controls.Add(BuildActions());
            rows.Controls.Add(Line(reason, Ui.MonoSmall(), Theme.Current.Accent2_600));
        }

        private void BuildHeader(string heading, string summary)
        {
            TableLayoutPanel header = new TableLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                Dock = DockStyle.Top,
                Margin = new Padding(0, 0, 0, Ui.SpaceM),
                Padding = Padding.Empty
            };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            header.Controls.Add(Line("STATUS", Ui.Kicker(), Theme.Current.Accent700));
            header.Controls.Add(Line(heading, Ui.Heading(), Theme.Current.Text));
            header.Controls.Add(Line(summary, Ui.Body(), Theme.Current.TextMuted));
            rows.Controls.Add(header);
        }

        private static Control BuildStatStrip(string snapshots, string lastRun, string onDisk, string drifted)
        {
            BlueprintFrame frame = new BlueprintFrame
            {
                AutoSize = false,
                Height = 106,
                Dock = DockStyle.Top,
                Margin = new Padding(0, 0, 0, Ui.SpaceM)
            };

            TableLayoutPanel cells = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 4,
                RowCount = 1,
                Margin = Padding.Empty,
                Padding = new Padding(Ui.SpaceM, Ui.SpaceS, Ui.SpaceM, Ui.SpaceS)
            };
            for (int i = 0; i < 4; i++)
                cells.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));

            cells.Controls.Add(StatCell("SNAPSHOTS", snapshots), 0, 0);
            cells.Controls.Add(StatCell("LAST RUN", lastRun), 1, 0);
            cells.Controls.Add(StatCell("ON DISK", onDisk), 2, 0);
            cells.Controls.Add(StatCell("DRIFTED", drifted), 3, 0);
            frame.Controls.Add(cells);
            return frame;
        }

        private static Control StatCell(string label, string value)
        {
            Panel cell = new Panel
            {
                Dock = DockStyle.Fill,
                Margin = Padding.Empty,
                Padding = new Padding(Ui.SpaceS, Ui.SpaceXs, Ui.SpaceS, Ui.SpaceXs)
            };
            Label labelControl = Line(label, Ui.Kicker(), Theme.Current.TextMuted);
            labelControl.Dock = DockStyle.Top;
            Label valueControl = Line(value, Ui.Figure(), Theme.Current.Text);
            valueControl.Dock = DockStyle.Fill;
            valueControl.TextAlign = ContentAlignment.MiddleLeft;
            valueControl.AutoEllipsis = true;
            cell.Controls.Add(valueControl);
            cell.Controls.Add(labelControl);
            return cell;
        }

        private Control BuildActions()
        {
            FlowLayoutPanel actions = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Top,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                Margin = new Padding(0, 0, 0, Ui.SpaceM),
                Padding = Padding.Empty
            };

            actions.Controls.Add(PrimaryButton("BACK UP NOW", (sender, args) => backUpAgain(null)));
            actions.Controls.Add(SecondaryButton("RESTORE FROM SNAPSHOT", (sender, args) => restoreFromSnapshot()));
            actions.Controls.Add(GhostButton("VIEW HISTORY", (sender, args) => viewDetails(null)));
            return actions;
        }

        private static Control BuildBottom(
            IReadOnlyList<DriftItem> drifted,
            string driftUnavailableReason,
            IReadOnlyList<WatchedGroupSummary> watchedGroups)
        {
            TableLayoutPanel bottom = new TableLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                Dock = DockStyle.Top,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            bottom.Controls.Add(BuildDriftPanel(drifted, driftUnavailableReason), 0, 0);
            bottom.Controls.Add(BuildWatchedGroups(watchedGroups), 1, 0);
            return bottom;
        }

        private static Control BuildDriftPanel(IReadOnlyList<DriftItem> drifted, string unavailableReason)
        {
            TableLayoutPanel panel = Section("DRIFT SINCE LAST SNAPSHOT");

            if (drifted == null)
            {
                panel.Controls.Add(Line(unavailableReason, Ui.Body(), Theme.Current.TextMuted));
                return panel;
            }

            if (drifted.Count == 0)
            {
                panel.Controls.Add(Line("No tracked changes found since this snapshot.", Ui.Body(), Theme.Current.TextMuted));
                return panel;
            }

            foreach (DriftItem item in drifted)
                panel.Controls.Add(DriftRow(item));

            return panel;
        }

        private static Control DriftRow(DriftItem item)
        {
            TableLayoutPanel row = new TableLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                Dock = DockStyle.Top,
                Margin = new Padding(0, 0, Ui.SpaceM, Ui.SpaceS),
                Padding = Padding.Empty
            };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 12f));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            Label marker = new Label
            {
                Text = "■",
                Font = Ui.MonoSmall(),
                ForeColor = Theme.Current.Accent2_600,
                AutoSize = true,
                Dock = DockStyle.Top,
                Margin = Padding.Empty
            };
            TableLayoutPanel text = new TableLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                Dock = DockStyle.Top,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };
            text.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            text.Controls.Add(Line(item.Name, Ui.BodyBold(), Theme.Current.Text));
            text.Controls.Add(Line(item.Path, Ui.MonoSmall(), Theme.Current.TextMuted));
            text.Controls.Add(Line(DescribeChanged(item.ChangedAt), Ui.MonoSmall(), Theme.Current.TextMuted));

            row.Controls.Add(marker, 0, 0);
            row.Controls.Add(text, 1, 0);
            return row;
        }

        private static Control BuildWatchedGroups(IReadOnlyList<WatchedGroupSummary> watchedGroups)
        {
            TableLayoutPanel section = Section("WATCHED GROUPS");
            TableLayoutPanel grid = new TableLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                Dock = DockStyle.Top,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));

            for (int index = 0; index < watchedGroups.Count; index++)
            {
                WatchedGroupSummary group = watchedGroups[index];
                Panel cell = new Panel
                {
                    AutoSize = true,
                    AutoSizeMode = AutoSizeMode.GrowAndShrink,
                    Dock = DockStyle.Top,
                    Margin = new Padding(0, 0, Ui.SpaceS, Ui.SpaceS),
                    Padding = new Padding(Ui.SpaceS),
                    BackColor = Theme.Current.Surface
                };
                cell.Controls.Add(Line(group.Count, Ui.MonoSmall(), Theme.Current.TextMuted));
                Label name = Line(group.Name, Ui.BodyBold(), Theme.Current.Text);
                name.Dock = DockStyle.Top;
                cell.Controls.Add(name);
                grid.Controls.Add(cell, index % 2, index / 2);
            }

            if (watchedGroups.Count == 0)
                grid.Controls.Add(Line("No settings groups are registered.", Ui.Body(), Theme.Current.TextMuted), 0, 0);

            section.Controls.Add(grid);
            return section;
        }

        private static TableLayoutPanel Section(string kicker)
        {
            TableLayoutPanel section = new TableLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                Dock = DockStyle.Top,
                Margin = new Padding(0, 0, Ui.SpaceM, 0),
                Padding = Padding.Empty
            };
            section.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            section.Controls.Add(Line(kicker, Ui.Kicker(), Theme.Current.Accent700));
            return section;
        }

        private static IReadOnlyList<DriftItem> DetectDrift(
            BackupFolder latest,
            IReadOnlyList<ModuleRegistration> registrations,
            out string unavailableReason)
        {
            BackupPayload.ReadScope payload = null;

            try
            {
                List<BackupBase> modules = new List<BackupBase>(registrations.Count);
                foreach (ModuleRegistration registration in registrations)
                    modules.Add(registration.Module);

                if (!BackupPayload.TryPrepareForRead(latest.Path, out payload, out string error))
                {
                    unavailableReason = "Drift detection is unavailable because the backup payload could not be prepared: " + error;
                    return null;
                }

                unavailableReason = null;
                return DriftDetector.Detect(payload.Path, modules);
            }
            catch (Exception ex)
            {
                unavailableReason = "Drift detection could not complete: " + ex.Message;
                return null;
            }
            finally
            {
                if (payload != null)
                    payload.Dispose();
            }
        }

        private static string DescribeStorage(BackupFolders folders, out bool complete)
        {
            complete = true;
            long bytes = 0;

            foreach (BackupFolder folder in folders.Backups)
                bytes += FolderBytes(folder.Path, ref complete);

            foreach (BackupFolder folder in folders.Snapshots)
                bytes += FolderBytes(folder.Path, ref complete);

            return complete ? FormatBytes(bytes) : "Unavailable";
        }

        private static long FolderBytes(string path, ref bool complete)
        {
            try
            {
                long bytes = 0;
                foreach (string file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                    bytes += new FileInfo(file).Length;

                return bytes;
            }
            catch (Exception)
            {
                complete = false;
                return 0;
            }
        }

        private static string DescribeLastRun(BackupFolder latest)
            => latest.Created == DateTime.MinValue ? "Unknown" : Ago(latest.Created);

        private static string DescribeChanged(DateTime? changedAt)
            => changedAt.HasValue ? "Changed " + Ago(changedAt.Value) : "Change detected";

        internal static string Ago(DateTime created)
        {
            if (created == DateTime.MinValue)
                return "unknown";

            int days = (int)(DateTime.Now.Date - created.Date).TotalDays;

            if (days <= 0)
                return "today";

            if (days == 1)
                return "yesterday";

            return days + " days ago";
        }

        private static string FormatBytes(long bytes)
        {
            const long Gigabyte = 1024L * 1024L * 1024L;
            const long Megabyte = 1024L * 1024L;

            if (bytes >= Gigabyte)
                return (bytes / (double)Gigabyte).ToString("0.0") + " GB";

            if (bytes >= Megabyte)
                return (bytes / (double)Megabyte).ToString("0.0") + " MB";

            return bytes + " B";
        }

        private static Label Line(string text, Font font, Color color)
            => new Label
            {
                Text = text,
                Font = font,
                ForeColor = color,
                AutoSize = true,
                MaximumSize = new Size(900, 0),
                Margin = new Padding(0, 0, 0, Ui.SpaceXs),
                Dock = DockStyle.Top
            };

        private static Control Spacer(int height)
            => new Panel
            {
                Height = height,
                Dock = DockStyle.Top,
                Margin = Padding.Empty
            };

        private static Button PrimaryButton(string text, EventHandler onClick)
        {
            AccentButton button = new AccentButton
            {
                Text = text,
                Font = Ui.Kicker(),
                ForeColor = Theme.Current.Bg,
                BackColor = Theme.Current.Accent,
                FlatStyle = FlatStyle.Flat,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(Ui.SpaceM, Ui.SpaceS, Ui.SpaceM, Ui.SpaceS),
                Margin = new Padding(0, 0, Ui.SpaceS, 0),
                Cursor = Cursors.Hand,
                UseVisualStyleBackColor = false
            };
            button.FlatAppearance.BorderColor = Theme.Current.Accent;
            button.Click += onClick;
            return button;
        }

        private static Button SecondaryButton(string text, EventHandler onClick)
        {
            Button button = StandardButton(text, onClick);
            button.FlatAppearance.BorderColor = Theme.Current.Accent;
            return button;
        }

        private static Button GhostButton(string text, EventHandler onClick)
        {
            Button button = StandardButton(text, onClick);
            button.FlatAppearance.BorderSize = 0;
            return button;
        }

        private static Button StandardButton(string text, EventHandler onClick)
        {
            Button button = new Button
            {
                Text = text,
                Font = Ui.Kicker(),
                FlatStyle = FlatStyle.Flat,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(Ui.SpaceM, Ui.SpaceS, Ui.SpaceM, Ui.SpaceS),
                Margin = new Padding(0, 0, Ui.SpaceS, 0),
                Cursor = Cursors.Hand,
                UseVisualStyleBackColor = false
            };
            button.Click += onClick;
            return button;
        }
    }
}
