using DataHelper;
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;
using WinRestoreKit;

namespace Views
{
    internal sealed partial class AboutPageView : UserControl
    {
        private const string ReleasesUrl = "https://github.com/nicolasestrem/WinRestoreKit/releases";

        private readonly AccentButton checkForUpdatesButton;
        private readonly Label updateStatus;

        internal AboutPageView(NavigationService navigation)
        {
            _ = navigation;

            BackColor = Theme.Current.Bg;
            Dock = DockStyle.Fill;
            AutoScroll = true;
            Padding = new Padding(0);

            checkForUpdatesButton = CreateAccentButton("CHECK FOR UPDATES");
            updateStatus = new Label
            {
                AutoSize = true,
                Font = Ui.MonoSmall(),
                ForeColor = Theme.Current.TextMuted,
                Margin = new Padding(0, Ui.SpaceS, 0, 0),
                Text = "CHECK GITHUB RELEASES ON DEMAND."
            };

            BuildLayout();
            ApplyPalette();
        }

        protected override void OnBackColorChanged(EventArgs e)
        {
            base.OnBackColorChanged(e);
            ApplyPalette();
        }

        private void BuildLayout()
        {
            TableLayoutPanel root = new TableLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                Dock = DockStyle.Top,
                Padding = new Padding(0, 0, 0, Ui.SpaceL)
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            TableLayoutPanel content = new TableLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                Dock = DockStyle.Top,
                Margin = new Padding(0)
            };
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 280f));
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            Control identity = BuildIdentityColumn();
            Control details = BuildDetailsColumn();
            content.Controls.Add(identity, 0, 0);
            content.Controls.Add(details, 1, 0);

            root.Controls.Add(content, 0, 0);
            root.Controls.Add(CreateFooter(), 0, 1);
            Controls.Add(root);

            SizeChanged += (sender, e) => UpdateContentLayout(content, identity, details);
            UpdateContentLayout(content, identity, details);
        }

        private Control BuildIdentityColumn()
        {
            TableLayoutPanel column = new TableLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                Dock = DockStyle.Top,
                Margin = new Padding(0, 0, Ui.SpaceL, 0),
                Padding = new Padding(0)
            };
            column.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            BlueprintFrame markFrame = new BlueprintFrame
            {
                Size = new Size(180, 180),
                Margin = new Padding(0, 0, 0, Ui.SpaceM)
            };
            markFrame.Controls.Add(new KeyedMark
            {
                Location = new Point(34, 34),
                Size = new Size(112, 112)
            });

            FlowLayoutPanel wordmark = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.LeftToRight,
                Margin = new Padding(0, 0, 0, Ui.SpaceS),
                Padding = new Padding(0),
                WrapContents = false
            };
            wordmark.Controls.Add(new Label
            {
                AutoSize = true,
                Font = FontLoader.Load(Ui.FontHeading, 26f, FontStyle.Bold),
                ForeColor = Theme.Current.Accent700,
                Margin = new Padding(0),
                Text = "Win"
            });
            wordmark.Controls.Add(new Label
            {
                AutoSize = true,
                Font = FontLoader.Load(Ui.FontHeading, 26f, FontStyle.Bold),
                ForeColor = Theme.Current.Text,
                Margin = new Padding(0),
                Text = "RestoreKit"
            });

            Label buildDetail = new Label
            {
                AutoSize = true,
                Font = Ui.MonoSmall(),
                ForeColor = Theme.Current.TextMuted,
                Margin = new Padding(0, 0, 0, Ui.SpaceS),
                MaximumSize = new Size(270, 0),
                Text = "VERSION " + VersionInfo.GetCurrentVersion(typeof(AboutPageView).Assembly)
                    + Environment.NewLine + "BUILD " + BuildIdentity()
                    + Environment.NewLine + "OS " + Environment.OSVersion.VersionString
            };

            FlowLayoutPanel tags = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.LeftToRight,
                Margin = new Padding(0),
                Padding = new Padding(0),
                WrapContents = false
            };
            tags.Controls.Add(CreateTag("MIT", TagVariant.Accent));
            tags.Controls.Add(CreateTag("PORTABLE", TagVariant.Outline));

            column.Controls.Add(markFrame, 0, 0);
            column.Controls.Add(wordmark, 0, 1);
            column.Controls.Add(buildDetail, 0, 2);
            column.Controls.Add(tags, 0, 3);
            return column;
        }

        private Control BuildDetailsColumn()
        {
            TableLayoutPanel column = new TableLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                Dock = DockStyle.Top,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
            column.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            column.Controls.Add(CreateLabel("ABOUT", Ui.Kicker(), Theme.Current.Accent700, Ui.SpaceXs), 0, 0);
            column.Controls.Add(CreateLabel(
                "A SETTINGS BACKUP FOR PEOPLE WHO CHANGE SETTINGS",
                Ui.Heading(), Theme.Current.Text, Ui.SpaceS), 0, 1);
            column.Controls.Add(CreateLabel(
                "Back up, copy and restore Windows settings locally. Each snapshot stays in a portable folder you can inspect and keep.",
                Ui.Body(), Theme.Current.TextMuted, Ui.SpaceL), 0, 2);
            column.Controls.Add(BuildKeyValueGrid(), 0, 3);
            column.Controls.Add(BuildActions(), 0, 4);

            return column;
        }

        private Control BuildKeyValueGrid()
        {
            TableLayoutPanel grid = new TableLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                Dock = DockStyle.Top,
                Margin = new Padding(0, 0, 0, Ui.SpaceL),
                Padding = new Padding(0)
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));

            grid.Controls.Add(CreateKeyValue("SNAPSHOT FORMAT", "Folder + JSON manifest"), 0, 0);
            grid.Controls.Add(CreateKeyValue("STORAGE PATH", StorageSummary()), 1, 0);
            grid.Controls.Add(CreateKeyValue("ELEVATION", IsElevated() ? "Administrator" : "Standard user"), 0, 1);
            grid.Controls.Add(CreateKeyValue("SCHEDULE", "On demand"), 1, 1);
            grid.Controls.Add(CreateKeyValue("UPDATES", "Manual GitHub check"), 0, 2);
            grid.Controls.Add(CreateKeyValue("LICENSE", "MIT"), 1, 2);
            return grid;
        }

        private Control BuildActions()
        {
            TableLayoutPanel actions = new TableLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                Dock = DockStyle.Top,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            BlueprintFrame updateFrame = new BlueprintFrame
            {
                Height = 46,
                Width = 210,
                Margin = new Padding(0, 0, 0, Ui.SpaceS)
            };
            checkForUpdatesButton.Dock = DockStyle.Fill;
            checkForUpdatesButton.Click += checkForUpdatesButton_Click;
            updateFrame.Controls.Add(checkForUpdatesButton);

            FlowLayoutPanel lowerActions = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.LeftToRight,
                Margin = new Padding(0),
                Padding = new Padding(0),
                WrapContents = true
            };
            lowerActions.Controls.Add(CreateSecondaryButton("OPEN LOG FOLDER", openLogFolderButton_Click));
            lowerActions.Controls.Add(CreateReleaseNotesLink());

            actions.Controls.Add(updateFrame, 0, 0);
            actions.Controls.Add(lowerActions, 0, 1);
            actions.Controls.Add(updateStatus, 0, 2);
            return actions;
        }

        private Control CreateFooter()
            => CreateLabel(
                "Registry access via offline hive parsing. No telemetry, no account, no background service.",
                Ui.Body(), Theme.Current.TextMuted, 0,
                new Padding(0, Ui.SpaceL, 0, 0));

        private static Control CreateKeyValue(string key, string value)
        {
            TableLayoutPanel cell = new TableLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                Dock = DockStyle.Top,
                Margin = new Padding(0, 0, Ui.SpaceM, Ui.SpaceM),
                Padding = new Padding(0, 0, Ui.SpaceS, 0)
            };
            cell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            cell.Controls.Add(CreateLabel(key, Ui.Kicker(), Theme.Current.Accent700, Ui.SpaceXs), 0, 0);
            cell.Controls.Add(CreateLabel(value, Ui.MonoSmall(), Theme.Current.Text, 0), 0, 1);
            return cell;
        }

        private static Label CreateLabel(string text, Font font, Color color, int bottomMargin, Padding? margin = null)
            => new Label
            {
                AutoSize = true,
                Dock = DockStyle.Top,
                Font = font,
                ForeColor = color,
                Margin = margin ?? new Padding(0, 0, 0, bottomMargin),
                MaximumSize = new Size(700, 0),
                Text = text
            };

        private static TagChip CreateTag(string text, TagVariant variant)
            => new TagChip
            {
                Text = text,
                Variant = variant,
                Margin = new Padding(0, 0, Ui.SpaceXs, 0)
            };

        private static AccentButton CreateAccentButton(string text)
            => new AccentButton
            {
                BackColor = Theme.Current.Accent,
                Cursor = Cursors.Hand,
                FlatStyle = FlatStyle.Flat,
                Font = Ui.Kicker(),
                ForeColor = Theme.Current.Bg,
                Text = text,
                UseVisualStyleBackColor = false
            };

        private static Button CreateSecondaryButton(string text, EventHandler click)
        {
            Button button = new Button
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = Theme.Current.Surface,
                Cursor = Cursors.Hand,
                FlatStyle = FlatStyle.Flat,
                Font = Ui.Kicker(),
                ForeColor = Theme.Current.Text,
                Margin = new Padding(0, 0, Ui.SpaceS, 0),
                Padding = new Padding(Ui.SpaceM, Ui.SpaceS, Ui.SpaceM, Ui.SpaceS),
                Text = text,
                UseVisualStyleBackColor = false
            };
            button.FlatAppearance.BorderColor = Theme.Current.Divider;
            button.Click += click;
            return button;
        }

        private static LinkLabel CreateReleaseNotesLink()
        {
            LinkLabel link = new LinkLabel
            {
                AutoSize = true,
                Cursor = Cursors.Hand,
                Font = Ui.Kicker(),
                LinkColor = Theme.Current.Accent700,
                Margin = new Padding(0, Ui.SpaceS, 0, 0),
                Text = "RELEASE NOTES"
            };
            link.LinkClicked += releaseNotesLink_LinkClicked;
            return link;
        }

        private async void checkForUpdatesButton_Click(object sender, EventArgs e)
        {
            checkForUpdatesButton.Enabled = false;
            SetUpdateStatus("CHECKING FOR UPDATES...", Theme.Current.Accent700);

            try
            {
                WinFormsUpdatePresenter updates = new WinFormsUpdatePresenter(
                    new UpdateCheckService(),
                    this,
                    VersionInfo.GetCurrentVersion(typeof(AboutPageView).Assembly));
                await updates.CheckAsync(CancellationToken.None);
                SetUpdateStatus("UPDATE CHECK FINISHED.", Theme.Current.Accent700);
            }
            catch (Exception ex)
            {
                SetUpdateStatus("UPDATE CHECK FAILED: " + ex.Message, Theme.Current.Accent2_600);
            }
            finally
            {
                checkForUpdatesButton.Enabled = true;
            }
        }

        private void openLogFolderButton_Click(object sender, EventArgs e)
        {
            try
            {
                Directory.CreateDirectory(Data.DataRootDir);
                Process.Start(new ProcessStartInfo(Data.DataRootDir) { UseShellExecute = true });
                SetUpdateStatus("OPENED " + Data.DataRootDir, Theme.Current.Accent700);
            }
            catch (Exception ex)
            {
                SetUpdateStatus("LOG FOLDER COULD NOT BE OPENED: " + ex.Message, Theme.Current.Accent2_600);
            }
        }

        private static void releaseNotesLink_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            Utils.OpenUrl(ReleasesUrl);
        }

        private void SetUpdateStatus(string text, Color color)
        {
            updateStatus.Text = text;
            updateStatus.ForeColor = color;
        }

        private void ApplyPalette()
        {
            if (checkForUpdatesButton == null)
                return;

            checkForUpdatesButton.BackColor = Theme.Current.Accent;
            checkForUpdatesButton.ForeColor = Theme.Current.Bg;
        }

        private static string BuildIdentity()
            => typeof(AboutPageView).Assembly.GetName().Version?.ToString() ?? "Unknown";

        private static bool IsElevated()
        {
            try
            {
                using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                {
                    return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static string StorageSummary()
        {
            try
            {
                DriveInfo drive = new DriveInfo(Path.GetPathRoot(Data.DataRootDir));
                long freeGigabytes = drive.AvailableFreeSpace / 1024 / 1024 / 1024;
                return Data.DataRootDir + Environment.NewLine + freeGigabytes + " GB free";
            }
            catch (Exception ex)
            {
                return Data.DataRootDir + Environment.NewLine + "Free space unavailable: " + ex.Message;
            }
        }

        private void UpdateContentLayout(TableLayoutPanel content, Control identity, Control details)
        {
            bool narrow = ClientSize.Width > 0 && ClientSize.Width < 720;
            if (narrow && (content.GetColumn(details) != 0 || content.GetRow(details) != 1))
            {
                content.ColumnStyles[0] = new ColumnStyle(SizeType.Percent, 100f);
                content.ColumnStyles[1] = new ColumnStyle(SizeType.Absolute, 0f);
                content.RowCount = 2;
                content.RowStyles.Clear();
                content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                content.SetColumn(identity, 0);
                content.SetRow(identity, 0);
                content.SetColumn(details, 0);
                content.SetRow(details, 1);
                identity.Margin = new Padding(0, 0, 0, Ui.SpaceL);
            }
            else if (!narrow && (content.GetColumn(details) != 1 || content.GetRow(details) != 0))
            {
                content.ColumnStyles[0] = new ColumnStyle(SizeType.Absolute, 280f);
                content.ColumnStyles[1] = new ColumnStyle(SizeType.Percent, 100f);
                content.RowCount = 1;
                content.RowStyles.Clear();
                content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                content.SetColumn(identity, 0);
                content.SetRow(identity, 0);
                content.SetColumn(details, 1);
                content.SetRow(details, 0);
                identity.Margin = new Padding(0, 0, Ui.SpaceL, 0);
            }
        }
    }
}
