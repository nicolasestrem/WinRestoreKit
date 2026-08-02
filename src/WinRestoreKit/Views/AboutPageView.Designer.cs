namespace Views
{
    partial class AboutPageView
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
        /// <remarks>
        /// Laid out with TableLayoutPanel and Dock rather than the absolute Location/Size it carried
        /// until Phase 4 PR 9, so it survives a WM_DPICHANGED rescale under PerMonitorV2.
        /// </remarks>
        private void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(AboutPageView));
            this.root = new System.Windows.Forms.TableLayoutPanel();
            this.headerRow = new System.Windows.Forms.TableLayoutPanel();
            this.btnBack = new System.Windows.Forms.Button();
            this.label1 = new System.Windows.Forms.Label();
            this.linksRow = new System.Windows.Forms.TableLayoutPanel();
            this.btnGithub = new System.Windows.Forms.Button();
            this.lnkStargazers = new System.Windows.Forms.LinkLabel();
            this.label2 = new System.Windows.Forms.Label();
            this.linkURLDev = new System.Windows.Forms.LinkLabel();
            this.linkURLIcon = new System.Windows.Forms.LinkLabel();
            this.root.SuspendLayout();
            this.headerRow.SuspendLayout();
            this.linksRow.SuspendLayout();
            this.SuspendLayout();
            //
            // btnBack
            //
            this.btnBack.AutoSize = true;
            this.btnBack.FlatAppearance.BorderSize = 0;
            this.btnBack.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnBack.Font = new System.Drawing.Font("Segoe MDL2 Assets", 14.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.btnBack.Margin = new System.Windows.Forms.Padding(0, 0, WinRestoreKit.Ui.SpaceM, 0);
            this.btnBack.Name = "btnBack";
            this.btnBack.Padding = new System.Windows.Forms.Padding(WinRestoreKit.Ui.SpaceS, WinRestoreKit.Ui.SpaceXs, WinRestoreKit.Ui.SpaceS, WinRestoreKit.Ui.SpaceXs);
            this.btnBack.TabIndex = 225;
            this.btnBack.Text = "...";
            this.btnBack.UseVisualStyleBackColor = true;
            this.btnBack.Click += new System.EventHandler(this.btnBack_Click);
            //
            // label1
            //
            this.label1.AutoSize = true;
            this.label1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.label1.Font = new System.Drawing.Font("Segoe UI Variable Display Semib", 20.25F, System.Drawing.FontStyle.Bold);
            this.label1.ForeColor = WinRestoreKit.Ui.Muted;
            this.label1.Name = "label1";
            this.label1.TabIndex = 239;
            this.label1.Text = "About";
            this.label1.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            //
            // headerRow
            //
            this.headerRow.AutoSize = true;
            this.headerRow.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this.headerRow.ColumnCount = 2;
            this.headerRow.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.AutoSize));
            this.headerRow.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.headerRow.Controls.Add(this.btnBack, 0, 0);
            this.headerRow.Controls.Add(this.label1, 1, 0);
            this.headerRow.Dock = System.Windows.Forms.DockStyle.Fill;
            this.headerRow.Margin = new System.Windows.Forms.Padding(0, 0, 0, WinRestoreKit.Ui.SpaceM);
            this.headerRow.Name = "headerRow";
            this.headerRow.RowCount = 1;
            this.headerRow.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            //
            // btnGithub
            //
            this.btnGithub.AutoEllipsis = true;
            this.btnGithub.AutoSize = true;
            this.btnGithub.BackColor = System.Drawing.Color.Transparent;
            this.btnGithub.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnGithub.FlatAppearance.BorderSize = 0;
            this.btnGithub.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnGithub.Font = new System.Drawing.Font("Segoe UI Variable Text Light", 9.75F);
            this.btnGithub.Image = ((System.Drawing.Image)(resources.GetObject("btnGithub.Image")));
            this.btnGithub.ImageAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.btnGithub.Margin = new System.Windows.Forms.Padding(0, 0, WinRestoreKit.Ui.SpaceM, 0);
            this.btnGithub.Name = "btnGithub";
            this.btnGithub.Padding = new System.Windows.Forms.Padding(0, 0, 5, 0);
            this.btnGithub.TabIndex = 236;
            this.btnGithub.TabStop = false;
            this.btnGithub.Text = "Github";
            this.btnGithub.UseVisualStyleBackColor = false;
            this.btnGithub.Click += new System.EventHandler(this.btnGithub_Click);
            //
            // lnkStargazers
            //
            this.lnkStargazers.ActiveLinkColor = System.Drawing.Color.MediumVioletRed;
            this.lnkStargazers.AutoSize = true;
            this.lnkStargazers.BackColor = System.Drawing.Color.Transparent;
            this.lnkStargazers.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lnkStargazers.Font = new System.Drawing.Font("Segoe UI Variable Text Light", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.lnkStargazers.LinkBehavior = System.Windows.Forms.LinkBehavior.HoverUnderline;
            this.lnkStargazers.Name = "lnkStargazers";
            this.lnkStargazers.TabIndex = 235;
            this.lnkStargazers.TabStop = true;
            this.lnkStargazers.Text = "Error fetching Github stargazers";
            this.lnkStargazers.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.lnkStargazers.Visible = false;
            this.lnkStargazers.VisitedLinkColor = System.Drawing.Color.MediumVioletRed;
            this.lnkStargazers.LinkClicked += new System.Windows.Forms.LinkLabelLinkClickedEventHandler(this.lnkStargazers_LinkClicked);
            //
            // linksRow
            //
            this.linksRow.AutoSize = true;
            this.linksRow.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this.linksRow.ColumnCount = 2;
            this.linksRow.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.AutoSize));
            this.linksRow.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.linksRow.Controls.Add(this.btnGithub, 0, 0);
            this.linksRow.Controls.Add(this.lnkStargazers, 1, 0);
            this.linksRow.Dock = System.Windows.Forms.DockStyle.Fill;
            this.linksRow.Margin = new System.Windows.Forms.Padding(0, 0, 0, WinRestoreKit.Ui.SpaceM);
            this.linksRow.Name = "linksRow";
            this.linksRow.RowCount = 1;
            this.linksRow.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            //
            // label2
            //
            this.label2.AutoSize = true;
            this.label2.Dock = System.Windows.Forms.DockStyle.Fill;
            this.label2.Font = new System.Drawing.Font("Segoe UI Variable Text Light", 9.25F);
            this.label2.Margin = new System.Windows.Forms.Padding(0, 0, 0, WinRestoreKit.Ui.SpaceM);
            this.label2.Name = "label2";
            this.label2.TabIndex = 238;
            this.label2.Text = "Back up key things on your Windows PC, perform a reset or simply go back in time.";
            this.label2.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            //
            // linkURLDev
            //
            this.linkURLDev.AutoSize = true;
            this.linkURLDev.Dock = System.Windows.Forms.DockStyle.Fill;
            this.linkURLDev.Font = new System.Drawing.Font("Segoe UI Variable Text Light", 9F);
            this.linkURLDev.Name = "linkURLDev";
            this.linkURLDev.TabIndex = 237;
            this.linkURLDev.TabStop = true;
            this.linkURLDev.Text = "A Belim app creation (C) 2024.";
            this.linkURLDev.LinkClicked += new System.Windows.Forms.LinkLabelLinkClickedEventHandler(this.linkURLDev_LinkClicked);
            //
            // linkURLIcon
            //
            this.linkURLIcon.AutoSize = true;
            this.linkURLIcon.Dock = System.Windows.Forms.DockStyle.Fill;
            this.linkURLIcon.Font = new System.Drawing.Font("Segoe UI Variable Text Light", 9F);
            this.linkURLIcon.LinkColor = WinRestoreKit.Ui.Muted;
            this.linkURLIcon.Name = "linkURLIcon";
            this.linkURLIcon.TabIndex = 240;
            this.linkURLIcon.TabStop = true;
            this.linkURLIcon.Text = "Appcopier Icon created by Icon Hubs - Flaticon";
            this.linkURLIcon.LinkClicked += new System.Windows.Forms.LinkLabelLinkClickedEventHandler(this.linkURLIcon_LinkClicked);
            //
            // root
            //
            this.root.ColumnCount = 1;
            this.root.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.root.Controls.Add(this.headerRow, 0, 0);
            this.root.Controls.Add(this.linksRow, 0, 1);
            this.root.Controls.Add(this.label2, 0, 2);
            this.root.Controls.Add(this.linkURLDev, 0, 3);
            this.root.Controls.Add(this.linkURLIcon, 0, 4);
            this.root.Dock = System.Windows.Forms.DockStyle.Fill;
            this.root.Name = "root";
            this.root.Padding = new System.Windows.Forms.Padding(WinRestoreKit.Ui.SpaceL);
            this.root.RowCount = 5;
            this.root.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            this.root.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            this.root.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            // The credits sit at the bottom; the slack above them absorbs the window height.
            this.root.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.root.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            //
            // AboutPageView
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(96F, 96F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Dpi;
            this.AutoScroll = true;
            this.Controls.Add(this.root);
            this.Name = "AboutPageView";
            this.Size = new System.Drawing.Size(942, 759);
            this.headerRow.ResumeLayout(false);
            this.headerRow.PerformLayout();
            this.linksRow.ResumeLayout(false);
            this.linksRow.PerformLayout();
            this.root.ResumeLayout(false);
            this.root.PerformLayout();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.TableLayoutPanel root;
        private System.Windows.Forms.TableLayoutPanel headerRow;
        private System.Windows.Forms.TableLayoutPanel linksRow;
        private System.Windows.Forms.Button btnBack;
        private System.Windows.Forms.LinkLabel lnkStargazers;
        private System.Windows.Forms.Button btnGithub;
        private System.Windows.Forms.LinkLabel linkURLDev;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.LinkLabel linkURLIcon;
    }
}
