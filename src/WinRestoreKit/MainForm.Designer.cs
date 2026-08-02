namespace WinRestoreKit
{
    partial class MainForm
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

        #region Vom Windows Form-Designer generierter Code

        /// <summary>
        /// Erforderliche Methode für die Designerunterstützung.
        /// Der Inhalt der Methode darf nicht mit dem Code-Editor geändert werden.
        /// </summary>
        /// <remarks>
        /// Hand-written rather than round-tripped through the Designer, and laid out entirely with
        /// TableLayoutPanel and Dock. Absolute Location/Size coordinates do not survive a
        /// WM_DPICHANGED rescale, and PR 9 flips HighDpiMode to PerMonitorV2 - the containers have to
        /// land first, or the resulting breakage gets attributed to DPI when it is really the 2023
        /// layout.
        /// </remarks>
        private void InitializeComponent()
        {
            this.layoutRoot = new System.Windows.Forms.TableLayoutPanel();
            this.pnlRail = new System.Windows.Forms.TableLayoutPanel();
            this.btnHome = new System.Windows.Forms.Button();
            this.btnBackUp = new System.Windows.Forms.Button();
            this.btnRestore = new System.Windows.Forms.Button();
            this.btnHistory = new System.Windows.Forms.Button();
            this.railSpacer = new System.Windows.Forms.Panel();
            this.btnAbout = new System.Windows.Forms.Button();
            this.pnlContentArea = new System.Windows.Forms.TableLayoutPanel();
            this.pnlStatusBar = new System.Windows.Forms.TableLayoutPanel();
            this.lblDiskSpace = new System.Windows.Forms.Label();
            this.checkVersion = new System.Windows.Forms.CheckBox();
            this.pnlForm = new System.Windows.Forms.Panel();
            this.layoutRoot.SuspendLayout();
            this.pnlRail.SuspendLayout();
            this.pnlContentArea.SuspendLayout();
            this.pnlStatusBar.SuspendLayout();
            this.SuspendLayout();
            // 
            // layoutRoot
            // 
            this.layoutRoot.ColumnCount = 2;
            // Absolute, not AutoSize. An AutoSize rail column is as wide as its widest child, and
            // the flexible spacer that pushes About to the bottom carries Panel's 200px default -
            // which silently made the rail a third of the window and squeezed the content host.
            // TableLayoutPanel scales absolute column widths on a DPI change, so this survives PR 9.
            this.layoutRoot.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 180F));
            this.layoutRoot.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.layoutRoot.Controls.Add(this.pnlRail, 0, 0);
            this.layoutRoot.Controls.Add(this.pnlContentArea, 1, 0);
            this.layoutRoot.Dock = System.Windows.Forms.DockStyle.Fill;
            this.layoutRoot.Name = "layoutRoot";
            this.layoutRoot.RowCount = 1;
            this.layoutRoot.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.layoutRoot.TabIndex = 0;
            // 
            // pnlRail
            // 
            this.pnlRail.ColumnCount = 1;
            this.pnlRail.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.pnlRail.Controls.Add(this.btnHome, 0, 0);
            this.pnlRail.Controls.Add(this.btnBackUp, 0, 1);
            this.pnlRail.Controls.Add(this.btnRestore, 0, 2);
            this.pnlRail.Controls.Add(this.btnHistory, 0, 3);
            this.pnlRail.Controls.Add(this.railSpacer, 0, 4);
            this.pnlRail.Controls.Add(this.btnAbout, 0, 5);
            this.pnlRail.Dock = System.Windows.Forms.DockStyle.Fill;
            this.pnlRail.Name = "pnlRail";
            this.pnlRail.Padding = new System.Windows.Forms.Padding(8);
            this.pnlRail.RowCount = 6;
            this.pnlRail.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            this.pnlRail.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            this.pnlRail.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            this.pnlRail.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            this.pnlRail.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.pnlRail.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            this.pnlRail.TabIndex = 0;
            // 
            // btnHome
            // 
            this.btnHome.AutoSize = true;
            this.btnHome.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this.btnHome.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnHome.Dock = System.Windows.Forms.DockStyle.Top;
            this.btnHome.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnHome.Margin = new System.Windows.Forms.Padding(0, 0, 0, 4);
            this.btnHome.Name = "btnHome";
            this.btnHome.Padding = new System.Windows.Forms.Padding(12, 8, 12, 8);
            this.btnHome.TabIndex = 0;
            this.btnHome.Text = "Home";
            this.btnHome.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.btnHome.UseVisualStyleBackColor = false;
            this.btnHome.Click += new System.EventHandler(this.btnHome_Click);
            // 
            // btnBackUp
            // 
            this.btnBackUp.AutoSize = true;
            this.btnBackUp.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this.btnBackUp.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnBackUp.Dock = System.Windows.Forms.DockStyle.Top;
            this.btnBackUp.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnBackUp.Margin = new System.Windows.Forms.Padding(0, 0, 0, 4);
            this.btnBackUp.Name = "btnBackUp";
            this.btnBackUp.Padding = new System.Windows.Forms.Padding(12, 8, 12, 8);
            this.btnBackUp.TabIndex = 1;
            this.btnBackUp.Text = "Back up";
            this.btnBackUp.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.btnBackUp.UseVisualStyleBackColor = false;
            this.btnBackUp.Click += new System.EventHandler(this.btnBackUp_Click);
            // 
            // btnRestore
            // 
            this.btnRestore.AutoSize = true;
            this.btnRestore.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this.btnRestore.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnRestore.Dock = System.Windows.Forms.DockStyle.Top;
            this.btnRestore.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnRestore.Margin = new System.Windows.Forms.Padding(0, 0, 0, 4);
            this.btnRestore.Name = "btnRestore";
            this.btnRestore.Padding = new System.Windows.Forms.Padding(12, 8, 12, 8);
            this.btnRestore.TabIndex = 2;
            this.btnRestore.Text = "Restore";
            this.btnRestore.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.btnRestore.UseVisualStyleBackColor = false;
            this.btnRestore.Click += new System.EventHandler(this.btnRestore_Click);
            //
            // btnHistory
            //
            this.btnHistory.AutoSize = true;
            this.btnHistory.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this.btnHistory.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnHistory.Dock = System.Windows.Forms.DockStyle.Top;
            this.btnHistory.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnHistory.Margin = new System.Windows.Forms.Padding(0, 0, 0, 4);
            this.btnHistory.Name = "btnHistory";
            this.btnHistory.Padding = new System.Windows.Forms.Padding(12, 8, 12, 8);
            this.btnHistory.TabIndex = 3;
            this.btnHistory.Text = "History";
            this.btnHistory.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.btnHistory.UseVisualStyleBackColor = false;
            this.btnHistory.Click += new System.EventHandler(this.btnHistory_Click);
            // 
            // railSpacer
            // 
            this.railSpacer.Dock = System.Windows.Forms.DockStyle.Fill;
            this.railSpacer.Margin = new System.Windows.Forms.Padding(0);
            this.railSpacer.Name = "railSpacer";
            this.railSpacer.TabIndex = 3;
            this.railSpacer.TabStop = false;
            // 
            // btnAbout
            // 
            this.btnAbout.AutoSize = true;
            this.btnAbout.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this.btnAbout.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnAbout.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.btnAbout.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnAbout.Margin = new System.Windows.Forms.Padding(0, 12, 0, 0);
            this.btnAbout.Name = "btnAbout";
            this.btnAbout.Padding = new System.Windows.Forms.Padding(12, 8, 12, 8);
            this.btnAbout.TabIndex = 4;
            this.btnAbout.Text = "About";
            this.btnAbout.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.btnAbout.UseVisualStyleBackColor = false;
            this.btnAbout.Click += new System.EventHandler(this.btnAbout_Click);
            // 
            // pnlContentArea
            // 
            this.pnlContentArea.ColumnCount = 1;
            this.pnlContentArea.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.pnlContentArea.Controls.Add(this.pnlForm, 0, 0);
            this.pnlContentArea.Controls.Add(this.pnlStatusBar, 0, 1);
            this.pnlContentArea.Dock = System.Windows.Forms.DockStyle.Fill;
            this.pnlContentArea.Margin = new System.Windows.Forms.Padding(0);
            this.pnlContentArea.Name = "pnlContentArea";
            this.pnlContentArea.RowCount = 2;
            this.pnlContentArea.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.pnlContentArea.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            this.pnlContentArea.TabIndex = 1;
            // 
            // pnlStatusBar
            // 
            this.pnlStatusBar.AutoSize = true;
            this.pnlStatusBar.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this.pnlStatusBar.ColumnCount = 2;
            this.pnlStatusBar.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.pnlStatusBar.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.AutoSize));
            this.pnlStatusBar.Controls.Add(this.lblDiskSpace, 0, 0);
            this.pnlStatusBar.Controls.Add(this.checkVersion, 1, 0);
            this.pnlStatusBar.Dock = System.Windows.Forms.DockStyle.Fill;
            this.pnlStatusBar.Margin = new System.Windows.Forms.Padding(0);
            this.pnlStatusBar.Name = "pnlStatusBar";
            this.pnlStatusBar.Padding = new System.Windows.Forms.Padding(8, 4, 8, 4);
            this.pnlStatusBar.RowCount = 1;
            this.pnlStatusBar.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            this.pnlStatusBar.TabIndex = 1;
            // 
            // lblDiskSpace
            // 
            this.lblDiskSpace.Anchor = System.Windows.Forms.AnchorStyles.Left;
            this.lblDiskSpace.AutoSize = true;
            this.lblDiskSpace.Margin = new System.Windows.Forms.Padding(0);
            this.lblDiskSpace.Name = "lblDiskSpace";
            this.lblDiskSpace.TabIndex = 0;
            this.lblDiskSpace.Text = "Storage estimate";
            // 
            // checkVersion
            // 
            this.checkVersion.Anchor = System.Windows.Forms.AnchorStyles.Right;
            this.checkVersion.Appearance = System.Windows.Forms.Appearance.Button;
            this.checkVersion.AutoSize = true;
            this.checkVersion.Cursor = System.Windows.Forms.Cursors.Hand;
            this.checkVersion.FlatAppearance.BorderSize = 0;
            this.checkVersion.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.checkVersion.Margin = new System.Windows.Forms.Padding(8, 0, 0, 0);
            this.checkVersion.Name = "checkVersion";
            this.checkVersion.TabIndex = 1;
            this.checkVersion.Text = "loading";
            this.checkVersion.UseVisualStyleBackColor = false;
            this.checkVersion.CheckedChanged += new System.EventHandler(this.checkVersion_CheckedChanged);
            // 
            // pnlForm
            // 
            this.pnlForm.Dock = System.Windows.Forms.DockStyle.Fill;
            this.pnlForm.Margin = new System.Windows.Forms.Padding(0);
            this.pnlForm.Name = "pnlForm";
            this.pnlForm.TabIndex = 0;
            // 
            // MainForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(96F, 96F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Dpi;
            // Grown by the width of the rail plus the height of the status strip, so the content
            // host keeps the 878x758 the pages were laid out against. ConfPageView is designed at
            // 824 wide with Left|Right-anchored children, which means a narrower host does not
            // reflow it - it shrinks every child by the shortfall, and at a 180px deficit the tree
            // and log pane collapse into an unusable column. PR 6 rebuilds that page; until then the
            // shell adds chrome around it rather than eating into it.
            this.ClientSize = new System.Drawing.Size(1058, 790);
            this.Controls.Add(this.layoutRoot);
            this.MinimumSize = new System.Drawing.Size(1074, 600);
            this.Name = "MainForm";
            this.ShowIcon = false;
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "Appcopier";
            this.Shown += new System.EventHandler(this.MainForm_Shown);
            this.layoutRoot.ResumeLayout(false);
            this.layoutRoot.PerformLayout();
            this.pnlRail.ResumeLayout(false);
            this.pnlRail.PerformLayout();
            this.pnlContentArea.ResumeLayout(false);
            this.pnlContentArea.PerformLayout();
            this.pnlStatusBar.ResumeLayout(false);
            this.pnlStatusBar.PerformLayout();
            this.ResumeLayout(false);
        }

        #endregion

        private System.Windows.Forms.TableLayoutPanel layoutRoot;
        private System.Windows.Forms.TableLayoutPanel pnlRail;
        private System.Windows.Forms.Button btnHome;
        private System.Windows.Forms.Button btnBackUp;
        private System.Windows.Forms.Button btnRestore;
        private System.Windows.Forms.Button btnHistory;
        private System.Windows.Forms.Panel railSpacer;
        private System.Windows.Forms.Button btnAbout;
        private System.Windows.Forms.TableLayoutPanel pnlContentArea;
        private System.Windows.Forms.TableLayoutPanel pnlStatusBar;
        private System.Windows.Forms.Label lblDiskSpace;
        private System.Windows.Forms.CheckBox checkVersion;
        private System.Windows.Forms.Panel pnlForm;
    }
}
