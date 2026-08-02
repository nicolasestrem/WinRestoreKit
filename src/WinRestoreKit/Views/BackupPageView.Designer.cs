namespace Views
{
    partial class BackupPageView
    {
        /// <summary>
        /// Erforderliche Designervariable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Verwendete Ressourcen bereinigen.
        /// </summary>
        /// <param name="disposing">True, wenn verwaltete Ressourcen gelöscht werden sollen; andernfalls False.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Vom Komponenten-Designer generierter Code

        /// <summary>
        /// Erforderliche Methode für die Designerunterstützung.
        /// Der Inhalt der Methode darf nicht mit dem Code-Editor geändert werden.
        /// </summary>
        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            this.headerLabel = new System.Windows.Forms.Label();
            this.linkSubHeader = new System.Windows.Forms.LinkLabel();
            this.treeConfigurations = new System.Windows.Forms.TreeView();
            this.txtInfo = new System.Windows.Forms.TextBox();
            this.btnBackup = new WinRestoreKit.AccentButton();
            this.btnMenuRestore = new System.Windows.Forms.Button();
            this.btnMenuMore = new System.Windows.Forms.Button();
            this.resultsPanel = new Views.RunResultsPanel();
            this.logToggle = new System.Windows.Forms.LinkLabel();
            this.rtbLog = new System.Windows.Forms.RichTextBox();
            this.tt = new System.Windows.Forms.ToolTip(this.components);
            this.contextMenu = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.menuSelectInstalled = new System.Windows.Forms.ToolStripMenuItem();
            this.menuSelectAll = new System.Windows.Forms.ToolStripMenuItem();
            this.toolStripSeparator1 = new System.Windows.Forms.ToolStripSeparator();
            this.textHeaderApp = new System.Windows.Forms.ToolStripTextBox();
            this.menuOpenBackupFolder = new System.Windows.Forms.ToolStripMenuItem();
            this.menuOpenAppBackups = new System.Windows.Forms.ToolStripMenuItem();
            this.root = new System.Windows.Forms.TableLayoutPanel();
            this.content = new System.Windows.Forms.TableLayoutPanel();
            this.leftColumn = new System.Windows.Forms.TableLayoutPanel();
            this.presetsPanel = new System.Windows.Forms.TableLayoutPanel();
            this.rbEverything = new System.Windows.Forms.RadioButton();
            this.rbDeveloper = new System.Windows.Forms.RadioButton();
            this.rbMinimal = new System.Windows.Forms.RadioButton();
            this.rbCustom = new System.Windows.Forms.RadioButton();
            this.lblWarnings = new System.Windows.Forms.Label();
            this.lnkAdvanced = new System.Windows.Forms.LinkLabel();
            this.actions = new System.Windows.Forms.TableLayoutPanel();
            this.contextMenu.SuspendLayout();
            this.root.SuspendLayout();
            this.content.SuspendLayout();
            this.leftColumn.SuspendLayout();
            this.presetsPanel.SuspendLayout();
            this.actions.SuspendLayout();
            this.SuspendLayout();
            //
            // headerLabel
            //
            this.headerLabel.AutoSize = true;
            this.headerLabel.Dock = System.Windows.Forms.DockStyle.Top;
            this.headerLabel.Margin = new System.Windows.Forms.Padding(0, 0, 0, WinRestoreKit.Ui.SpaceXs);
            this.headerLabel.Name = "headerLabel";
            this.headerLabel.Text = "Choose what to back up";
            //
            // linkSubHeader
            //
            this.linkSubHeader.AutoSize = true;
            this.linkSubHeader.Dock = System.Windows.Forms.DockStyle.Top;
            this.linkSubHeader.LinkBehavior = System.Windows.Forms.LinkBehavior.HoverUnderline;
            this.linkSubHeader.LinkColor = System.Drawing.Color.Black;
            this.linkSubHeader.Margin = new System.Windows.Forms.Padding(0, 0, 0, WinRestoreKit.Ui.SpaceS);
            this.linkSubHeader.Name = "linkSubHeader";
            this.linkSubHeader.TabStop = true;
            this.linkSubHeader.Text = "Choose settings";
            //
            // rbEverything
            //
            this.rbEverything.AutoSize = true;
            this.rbEverything.Dock = System.Windows.Forms.DockStyle.Top;
            this.rbEverything.Name = "rbEverything";
            this.rbEverything.Text = "Everything on this PC";
            this.rbEverything.UseVisualStyleBackColor = true;
            this.rbEverything.CheckedChanged += new System.EventHandler(this.rbPreset_CheckedChanged);
            //
            // rbDeveloper
            //
            this.rbDeveloper.AutoSize = true;
            this.rbDeveloper.Dock = System.Windows.Forms.DockStyle.Top;
            this.rbDeveloper.Name = "rbDeveloper";
            this.rbDeveloper.Text = "Developer machine";
            this.rbDeveloper.UseVisualStyleBackColor = true;
            this.rbDeveloper.CheckedChanged += new System.EventHandler(this.rbPreset_CheckedChanged);
            //
            // rbMinimal
            //
            this.rbMinimal.AutoSize = true;
            this.rbMinimal.Dock = System.Windows.Forms.DockStyle.Top;
            this.rbMinimal.Name = "rbMinimal";
            this.rbMinimal.Text = "Minimal privacy-safe";
            this.rbMinimal.UseVisualStyleBackColor = true;
            this.rbMinimal.CheckedChanged += new System.EventHandler(this.rbPreset_CheckedChanged);
            //
            // rbCustom
            //
            this.rbCustom.AutoSize = true;
            this.rbCustom.Dock = System.Windows.Forms.DockStyle.Top;
            this.rbCustom.Name = "rbCustom";
            this.rbCustom.Text = "Custom";
            this.rbCustom.UseVisualStyleBackColor = true;
            this.rbCustom.CheckedChanged += new System.EventHandler(this.rbPreset_CheckedChanged);
            //
            // lblWarnings
            //
            this.lblWarnings.AutoSize = true;
            this.lblWarnings.Dock = System.Windows.Forms.DockStyle.Top;
            this.lblWarnings.ForeColor = WinRestoreKit.Ui.Caution;
            this.lblWarnings.Margin = new System.Windows.Forms.Padding(20, WinRestoreKit.Ui.SpaceXs, 0, WinRestoreKit.Ui.SpaceXs);
            this.lblWarnings.Name = "lblWarnings";
            this.lblWarnings.Text = "";
            this.lblWarnings.Visible = false;
            //
            // lnkAdvanced
            //
            this.lnkAdvanced.AutoSize = true;
            this.lnkAdvanced.Dock = System.Windows.Forms.DockStyle.Top;
            this.lnkAdvanced.LinkBehavior = System.Windows.Forms.LinkBehavior.HoverUnderline;
            this.lnkAdvanced.LinkColor = System.Drawing.Color.DimGray;
            this.lnkAdvanced.Margin = new System.Windows.Forms.Padding(20, 0, 0, WinRestoreKit.Ui.SpaceS);
            this.lnkAdvanced.Name = "lnkAdvanced";
            this.lnkAdvanced.TabStop = true;
            this.lnkAdvanced.Text = "\u25B8 Advanced: full module list";
            this.lnkAdvanced.LinkClicked += new System.Windows.Forms.LinkLabelLinkClickedEventHandler(this.lnkAdvanced_LinkClicked);
            //
            // presetsPanel
            //
            this.presetsPanel.AutoSize = true;
            this.presetsPanel.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this.presetsPanel.ColumnCount = 1;
            this.presetsPanel.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.presetsPanel.Controls.Add(this.lnkAdvanced, 0, 5);
            this.presetsPanel.Controls.Add(this.lblWarnings, 0, 4);
            this.presetsPanel.Controls.Add(this.rbCustom, 0, 3);
            this.presetsPanel.Controls.Add(this.rbMinimal, 0, 2);
            this.presetsPanel.Controls.Add(this.rbDeveloper, 0, 1);
            this.presetsPanel.Controls.Add(this.rbEverything, 0, 0);
            this.presetsPanel.Dock = System.Windows.Forms.DockStyle.Top;
            this.presetsPanel.Name = "presetsPanel";
            this.presetsPanel.RowCount = 6;
            this.presetsPanel.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            this.presetsPanel.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            this.presetsPanel.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            this.presetsPanel.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            this.presetsPanel.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            this.presetsPanel.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            //
            // treeConfigurations
            //
            this.treeConfigurations.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.treeConfigurations.CheckBoxes = true;
            this.treeConfigurations.Dock = System.Windows.Forms.DockStyle.Fill;
            this.treeConfigurations.FullRowSelect = true;
            this.treeConfigurations.ItemHeight = 30;
            this.treeConfigurations.LineColor = System.Drawing.Color.Orchid;
            this.treeConfigurations.Name = "treeConfigurations";
            this.treeConfigurations.AfterCheck += new System.Windows.Forms.TreeViewEventHandler(this.treeConfigurations_AfterCheck);
            this.treeConfigurations.AfterSelect += new System.Windows.Forms.TreeViewEventHandler(this.treeConfigurations_AfterSelect);
            //
            // leftColumn
            //
            this.leftColumn.ColumnCount = 1;
            this.leftColumn.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.leftColumn.Controls.Add(this.treeConfigurations, 0, 1);
            this.leftColumn.Controls.Add(this.presetsPanel, 0, 0);
            this.leftColumn.Dock = System.Windows.Forms.DockStyle.Fill;
            this.leftColumn.Name = "leftColumn";
            this.leftColumn.RowCount = 2;
            this.leftColumn.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            this.leftColumn.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            //
            // txtInfo
            //
            this.txtInfo.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.txtInfo.Dock = System.Windows.Forms.DockStyle.Fill;
            this.txtInfo.Multiline = true;
            this.txtInfo.Name = "txtInfo";
            this.txtInfo.ReadOnly = true;
            this.txtInfo.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            //
            // content (leftColumn | info)
            //
            this.content.AutoScroll = true;
            this.content.ColumnCount = 2;
            this.content.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 55F));
            this.content.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 45F));
            this.content.Controls.Add(this.leftColumn, 0, 0);
            this.content.Controls.Add(this.txtInfo, 1, 0);
            this.content.Dock = System.Windows.Forms.DockStyle.Fill;
            this.content.Margin = new System.Windows.Forms.Padding(0);
            this.content.Name = "content";
            this.content.RowCount = 1;
            this.content.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            //
            // btnBackup
            //
            this.btnBackup.AutoSize = true;
            this.btnBackup.FlatAppearance.BorderSize = 0;
            this.btnBackup.Name = "btnBackup";
            this.btnBackup.Padding = new System.Windows.Forms.Padding(WinRestoreKit.Ui.SpaceM, WinRestoreKit.Ui.SpaceXs, WinRestoreKit.Ui.SpaceM, WinRestoreKit.Ui.SpaceXs);
            this.btnBackup.Text = "Back up now";
            this.btnBackup.UseVisualStyleBackColor = false;
            this.btnBackup.Click += new System.EventHandler(this.btnBackup_Click);
            //
            // btnMenuRestore
            //
            this.btnMenuRestore.AutoSize = true;
            this.btnMenuRestore.BackColor = System.Drawing.Color.Transparent;
            this.btnMenuRestore.FlatAppearance.BorderSize = 0;
            this.btnMenuRestore.FlatAppearance.MouseOverBackColor = System.Drawing.Color.Thistle;
            this.btnMenuRestore.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnMenuRestore.Font = new System.Drawing.Font("Segoe MDL2 Assets", 12F);
            this.btnMenuRestore.ForeColor = System.Drawing.Color.DimGray;
            this.btnMenuRestore.Name = "btnMenuRestore";
            this.btnMenuRestore.Padding = new System.Windows.Forms.Padding(WinRestoreKit.Ui.SpaceXs, 0, WinRestoreKit.Ui.SpaceXs, 0);
            this.btnMenuRestore.Text = "Restore";
            this.tt.SetToolTip(this.btnMenuRestore, "Restore your back ups");
            this.btnMenuRestore.UseVisualStyleBackColor = false;
            this.btnMenuRestore.Click += new System.EventHandler(this.btnRestore_Click);
            //
            // btnMenuMore
            //
            this.btnMenuMore.AutoSize = true;
            this.btnMenuMore.BackColor = System.Drawing.Color.Transparent;
            this.btnMenuMore.FlatAppearance.BorderSize = 0;
            this.btnMenuMore.FlatAppearance.MouseOverBackColor = System.Drawing.Color.Thistle;
            this.btnMenuMore.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnMenuMore.Font = new System.Drawing.Font("Segoe MDL2 Assets", 10.25F);
            this.btnMenuMore.ForeColor = System.Drawing.Color.Black;
            this.btnMenuMore.Name = "btnMenuMore";
            this.btnMenuMore.Padding = new System.Windows.Forms.Padding(WinRestoreKit.Ui.SpaceXs, 0, WinRestoreKit.Ui.SpaceXs, 0);
            this.btnMenuMore.Text = "...";
            this.btnMenuMore.UseVisualStyleBackColor = false;
            this.btnMenuMore.Click += new System.EventHandler(this.btnMenuMore_Click);
            //
            // resultsPanel
            //
            this.resultsPanel.AutoScroll = true;
            this.resultsPanel.Dock = System.Windows.Forms.DockStyle.Top;
            this.resultsPanel.Name = "resultsPanel";
            //
            // logToggle
            //
            this.logToggle.AutoSize = true;
            this.logToggle.Dock = System.Windows.Forms.DockStyle.Top;
            this.logToggle.LinkBehavior = System.Windows.Forms.LinkBehavior.HoverUnderline;
            this.logToggle.LinkColor = System.Drawing.Color.DimGray;
            this.logToggle.Margin = new System.Windows.Forms.Padding(0, WinRestoreKit.Ui.SpaceS, 0, WinRestoreKit.Ui.SpaceXs);
            this.logToggle.Name = "logToggle";
            this.logToggle.TabStop = true;
            this.logToggle.Text = "Activity log";
            this.logToggle.LinkClicked += new System.Windows.Forms.LinkLabelLinkClickedEventHandler(this.logToggle_LinkClicked);
            //
            // rtbLog
            //
            this.rtbLog.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.rtbLog.Dock = System.Windows.Forms.DockStyle.Top;
            this.rtbLog.Font = new System.Drawing.Font("Segoe UI Variable Text", 9F);
            this.rtbLog.ForeColor = System.Drawing.Color.DimGray;
            this.rtbLog.HideSelection = false;
            this.rtbLog.Name = "rtbLog";
            this.rtbLog.ReadOnly = true;
            this.rtbLog.ScrollBars = System.Windows.Forms.RichTextBoxScrollBars.Vertical;
            this.rtbLog.Size = new System.Drawing.Size(0, 120);
            this.rtbLog.Text = "This app supports you in backing up, sharing, and restoring your key settings.";
            //
            // actions
            //
            this.actions.AutoSize = true;
            this.actions.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this.actions.ColumnCount = 4;
            this.actions.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, WinRestoreKit.Ui.SpaceM));
            this.actions.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.AutoSize));
            this.actions.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.AutoSize));
            this.actions.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.actions.Controls.Add(new System.Windows.Forms.Label(), 0, 0);
            this.actions.Controls.Add(this.btnBackup, 1, 0);
            this.actions.Controls.Add(this.btnMenuRestore, 2, 0);
            this.actions.Controls.Add(this.btnMenuMore, 3, 0);
            this.actions.Dock = System.Windows.Forms.DockStyle.Top;
            this.actions.Name = "actions";
            this.actions.RowCount = 1;
            this.actions.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            //
            // root
            //
            this.root.AutoScroll = true;
            this.root.ColumnCount = 1;
            this.root.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.root.Controls.Add(this.rtbLog, 0, 6);
            this.root.Controls.Add(this.logToggle, 0, 5);
            this.root.Controls.Add(this.resultsPanel, 0, 4);
            this.root.Controls.Add(this.actions, 0, 3);
            this.root.Controls.Add(this.content, 0, 2);
            this.root.Controls.Add(this.linkSubHeader, 0, 1);
            this.root.Controls.Add(this.headerLabel, 0, 0);
            this.root.Dock = System.Windows.Forms.DockStyle.Fill;
            this.root.Padding = new System.Windows.Forms.Padding(WinRestoreKit.Ui.SpaceM);
            this.root.Name = "root";
            this.root.RowCount = 7;
            this.root.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            this.root.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            this.root.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.root.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            this.root.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            this.root.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            this.root.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 0F));
            //
            // contextMenu
            //
            this.contextMenu.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.menuSelectInstalled,
            this.menuSelectAll,
            this.toolStripSeparator1,
            this.textHeaderApp,
            this.menuOpenBackupFolder,
            this.menuOpenAppBackups});
            this.contextMenu.Name = "menuMain";
            this.contextMenu.Size = new System.Drawing.Size(204, 148);
            //
            // menuSelectInstalled
            //
            this.menuSelectInstalled.Name = "menuSelectInstalled";
            this.menuSelectInstalled.Size = new System.Drawing.Size(203, 24);
            this.menuSelectInstalled.Text = "Select available";
            this.menuSelectInstalled.Click += new System.EventHandler(this.menuSelectInstalled_Click);
            //
            // menuSelectAll
            //
            this.menuSelectAll.Name = "menuSelectAll";
            this.menuSelectAll.Size = new System.Drawing.Size(203, 24);
            this.menuSelectAll.Text = "Select all";
            this.menuSelectAll.Click += new System.EventHandler(this.menuSelectAll_Click);
            //
            // toolStripSeparator1
            //
            this.toolStripSeparator1.Name = "toolStripSeparator1";
            this.toolStripSeparator1.Size = new System.Drawing.Size(200, 6);
            //
            // textHeaderApp
            //
            this.textHeaderApp.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.textHeaderApp.Name = "textHeaderApp";
            this.textHeaderApp.Size = new System.Drawing.Size(100, 16);
            this.textHeaderApp.Text = "Settings";
            //
            // menuOpenBackupFolder
            //
            this.menuOpenBackupFolder.Name = "menuOpenBackupFolder";
            this.menuOpenBackupFolder.Size = new System.Drawing.Size(203, 24);
            this.menuOpenBackupFolder.Text = "Open backups folder";
            this.menuOpenBackupFolder.Click += new System.EventHandler(this.menuOpenBackupFolder_Click);
            //
            // menuOpenAppBackups
            //
            this.menuOpenAppBackups.Name = "menuOpenAppBackups";
            this.menuOpenAppBackups.Size = new System.Drawing.Size(203, 24);
            this.menuOpenAppBackups.Text = "Open app backups";
            this.menuOpenAppBackups.Click += new System.EventHandler(this.menuOpenAppBackups_Click);
            //
            // BackupPageView
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(96F, 96F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Dpi;
            this.AutoScroll = true;
            this.BackColor = WinRestoreKit.Ui.Surface;
            this.Controls.Add(this.root);
            this.Name = "BackupPageView";
            this.Size = new System.Drawing.Size(824, 759);
            this.contextMenu.ResumeLayout(false);
            this.contextMenu.PerformLayout();
            this.content.ResumeLayout(false);
            this.content.PerformLayout();
            this.leftColumn.ResumeLayout(false);
            this.leftColumn.PerformLayout();
            this.presetsPanel.ResumeLayout(false);
            this.presetsPanel.PerformLayout();
            this.actions.ResumeLayout(false);
            this.actions.PerformLayout();
            this.root.ResumeLayout(false);
            this.root.PerformLayout();
            this.ResumeLayout(false);

        }

        #endregion
        private System.Windows.Forms.Label headerLabel;
        private System.Windows.Forms.LinkLabel linkSubHeader;
        private System.Windows.Forms.TreeView treeConfigurations;
        private System.Windows.Forms.TextBox txtInfo;
        private WinRestoreKit.AccentButton btnBackup;
        private System.Windows.Forms.Button btnMenuRestore;
        private System.Windows.Forms.Button btnMenuMore;
        private Views.RunResultsPanel resultsPanel;
        private System.Windows.Forms.LinkLabel logToggle;
        private System.Windows.Forms.RichTextBox rtbLog;
        private System.Windows.Forms.ToolTip tt;
        private System.Windows.Forms.ContextMenuStrip contextMenu;
        private System.Windows.Forms.ToolStripMenuItem menuSelectInstalled;
        private System.Windows.Forms.ToolStripMenuItem menuSelectAll;
        private System.Windows.Forms.ToolStripSeparator toolStripSeparator1;
        private System.Windows.Forms.ToolStripTextBox textHeaderApp;
        private System.Windows.Forms.ToolStripMenuItem menuOpenBackupFolder;
        private System.Windows.Forms.ToolStripMenuItem menuOpenAppBackups;
        private System.Windows.Forms.TableLayoutPanel root;
        private System.Windows.Forms.TableLayoutPanel content;
        private System.Windows.Forms.TableLayoutPanel leftColumn;
        private System.Windows.Forms.TableLayoutPanel presetsPanel;
        private System.Windows.Forms.RadioButton rbEverything;
        private System.Windows.Forms.RadioButton rbDeveloper;
        private System.Windows.Forms.RadioButton rbMinimal;
        private System.Windows.Forms.RadioButton rbCustom;
        private System.Windows.Forms.Label lblWarnings;
        private System.Windows.Forms.LinkLabel lnkAdvanced;
        private System.Windows.Forms.TableLayoutPanel actions;
    }
}
