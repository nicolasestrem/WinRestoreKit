using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Views;

namespace WinRestoreKit
{
    /// <summary>
    /// The shell: a left rail and a content host. It owns the views and the navigation, nothing else.
    /// </summary>
    /// <remarks>
    /// Rail entries are constructed once and reused rather than rebuilt on each visit. That is what
    /// makes the backup page's checkbox selection, its log pane and its LogHelper target survive a
    /// trip to Home and back - the same lifetime the single backupPage field already had before the
    /// rail existed. Home is the exception in spirit only: the instance is reused, but it re-reads the
    /// backup directory every time it is shown, via IRefreshableView.
    ///
    /// Phase 4 PR 7 inverted the restore flow into a wizard (step 1 picks the backup, step 2 its
    /// contents), so the rail's Restore and the backup page's Restore button both open the wizard
    /// rather than the old module-set-first picker.
    /// </remarks>
    public partial class MainForm : Form
    {
        private readonly NavigationService navigation;

        private readonly BackupPageView backupPage;
        private readonly HomePageView homePage;
        private readonly HistoryPageView historyPage;
        private readonly RestoreWizardStep1View wizardStep1;
        private readonly RestoreWizardStep2View wizardStep2;

        /// <summary>
        /// Built on first use, unlike the rail pages.
        /// </summary>
        /// <remarks>
        /// Its constructor fetches the GitHub stargazer count. Building it up front would put a
        /// network request in every app start for a page most sessions never open - and the old
        /// code, which built it inside the click handler, did not.
        /// </remarks>
        private AboutPageView aboutPage;

        public MainForm()
        {
            InitializeComponent();

            // Before any view is constructed: the views read Ui.* (which forwards to the active
            // palette) as they build, so choosing the palette afterwards would leave them light
            // until the first re-apply.
            Theme.Use(Theme.IsDarkOs());

            navigation = new NavigationService(pnlForm);

            backupPage = new BackupPageView();
            historyPage = new HistoryPageView(OnRestoreSourcePicked);

            wizardStep1 = new RestoreWizardStep1View(navigation, OnRestoreSourcePicked, () => navigation.Show(backupPage));
            wizardStep2 = new RestoreWizardStep2View(navigation, running => SetRailEnabled(!running));

            homePage = new HomePageView(GoToBackUp, GoToHistory);

            // The backup page's Restore button opens the wizard (step 1). A delegate rather than a
            // reference to this form: the view needs one navigation, not the shell.
            backupPage.ShowRestoreView = () => navigation.Push(wizardStep1);

            // The backup page disables itself while a run is going, but the rail lives on the form
            // and stayed live - so its buttons could navigate away from a running backup, or reach
            // back into that page's state while the run was suspended at an await.
            backupPage.RunStateChanged = running => SetRailEnabled(!running);

            navigation.Root = homePage;

            SetStyle();

            // The rail pages are constructed once and swapped in and out of pnlForm, so only the
            // CURRENT one is in this form's control tree. A live theme change has to reach all of
            // them, which is why ApplyTheme walks each page rather than just `this`.
            ApplyTheme();

            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
            FormClosed += (s, e) => SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        }

        private void MainForm_Shown(object sender, EventArgs e)
        {
            checkVersion.Text = GetMinorVersion(Program.GetCurrentVersionTostring());
            lblDiskSpace.Text = Utils.GetSystemPartitionDiskSpaceInfo();

            navigation.Show(homePage);

            // Needs a created handle, so it happens here rather than in the constructor.
            Theme.ApplyTitleBar(this);
        }

        private void SetStyle()
        {
            BackColor = Ui.Surface;
            pnlRail.BackColor = Ui.RailSurface;
            pnlStatusBar.BackColor = Ui.RailSurface;

            // The form's own Font is deliberately NOT set. Assigning it here cascades into every
            // child that inherits, and the hosted UserControls scale their layout against the font
            // they were designed with. Fonts are applied per control instead, and PR 9's theme pass
            // is where a coordinated sweep belongs.

            lblDiskSpace.Font = Ui.Body();
            lblDiskSpace.ForeColor = Ui.Muted;

            checkVersion.Font = Ui.Body();
            checkVersion.ForeColor = Ui.TextPrimary;
            checkVersion.BackColor = Ui.RailSurface;
            checkVersion.FlatAppearance.CheckedBackColor = Ui.RailSurface;

            // Text only. A Button draws its whole caption in one font, so a Segoe Fluent Icons glyph
            // prefixed to a word either renders the word in the icon font or renders the glyph as a
            // fallback square - which is exactly what it did. Icons on the rail are PR 9's problem,
            // when the labels can carry a real glyph control beside them.
            StyleRailButton(btnHome, "Home");
            StyleRailButton(btnBackUp, "Back up");
            StyleRailButton(btnRestore, "Restore");
            StyleRailButton(btnHistory, "History");
            StyleRailButton(btnAbout, "About");
        }

        private static void StyleRailButton(Button button, string text)
        {
            button.Text = text;
            button.Font = Ui.Body();
            button.ForeColor = Ui.TextPrimary;
            button.BackColor = Ui.RailSurface;
            button.FlatAppearance.BorderSize = 0;
        }

        /// <summary>
        /// Shuts the rail while a backup or restore is running, and opens it again afterwards.
        /// </summary>
        /// <remarks>
        /// The whole rail, not just Restore. Navigating away mid-run would take the page reporting
        /// progress off screen and leave the user watching a screen that cannot tell them what is
        /// happening, and Home's "Back up again" re-ticks the tree a running backup is reading.
        /// About is included for consistency: there is no reason to browse to it during a restore,
        /// and a half-disabled rail invites the question of which half.
        /// </remarks>
        private void SetRailEnabled(bool enabled)
        {
            btnHome.Enabled = enabled;
            btnBackUp.Enabled = enabled;
            btnRestore.Enabled = enabled;
            btnHistory.Enabled = enabled;
            btnAbout.Enabled = enabled;
        }

        // -----------------------------------------------------------------------------------------
        // Theme
        // -----------------------------------------------------------------------------------------

        /// <summary>
        /// Paints the shell and every page with the active palette.
        /// </summary>
        /// <remarks>
        /// Each page is walked explicitly because only the page currently in pnlForm is part of this
        /// form's control tree - the others are held by fields and re-inserted on navigation, so a
        /// walk of `this` alone would leave them in the previous palette until they were rebuilt.
        /// SetStyle runs afterwards to re-assert the rail's own colours over the generic walk.
        /// </remarks>
        private void ApplyTheme()
        {
            Theme.Apply(this);

            Theme.Apply(backupPage);
            Theme.Apply(homePage);
            Theme.Apply(historyPage);
            Theme.Apply(wizardStep1);
            Theme.Apply(wizardStep2);

            if (aboutPage != null)
                Theme.Apply(aboutPage);

            SetStyle();
            Theme.ApplyTitleBar(this);
        }

        /// <summary>
        /// Follows a live OS light/dark switch.
        /// </summary>
        /// <remarks>
        /// SystemEvents raises this on a system thread, so the work is marshalled onto the UI thread
        /// before touching a single control. The subscription is static and would otherwise keep this
        /// form alive for the life of the process, which is why FormClosed unsubscribes.
        /// </remarks>
        private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (e.Category != UserPreferenceCategory.General)
                return;

            if (!IsHandleCreated || IsDisposed)
                return;

            BeginInvoke((Action)(() =>
            {
                bool dark = Theme.IsDarkOs();

                if (dark == Theme.IsDark)
                    return;

                Theme.Use(dark);
                ApplyTheme();
            }));
        }

        // -----------------------------------------------------------------------------------------
        // Rail
        // -----------------------------------------------------------------------------------------

        private void btnHome_Click(object sender, EventArgs e)
            => navigation.Show(homePage);

        private void btnBackUp_Click(object sender, EventArgs e)
            => navigation.Show(backupPage);

        /// <summary>
        /// Restore from the rail opens the wizard. The old module-set-first gate is gone: the wizard
        /// picks what to restore FROM the chosen backup, so there is nothing to validate first.
        /// </summary>
        private void btnRestore_Click(object sender, EventArgs e)
            => navigation.Show(wizardStep1);

        private void btnHistory_Click(object sender, EventArgs e)
            => navigation.Show(historyPage);

        private void btnAbout_Click(object sender, EventArgs e)
        {
            if (aboutPage == null)
                aboutPage = new AboutPageView(navigation);

            navigation.Push(aboutPage);
        }

        // -----------------------------------------------------------------------------------------
        // Wizard wiring + Home's actions
        // -----------------------------------------------------------------------------------------

        /// <summary>
        /// A restore source was picked - from wizard step 1, or from a History row. Load its
        /// contents into step 2 and push, so Back returns to wherever the user came from.
        /// </summary>
        private void OnRestoreSourcePicked(BackupFolder folder)
        {
            wizardStep2.LoadFolder(folder);
            navigation.Push(wizardStep2);
        }

        /// <summary>
        /// Home's "Back up again": go to the backup page, re-ticking what the last run recorded.
        /// </summary>
        /// <remarks>
        /// A null list means the last backup had no readable manifest, so there is nothing to
        /// re-select and the navigation is plain. Inventing a selection there would tick items the
        /// user never chose.
        /// </remarks>
        private void GoToBackUp(IReadOnlyList<string> moduleTypeNames)
        {
            if (moduleTypeNames != null)
                backupPage.SelectModulesByTypeName(moduleTypeNames);

            navigation.Show(backupPage);
        }

        /// <summary>
        /// Home's "View details": open History with that folder's row selected.
        /// </summary>
        /// <remarks>
        /// Reading, not restoring - so no selection is required. The timeline is rebuilt by
        /// NavigationService through IRefreshableView on the way in, which is why the selection is
        /// requested first and applied on the far side of that refresh.
        /// </remarks>
        private void GoToHistory(string backupFolderName)
        {
            historyPage.SelectFolder(backupFolderName);

            navigation.Show(historyPage);
        }

        // -----------------------------------------------------------------------------------------
        // Version and update check - unchanged behavior
        // -----------------------------------------------------------------------------------------

        private string GetMinorVersion(string version)
        {
            // Display everything until the second dot without the dot
            int secondDotIndex = version.IndexOf('.', version.IndexOf('.') + 1);
            if (secondDotIndex != -1)
            {
                version = version.Substring(0, secondDotIndex);
            }
            return $"Version {version}";
        }

        /// <remarks>
        /// async void because it is an event handler, which makes the try/catch mandatory rather
        /// than defensive: an exception escaping an async void handler is unhandled and takes the
        /// process down. The message matches the one the update check shows for its own failures.
        /// </remarks>
        private async void checkVersion_CheckedChanged(object sender, EventArgs e)
        {
            // Get full version
            string fullVersion = Program.GetCurrentVersionTostring();

            // Display version based on the CheckBox state
            checkVersion.Text = checkVersion.Checked ? fullVersion : GetMinorVersion(fullVersion);

            // Optionally, check for updates when checked
            if (checkVersion.Checked)
            {
                try
                {
                    await UpdateCheck.CheckForUpdatesAsync();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Checking for App updates failed.\n{ex.Message}");
                }
            }
        }
    }
}
