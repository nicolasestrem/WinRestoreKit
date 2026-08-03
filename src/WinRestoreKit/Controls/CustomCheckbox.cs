using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace WinRestoreKit
{
    internal sealed class CustomCheckbox : Control
    {
        private const int BoxSize = 16;
        private const int LabelGap = 8;
        private bool isChecked;
        private string labelText = string.Empty;

        internal CustomCheckbox()
        {
            Font = Ui.Body();
            AutoSize = true;
            Size = new Size(BoxSize, Math.Max(BoxSize, Font.Height));
            Cursor = Cursors.Hand;
            AccessibleRole = AccessibleRole.CheckButton;
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.SupportsTransparentBackColor,
                true);
            BackColor = Color.Transparent;
            ForeColor = Theme.Current.Text;
        }

        internal bool Checked
        {
            get => isChecked;
            set
            {
                if (isChecked == value)
                    return;

                isChecked = value;
                Invalidate();
                CheckedChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        internal string LabelText
        {
            get => labelText;
            set
            {
                labelText = value ?? string.Empty;
                AccessibleName = labelText;
                PerformLayout();
                Invalidate();
            }
        }

        internal event EventHandler CheckedChanged;

        public override Size GetPreferredSize(Size proposedSize)
        {
            Size textSize = TextRenderer.MeasureText(labelText, Font, Size.Empty, TextFormatFlags.NoPadding);
            int width = BoxSize + (string.IsNullOrEmpty(labelText) ? 0 : LabelGap + textSize.Width);
            return new Size(width, Math.Max(BoxSize, textSize.Height));
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            int boxY = Math.Max(0, (ClientSize.Height - BoxSize) / 2);
            Rectangle box = new Rectangle(0, boxY, BoxSize - 1, BoxSize - 1);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            if (isChecked)
            {
                using (Brush fill = new SolidBrush(Theme.Current.Accent))
                {
                    e.Graphics.FillRectangle(fill, box);
                }

                using (Pen check = new Pen(Color.White, 1.8f))
                {
                    check.StartCap = LineCap.Round;
                    check.EndCap = LineCap.Round;
                    check.LineJoin = LineJoin.Round;
                    e.Graphics.DrawLines(
                        check,
                        new[]
                        {
                            new PointF(2f, boxY + 6.3f),
                            new PointF(4.6f, boxY + 8.9f),
                            new PointF(10f, boxY + 3.4f)
                        });
                }
            }
            else
            {
                using (Pen border = new Pen(Theme.Current.Divider, 1.5f))
                {
                    e.Graphics.DrawRectangle(border, box);
                }
            }

            if (!string.IsNullOrEmpty(labelText))
            {
                TextRenderer.DrawText(
                    e.Graphics,
                    labelText,
                    Font,
                    new Rectangle(BoxSize + LabelGap, 0, Math.Max(0, ClientSize.Width - BoxSize - LabelGap), ClientSize.Height),
                    Theme.Current.Text,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
            }
        }

        protected override void OnClick(EventArgs e)
        {
            Checked = !Checked;
            base.OnClick(e);
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space)
            {
                Checked = !Checked;
                e.Handled = true;
            }

            base.OnKeyUp(e);
        }
    }
}
