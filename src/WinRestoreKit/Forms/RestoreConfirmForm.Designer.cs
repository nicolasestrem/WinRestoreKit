namespace Views
{
    partial class RestoreConfirmForm
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
        /// Laid out with TableLayoutPanel and Dock rather than the absolute Location/Size it carried
        /// until Phase 4 PR 9. Absolute positions do not survive a WM_DPICHANGED rescale, and the
        /// PerMonitorV2 flip lands in this same PR - the containers have to come first or the
        /// breakage gets attributed to DPI when it is really the old layout.
        ///
        /// Nothing about the CONSENT semantics changed: btnCancel is still both AcceptButton and
        /// CancelButton (Enter and Escape both land on the harmless outcome), the constructor still
        /// sets ActiveControl = btnCancel, and every consent box is still created unchecked in
        /// AddConsentBox. Only the geometry moved.
        /// </remarks>
        private void InitializeComponent()
        {
            this.root = new System.Windows.Forms.TableLayoutPanel();
            this.txtPlan = new System.Windows.Forms.TextBox();
            this.pnlConsent = new System.Windows.Forms.FlowLayoutPanel();
            this.lblCaveat = new System.Windows.Forms.Label();
            this.pnlButtons = new System.Windows.Forms.TableLayoutPanel();
            this.btnRestore = new System.Windows.Forms.Button();
            this.btnCancel = new System.Windows.Forms.Button();
            this.root.SuspendLayout();
            this.pnlButtons.SuspendLayout();
            this.SuspendLayout();
            //
            // txtPlan
            //
            this.txtPlan.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.txtPlan.Dock = System.Windows.Forms.DockStyle.Fill;
            this.txtPlan.Font = WinRestoreKit.Ui.Mono();
            this.txtPlan.ForeColor = WinRestoreKit.Theme.Current.Text;
            this.txtPlan.Margin = new System.Windows.Forms.Padding(0, 0, 0, WinRestoreKit.Ui.SpaceM);
            this.txtPlan.Multiline = true;
            this.txtPlan.Name = "txtPlan";
            this.txtPlan.ReadOnly = true;
            this.txtPlan.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.txtPlan.TabIndex = 1;
            // pnlConsent
            //
            this.pnlConsent.AutoScroll = true;
            this.pnlConsent.Dock = System.Windows.Forms.DockStyle.Fill;
            this.pnlConsent.FlowDirection = System.Windows.Forms.FlowDirection.TopDown;
            this.pnlConsent.Margin = new System.Windows.Forms.Padding(0, 0, 0, WinRestoreKit.Ui.SpaceM);
            this.pnlConsent.Name = "pnlConsent";
            this.pnlConsent.TabIndex = 2;
            this.pnlConsent.WrapContents = false;
            //
            // lblCaveat
            //
            this.lblCaveat.AutoSize = true;
            this.lblCaveat.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblCaveat.Font = WinRestoreKit.Ui.BodyBold();
            this.lblCaveat.ForeColor = WinRestoreKit.Theme.Current.Accent2_700;
            this.lblCaveat.Margin = new System.Windows.Forms.Padding(0, 0, 0, WinRestoreKit.Ui.SpaceM);
            this.lblCaveat.Name = "lblCaveat";
            this.lblCaveat.TabIndex = 3;
            // btnRestore
            //
            this.btnRestore.AutoEllipsis = true;
            this.btnRestore.Dock = System.Windows.Forms.DockStyle.Fill;
            this.btnRestore.FlatAppearance.BorderSize = 1;
            this.btnRestore.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnRestore.Font = WinRestoreKit.Ui.BodyBold();
            this.btnRestore.ForeColor = WinRestoreKit.Theme.Current.Accent2_700;
            this.btnRestore.Margin = new System.Windows.Forms.Padding(0, 0, WinRestoreKit.Ui.SpaceS, 0);
            this.btnRestore.Name = "btnRestore";
            this.btnRestore.Padding = new System.Windows.Forms.Padding(0, WinRestoreKit.Ui.SpaceXs, 0, WinRestoreKit.Ui.SpaceXs);
            this.btnRestore.TabIndex = 5;
            this.btnRestore.TabStop = false;
            this.btnRestore.Text = "Restore";
            this.btnRestore.UseVisualStyleBackColor = false;
            this.btnRestore.Click += new System.EventHandler(this.btnRestore_Click);
            //
            // btnCancel
            //
            this.btnCancel.AutoEllipsis = true;
            this.btnCancel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.btnCancel.FlatAppearance.BorderSize = 1;
            this.btnCancel.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnCancel.Font = WinRestoreKit.Ui.BodyBold();
            this.btnCancel.ForeColor = WinRestoreKit.Theme.Current.Bg;
            this.btnCancel.Margin = new System.Windows.Forms.Padding(WinRestoreKit.Ui.SpaceS, 0, 0, 0);
            this.btnCancel.Name = "btnCancel";
            this.btnCancel.Padding = new System.Windows.Forms.Padding(0, WinRestoreKit.Ui.SpaceXs, 0, WinRestoreKit.Ui.SpaceXs);
            this.btnCancel.TabIndex = 4;
            this.btnCancel.Text = "Cancel";
            this.btnCancel.UseVisualStyleBackColor = false;
            this.btnCancel.Click += new System.EventHandler(this.btnCancel_Click);
            //
            // pnlButtons
            //
            this.pnlButtons.AutoSize = true;
            this.pnlButtons.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this.pnlButtons.ColumnCount = 2;
            this.pnlButtons.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 50F));
            this.pnlButtons.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 50F));
            this.pnlButtons.Controls.Add(this.btnRestore, 0, 0);
            this.pnlButtons.Controls.Add(this.btnCancel, 1, 0);
            this.pnlButtons.Dock = System.Windows.Forms.DockStyle.Fill;
            this.pnlButtons.Margin = new System.Windows.Forms.Padding(0);
            this.pnlButtons.Name = "pnlButtons";
            this.pnlButtons.RowCount = 1;
            this.pnlButtons.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            //
            // root
            //
            this.root.ColumnCount = 1;
            this.root.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.root.Controls.Add(this.txtPlan, 0, 0);
            this.root.Controls.Add(this.pnlConsent, 0, 1);
            this.root.Controls.Add(this.lblCaveat, 0, 2);
            this.root.Controls.Add(this.pnlButtons, 0, 3);
            this.root.Dock = System.Windows.Forms.DockStyle.Fill;
            this.root.Name = "root";
            this.root.Padding = new System.Windows.Forms.Padding(WinRestoreKit.Ui.SpaceL);
            this.root.RowCount = 4;
            // The plan text takes the slack; the consent list is a fixed, scrollable band so a long
            // process list can never push the buttons off the bottom of the dialog.
            this.root.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.root.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 116F));
            this.root.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            this.root.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            //
            // RestoreConfirmForm
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            // Cancel is BOTH the accept and the cancel button: Enter and Escape must land on the
            // harmless outcome, because this dialog is the last point at which a restore that
            // overwrites the machine can still be stopped.
            this.AcceptButton = this.btnCancel;
            this.CancelButton = this.btnCancel;
            this.ClientSize = new System.Drawing.Size(560, 566);
            this.Controls.Add(this.root);
            this.MinimizeBox = false;
            this.MaximizeBox = false;
            this.MinimumSize = new System.Drawing.Size(480, 480);
            this.Name = "RestoreConfirmForm";
            this.Opacity = 0.95D;
            this.ShowIcon = false;
            this.ShowInTaskbar = false;
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Confirm restore";
            this.pnlButtons.ResumeLayout(false);
            this.root.ResumeLayout(false);
            this.root.PerformLayout();
            this.ResumeLayout(false);

        }

        #endregion
        private System.Windows.Forms.TableLayoutPanel root;
        private System.Windows.Forms.TextBox txtPlan;
        private System.Windows.Forms.FlowLayoutPanel pnlConsent;
        private System.Windows.Forms.Label lblCaveat;
        private System.Windows.Forms.TableLayoutPanel pnlButtons;
        private System.Windows.Forms.Button btnRestore;
        private System.Windows.Forms.Button btnCancel;
    }
}
