using WinRestoreKit;
using DataHelper;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace Views
{
    /// <summary>
    /// History: one merged timeline of every folder under the backup root - user backups and the
    /// (pre-restore) Undo-point snapshots - newest first. Selecting a row shows its logs; a snapshot's
    /// "Undo this restore" reopens the wizard on that snapshot (rollback is just a restore of it).
    /// </summary>
    /// <remarks>
    /// Replaces RestPageView (deleted in this PR). ReadLogOrNull and the backup/restore log
    /// concatenation moved here intact from RestPageView, where they could only ever show one log at
    /// a time behind a picker that the wizard has now replaced.
    /// </remarks>
    internal sealed partial class HistoryPageView : UserControl, IRefreshableView
    {
        private readonly Action<BackupFolder> restoreFrom;

        private readonly Label headerLabel;
        private readonly FlowLayoutPanel rows;
        private readonly TextBox detailPane;

        // name -> the folder + its full concatenated log, rebuilt on every RefreshView.
        private readonly Dictionary<string, BackupFolder> foldersByName = new Dictionary<string, BackupFolder>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> logsByName = new Dictionary<string, string>(StringComparer.Ordinal);

        // A folder name Home asked to preselect, honoured on the far side of the refresh.
        private string pendingSelection;

        public HistoryPageView(Action<BackupFolder> restoreFrom)
        {
            this.restoreFrom = restoreFrom;

            BackColor = Ui.Surface;

            headerLabel = new Label
            {
                AutoSize = true,
                Dock = DockStyle.Top,
                Font = Ui.Title(),
                ForeColor = Ui.TextPrimary,
                Margin = new Padding(Ui.SpaceM, Ui.SpaceM, Ui.SpaceM, Ui.SpaceS),
                Text = "History",
            };

            detailPane = new TextBox
            {
                BorderStyle = BorderStyle.FixedSingle,
                Dock = DockStyle.Bottom,
                Font = Ui.Body(),
                ForeColor = Ui.Muted,
                Height = 150,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Text = "Select a row to see its log.",
            };

            rows = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true,
                Dock = DockStyle.Fill,
                Padding = new Padding(Ui.SpaceM),
            };

            Controls.Add(rows);
            Controls.Add(detailPane);
            Controls.Add(headerLabel);
        }

        void IRefreshableView.RefreshView(ViewEntry entry)
        {
            rows.Controls.Clear();
            foldersByName.Clear();
            logsByName.Clear();
            detailPane.Text = "Select a row to see its log.";

            BackupFolders folders = BackupFolders.Read();

            if (folders.UnreadableReason != null)
            {
                rows.Controls.Add(MakeNote(folders.UnreadableReason));
                Theme.Apply(this);
                return;
            }

            List<BackupFolder> timeline = new List<BackupFolder>();
            timeline.AddRange(folders.Backups);
            timeline.AddRange(folders.Snapshots);
            timeline.Sort((a, b) => b.Created.CompareTo(a.Created));

            if (timeline.Count == 0)
            {
                rows.Controls.Add(MakeNote("No backups yet."));
                Theme.Apply(this);
                return;
            }

            foreach (BackupFolder folder in timeline)
            {
                bool isSnapshot = BackupFolders.IsSnapshot(folder.Name);
                Control row = MakeRow(folder, isSnapshot);
                rows.Controls.Add(row);
                foldersByName[folder.Name] = folder;
                logsByName[folder.Name] = ConcatenatedLog(folder);
            }

            // Built just now, so they missed the startup theme pass - see the note in
            // RestoreWizardStep2View.LoadFolder. Before the selection below, so the detail pane's
            // text is set against the right palette.
            Theme.Apply(this);

            if (!string.IsNullOrEmpty(pendingSelection))
            {
                string wanted = pendingSelection;
                pendingSelection = null;
                ShowLogFor(wanted);
            }
        }

        /// <summary>Preselects a folder's row (Home's "View details"), applied after the next refresh.</summary>
        internal void SelectFolder(string folderName)
        {
            pendingSelection = folderName;
            ShowLogFor(folderName);
        }

        private Control MakeRow(BackupFolder folder, bool isSnapshot)
        {
            ManifestData manifest = folder.ReadManifest();
            string created = folder.Created == DateTime.MinValue ? "unknown date" : folder.Created.ToString("yyyy-MM-dd HH.mm");

            string title = isSnapshot ? "Undo point" : folder.Name;
            string summary = isSnapshot ? FirstLinesOfRestoreLog(folder) : DescribeManifest(manifest);

            Label titleLabel = new Label
            {
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Font = Ui.BodyBold(),
                ForeColor = Ui.TextPrimary,
                Margin = new Padding(0),
                Text = title + "   (" + created + ")",
            };

            Label summaryLabel = new Label
            {
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Font = Ui.Body(),
                ForeColor = Ui.Muted,
                Margin = new Padding(0, 0, 0, Ui.SpaceXs),
                Text = summary,
            };

            LinkLabel restoreLink = new LinkLabel
            {
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Font = Ui.Body(),
                LinkColor = Ui.TextPrimary,
                Margin = new Padding(0, 0, Ui.SpaceL, 0),
                Text = isSnapshot ? "Undo this restore" : "Restore from this backup",
                Tag = folder,
            };
            restoreLink.LinkClicked += (s, e) => restoreFrom(folder);

            LinkLabel openLink = new LinkLabel
            {
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Font = Ui.Body(),
                LinkColor = Ui.Muted,
                Margin = new Padding(0),
                Text = "Open folder",
                Tag = folder,
            };
            openLink.LinkClicked += (s, e) => OpenFolder(folder);

            // FlowLayoutPanel, so the two links sit side by side and the panel reports a real width.
            FlowLayoutPanel links = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.LeftToRight,
                Margin = new Padding(0),
                WrapContents = false,
            };
            links.Controls.Add(restoreLink);
            links.Controls.Add(openLink);

            // A TableLayoutPanel, NOT a Panel. A plain Panel whose children are all Dock=Top reports
            // a preferred WIDTH of zero, and this row lives in a FlowLayoutPanel, which sizes its
            // children from exactly that. Every row was therefore laid out 0 pixels wide - present,
            // Visible=true, correct height, and completely invisible. The whole History timeline
            // rendered as an empty page, and it shipped that way because the original check only
            // confirmed the view constructed without throwing.
            TableLayoutPanel row = new TableLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                Margin = new Padding(0, 0, 0, Ui.SpaceS),
                Padding = new Padding(0),
                Tag = folder.Name,
                Cursor = Cursors.Hand,
            };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            row.Controls.Add(titleLabel, 0, 0);
            row.Controls.Add(summaryLabel, 0, 1);
            row.Controls.Add(links, 0, 2);
            row.Click += (s, e) => ShowLogFor(folder.Name);

            // And on every child, because a Click does NOT bubble to a parent in WinForms. The
            // children cover the row, so the handler above could only ever fire on leftover
            // background pixels - meaning clicking the title or the summary, which is what "select a
            // row to read its log" means to anyone using this, did nothing at all.
            ForwardClickToRow(row, folder.Name);

            return row;
        }

        /// <summary>
        /// Gives every descendant of a row the row's own selection behaviour.
        /// </summary>
        /// <remarks>
        /// LinkLabels are skipped: they already carry an action, and "Restore from this backup"
        /// starting a restore should not also quietly change what the pane below is showing.
        /// Recursive because the links sit one Panel deep.
        /// </remarks>
        private void ForwardClickToRow(Control parent, string folderName)
        {
            foreach (Control child in parent.Controls)
            {
                if (child is LinkLabel)
                    continue;

                child.Cursor = Cursors.Hand;
                child.Click += (s, e) => ShowLogFor(folderName);

                ForwardClickToRow(child, folderName);
            }
        }

        private void ShowLogFor(string folderName)
        {
            if (folderName != null && logsByName.TryGetValue(folderName, out string log))
                detailPane.Text = log;
        }

        private static void OpenFolder(BackupFolder folder)
        {
            try
            {
                Process.Start(new ProcessStartInfo("explorer.exe", folder.Path) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                LogHelper.Instance.LogMessage("Could not open " + folder.Path + ": " + ex.Message);
            }
        }

        private static Label MakeNote(string text)
            => new Label
            {
                AutoSize = true,
                Dock = DockStyle.Top,
                Font = Ui.Body(),
                ForeColor = Ui.Muted,
                Margin = new Padding(0, Ui.SpaceS, 0, 0),
                Text = text,
            };

        private static string DescribeManifest(ManifestData manifest)
        {
            if (manifest == null)
                return "details unavailable";

            int items = manifest.Modules == null ? 0 : manifest.Modules.Count;
            int failed = 0;

            if (manifest.Modules != null)
                foreach (ManifestModule module in manifest.Modules)
                    if (module != null && module.State == BackupManifest.StateFailed)
                        failed++;

            return items + " item(s)" + (failed > 0 ? " \u00b7 " + failed + " failed" : "")
                 + "   from " + (string.IsNullOrEmpty(manifest.MachineName) ? "?" : manifest.MachineName)
                 + " / " + (string.IsNullOrEmpty(manifest.UserName) ? "?" : manifest.UserName);
        }

        /// <summary>
        /// The backup's and the restore's logs joined, as RestPageView used to show. Never throws: an
        /// unreadable log must not stop the timeline. Moved intact from RestPageView.ReadLogOrNull.
        /// </summary>
        private static string ConcatenatedLog(BackupFolder folder)
        {
            string backupLog = ReadLogOrNull(Path.Combine(folder.Path, "backup_log.txt"));

            // A pre-restore snapshot folder also holds the log of the restore it was taken for. That
            // log names what the restore changed, so it is the reason someone would be looking at this
            // folder at all - showing only the backup half would hide the half they came for.
            string restoreLog = ReadLogOrNull(Path.Combine(folder.Path, "restore_log.txt"));

            if (backupLog == null && restoreLog == null)
                return "No log available for this folder.";

            return backupLog == null ? restoreLog
                : restoreLog == null ? backupLog
                : backupLog + "\r\n\r\n" + restoreLog;
        }

        // The first lines of the restore log: the one-line summary of what the restore changed.
        private static string FirstLinesOfRestoreLog(BackupFolder folder)
        {
            string restoreLog = ReadLogOrNull(Path.Combine(folder.Path, "restore_log.txt"));

            if (string.IsNullOrEmpty(restoreLog))
                return "Pre-restore snapshot.";

            string[] lines = restoreLog.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            return lines.Length == 0 ? restoreLog : lines[0];
        }

        /// <summary>
        /// The log's text, or null when there is none to show. Never throws: an unreadable log must
        /// not stop the user reading the folder it sits beside.
        /// </summary>
        private static string ReadLogOrNull(string path)
        {
            try
            {
                return File.Exists(path) ? File.ReadAllText(path) : null;
            }
            catch (Exception ex)
            {
                return "Could not read " + Path.GetFileName(path) + ": " + ex.Message;
            }
        }
    }
}
