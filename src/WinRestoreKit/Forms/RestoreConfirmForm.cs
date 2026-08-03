using WinRestoreKit;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Views
{
    /// <summary>
    /// Asks for the restore, showing what it overwrites and what it cannot undo.
    /// </summary>
    /// <remarks>
    /// Deliberately thin. Every sentence shown here comes from <see cref="RestorePlan"/>, which is
    /// pure and tested; this class only lays text out. Composing anything locally would put the
    /// words the user consents to in a class the suite cannot reach.
    /// </remarks>
    public partial class RestoreConfirmForm : Form
    {
        private readonly List<KeyValuePair<RestoreConsentEntry, CheckBox>> boxes =
            new List<KeyValuePair<RestoreConsentEntry, CheckBox>>();

        public RestoreConfirmForm(RestorePlan plan)
        {
            if (plan == null)
                throw new ArgumentNullException(nameof(plan));

            InitializeComponent();

            txtPlan.Text = plan.ConfirmationText;
            lblCaveat.Text = RestorePlan.FidelityCaveat;

            foreach (RestoreConsentEntry entry in plan.ConsentEntries)
                AddConsentBox(entry);

            foreach (string line in plan.InformationalCloseLines)
                AddInformationalLine(line);

            SetStyle();

            // Focus follows the safe default the accept button already carries, so a dialog that
            // appears under the user's hand cannot be dismissed into a restore.
            ActiveControl = btnCancel;
        }

        /// <summary>
        /// The processes the user ticked, empty if none. Never null.
        /// </summary>
        public IReadOnlyList<string> ConsentedProcessNames
        {
            get
            {
                List<string> consented = new List<string>();

                foreach (KeyValuePair<RestoreConsentEntry, CheckBox> pair in boxes)
                {
                    if (pair.Value.Checked)
                        consented.Add(pair.Key.ProcessName);
                }

                return consented;
            }
        }

        private void AddConsentBox(RestoreConsentEntry entry)
        {
            CheckBox box = new CheckBox
            {
                AutoSize = true,
                Checked = false,
                Font = Ui.Body(),
                ForeColor = Theme.Current.Text,
                Margin = new Padding(0, 2, 0, 2),
                Text = entry.Label
            };

            pnlConsent.Controls.Add(box);
            boxes.Add(new KeyValuePair<RestoreConsentEntry, CheckBox>(entry, box));
        }

        private void AddInformationalLine(string line)
        {
            pnlConsent.Controls.Add(new Label
            {
                AutoSize = true,
                Font = Ui.Body(),
                ForeColor = Theme.Current.TextMuted,
                Padding = new Padding(0, 4, 0, 4),
                Text = line
            });
        }

        private void SetStyle()
        {
            Theme.Apply(this);

            BackColor = Theme.Current.Bg;
            root.BackColor = Theme.Current.Bg;
            txtPlan.BackColor = Theme.Current.Surface;
            txtPlan.ForeColor = Theme.Current.Text;
            pnlConsent.BackColor = Theme.Current.Bg;
            lblCaveat.ForeColor = Theme.Current.Accent2_700;

            btnRestore.BackColor = Theme.Current.Surface;
            btnRestore.FlatAppearance.BorderColor = Theme.Current.Accent2_600;
            btnRestore.ForeColor = Theme.Current.Accent2_700;

            btnCancel.BackColor = Theme.Current.Accent;
            btnCancel.FlatAppearance.BorderColor = Theme.Current.Accent;
            btnCancel.ForeColor = Theme.Current.Bg;
        }

        private void btnRestore_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.OK;
            Close();
        }

        private void btnCancel_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }
    }
}
