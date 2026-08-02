namespace Views
{
    partial class RestAppsForm
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        /// <remarks>
        /// Laid out with TableLayoutPanel and Dock rather than absolute Location/Size since Phase 4
        /// PR 9: absolute positions do not survive a WM_DPICHANGED rescale and the PerMonitorV2 flip
        /// lands in the same PR. Behaviour is untouched - Restore still ships DISABLED.
        /// </remarks>
        private void InitializeComponent()
        {
            this.root = new System.Windows.Forms.TableLayoutPanel();
            this.comboBackups = new System.Windows.Forms.ComboBox();
            this.btnRestore = new System.Windows.Forms.Button();
            this.listApps = new System.Windows.Forms.CheckedListBox();
            this.btnCancel = new System.Windows.Forms.Button();
            this.root.SuspendLayout();
            this.SuspendLayout();
            //
            // comboBackups
            //
            this.comboBackups.Dock = System.Windows.Forms.DockStyle.Fill;
            this.comboBackups.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.comboBackups.FlatStyle = System.Windows.Forms.FlatStyle.System;
            this.comboBackups.Font = new System.Drawing.Font("Segoe UI Variable Display", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.comboBackups.FormattingEnabled = true;
            this.comboBackups.Margin = new System.Windows.Forms.Padding(0, 0, 0, WinRestoreKit.Ui.SpaceS);
            this.comboBackups.Name = "comboBackups";
            this.comboBackups.TabIndex = 1;
            this.comboBackups.SelectedIndexChanged += new System.EventHandler(this.comboBackups_SelectedIndexChanged);
            //
            // listApps
            //
            this.listApps.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.listApps.Dock = System.Windows.Forms.DockStyle.Fill;
            this.listApps.Font = new System.Drawing.Font("Segoe UI Variable Display", 9.75F);
            this.listApps.FormattingEnabled = true;
            this.listApps.Margin = new System.Windows.Forms.Padding(0, 0, 0, WinRestoreKit.Ui.SpaceM);
            this.listApps.Name = "listApps";
            this.listApps.ScrollAlwaysVisible = true;
            this.listApps.TabIndex = 184;
            //
            // btnRestore
            //
            this.btnRestore.AutoEllipsis = true;
            this.btnRestore.Dock = System.Windows.Forms.DockStyle.Fill;
            // Disabled is the DEFAULT state, not something the selection handler derives on its own.
            // LoadBackups only reaches SelectedIndex = 0 when the app folder exists and holds at
            // least one backup, so with no backups at all the handler never fires - and Restore used
            // to ship enabled over an empty list, which is exactly the state a new user is in.
            this.btnRestore.Enabled = false;
            this.btnRestore.FlatAppearance.BorderSize = 0;
            this.btnRestore.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnRestore.Font = new System.Drawing.Font("Segoe UI Variable Display", 12F, System.Drawing.FontStyle.Bold);
            this.btnRestore.ForeColor = WinRestoreKit.Ui.Danger;
            this.btnRestore.Margin = new System.Windows.Forms.Padding(0, 0, 0, WinRestoreKit.Ui.SpaceS);
            this.btnRestore.Name = "btnRestore";
            this.btnRestore.Padding = new System.Windows.Forms.Padding(0, WinRestoreKit.Ui.SpaceXs, 0, WinRestoreKit.Ui.SpaceXs);
            this.btnRestore.TabIndex = 183;
            this.btnRestore.TabStop = false;
            this.btnRestore.Text = "Restore";
            this.btnRestore.UseVisualStyleBackColor = false;
            this.btnRestore.Click += new System.EventHandler(this.btnRestore_Click);
            //
            // btnCancel
            //
            this.btnCancel.AutoEllipsis = true;
            this.btnCancel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.btnCancel.FlatAppearance.BorderSize = 0;
            this.btnCancel.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnCancel.Font = new System.Drawing.Font("Segoe UI Variable Display", 12F, System.Drawing.FontStyle.Bold);
            this.btnCancel.Margin = new System.Windows.Forms.Padding(0);
            this.btnCancel.Name = "btnCancel";
            this.btnCancel.Padding = new System.Windows.Forms.Padding(0, WinRestoreKit.Ui.SpaceXs, 0, WinRestoreKit.Ui.SpaceXs);
            this.btnCancel.TabIndex = 185;
            this.btnCancel.TabStop = false;
            this.btnCancel.Text = "Cancel";
            this.btnCancel.UseVisualStyleBackColor = false;
            this.btnCancel.Click += new System.EventHandler(this.btnCancel_Click);
            //
            // root
            //
            this.root.ColumnCount = 1;
            this.root.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.root.Controls.Add(this.comboBackups, 0, 0);
            this.root.Controls.Add(this.listApps, 0, 1);
            this.root.Controls.Add(this.btnRestore, 0, 2);
            this.root.Controls.Add(this.btnCancel, 0, 3);
            this.root.Dock = System.Windows.Forms.DockStyle.Fill;
            this.root.Name = "root";
            this.root.Padding = new System.Windows.Forms.Padding(WinRestoreKit.Ui.SpaceL);
            this.root.RowCount = 4;
            this.root.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            this.root.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.root.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            this.root.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            //
            // RestAppsForm
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(361, 583);
            this.Controls.Add(this.root);
            this.MinimumSize = new System.Drawing.Size(320, 420);
            this.Name = "RestAppsForm";
            this.Opacity = 0.95D;
            this.ShowIcon = false;
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Restore your apps";
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.RestAppsForm_FormClosing);
            this.root.ResumeLayout(false);
            this.ResumeLayout(false);

        }

        #endregion
        private System.Windows.Forms.TableLayoutPanel root;
        private System.Windows.Forms.ComboBox comboBackups;
        private System.Windows.Forms.Button btnRestore;
        private System.Windows.Forms.CheckedListBox listApps;
        private System.Windows.Forms.Button btnCancel;
    }
}