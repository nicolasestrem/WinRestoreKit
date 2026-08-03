using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using WinRestoreKit;

namespace Views
{
    /// <summary>
    /// Runs one backup or restore and presents its live engine progress and final outcome.
    /// </summary>
    internal sealed class ProgressPageView : UserControl, IRunUi
    {
        private readonly NavigationService navigation;
        private readonly IReadOnlyList<BackupBase> modules;
        private readonly string snapshotName;
        private readonly SnapshotCompression compression;
        private readonly string destination;
        private readonly BackupFolder restoreFolder;
        private readonly RunControl runControl = new RunControl();
        private readonly BackupRestoreOrchestrator runner;
        private readonly LogHelper logger = LogHelper.Instance;
        private readonly ProgressLogSink logSink;
        private readonly Stopwatch elapsed = new Stopwatch();
        private readonly Timer elapsedTimer = new Timer { Interval = 1000 };

        private Label kickerLabel;
        private Label titleLabel;
        private Label percentLabel;
        private Label groupLabel;
        private Label timingLabel;
        private Label throughputLabel;
        private Label bytesLabel;
        private Label diagnosticsLabel;
        private Panel progressTrack;
        private Panel progressFill;
        private RichTextBox activityLog;
        private BlueprintFrame resultsFrame;
        private Label resultsHeadline;
        private Label resultsDetail;
        private FlowLayoutPanel outcomeList;
        private AccentButton backToHomeButton;
        private Button pauseButton;
        private Button cancelButton;

        private bool runStarted;
        private bool logSinkActive;
        private bool summaryShown;
        private string reportedElapsed;
        private string reportedRemaining;
        private int progressPercent;

        public ProgressPageView(NavigationService navigation, IReadOnlyList<BackupBase> modules,
                                string snapshotName, SnapshotCompression compression, string destination)
            : this(navigation, modules, snapshotName, compression, destination, null)
        {
        }

        private ProgressPageView(NavigationService navigation, IReadOnlyList<BackupBase> modules,
                                 string snapshotName, SnapshotCompression compression, string destination,
                                 BackupFolder restoreFolder)
        {
            this.snapshotName = snapshotName ?? string.Empty;
            this.compression = compression;
            this.destination = destination ?? string.Empty;
            this.restoreFolder = restoreFolder;
            this.navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
            this.modules = (modules ?? Array.Empty<BackupBase>()).Where(module => module != null).ToArray();
            runner = new BackupRestoreOrchestrator(this, runControl);
            logSink = new ProgressLogSink(this, AppendLog, ClearLog);

            BackColor = Theme.Current.Bg;
            Dock = DockStyle.Fill;
            AutoScroll = true;
            Padding = new Padding(30, 26, 30, 26);

            BuildLayout(this.snapshotName);
            elapsedTimer.Tick += elapsedTimer_Tick;
        }

        internal static ProgressPageView ForRestore(NavigationService navigation,
                                                    IReadOnlyList<BackupBase> modules, BackupFolder folder)
        {
            if (folder == null)
                throw new ArgumentNullException(nameof(folder));

            return new ProgressPageView(navigation, modules, folder.DisplayName, SnapshotCompression.Fast,
                string.Empty, folder);
        }

        private bool IsRestore => restoreFolder != null;

        protected override async void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            if (runStarted || DesignMode)
                return;

            runStarted = true;
            await RunAsync();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                runControl.RequestCancellation();
                elapsedTimer.Stop();
                elapsedTimer.Dispose();

                if (logSinkActive)
                {
                    logger.SetSink(null);
                    logSinkActive = false;
                }
            }

            base.Dispose(disposing);
        }

        private void BuildLayout(string snapshotName)
        {
            TableLayoutPanel layout = new TableLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                Dock = DockStyle.Top,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            kickerLabel = new Label
            {
                AutoSize = true,
                Font = Ui.Kicker(),
                ForeColor = Theme.Current.Accent700,
                Text = "RUNNING. DO NOT SLEEP THE MACHINE",
                Margin = new Padding(0, 0, 0, Ui.SpaceXs)
            };
            layout.Controls.Add(kickerLabel, 0, 0);

            titleLabel = new Label
            {
                AutoSize = true,
                Font = Ui.Heading(),
                ForeColor = Theme.Current.Text,
                Text = IsRestore ? "RESTORING " + snapshotName : "WRITING " + snapshotName.ToUpperInvariant(),
                Margin = new Padding(0, 0, 0, Ui.SpaceL)
            };
            layout.Controls.Add(titleLabel, 0, 1);

            BlueprintFrame progressFrame = CreateProgressFrame();
            progressFrame.Margin = new Padding(0, 0, 0, Ui.SpaceL);
            layout.Controls.Add(progressFrame, 0, 2);

            Label logHeading = new Label
            {
                AutoSize = true,
                Font = Ui.Kicker(),
                ForeColor = Theme.Current.Accent700,
                Text = "ACTIVITY LOG",
                Margin = new Padding(0, 0, 0, Ui.SpaceS)
            };
            layout.Controls.Add(logHeading, 0, 3);

            activityLog = new RichTextBox
            {
                BackColor = Blend(Theme.Current.Bg, Theme.Current.Text, 0.05f),
                BorderStyle = BorderStyle.None,
                DetectUrls = false,
                Dock = DockStyle.Fill,
                Font = Ui.Mono(),
                ForeColor = Theme.Current.Text,
                HideSelection = false,
                ReadOnly = true,
                ScrollBars = RichTextBoxScrollBars.Vertical,
                ShortcutsEnabled = false
            };

            BlueprintFrame logFrame = new BlueprintFrame
            {
                Height = 210,
                Dock = DockStyle.Top,
                Margin = new Padding(0, 0, 0, Ui.SpaceL)
            };
            logFrame.Controls.Add(activityLog);
            layout.Controls.Add(logFrame, 0, 4);

            resultsFrame = CreateResultsFrame();
            resultsFrame.Visible = false;
            resultsFrame.Margin = new Padding(0, 0, 0, Ui.SpaceL);
            layout.Controls.Add(resultsFrame, 0, 5);

            FlowLayoutPanel runActions = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Top,
                FlowDirection = FlowDirection.LeftToRight,
                Margin = new Padding(0, 0, 0, Ui.SpaceL),
                WrapContents = false
            };

            pauseButton = CreateSecondaryButton("PAUSE", pauseButton_Click, Theme.Current.Text, Theme.Current.Divider);
            cancelButton = CreateSecondaryButton("CANCEL RUN", cancelButton_Click, Theme.Current.Accent2_600,
                Theme.Current.Accent2);
            runActions.Controls.Add(pauseButton);
            runActions.Controls.Add(cancelButton);
            layout.Controls.Add(runActions, 0, 6);

            backToHomeButton = new AccentButton
            {
                AutoSize = true,
                BackColor = Theme.Current.Accent,
                FlatStyle = FlatStyle.Flat,
                Font = Ui.BodyBold(),
                ForeColor = Theme.Current.Bg,
                Padding = new Padding(14, 8, 14, 8),
                Text = "BACK TO HOME",
                Visible = false
            };
            backToHomeButton.FlatAppearance.BorderSize = 0;
            backToHomeButton.Click += backToHomeButton_Click;
            layout.Controls.Add(backToHomeButton, 0, 7);

            Controls.Add(layout);
        }

        private BlueprintFrame CreateProgressFrame()
        {
            BlueprintFrame frame = new BlueprintFrame
            {
                Height = 210,
                Dock = DockStyle.Top
            };

            TableLayoutPanel content = new TableLayoutPanel
            {
                ColumnCount = 2,
                Dock = DockStyle.Fill,
                Padding = new Padding(20),
                RowCount = 2
            };
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 54f));
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 46f));
            content.RowStyles.Add(new RowStyle(SizeType.Percent, 78f));
            content.RowStyles.Add(new RowStyle(SizeType.Percent, 22f));

            percentLabel = new Label
            {
                AutoSize = true,
                Font = Ui.Pct(),
                ForeColor = Theme.Current.Text,
                Text = "0%",
                TextAlign = ContentAlignment.MiddleLeft,
                Dock = DockStyle.Fill
            };
            content.Controls.Add(percentLabel, 0, 0);

            TableLayoutPanel details = new TableLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                Dock = DockStyle.Fill,
                RowCount = 5
            };

            groupLabel = CreateDetailLabel("WAITING FOR FIRST GROUP", Ui.BodyBold(), Theme.Current.Text);
            timingLabel = CreateDetailLabel("ELAPSED 00:00   REMAINING ESTIMATING", Ui.MonoSmall(), Theme.Current.TextMuted);
            throughputLabel = CreateDetailLabel("THROUGHPUT MEASURING", Ui.MonoSmall(), Theme.Current.TextMuted);
            bytesLabel = CreateDetailLabel("BYTES WRITTEN 0 B", Ui.MonoSmall(), Theme.Current.TextMuted);
            diagnosticsLabel = CreateDetailLabel("", Ui.MonoSmall(), Theme.Current.Accent2_600);

            details.Controls.Add(groupLabel, 0, 0);
            details.Controls.Add(timingLabel, 0, 1);
            details.Controls.Add(throughputLabel, 0, 2);
            details.Controls.Add(bytesLabel, 0, 3);
            details.Controls.Add(diagnosticsLabel, 0, 4);
            content.Controls.Add(details, 1, 0);

            progressTrack = new Panel
            {
                BackColor = Theme.Current.Divider,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, Ui.SpaceS, 0, 0),
                Height = 10
            };
            progressFill = new Panel
            {
                BackColor = Theme.Current.Accent,
                Height = 10,
                Left = 0,
                Top = 0,
                Width = 0
            };
            progressTrack.Controls.Add(progressFill);
            progressTrack.Resize += progressTrack_Resize;
            content.SetColumnSpan(progressTrack, 2);
            content.Controls.Add(progressTrack, 0, 1);

            frame.Controls.Add(content);
            return frame;
        }

        private BlueprintFrame CreateResultsFrame()
        {
            BlueprintFrame frame = new BlueprintFrame
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Top
            };

            TableLayoutPanel content = new TableLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                Dock = DockStyle.Top,
                Padding = new Padding(20)
            };

            resultsHeadline = new Label
            {
                AutoSize = true,
                Font = Ui.Heading2(),
                ForeColor = Theme.Current.Text,
                Margin = new Padding(0, 0, 0, Ui.SpaceS)
            };
            resultsDetail = new Label
            {
                AutoSize = true,
                Font = Ui.Body(),
                ForeColor = Theme.Current.TextMuted,
                MaximumSize = new Size(900, 0),
                Margin = new Padding(0, 0, 0, Ui.SpaceS)
            };
            outcomeList = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Top,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                Margin = Padding.Empty
            };

            content.Controls.Add(resultsHeadline, 0, 0);
            content.Controls.Add(resultsDetail, 0, 1);
            content.Controls.Add(outcomeList, 0, 2);
            frame.Controls.Add(content);
            return frame;
        }

        private static Button CreateSecondaryButton(string text, EventHandler click, Color foreground,
                                                    Color border)
        {
            Button button = new Button
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = Theme.Current.Surface,
                Cursor = Cursors.Hand,
                FlatStyle = FlatStyle.Flat,
                Font = Ui.Kicker(),
                ForeColor = foreground,
                Margin = new Padding(0, 0, Ui.SpaceS, 0),
                Padding = new Padding(Ui.SpaceM, Ui.SpaceS, Ui.SpaceM, Ui.SpaceS),
                Text = text,
                UseVisualStyleBackColor = false
            };
            button.FlatAppearance.BorderColor = border;
            button.Click += click;
            return button;
        }

        private static Label CreateDetailLabel(string text, Font font, Color color)
        {
            return new Label
            {
                AutoEllipsis = true,
                AutoSize = false,
                Dock = DockStyle.Top,
                Font = font,
                ForeColor = color,
                Height = 24,
                Text = text,
                TextAlign = ContentAlignment.MiddleRight
            };
        }

        private async System.Threading.Tasks.Task RunAsync()
        {
            RunVerb verb = IsRestore ? RunVerb.Restore : RunVerb.Backup;
            string caption = IsRestore ? "Restore" : "Backup";
            RunCoordinator.SetRunning(true);
            elapsed.Start();
            elapsedTimer.Start();
            logger.SetSink(logSink);
            logSinkActive = true;
            ((IRunUi)this).SetProgressText((IsRestore ? "Started restore " : "Started snapshot ") + snapshotName + ".");

            try
            {
                if (IsRestore)
                    await runner.RunRestore(modules, restoreFolder.Path + "\\");
                else
                    await runner.RunBackup(modules, destination, snapshotName, compression);

                if (!summaryShown)
                {
                    ((IRunUi)this).ShowSummary(
                        RunSummary.For(new List<ModuleOutcome>(), false, verb,
                            "the " + caption.ToLowerInvariant() + " runner returned without a result"),
                        caption,
                        new List<ModuleOutcome>());
                }
            }
            catch (Exception ex)
            {
                ((IRunUi)this).ShowSummary(
                    RunSummary.For(new List<ModuleOutcome>(), false, verb, ex.Message),
                    caption,
                    new List<ModuleOutcome>());
            }
            finally
            {
                elapsed.Stop();
                elapsedTimer.Stop();

                if (logSinkActive)
                {
                    logger.SetSink(null);
                    logSinkActive = false;
                }

                RunCoordinator.SetRunning(false);
            }
        }

        private void elapsedTimer_Tick(object sender, EventArgs e)
        {
            if (!summaryShown && string.IsNullOrWhiteSpace(reportedElapsed))
                timingLabel.Text = "ELAPSED " + FormatElapsed(elapsed.Elapsed) + "   REMAINING " + RemainingText();
        }

        private void progressTrack_Resize(object sender, EventArgs e)
        {
            progressFill.Width = progressTrack.ClientSize.Width * progressPercent / 100;
        }

        private void backToHomeButton_Click(object sender, EventArgs e)
        {
            if (navigation.Root != null)
                navigation.Show(navigation.Root);
            else
                navigation.Pop();
        }

        private void pauseButton_Click(object sender, EventArgs e)
        {
            if (summaryShown || runControl.IsCancellationRequested)
                return;

            if (runControl.IsPaused)
            {
                runControl.Resume();
                pauseButton.Text = "PAUSE";
                AppendLog("Run resumed. The next group may start.");
            }
            else
            {
                runControl.Pause();
                pauseButton.Text = "RESUME";
                AppendLog("Run paused. The active group will finish before pausing.");
            }
        }

        private void cancelButton_Click(object sender, EventArgs e)
        {
            if (summaryShown || runControl.IsCancellationRequested)
                return;

            DialogResult result = MessageBox.Show((IWin32Window)FindForm() ?? this,
                "Cancel this " + (IsRestore ? "restore" : "backup") +
                "? The active group will finish before cancellation. No further groups will start. " +
                "Already applied restored settings are not rolled back. Partial backup output created by this run " +
                "is removed only when it can be identified as new. Otherwise it is preserved without a trusted manifest.",
                "Cancel " + (IsRestore ? "restore" : "backup"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);

            if (result != DialogResult.Yes)
                return;

            runControl.RequestCancellation();
            pauseButton.Enabled = false;
            cancelButton.Enabled = false;
            AppendLog("Cancellation requested. The active group will finish before cancellation.");
        }

        private void AppendLog(string text)
        {
            if (IsDisposed || Disposing)
                return;

            if (InvokeRequired)
            {
                try
                {
                    BeginInvoke(new Action<string>(AppendLog), text);
                }
                catch (InvalidOperationException)
                {
                }

                return;
            }

            string message = (text ?? string.Empty).Trim();
            if (message.Length == 0)
                return;

            bool isProblem = message.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             message.IndexOf("failed", StringComparison.OrdinalIgnoreCase) >= 0;
            activityLog.SelectionStart = activityLog.TextLength;
            activityLog.SelectionColor = Theme.Current.TextMuted;
            activityLog.AppendText(DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + "  ");
            activityLog.SelectionColor = isProblem ? Theme.Current.Accent2_600 : Theme.Current.Accent700;
            activityLog.AppendText(message + Environment.NewLine);
            activityLog.SelectionColor = activityLog.ForeColor;
            activityLog.SelectionStart = activityLog.TextLength;
            activityLog.ScrollToCaret();
        }

        private void ClearLog()
        {
            if (IsDisposed || Disposing)
                return;

            if (InvokeRequired)
            {
                try
                {
                    BeginInvoke(new Action(ClearLog));
                }
                catch (InvalidOperationException)
                {
                }

                return;
            }

            activityLog.Clear();
        }

        private void RenderSummary(RunSummary summary, IReadOnlyList<ModuleOutcome> outcomes)
        {
            summaryShown = true;
            bool canceled = runControl.IsCancellationRequested;
            kickerLabel.Text = canceled ? "RUN CANCELED, INCOMPLETE" : "RUN COMPLETE";
            titleLabel.Text = summary.Headline.ToUpperInvariant();
            titleLabel.ForeColor = summary.State == RunState.Done ? Theme.Current.Text : Theme.Current.Accent2_600;
            resultsHeadline.Text = summary.Headline;
            resultsDetail.Text = summary.Detail;
            outcomeList.Controls.Clear();

            foreach (ModuleOutcome outcome in outcomes ?? Array.Empty<ModuleOutcome>())
            {
                if (outcome == null || outcome.Result == null)
                    continue;

                bool failed = outcome.Result.State == ResultState.Failed;
                Label row = new Label
                {
                    AutoSize = true,
                    Font = Ui.MonoSmall(),
                    ForeColor = failed ? Theme.Current.Accent2_600 : Theme.Current.Text,
                    Margin = new Padding(0, 0, 0, Ui.SpaceXs),
                    MaximumSize = new Size(900, 0),
                    Text = outcome.Title + ": " + outcome.Result.Reason
                };
                outcomeList.Controls.Add(row);
            }

            resultsFrame.Visible = true;
            pauseButton.Visible = false;
            cancelButton.Visible = false;
            backToHomeButton.Visible = true;
            AppendLog(summary.Headline);
            AppendLog(summary.Detail);
        }

        private void UpdateUi(Action action)
        {
            if (IsDisposed || Disposing)
                return;

            if (InvokeRequired)
            {
                try
                {
                    BeginInvoke(action);
                }
                catch (InvalidOperationException)
                {
                }

                return;
            }

            action();
        }

        private T InvokeUi<T>(Func<T> action, T fallback)
        {
            if (IsDisposed || Disposing)
                return fallback;

            try
            {
                if (InvokeRequired)
                    return (T)Invoke(action);

                return action();
            }
            catch (InvalidOperationException)
            {
                return fallback;
            }
        }

        private string RemainingText() => string.IsNullOrWhiteSpace(reportedRemaining) ? "ESTIMATING" : reportedRemaining;

        private static string FormatElapsed(TimeSpan duration)
        {
            return duration.TotalHours >= 1
                ? duration.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture)
                : duration.ToString(@"mm\:ss", CultureInfo.InvariantCulture);
        }

        private static string FormatBytes(long bytes)
        {
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double value = Math.Max(0, bytes);
            int unit = 0;

            while (value >= 1024d && unit < units.Length - 1)
            {
                value /= 1024d;
                unit++;
            }

            return unit == 0
                ? value.ToString("0", CultureInfo.InvariantCulture) + " " + units[unit]
                : value.ToString("0.0", CultureInfo.InvariantCulture) + " " + units[unit];
        }

        private static Color Blend(Color background, Color foreground, float amount)
        {
            return Color.FromArgb(
                (int)(background.R + (foreground.R - background.R) * amount),
                (int)(background.G + (foreground.G - background.G) * amount),
                (int)(background.B + (foreground.B - background.B) * amount));
        }

        void IRunUi.SetProgressText(string text)
        {
            UpdateUi(() =>
            {
                if (!string.Equals(text, "Choose settings", StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(text))
                {
                    groupLabel.Text = text;
                }

                AppendLog(text);
            });
        }

        void IRunUi.SetProgressPercent(int percent)
        {
            UpdateUi(() =>
            {
                progressPercent = Math.Max(0, Math.Min(100, percent));
                percentLabel.Text = progressPercent.ToString(CultureInfo.InvariantCulture) + "%";
                progressFill.Width = progressTrack.ClientSize.Width * progressPercent / 100;
            });
        }

        void IRunUi.SetProgressDetail(string groupInfo, string elapsedValue, string remaining, string throughput,
                                      long bytesWritten, int errors, int warnings)
        {
            UpdateUi(() =>
            {
                reportedElapsed = elapsedValue;
                reportedRemaining = remaining;

                if (!string.IsNullOrWhiteSpace(groupInfo))
                    groupLabel.Text = groupInfo;

                string visibleElapsed = string.IsNullOrWhiteSpace(reportedElapsed)
                    ? FormatElapsed(elapsed.Elapsed)
                    : reportedElapsed;
                timingLabel.Text = "ELAPSED " + visibleElapsed + "   REMAINING " + RemainingText();
                throughputLabel.Text = "THROUGHPUT " +
                                       (string.IsNullOrWhiteSpace(throughput) ? "MEASURING" : throughput);
                bytesLabel.Text = "BYTES WRITTEN " + (bytesWritten < 0 ? "N/A" : FormatBytes(bytesWritten));

                if (errors > 0 || warnings > 0)
                {
                    diagnosticsLabel.Text = "ERRORS " + errors.ToString(CultureInfo.InvariantCulture) +
                                            "   WARNINGS " + warnings.ToString(CultureInfo.InvariantCulture);
                }
                else
                {
                    diagnosticsLabel.Text = string.Empty;
                }
            });
        }

        IWin32Window IRunUi.Owner => (IWin32Window)FindForm() ?? this;

        void IRunUi.ShowSummary(RunSummary summary, string caption, IReadOnlyList<ModuleOutcome> outcomes)
        {
            InvokeUi(() =>
            {
                RenderSummary(summary, outcomes);
                return true;
            }, false);
        }

        IReadOnlyList<string> IRunUi.ShowConsentDialog(RestorePlan plan)
        {
            return InvokeUi(() =>
            {
                using (RestoreConfirmForm confirm = new RestoreConfirmForm(plan))
                {
                    IWin32Window owner = FindForm();
                    if (confirm.ShowDialog(owner ?? this) != DialogResult.OK)
                        return null;

                    return confirm.ConsentedProcessNames;
                }
            }, null);
        }

        bool IRunUi.ConfirmSnapshotOverride(string text, string caption)
        {
            return InvokeUi(() =>
                MessageBox.Show((IWin32Window)FindForm() ?? this, text, caption, MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.Yes,
                false);
        }

        void IRunUi.ShowPlanCompositionError(string text, string caption)
        {
            InvokeUi(() =>
            {
                MessageBox.Show((IWin32Window)FindForm() ?? this, text, caption, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return true;
            }, false);
        }

        void IRunUi.SetExplorerRestartVisible(bool visible)
        {
            if (visible)
                UpdateUi(() => AppendLog("Explorer restart is required to apply restored settings."));
        }
    }
}
