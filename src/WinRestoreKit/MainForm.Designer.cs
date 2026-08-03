namespace WinRestoreKit
{
    partial class MainForm
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && components != null)
                components.Dispose();

            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        private void InitializeComponent()
        {
            this.titleBar = new System.Windows.Forms.Panel();
            this.keyedMark = new KeyedMark();
            this.lblWordmarkPrefix = new AccentLabel();
            this.lblWordmarkSuffix = new System.Windows.Forms.Label();
            this.lblVersion = new System.Windows.Forms.Label();
            this.btnPalette = new System.Windows.Forms.Button();
            this.contentSplit = new System.Windows.Forms.TableLayoutPanel();
            this.railPanel = new System.Windows.Forms.Panel();
            this.lblKit = new AccentLabel();
            this.railButtons = new System.Windows.Forms.FlowLayoutPanel();
            this.btnHome = new NavButton();
            this.btnBackUp = new NavButton();
            this.btnProgress = new NavButton();
            this.btnRestore = new NavButton();
            this.btnHistory = new NavButton();
            this.btnAbout = new NavButton();
            this.railFooter = new System.Windows.Forms.Panel();
            this.railFooterDivider = new System.Windows.Forms.Panel();
            this.lblDestinationLabel = new AccentLabel();
            this.lblDestinationPath = new System.Windows.Forms.Label();
            this.dotActive = new System.Windows.Forms.Panel();
            this.lblDestinationSpace = new System.Windows.Forms.Label();
            this.railDivider = new System.Windows.Forms.Panel();
            this.contentPanel = new System.Windows.Forms.Panel();
            this.titleBar.SuspendLayout();
            this.contentSplit.SuspendLayout();
            this.railPanel.SuspendLayout();
            this.railButtons.SuspendLayout();
            this.railFooter.SuspendLayout();
            this.SuspendLayout();
            // 
            // titleBar
            // 
            this.titleBar.Controls.Add(this.keyedMark);
            this.titleBar.Controls.Add(this.lblWordmarkPrefix);
            this.titleBar.Controls.Add(this.lblWordmarkSuffix);
            this.titleBar.Controls.Add(this.lblVersion);
            this.titleBar.Controls.Add(this.btnPalette);
            this.titleBar.Dock = System.Windows.Forms.DockStyle.Top;
            this.titleBar.Height = 44;
            this.titleBar.Name = "titleBar";
            this.titleBar.TabIndex = 0;
            // 
            // keyedMark
            // 
            this.keyedMark.BackColor = System.Drawing.Color.Transparent;
            this.keyedMark.Location = new System.Drawing.Point(14, 12);
            this.keyedMark.Name = "keyedMark";
            this.keyedMark.Size = new System.Drawing.Size(20, 20);
            this.keyedMark.TabIndex = 0;
            this.keyedMark.TabStop = false;
            // 
            // lblWordmarkPrefix
            // 
            this.lblWordmarkPrefix.AutoSize = true;
            this.lblWordmarkPrefix.Location = new System.Drawing.Point(42, 13);
            this.lblWordmarkPrefix.Name = "lblWordmarkPrefix";
            this.lblWordmarkPrefix.Size = new System.Drawing.Size(28, 17);
            this.lblWordmarkPrefix.TabIndex = 1;
            this.lblWordmarkPrefix.Text = "Win";
            // 
            // lblWordmarkSuffix
            // 
            this.lblWordmarkSuffix.AutoSize = true;
            this.lblWordmarkSuffix.Location = new System.Drawing.Point(69, 13);
            this.lblWordmarkSuffix.Name = "lblWordmarkSuffix";
            this.lblWordmarkSuffix.Size = new System.Drawing.Size(75, 17);
            this.lblWordmarkSuffix.TabIndex = 2;
            this.lblWordmarkSuffix.Text = "RestoreKit";
            // 
            // lblVersion
            // 
            this.lblVersion.AutoSize = true;
            this.lblVersion.Location = new System.Drawing.Point(150, 15);
            this.lblVersion.Name = "lblVersion";
            this.lblVersion.Size = new System.Drawing.Size(47, 15);
            this.lblVersion.TabIndex = 3;
            this.lblVersion.Text = "Version";
            // 
            // btnPalette
            // 
            this.btnPalette.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
            this.btnPalette.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnPalette.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnPalette.Location = new System.Drawing.Point(924, 9);
            this.btnPalette.Name = "btnPalette";
            this.btnPalette.Size = new System.Drawing.Size(104, 26);
            this.btnPalette.TabIndex = 0;
            this.btnPalette.Text = "VOLTAGE";
            this.btnPalette.UseVisualStyleBackColor = false;
            this.btnPalette.Click += new System.EventHandler(this.btnPalette_Click);
            // 
            // contentSplit
            // 
            this.contentSplit.ColumnCount = 3;
            this.contentSplit.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 214F));
            this.contentSplit.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 1F));
            this.contentSplit.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.contentSplit.Controls.Add(this.railPanel, 0, 0);
            this.contentSplit.Controls.Add(this.railDivider, 1, 0);
            this.contentSplit.Controls.Add(this.contentPanel, 2, 0);
            this.contentSplit.Dock = System.Windows.Forms.DockStyle.Fill;
            this.contentSplit.Name = "contentSplit";
            this.contentSplit.RowCount = 1;
            this.contentSplit.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.contentSplit.TabIndex = 1;
            // 
            // railPanel
            // 
            this.railPanel.Controls.Add(this.lblKit);
            this.railPanel.Controls.Add(this.railButtons);
            this.railPanel.Controls.Add(this.railFooter);
            this.railPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.railPanel.Name = "railPanel";
            this.railPanel.TabIndex = 0;
            // 
            // lblKit
            // 
            this.lblKit.AutoSize = true;
            this.lblKit.Location = new System.Drawing.Point(12, 17);
            this.lblKit.Name = "lblKit";
            this.lblKit.Size = new System.Drawing.Size(19, 15);
            this.lblKit.TabIndex = 0;
            this.lblKit.Text = "KIT";
            // 
            // railButtons
            // 
            this.railButtons.Controls.Add(this.btnHome);
            this.railButtons.Controls.Add(this.btnBackUp);
            this.railButtons.Controls.Add(this.btnProgress);
            this.railButtons.Controls.Add(this.btnRestore);
            this.railButtons.Controls.Add(this.btnHistory);
            this.railButtons.Controls.Add(this.btnAbout);
            this.railButtons.FlowDirection = System.Windows.Forms.FlowDirection.TopDown;
            this.railButtons.Location = new System.Drawing.Point(12, 42);
            this.railButtons.Margin = new System.Windows.Forms.Padding(0);
            this.railButtons.Name = "railButtons";
            this.railButtons.Padding = new System.Windows.Forms.Padding(0);
            this.railButtons.Size = new System.Drawing.Size(190, 240);
            this.railButtons.TabIndex = 1;
            this.railButtons.WrapContents = false;
            // 
            // btnHome
            // 
            this.btnHome.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnHome.Label = "Home";
            this.btnHome.LucidePath = "M3.5 10.6 12 3.6l8.5 7v9.8h-17z";
            this.btnHome.Margin = new System.Windows.Forms.Padding(0, 0, 0, 2);
            this.btnHome.Name = "btnHome";
            this.btnHome.Size = new System.Drawing.Size(190, 38);
            this.btnHome.TabIndex = 0;
            this.btnHome.Click += new System.EventHandler(this.btnHome_Click);
            // 
            // btnBackUp
            // 
            this.btnBackUp.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnBackUp.Label = "Back up";
            this.btnBackUp.LucidePath = "M12 16.5V8m0 0-3.4 3.4M12 8l3.4 3.4M5 17.5a4.2 4.2 0 0 1 1.4-8.2 6 6 0 0 1 11.4-1.4A4.1 4.1 0 0 1 19.4 17.5";
            this.btnBackUp.Margin = new System.Windows.Forms.Padding(0, 0, 0, 2);
            this.btnBackUp.Name = "btnBackUp";
            this.btnBackUp.Size = new System.Drawing.Size(190, 38);
            this.btnBackUp.TabIndex = 1;
            this.btnBackUp.Click += new System.EventHandler(this.btnBackUp_Click);
            // 
            // btnProgress
            // 
            this.btnProgress.Enabled = false;
            this.btnProgress.Label = "In progress";
            this.btnProgress.LucidePath = "M3 12h3.6L9 5l4 14 2.3-7H21";
            this.btnProgress.Margin = new System.Windows.Forms.Padding(0, 0, 0, 2);
            this.btnProgress.Name = "btnProgress";
            this.btnProgress.Size = new System.Drawing.Size(190, 38);
            this.btnProgress.TabIndex = 2;
            this.btnProgress.Click += new System.EventHandler(this.btnProgress_Click);
            // 
            // btnRestore
            // 
            this.btnRestore.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnRestore.Label = "Restore";
            this.btnRestore.LucidePath = "M3.6 12a8.4 8.4 0 1 0 2.9-6.3M3.2 4.3v5h5";
            this.btnRestore.Margin = new System.Windows.Forms.Padding(0, 0, 0, 2);
            this.btnRestore.Name = "btnRestore";
            this.btnRestore.Size = new System.Drawing.Size(190, 38);
            this.btnRestore.TabIndex = 3;
            this.btnRestore.Click += new System.EventHandler(this.btnRestore_Click);
            // 
            // btnHistory
            // 
            this.btnHistory.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnHistory.Label = "History";
            this.btnHistory.LucidePath = "M4.5 6.5h15M4.5 12h15M4.5 17.5h9.5";
            this.btnHistory.Margin = new System.Windows.Forms.Padding(0, 0, 0, 2);
            this.btnHistory.Name = "btnHistory";
            this.btnHistory.Size = new System.Drawing.Size(190, 38);
            this.btnHistory.TabIndex = 4;
            this.btnHistory.Click += new System.EventHandler(this.btnHistory_Click);
            // 
            // btnAbout
            // 
            this.btnAbout.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnAbout.Label = "About";
            this.btnAbout.LucidePath = "M12 10.8V17M12 7.6h.01M12 21a9 9 0 1 0 0-18 9 9 0 0 0 0 18z";
            this.btnAbout.Margin = new System.Windows.Forms.Padding(0);
            this.btnAbout.Name = "btnAbout";
            this.btnAbout.Size = new System.Drawing.Size(190, 38);
            this.btnAbout.TabIndex = 5;
            this.btnAbout.Click += new System.EventHandler(this.btnAbout_Click);
            // 
            // railFooter
            // 
            this.railFooter.Controls.Add(this.lblDestinationSpace);
            this.railFooter.Controls.Add(this.dotActive);
            this.railFooter.Controls.Add(this.lblDestinationPath);
            this.railFooter.Controls.Add(this.lblDestinationLabel);
            this.railFooter.Controls.Add(this.railFooterDivider);
            this.railFooter.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.railFooter.Height = 108;
            this.railFooter.Name = "railFooter";
            this.railFooter.TabIndex = 2;
            // 
            // railFooterDivider
            // 
            this.railFooterDivider.Dock = System.Windows.Forms.DockStyle.Top;
            this.railFooterDivider.Height = 1;
            this.railFooterDivider.Name = "railFooterDivider";
            this.railFooterDivider.TabIndex = 0;
            // 
            // lblDestinationLabel
            // 
            this.lblDestinationLabel.AutoSize = true;
            this.lblDestinationLabel.Location = new System.Drawing.Point(12, 15);
            this.lblDestinationLabel.Name = "lblDestinationLabel";
            this.lblDestinationLabel.Size = new System.Drawing.Size(78, 15);
            this.lblDestinationLabel.TabIndex = 1;
            this.lblDestinationLabel.Text = "DESTINATION";
            // 
            // lblDestinationPath
            // 
            this.lblDestinationPath.AutoEllipsis = true;
            this.lblDestinationPath.Location = new System.Drawing.Point(12, 37);
            this.lblDestinationPath.Name = "lblDestinationPath";
            this.lblDestinationPath.Size = new System.Drawing.Size(190, 19);
            this.lblDestinationPath.TabIndex = 2;
            this.lblDestinationPath.Text = "app";
            // 
            // dotActive
            // 
            this.dotActive.Location = new System.Drawing.Point(12, 71);
            this.dotActive.Name = "dotActive";
            this.dotActive.Size = new System.Drawing.Size(6, 6);
            this.dotActive.TabIndex = 3;
            // 
            // lblDestinationSpace
            // 
            this.lblDestinationSpace.AutoEllipsis = true;
            this.lblDestinationSpace.Location = new System.Drawing.Point(26, 65);
            this.lblDestinationSpace.Name = "lblDestinationSpace";
            this.lblDestinationSpace.Size = new System.Drawing.Size(176, 19);
            this.lblDestinationSpace.TabIndex = 4;
            this.lblDestinationSpace.Text = "Storage unavailable";
            // 
            // railDivider
            // 
            this.railDivider.Dock = System.Windows.Forms.DockStyle.Fill;
            this.railDivider.Name = "railDivider";
            this.railDivider.TabIndex = 1;
            // 
            // contentPanel
            // 
            this.contentPanel.AutoScroll = true;
            this.contentPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.contentPanel.Name = "contentPanel";
            this.contentPanel.Padding = new System.Windows.Forms.Padding(30, 26, 30, 26);
            this.contentPanel.TabIndex = 2;
            // 
            // MainForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(96F, 96F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Dpi;
            this.ClientSize = new System.Drawing.Size(1080, 790);
            this.Controls.Add(this.contentSplit);
            this.Controls.Add(this.titleBar);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.Sizable;
            this.MinimumSize = new System.Drawing.Size(920, 600);
            this.Name = "MainForm";
            this.ShowIcon = false;
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "WinRestoreKit";
            this.Shown += new System.EventHandler(this.MainForm_Shown);
            this.titleBar.ResumeLayout(false);
            this.titleBar.PerformLayout();
            this.contentSplit.ResumeLayout(false);
            this.railPanel.ResumeLayout(false);
            this.railPanel.PerformLayout();
            this.railButtons.ResumeLayout(false);
            this.railFooter.ResumeLayout(false);
            this.railFooter.PerformLayout();
            this.ResumeLayout(false);
        }

        #endregion

        private System.Windows.Forms.Panel titleBar;
        private KeyedMark keyedMark;
        private AccentLabel lblWordmarkPrefix;
        private System.Windows.Forms.Label lblWordmarkSuffix;
        private System.Windows.Forms.Label lblVersion;
        private System.Windows.Forms.Button btnPalette;
        private System.Windows.Forms.TableLayoutPanel contentSplit;
        private System.Windows.Forms.Panel railPanel;
        private AccentLabel lblKit;
        private System.Windows.Forms.FlowLayoutPanel railButtons;
        private NavButton btnHome;
        private NavButton btnBackUp;
        private NavButton btnProgress;
        private NavButton btnRestore;
        private NavButton btnHistory;
        private NavButton btnAbout;
        private System.Windows.Forms.Panel railFooter;
        private System.Windows.Forms.Panel railFooterDivider;
        private AccentLabel lblDestinationLabel;
        private System.Windows.Forms.Label lblDestinationPath;
        private System.Windows.Forms.Panel dotActive;
        private System.Windows.Forms.Label lblDestinationSpace;
        private System.Windows.Forms.Panel railDivider;
        private System.Windows.Forms.Panel contentPanel;
    }
}
