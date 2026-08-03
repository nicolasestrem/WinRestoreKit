using DataHelper;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using WinRestoreKit;

namespace Views
{
    internal sealed class HistoryPageView : UserControl, IRefreshableView
    {
        private const int TableWidth = 940;
        private readonly Action<BackupFolder> restoreFrom;
        private readonly Label snapshotCount;
        private readonly TextBox filterInput;
        private readonly SegmentedControl triggerFilter;
        private readonly FlowLayoutPanel tableRows;
        private readonly Label footer;
        private readonly List<HistoryItem> items = new List<HistoryItem>();
        private string pendingSelection;
        private string selectedFolderName;

        public HistoryPageView(Action<BackupFolder> restoreFrom)
        {
            this.restoreFrom = restoreFrom ?? throw new ArgumentNullException(nameof(restoreFrom));
            BackColor = Theme.Current.Bg;
            Dock = DockStyle.Fill;
            AutoScroll = true;
            Padding = new Padding(30, 26, 30, 26);
            Resize += (sender, args) => UpdateTableWidth();

            TableLayoutPanel topRow = new TableLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                Dock = DockStyle.Top,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
            };
            topRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            topRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            FlowLayoutPanel titleStack = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
            };
            titleStack.Controls.Add(new Label
            {
                AutoSize = true,
                Font = Ui.Kicker(),
                ForeColor = Theme.Current.Accent700,
                Margin = new Padding(0, 0, 0, Ui.SpaceXs),
                Text = "ARCHIVE",
            });
            snapshotCount = new Label
            {
                AutoSize = true,
                Font = Ui.Heading(),
                ForeColor = Theme.Current.Text,
                Margin = Padding.Empty,
                Text = "0 SNAPSHOTS",
            };
            titleStack.Controls.Add(snapshotCount);

            FlowLayoutPanel filters = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Anchor = AnchorStyles.Right | AnchorStyles.Bottom,
                Margin = new Padding(0, 12, 0, 0),
                Padding = Padding.Empty,
            };
            filterInput = new TextBox
            {
                Width = 220,
                Height = 32,
                Font = Ui.MonoSmall(),
                ForeColor = Theme.Current.Text,
                BackColor = Theme.Current.Surface,
                BorderStyle = BorderStyle.FixedSingle,
                PlaceholderText = "Filter by name or key",
                Margin = new Padding(0, 0, Ui.SpaceS, 0),
            };
            filterInput.TextChanged += (sender, args) => RenderTable();
            triggerFilter = new SegmentedControl(new[] { "All", "Manual", "Scheduled" })
            {
                Width = 246,
                Margin = Padding.Empty,
            };
            triggerFilter.SelectedIndexChanged += (sender, args) => RenderTable();
            filters.Controls.Add(filterInput);
            filters.Controls.Add(triggerFilter);
            topRow.Controls.Add(titleStack, 0, 0);
            topRow.Controls.Add(filters, 1, 0);

            Panel separator = new Panel
            {
                BackColor = Theme.Current.Divider,
                Dock = DockStyle.Top,
                Height = 1,
                Margin = new Padding(0, Ui.SpaceL, 0, Ui.SpaceS),
            };

            tableRows = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                Dock = DockStyle.Top,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
            };

            FlowLayoutPanel actions = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Top,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Margin = new Padding(0, Ui.SpaceS, 0, 0),
                Padding = Padding.Empty,
            };
            footer = new Label
            {
                AutoSize = true,
                Font = Ui.MonoSmall(),
                ForeColor = Theme.Current.Neutral600,
                Margin = new Padding(0, Ui.SpaceS, Ui.SpaceL, 0),
                Text = "0 B across 0 snapshots.",
            };
            actions.Controls.Add(footer);
            actions.Controls.Add(GhostButton("EXPORT MANIFEST", (sender, args) => ExportManifest()));
            actions.Controls.Add(SecondaryButton("PRUNE OLDER THAN 90 DAYS", (sender, args) => PruneOldBackups()));

            Controls.Add(actions);
            Controls.Add(tableRows);
            Controls.Add(separator);
            Controls.Add(topRow);

            UpdateTableWidth();
        }

        void IRefreshableView.RefreshView(ViewEntry entry)
        {
            items.Clear();
            BackupFolders folders = BackupFolders.Read();

            if (folders.UnreadableReason != null)
            {
                snapshotCount.Text = "ARCHIVE UNAVAILABLE";
                tableRows.Controls.Clear();
                tableRows.Controls.Add(NoteRow("Could not read the backup archive: " + folders.UnreadableReason));
                footer.Text = "Storage information is unavailable.";
                Theme.Apply(this);
                return;
            }

            foreach (BackupFolder folder in folders.Backups)
                items.Add(CreateItem(folder));

            foreach (BackupFolder folder in folders.Snapshots)
                items.Add(CreateItem(folder));

            items.Sort((left, right) => right.Folder.Created.CompareTo(left.Folder.Created));
            snapshotCount.Text = items.Count + " SNAPSHOTS";
            UpdateFooter();
            RenderTable();
            Theme.Apply(this);
            ApplySelectedRow();

            if (!string.IsNullOrEmpty(pendingSelection))
            {
                selectedFolderName = pendingSelection;
                pendingSelection = null;
                ApplySelectedRow();
            }
        }

        internal void SelectFolder(string folderName)
        {
            pendingSelection = folderName;
            selectedFolderName = folderName;
            ApplySelectedRow();
        }

        private HistoryItem CreateItem(BackupFolder folder)
        {
            ManifestData manifest = folder.ReadManifest();
            bool complete;
            long bytes = FolderBytes(folder.Path, out complete);
            return new HistoryItem(folder, manifest, ReadTrigger(folder, manifest), ReadResult(manifest), bytes, complete);
        }

        private void RenderTable()
        {
            if (tableRows == null)
                return;

            tableRows.SuspendLayout();
            tableRows.Controls.Clear();

            if (items.Count == 0)
            {
                tableRows.Controls.Add(NoteRow("No backups yet."));
                tableRows.ResumeLayout();
                return;
            }

            tableRows.Controls.Add(HeaderRow());
            bool found = false;
            foreach (HistoryItem item in items)
            {
                if (!MatchesFilter(item))
                    continue;

                tableRows.Controls.Add(CreateRow(item));
                found = true;
            }

            if (!found)
                tableRows.Controls.Add(NoteRow("No snapshots match the current filters."));

            tableRows.ResumeLayout();
            UpdateTableWidth();
            ApplySelectedRow();
        }

        private bool MatchesFilter(HistoryItem item)
        {
            string search = filterInput.Text.Trim();
            if (search.Length > 0
                && item.Folder.DisplayName.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0
                && item.Folder.Name.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0)
            {
                return false;
            }

            switch (triggerFilter.SelectedIndex)
            {
                case 1: return item.Trigger == "Manual";
                case 2: return item.Trigger == "Scheduled";
                default: return true;
            }
        }

        private Control HeaderRow()
        {
            TableLayoutPanel row = CreateGridRow(30);
            row.BackColor = Theme.Current.Surface;
            AddHeader(row, "SNAPSHOT", 0);
            AddHeader(row, "TAKEN", 1);
            AddHeader(row, "TRIGGER", 2);
            AddHeader(row, "GROUPS", 3);
            AddHeader(row, "SIZE", 4);
            AddHeader(row, "RESULT", 5);
            return row;
        }

        private Control CreateRow(HistoryItem item)
        {
            TableLayoutPanel row = CreateGridRow(52);
            row.Tag = item.Folder.Name;
            row.Cursor = Cursors.Hand;
            row.Click += (sender, args) => SelectRow(item.Folder.Name);

            FlowLayoutPanel snapshot = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Anchor = AnchorStyles.Left,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Margin = new Padding(Ui.SpaceS, 0, 0, 0),
                Padding = Padding.Empty,
            };
            KeyedMark mark = new KeyedMark
            {
                Size = new Size(18, 18),
                Margin = new Padding(0, 0, Ui.SpaceS, 0),
            };
            Label name = CellLabel(item.Folder.DisplayName, Ui.BodyBold(), Theme.Current.Text);
            name.MaximumSize = new Size(180, 0);
            snapshot.Controls.Add(mark);
            snapshot.Controls.Add(name);
            row.Controls.Add(snapshot, 0, 0);

            row.Controls.Add(CellLabel(DescribeDate(item.Folder.Created), Ui.MonoSmall(), Theme.Current.Neutral600), 1, 0);
            row.Controls.Add(CellLabel(item.Trigger, Ui.MonoSmall(), Theme.Current.Neutral600), 2, 0);
            row.Controls.Add(CellLabel(DescribeGroups(item.Manifest), Ui.MonoSmall(), Theme.Current.Text), 3, 0);
            row.Controls.Add(CellLabel(item.SizeComplete ? FormatBytes(item.Bytes) : "Unavailable", Ui.MonoSmall(), Theme.Current.Text), 4, 0);

            TagChip result = new TagChip
            {
                Text = item.Result.Text,
                Variant = item.Result.Variant,
                Anchor = AnchorStyles.Left,
                Margin = Padding.Empty,
            };
            row.Controls.Add(result, 5, 0);

            Button restore = GhostButton("RESTORE", (sender, args) => restoreFrom(item.Folder));
            restore.Margin = Padding.Empty;
            row.Controls.Add(restore, 6, 0);

            ForwardSelectionClick(row, item.Folder.Name);
            return row;
        }

        private TableLayoutPanel CreateGridRow(int height)
        {
            TableLayoutPanel row = new TableLayoutPanel
            {
                ColumnCount = 7,
                Height = height,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
            };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 250));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 75));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
            return row;
        }

        private static void AddHeader(TableLayoutPanel row, string text, int column)
        {
            row.Controls.Add(new Label
            {
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Font = Ui.Kicker(),
                ForeColor = Theme.Current.Neutral600,
                Margin = new Padding(Ui.SpaceS, 0, 0, 0),
                Text = text,
            }, column, 0);
        }

        private static Label CellLabel(string text, Font font, Color color)
            => new Label
            {
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Font = font,
                ForeColor = color,
                Margin = new Padding(Ui.SpaceS, 0, 0, 0),
                Text = text,
            };

        private Control NoteRow(string text)
        {
            return new Label
            {
                AutoSize = true,
                Font = Ui.Body(),
                ForeColor = Theme.Current.Neutral600,
                Margin = new Padding(0, Ui.SpaceS, 0, 0),
                Text = text,
            };
        }

        private void SelectRow(string folderName)
        {
            selectedFolderName = folderName;
            ApplySelectedRow();
        }

        private void ApplySelectedRow()
        {
            if (tableRows == null)
                return;

            foreach (Control control in tableRows.Controls)
            {
                if (!(control is TableLayoutPanel row) || !(row.Tag is string name))
                    continue;

                row.BackColor = string.Equals(name, selectedFolderName, StringComparison.Ordinal)
                    ? Theme.Current.Accent100
                    : Theme.Current.Bg;
            }
        }

        private void UpdateTableWidth()
        {
            if (tableRows == null)
                return;

            int width = Math.Max(TableWidth, ClientSize.Width - Padding.Horizontal - SystemInformation.VerticalScrollBarWidth);
            tableRows.Width = width;
            foreach (Control control in tableRows.Controls)
                control.Width = width;
        }

        private void UpdateFooter()
        {
            bool complete = true;
            long bytes = 0;
            DateTime oldest = DateTime.MaxValue;

            foreach (HistoryItem item in items)
            {
                bytes += item.Bytes;
                complete &= item.SizeComplete;
                if (item.Folder.Created != DateTime.MinValue && item.Folder.Created < oldest)
                    oldest = item.Folder.Created;
            }

            string storage = complete ? FormatBytes(bytes) : "Unavailable";
            string oldestText = oldest == DateTime.MaxValue ? "unknown" : oldest.ToString("yyyy-MM-dd");
            footer.Text = storage + " across " + items.Count + " snapshots. Oldest " + oldestText + ".";
        }

        private void ExportManifest()
        {
            using (SaveFileDialog dialog = new SaveFileDialog
            {
                AddExtension = true,
                DefaultExt = "json",
                FileName = "WinRestoreKit-manifests-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json",
                Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                Title = "Export backup manifests",
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK)
                    return;

                try
                {
                    JArray export = new JArray();
                    foreach (HistoryItem item in items)
                    {
                        JObject row = new JObject
                        {
                            ["folder_name"] = item.Folder.Name,
                            ["display_name"] = item.Folder.DisplayName,
                            ["created"] = item.Folder.Created == DateTime.MinValue ? null : item.Folder.Created.ToString("o"),
                        };
                        string manifestPath = Path.Combine(item.Folder.Path, BackupManifest.FileName);
                        try
                        {
                            row["manifest"] = File.Exists(manifestPath)
                                ? JToken.Parse(File.ReadAllText(manifestPath))
                                : null;
                        }
                        catch (Exception ex)
                        {
                            row["manifest"] = null;
                            row["manifest_read_error"] = ex.Message;
                        }

                        export.Add(row);
                    }

                    File.WriteAllText(dialog.FileName, export.ToString(Formatting.Indented));
                    Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + dialog.FileName + "\"")
                    {
                        UseShellExecute = true,
                    });
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Could not export manifests: " + ex.Message,
                        "Export manifest", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void PruneOldBackups()
        {
            if (RunCoordinator.IsRunning)
            {
                ShowPruneBlocked();
                return;
            }

            DateTime threshold = DateTime.Now.AddDays(-90);
            List<HistoryItem> candidates = items.Where(item => IsSafePruneCandidate(item, threshold)).ToList();
            if (candidates.Count == 0)
            {
                MessageBox.Show(this, "No recognized user backups are older than 90 days.",
                    "Prune archive", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string names = string.Join("\r\n", candidates.Select(item => item.Folder.Name));
            DialogResult confirmation = MessageBox.Show(this,
                "Permanently delete these " + candidates.Count + " recognized user backup folder(s)?\r\n\r\n"
                + names + "\r\n\r\nPre-restore snapshots and folders without a valid manifest are excluded.",
                "Prune archive", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
            if (confirmation != DialogResult.Yes)
                return;

            if (!TryPruneCandidates(candidates.Select(item => item.Folder.Path), out List<string> failures))
            {
                ShowPruneBlocked();
                return;
            }

            if (failures.Count > 0)
            {
                MessageBox.Show(this, "Some backup folders could not be removed:\r\n\r\n" + string.Join("\r\n", failures),
                    "Prune archive", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            ((IRefreshableView)this).RefreshView(default);
        }

        internal static bool TryPruneCandidates(IEnumerable<string> candidatePaths, out List<string> failures)
        {
            if (RunCoordinator.IsRunning)
            {
                failures = new List<string>();
                return false;
            }

            failures = new List<string>();
            foreach (string path in candidatePaths)
            {
                try
                {
                    Directory.Delete(path, true);
                }
                catch (Exception ex)
                {
                    failures.Add(Path.GetFileName(path) + ": " + ex.Message);
                }
            }

            return true;
        }


        private void ShowPruneBlocked()
        {
            MessageBox.Show(this, "Pruning is unavailable while a backup or restore is running. No changes were made.",
                "Prune archive", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private static bool IsSafePruneCandidate(HistoryItem item, DateTime threshold)
        {
            if (item.Manifest == null || item.Folder.Created == DateTime.MinValue || item.Folder.Created >= threshold)
                return false;

            if (BackupFolders.IsSnapshot(item.Folder.Name))
                return false;

            try
            {
                string root = Path.GetFullPath(Data.DataRootDir)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                string path = Path.GetFullPath(item.Folder.Path)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                return path.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(Path.GetFileName(path), item.Folder.Name, StringComparison.Ordinal)
                    && Directory.Exists(path);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static string ReadTrigger(BackupFolder folder, ManifestData manifest)
        {
            if (manifest == null)
                return "Manual";

            try
            {
                string path = Path.Combine(folder.Path, BackupManifest.FileName);
                if (!File.Exists(path))
                    return "Manual";

                JObject root = JObject.Parse(File.ReadAllText(path));
                string trigger = root.Value<string>("trigger");
                return string.Equals(trigger, "scheduled", StringComparison.OrdinalIgnoreCase)
                    ? "Scheduled"
                    : "Manual";
            }
            catch (Exception)
            {
                return "Manual";
            }
        }

        private static ResultInfo ReadResult(ManifestData manifest)
        {
            if (manifest == null || manifest.Modules == null)
                return new ResultInfo("Partial", TagVariant.Accent2);

            bool partial = false;
            foreach (ManifestModule module in manifest.Modules)
            {
                if (module != null && module.State == BackupManifest.StateFailed)
                    return new ResultInfo("Failed", TagVariant.Accent2);

                if (module == null)
                {
                    partial = true;
                    continue;
                }

                if (module.State == BackupManifest.StateSkipped || module.State == BackupManifest.StateUnknown)
                    partial = true;
            }

            return partial
                ? new ResultInfo("Partial", TagVariant.Accent2)
                : new ResultInfo("Verified", TagVariant.Accent);
        }

        private static string DescribeDate(DateTime date)
            => date == DateTime.MinValue ? "Unknown" : date.ToString("yyyy-MM-dd HH:mm");

        private static string DescribeGroups(ManifestData manifest)
            => manifest == null || manifest.Modules == null ? "Unknown" : manifest.Modules.Count.ToString();

        private static long FolderBytes(string path, out bool complete)
        {
            complete = true;
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

        private static string FormatBytes(long bytes)
        {
            const long gigabyte = 1024L * 1024L * 1024L;
            const long megabyte = 1024L * 1024L;
            const long kilobyte = 1024L;

            if (bytes >= gigabyte)
                return (bytes / (double)gigabyte).ToString("0.0") + " GB";

            if (bytes >= megabyte)
                return (bytes / (double)megabyte).ToString("0.0") + " MB";

            if (bytes >= kilobyte)
                return (bytes / (double)kilobyte).ToString("0.0") + " KB";

            return bytes + " B";
        }

        private static Button GhostButton(string text, EventHandler onClick)
        {
            Button button = new Button
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = Theme.Current.Bg,
                Cursor = Cursors.Hand,
                FlatStyle = FlatStyle.Flat,
                Font = Ui.Kicker(),
                ForeColor = Theme.Current.Text,
                Margin = new Padding(0, 0, Ui.SpaceS, 0),
                Padding = new Padding(Ui.SpaceS, 5, Ui.SpaceS, 5),
                Text = text,
                UseVisualStyleBackColor = false,
            };
            button.FlatAppearance.BorderSize = 0;
            button.Click += onClick;
            return button;
        }

        private static Button SecondaryButton(string text, EventHandler onClick)
        {
            Button button = GhostButton(text, onClick);
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.BorderColor = Theme.Current.Accent2;
            button.ForeColor = Theme.Current.Accent2_600;
            return button;
        }

        private void ForwardSelectionClick(Control parent, string folderName)
        {
            foreach (Control child in parent.Controls)
            {
                if (child is Button)
                    continue;

                child.Cursor = Cursors.Hand;
                child.Click += (sender, args) => SelectRow(folderName);
                ForwardSelectionClick(child, folderName);
            }
        }

        private sealed class HistoryItem
        {
            internal HistoryItem(BackupFolder folder, ManifestData manifest, string trigger, ResultInfo result,
                                 long bytes, bool sizeComplete)
            {
                Folder = folder;
                Manifest = manifest;
                Trigger = trigger;
                Result = result;
                Bytes = bytes;
                SizeComplete = sizeComplete;
            }

            internal BackupFolder Folder { get; }
            internal ManifestData Manifest { get; }
            internal string Trigger { get; }
            internal ResultInfo Result { get; }
            internal long Bytes { get; }
            internal bool SizeComplete { get; }
        }

        private sealed class ResultInfo
        {
            internal ResultInfo(string text, TagVariant variant)
            {
                Text = text;
                Variant = variant;
            }

            internal string Text { get; }
            internal TagVariant Variant { get; }
        }
    }
}
