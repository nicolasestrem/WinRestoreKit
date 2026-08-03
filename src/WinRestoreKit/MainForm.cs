using DataHelper;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using Views;

namespace WinRestoreKit
{
    /// <summary>
    /// Owns the native-frame shell, rail navigation, and palette application.
    /// </summary>
    public partial class MainForm : Form
    {
        private readonly NavigationService navigation;
        private readonly BackupPageView backupPage;
        private readonly HomePageView homePage;
        private readonly HistoryPageView historyPage;
        private readonly RestoreWizardStep1View wizardStep1;
        private readonly RestoreWizardStep2View wizardStep2;
        private readonly NavButton[] navigationButtons;
        private ProgressPageView progressPage;
        private AboutPageView aboutPage;
        private bool railEnabled = true;
        private bool hasCompletedProgressResult;

        public MainForm()
        {
            InitializeComponent();
            Theme.RefreshFromMode();

            navigation = new NavigationService(contentPanel);
            backupPage = new BackupPageView();
            historyPage = new HistoryPageView(OnRestoreSourcePicked);
            wizardStep1 = new RestoreWizardStep1View(navigation, OnRestoreSourcePicked, () => ShowBackup());
            wizardStep2 = new RestoreWizardStep2View(navigation);
            homePage = new HomePageView(GoToBackUp, GoToHistory, ShowRestoreWizard);
            navigationButtons = new[] { btnHome, btnBackUp, btnProgress, btnRestore, btnHistory, btnAbout };

            backupPage.ShowRestoreView = ShowRestoreWizard;
            backupPage.StartBackupRequested = StartBackup;
            wizardStep2.StartRestoreRequested = StartRestore;
            navigation.Root = homePage;

            ConfigureShell();
            ApplyTheme();

            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
            RunCoordinator.RunningChanged += OnRunningChanged;
            FormClosed += MainForm_FormClosed;
        }

        private void MainForm_Shown(object sender, EventArgs e)
        {
            lblVersion.Text = GetMinorVersion(Program.GetCurrentVersionTostring());
            RefreshDestinationStatus();
            ShowHome();
            Theme.ApplyTitleBar(this);
        }

        private void ConfigureShell()
        {
            lblWordmarkPrefix.Font = Ui.BodyBold();
            lblWordmarkSuffix.Font = Ui.BodyBold();
            lblVersion.Font = Ui.MonoSmall();
            LayoutWordmark();
            btnPalette.Font = Ui.Kicker();
            lblKit.Font = Ui.Kicker();
            lblDestinationLabel.Font = Ui.Kicker();
            lblDestinationPath.Font = Ui.MonoSmall();
            lblDestinationSpace.Font = Ui.MonoSmall();
            btnPalette.FlatAppearance.BorderSize = 1;
            btnPalette.FlatAppearance.MouseDownBackColor = Color.Transparent;
            btnPalette.FlatAppearance.MouseOverBackColor = Color.Transparent;
            UpdatePaletteText();
            RefreshDestinationStatus();
            UpdateProgressButton(RunCoordinator.IsRunning);
        }

        private void LayoutWordmark()
        {
            const int wordmarkLeft = 42;

            lblWordmarkPrefix.Left = wordmarkLeft;
            lblWordmarkPrefix.Top = (titleBar.Height - lblWordmarkPrefix.Height) / 2;
            lblWordmarkSuffix.Left = lblWordmarkPrefix.Right + 2;
            lblWordmarkSuffix.Top = (titleBar.Height - lblWordmarkSuffix.Height) / 2;
            lblVersion.Left = lblWordmarkSuffix.Right + Ui.SpaceM;
            lblVersion.Top = (titleBar.Height - lblVersion.Height) / 2;
        }

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

            PaletteV2 palette = Theme.Current;
            BackColor = palette.Bg;
            titleBar.BackColor = palette.Surface;
            railPanel.BackColor = palette.Surface;
            railButtons.BackColor = palette.Surface;
            railFooter.BackColor = palette.Surface;
            railDivider.BackColor = palette.Divider;
            railFooterDivider.BackColor = palette.Divider;
            contentPanel.BackColor = palette.Bg;
            lblWordmarkPrefix.ForeColor = palette.Accent700;
            lblWordmarkSuffix.ForeColor = palette.Text;
            lblVersion.ForeColor = Color.FromArgb(115, palette.Text);
            lblKit.ForeColor = palette.Accent700;
            lblDestinationLabel.ForeColor = palette.Accent700;
            lblDestinationPath.ForeColor = palette.Text;
            lblDestinationSpace.ForeColor = Color.FromArgb(165, palette.Text);
            dotActive.BackColor = palette.Accent;
            btnPalette.BackColor = palette.Surface;
            btnPalette.ForeColor = palette.Text;
            btnPalette.FlatAppearance.BorderColor = palette.Divider;
            UpdatePaletteText();
            Theme.ApplyTitleBar(this);
        }

        private void btnPalette_Click(object sender, EventArgs e)
        {
            PaletteMode next = Theme.Mode switch
            {
                PaletteMode.Voltage => PaletteMode.Flux,
                PaletteMode.Flux => PaletteMode.FollowSystem,
                _ => PaletteMode.Voltage,
            };

            Theme.SetMode(next);
            ApplyTheme();
        }

        private void UpdatePaletteText()
        {
            btnPalette.Text = Theme.Mode switch
            {
                PaletteMode.Voltage => "VOLTAGE",
                PaletteMode.Flux => "FLUX",
                _ => "AUTO",
            };
        }

        private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (e.Category != UserPreferenceCategory.General || Theme.Mode != PaletteMode.FollowSystem)
                return;

            if (!IsHandleCreated || IsDisposed)
                return;

            try
            {
                BeginInvoke((Action)(() =>
                {
                    if (Theme.Mode != PaletteMode.FollowSystem)
                        return;

                    Theme.RefreshFromMode();
                    ApplyTheme();
                }));
            }
            catch (InvalidOperationException)
            {
            }
        }

        private void OnRunningChanged(bool running)
        {
            if (!IsHandleCreated || IsDisposed)
                return;

            if (InvokeRequired)
            {
                try
                {
                    BeginInvoke((Action)(() => ApplyRunningState(running)));
                }
                catch (InvalidOperationException)
                {
                }

                return;
            }

            ApplyRunningState(running);
        }

        private void ApplyRunningState(bool running)
        {
            if (!running && progressPage != null)
                hasCompletedProgressResult = true;

            UpdateProgressButton(running);
        }

        private void UpdateProgressButton(bool running)
        {
            bool hasReachableProgressPage = progressPage != null && (running || hasCompletedProgressResult);
            btnProgress.Enabled = railEnabled && hasReachableProgressPage;

            if (!btnProgress.Enabled)
                btnProgress.IsSelected = false;
        }

        private void SetRailEnabled(bool enabled)
        {
            railEnabled = enabled;
            btnHome.Enabled = enabled;
            btnBackUp.Enabled = enabled;
            btnRestore.Enabled = enabled;
            btnHistory.Enabled = enabled;
            btnAbout.Enabled = enabled;
            UpdateProgressButton(RunCoordinator.IsRunning);
        }

        private void btnHome_Click(object sender, EventArgs e) => ShowHome();

        private void btnProgress_Click(object sender, EventArgs e)
        {
            if (progressPage == null || (!RunCoordinator.IsRunning && !hasCompletedProgressResult))
                return;

            SelectNavigation(btnProgress);
            navigation.Push(progressPage);
        }

        private void btnBackUp_Click(object sender, EventArgs e) => ShowBackup();

        private void btnRestore_Click(object sender, EventArgs e) => ShowRestoreWizard();

        private void btnHistory_Click(object sender, EventArgs e) => ShowHistory();

        private void btnAbout_Click(object sender, EventArgs e)
        {
            if (aboutPage == null)
                aboutPage = new AboutPageView(navigation);

            SelectNavigation(btnAbout);
            navigation.Push(aboutPage);
        }

        private void ShowHome()
        {
            SelectNavigation(btnHome);
            navigation.Show(homePage);
        }

        private void ShowBackup()
        {
            SelectNavigation(btnBackUp);
            navigation.Show(backupPage);
        }

        private void StartBackup(IReadOnlyList<BackupBase> selectedModules, string snapshotName,
            SnapshotCompression compression, string destination)
        {
            if (!RunCoordinator.TryStart())
                return;

            DismissCompletedProgressResult();

            try
            {
                progressPage = new ProgressPageView(navigation, selectedModules, snapshotName, compression, destination);
                UpdateProgressButton(RunCoordinator.IsRunning);
                SelectNavigation(btnProgress);
                navigation.Push(progressPage);
            }
            catch
            {
                RunCoordinator.SetRunning(false);
                throw;
            }
        }
        private void StartRestore(IReadOnlyList<BackupBase> selectedModules, BackupFolder folder)
        {
            if (!RunCoordinator.TryStart())
                return;

            DismissCompletedProgressResult();

            try
            {
                progressPage = ProgressPageView.ForRestore(navigation, selectedModules, folder);
                UpdateProgressButton(RunCoordinator.IsRunning);
                SelectNavigation(btnProgress);
                navigation.Push(progressPage);
            }
            catch
            {
                RunCoordinator.SetRunning(false);
                throw;
            }
        }

        /// <summary>
        /// Removes a completed result from the shell before a newly admitted run installs its replacement page.
        /// </summary>
        /// <remarks>
        /// Rail navigation, including the completed page's Back to Home action, only hides the result so it can
        /// be reopened. Starting a new backup or restore is the single dismissal point because its outcome
        /// supersedes the prior page.
        /// </remarks>
        private void DismissCompletedProgressResult()
        {
            hasCompletedProgressResult = false;
            progressPage = null;
        }


        private void ShowRestoreWizard()
        {
            SelectNavigation(btnRestore);
            navigation.Show(wizardStep1);
        }

        private void ShowHistory()
        {
            SelectNavigation(btnHistory);
            navigation.Show(historyPage);
        }

        private void SelectNavigation(NavButton selected)
        {
            foreach (NavButton button in navigationButtons)
                button.IsSelected = button == selected;
        }

        private void OnRestoreSourcePicked(BackupFolder folder)
        {
            wizardStep2.LoadFolder(folder);
            SelectNavigation(btnRestore);
            navigation.Push(wizardStep2);
        }

        private void GoToBackUp(IReadOnlyList<string> moduleTypeNames)
        {
            if (moduleTypeNames != null)
                backupPage.SelectModulesByTypeName(moduleTypeNames);

            ShowBackup();
        }

        private void GoToHistory(string backupFolderName)
        {
            historyPage.SelectFolder(backupFolderName);
            ShowHistory();
        }

        private void RefreshDestinationStatus()
        {
            string root = Data.DataRootDir;
            lblDestinationPath.Text = root;

            try
            {
                DriveInfo drive = new DriveInfo(Path.GetPathRoot(root));
                long freeGb = drive.AvailableFreeSpace / 1024 / 1024 / 1024;
                lblDestinationSpace.Text = freeGb + " GB free";
            }
            catch (Exception)
            {
                lblDestinationSpace.Text = "Storage unavailable";
            }
        }

        private static string GetMinorVersion(string version)
        {
            int secondDotIndex = version.IndexOf('.', version.IndexOf('.') + 1);
            return secondDotIndex == -1 ? "Version " + version : "Version " + version.Substring(0, secondDotIndex);
        }

        /// <summary>
        /// Runs the existing interactive update check without leaking an exception to a future About page.
        /// </summary>
        internal async Task<string> CheckForUpdatesAsync()
        {
            try
            {
                await UpdateCheck.CheckForUpdatesAsync();
                return "Update check completed.";
            }
            catch (Exception)
            {
                return "Update check failed.";
            }
        }

        private void MainForm_FormClosed(object sender, FormClosedEventArgs e)
        {
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            RunCoordinator.RunningChanged -= OnRunningChanged;
        }
    }
}
