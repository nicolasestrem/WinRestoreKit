using System;
using System.Drawing;
using System.Windows.Forms;

namespace WinRestoreKit
{
    internal enum TagVariant
    {
        Accent,
        Accent2,
        Neutral,
        Outline
    }

    internal sealed class TagChip : Label
    {
        private TagVariant variant;

        internal TagChip()
        {
            AutoSize = true;
            Padding = new Padding(10, 3, 10, 3);
            BorderStyle = BorderStyle.None;
            TextAlign = ContentAlignment.MiddleCenter;
            Font = CreateTagFont();
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.SupportsTransparentBackColor,
                true);
            ApplyPalette();
        }

        internal TagVariant Variant
        {
            get => variant;
            set
            {
                if (variant == value)
                    return;

                variant = value;
                ApplyPalette();
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            ApplyPalette();
            base.OnPaint(e);

            if (variant == TagVariant.Outline && ClientSize.Width > 0 && ClientSize.Height > 0)
            {
                using (Pen border = new Pen(Theme.Current.Accent, 1f))
                {
                    e.Graphics.DrawRectangle(border, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
                }
            }
        }

        private void ApplyPalette()
        {
            switch (variant)
            {
                case TagVariant.Accent:
                    BackColor = Theme.Current.Accent100;
                    ForeColor = Theme.Current.Accent800;
                    break;

                case TagVariant.Accent2:
                    BackColor = Theme.Current.Accent2_100;
                    ForeColor = Theme.Current.Accent2_800;
                    break;

                case TagVariant.Neutral:
                    BackColor = Theme.Current.Neutral100;
                    ForeColor = Theme.Current.Neutral800;
                    break;

                default:
                    BackColor = Color.Transparent;
                    ForeColor = Theme.Current.Accent;
                    break;
            }
        }

        private static Font CreateTagFont()
        {
            using (Font body = Ui.Body())
            {
                return new Font(body.FontFamily, 11f, FontStyle.Regular, GraphicsUnit.Point);
            }
        }
    }
}
