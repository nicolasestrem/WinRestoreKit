using WinRestoreKit;
using DataHelper;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace Views
{
    /// <summary>
    /// Answers "am I okay?" - the last backup, what failed in it, and what can be undone.
    /// </summary>
    /// <remarks>
    /// The screen's whole value is that it may be trusted, so it is built around one rule: a status
    /// claim comes from backup_manifest.json or it is not made at all. A folder with no manifest, an
    /// unreadable one, or one TryParse refuses reads as "details unavailable" - never as a count, and
    /// never as a green tick. Every backup taken before the manifest existed is in that category, and
    /// inferring success for those is the cry-wolf failure running in the dangerous direction.
    ///
    /// Failure reasons are rendered verbatim, pinned above everything else, in read-only TextBoxes
    /// rather than Labels so they can be selected and pasted into a bug report. That is an honesty
    /// rule that happens to be implemented as a styling one.
    ///
    /// Laid out with TableLayoutPanel and Dock throughout, no absolute positions: PR 9 flips
    /// HighDpiMode to PerMonitorV2, and absolute coordinates do not survive a WM_DPICHANGED rescale.
    /// Built in code rather than in a Designer file because almost every row is conditional on what is
    /// on disk.
    /// </remarks>
    internal sealed class HomePageView : UserControl, IRefreshableView
    {
        private readonly Action<IReadOnlyList<string>> backUpAgain;
        private readonly Action<string> viewDetails;

        private readonly TableLayoutPanel rows;

        internal HomePageView(Action<IReadOnlyList<string>> backUpAgain, Action<string> viewDetails)
        {
            this.backUpAgain = backUpAgain;
            this.viewDetails = viewDetails;

            BackColor = Ui.Surface;
            Padding = new Padding(Ui.SpaceL);
            AutoScroll = true;

            rows = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1
            };
            rows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            Controls.Add(rows);

            RefreshView(ViewEntry.Fresh);
        }

        /// <summary>
        /// Rebuilt from disk on every visit. The entry kind is not consulted: Home carries no
        /// selection to preserve, and the question it answers can change between any two visits.
        /// </summary>
        public void RefreshView(ViewEntry entry)
        {
            rows.SuspendLayout();

            // Disposed, not merely removed: this runs on every visit to Home, and the rows own fonts
            // and brushes. Leaking a screenful of controls per navigation is the kind of thing that
            // only shows up after an hour of use.
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
                // Home is the startup view. It reads the file system, and a denied or vanished
                // backup directory must degrade to a sentence rather than take the app down before
                // its window is usable.
                rows.Controls.Add(Line("This screen could not be built: " + ex.Message, Ui.Body(), Ui.Danger));
            }

            rows.ResumeLayout(true);

            // Home disposes and rebuilds its whole row set on every visit, so the controls the
            // startup theme pass walked are gone by the second navigation. Without this they come
            // back on WinForms' light defaults. See the note in RestoreWizardStep2View.LoadFolder.
            Theme.Apply(this);
        }

        private void Build()
        {
            rows.Controls.Add(Line("This PC: " + Environment.MachineName, Ui.Title(), Ui.TextPrimary));

            BackupFolders folders = BackupFolders.Read();

            if (folders.UnreadableReason != null)
                BuildUnreadableRoot(folders.UnreadableReason);
            else if (folders.Backups.Count == 0)
                BuildNoBackups();
            else
                BuildLatestBackup(folders.Backups[0]);

            rows.Controls.Add(Separator());

            // The snapshot list comes from the SAME enumeration that just failed, so an empty one
            // means "could not look", not "there are none". Saying "Undo points: none" here would be
            // the inferred negative this screen refuses to make about backups, made about the thing
            // that undoes a restore.
            rows.Controls.Add(Line(
                folders.UnreadableReason == null
                    ? DescribeUndoPoints(folders.Snapshots)
                    : "Undo points: unknown while the backup folder cannot be read",
                Ui.Body(), Ui.Muted));

            // Disk space is a property of the drive, not of the folder listing, so it stays.
            rows.Controls.Add(Line(DescribeDisk(), Ui.Body(), Ui.Muted));
        }

        /// <summary>
        /// The backup folder is there but could not be listed.
        /// </summary>
        /// <remarks>
        /// Emphatically NOT "No backups yet". That sentence would tell someone their backups are
        /// gone when the far likelier truth is that they are sitting there intact behind a
        /// permission this process does not have. Same rule as an unreadable manifest: not knowing
        /// is reported as not knowing.
        /// </remarks>
        private void BuildUnreadableRoot(string reason)
        {
            rows.Controls.Add(Line("The backup folder could not be read.", Ui.Heading(), Ui.Danger));
            rows.Controls.Add(Line(Data.DataRootDir, Ui.Body(), Ui.Muted));
            rows.Controls.Add(Line(
                "Any backups already there are untouched - this screen simply cannot list them.",
                Ui.Body(), Ui.Muted));
            rows.Controls.Add(Line(reason, Ui.Body(), Ui.Danger));
        }

        private void BuildNoBackups()
        {
            rows.Controls.Add(Line("No backups yet.", Ui.Heading(), Ui.TextPrimary));
            rows.Controls.Add(Line("Nothing on this PC has been backed up with Appcopier.", Ui.Body(), Ui.Muted));
            rows.Controls.Add(Button("Back up this PC", (s, e) => backUpAgain(null)));
        }

        private void BuildLatestBackup(BackupFolder latest)
        {
            ManifestData manifest = latest.ReadManifest();

            rows.Controls.Add(Line("Last backup: " + Ago(latest.Created), Ui.Heading(), Ui.TextPrimary));
            rows.Controls.Add(Line(latest.Name, Ui.Body(), Ui.Muted));

            if (manifest == null)
            {
                // Absent, unreadable, or refused by TryParse - all the same answer. Saying anything
                // else here would mean deriving a verdict from a file this app is not willing to
                // trust, which is the one thing the manifest exists to prevent.
                rows.Controls.Add(Line("Details unavailable for this backup.", Ui.BodyBold(), Ui.TextPrimary));
                rows.Controls.Add(Line(
                    "It carries no readable record of what was captured - backups made before this "
                        + "version have none, and neither does a run that was interrupted. The backup "
                        + "itself is intact and can still be restored.",
                    Ui.Body(), Ui.Muted));
            }
            else
            {
                BuildManifestSummary(manifest);
            }

            rows.Controls.Add(Actions(latest, manifest));
        }

        /// <summary>
        /// The counts line, plus one verbatim row per outcome that is not a success.
        /// </summary>
        /// <remarks>
        /// Three buckets, not two. A row is failed, or it is a state this build recognises as an
        /// outcome (succeeded/skipped), or it is neither - and the third bucket is real: Compose
        /// writes state "unknown" for a module that produced no result at all, and a manifest from a
        /// later build can carry a literal this one has never heard of. Folding those into "none
        /// failed" would report an item with NO recorded outcome as an item that went fine, which is
        /// the same inferred-green this screen refuses to do for a whole missing manifest. An
        /// unrecorded item is not evidence of success; it is the absence of evidence.
        /// </remarks>
        private void BuildManifestSummary(ManifestData manifest)
        {
            List<ManifestModule> failed = new List<ManifestModule>();
            List<ManifestModule> unrecorded = new List<ManifestModule>();

            foreach (ManifestModule module in manifest.Modules)
            {
                if (module.State == BackupManifest.StateFailed)
                    failed.Add(module);
                else if (module.State != BackupManifest.StateSucceeded
                         && module.State != BackupManifest.StateSkipped)
                    unrecorded.Add(module);
            }

            string counts = manifest.Modules.Count + " item" + (manifest.Modules.Count == 1 ? "" : "s");

            if (failed.Count == 0 && unrecorded.Count == 0)
            {
                rows.Controls.Add(Line(counts + " · none failed", Ui.Body(), Ui.TextPrimary));
                return;
            }

            if (failed.Count > 0)
                counts += " · " + failed.Count + " failed";

            if (unrecorded.Count > 0)
                counts += " · " + unrecorded.Count + " not recorded";

            rows.Controls.Add(Line(counts, Ui.BodyBold(),
                failed.Count > 0 ? Ui.Danger : Ui.Caution));

            // Pinned above everything else and quoted verbatim. A rollup here would hide the only
            // text that says what actually went wrong. Failures first, then the unrecorded ones.
            foreach (ManifestModule module in failed)
                rows.Controls.Add(Reason(module, "FAILED", Ui.Danger));

            foreach (ManifestModule module in unrecorded)
                rows.Controls.Add(Reason(module, "NOT RECORDED", Ui.Caution));
        }

        private Control Actions(BackupFolder latest, ManifestData manifest)
        {
            FlowLayoutPanel panel = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0, Ui.SpaceM, 0, 0),
                Padding = new Padding(0)
            };

            panel.Controls.Add(Button("View details", (s, e) => viewDetails(latest.Name)));

            // With no manifest there is no list of what the run selected, so this is a plain
            // navigation. Guessing the selection from folder contents would re-tick items the user
            // never chose, on the screen whose button says "again".
            IReadOnlyList<string> types = manifest == null ? null : TypeNames(manifest);

            panel.Controls.Add(Button("Back up again", (s, e) => backUpAgain(types)));

            return panel;
        }

        private static IReadOnlyList<string> TypeNames(ManifestData manifest)
        {
            List<string> names = new List<string>(manifest.Modules.Count);

            foreach (ManifestModule module in manifest.Modules)
            {
                if (!string.IsNullOrEmpty(module.Type))
                    names.Add(module.Type);
            }

            return names;
        }

        // -----------------------------------------------------------------------------------------
        // Rendering helpers
        // -----------------------------------------------------------------------------------------

        private static Label Line(string text, Font font, Color color)
            => new Label
            {
                Text = text,
                Font = font,
                ForeColor = color,
                AutoSize = true,
                MaximumSize = new Size(720, 0),
                Margin = new Padding(0, 0, 0, Ui.SpaceXs),
                Dock = DockStyle.Top
            };

        /// <summary>
        /// One non-success module, as a selectable read-only row sized to its whole reason.
        /// </summary>
        /// <remarks>
        /// A TextBox and not a Label, on purpose: the reason is the text a user needs to paste into
        /// an issue, and Label text cannot be selected. ReadOnly rather than disabled so the caret
        /// and Ctrl+C still work; BorderStyle.None and the parent colour so it does not read as an
        /// input someone is meant to type into.
        ///
        /// The height is MEASURED rather than fixed. It was 40px with no scrollbars, which is about
        /// two lines - and real reasons run longer than that (the registry modules quote whole key
        /// paths). The overflow was clipped silently, so someone copying the visible text would have
        /// pasted a truncated reason into a bug report without knowing it, which defeats the entire
        /// point of making the row selectable. Fixed width plus a measurement at that same width
        /// means what is laid out is what is drawn.
        /// </remarks>
        private static TextBox Reason(ManifestModule module, string label, Color color)
        {
            const int RowWidth = 720;

            string text = "! " + (module.Title ?? module.Type ?? "Unknown item") + " " + label + " - "
                + (module.Reason ?? "no reason was recorded");

            Font font = Ui.Body();

            Size measured = TextRenderer.MeasureText(
                text, font, new Size(RowWidth, int.MaxValue), TextFormatFlags.WordBreak);

            return new TextBox
            {
                Text = text,
                Font = font,
                ForeColor = color,
                BackColor = Ui.Surface,
                ReadOnly = true,
                BorderStyle = BorderStyle.None,
                Multiline = true,
                ScrollBars = ScrollBars.None,
                WordWrap = true,
                Width = RowWidth,
                // One line of slack: TextRenderer and the TextBox's own wrapping can disagree by a
                // word at a boundary, and this is the direction where being wrong is invisible
                // rather than silently lossy.
                Height = measured.Height + font.Height,
                Margin = new Padding(Ui.SpaceM, 0, 0, Ui.SpaceXs)
            };
        }

        private static Button Button(string text, EventHandler onClick)
        {
            Button button = new Button
            {
                Text = text,
                Font = Ui.Body(),
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(Ui.SpaceM, Ui.SpaceXs, Ui.SpaceM, Ui.SpaceXs),
                Margin = new Padding(0, 0, Ui.SpaceS, 0),
                Cursor = Cursors.Hand,
                UseVisualStyleBackColor = true
            };

            button.Click += onClick;

            return button;
        }

        /// <remarks>
        /// <c>Ui.Border</c> rather than a literal: light's Border IS Gainsboro, so this is the same
        /// hairline it always was, but the theme walker can now recognise and re-colour it instead
        /// of flattening it into the surface it is meant to divide.
        /// </remarks>
        private static Control Separator()
            => new Panel
            {
                Height = 1,
                Dock = DockStyle.Top,
                BackColor = Ui.Border,
                Margin = new Padding(0, Ui.SpaceL, 0, Ui.SpaceM)
            };

        // -----------------------------------------------------------------------------------------
        // Wording
        // -----------------------------------------------------------------------------------------

        internal static string Ago(DateTime created)
        {
            int days = (int)(DateTime.Now.Date - created.Date).TotalDays;

            if (days <= 0)
                return "today";

            if (days == 1)
                return "yesterday";

            return days + " days ago";
        }

        private static string DescribeUndoPoints(IReadOnlyList<BackupFolder> snapshots)
        {
            if (snapshots.Count == 0)
                return "Undo points: none";

            return "Undo points: " + snapshots.Count + " pre-restore snapshot"
                + (snapshots.Count == 1 ? "" : "s")
                + " (newest " + snapshots[0].Created.ToString("d MMM yyyy") + ")";
        }

        private static string DescribeDisk()
        {
            try
            {
                DriveInfo drive = new DriveInfo(Path.GetPathRoot(Data.DataRootDir));

                return "Disk: " + (drive.AvailableFreeSpace / 1024 / 1024 / 1024) + " GB free on "
                    + drive.Name;
            }
            catch (Exception ex)
            {
                // A network or removed volume under the backup path. Naming the failure beats an
                // omitted line that reads as "plenty of room".
                return "Disk: free space unavailable (" + ex.Message + ")";
            }
        }
    }
}
