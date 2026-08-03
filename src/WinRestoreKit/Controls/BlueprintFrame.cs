using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace WinRestoreKit
{
    internal sealed class BlueprintFrame : Panel
    {
        private const int CornerSize = 11;
        private const int CornerOffset = -6;

        internal BlueprintFrame()
        {
            Padding = new Padding(6);
            BorderStyle = BorderStyle.None;
            DoubleBuffered = true;
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.SupportsTransparentBackColor,
                true);
            BackColor = Color.Transparent;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            if (ClientSize.Width <= 0 || ClientSize.Height <= 0)
                return;

            e.Graphics.SmoothingMode = SmoothingMode.None;
            using (Pen border = new Pen(Theme.Current.Divider, 1f))
            {
                e.Graphics.DrawRectangle(border, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
            }

            Color cornerColor = Color.FromArgb(
                140,
                Theme.Current.Text.R,
                Theme.Current.Text.G,
                Theme.Current.Text.B);
            using (Pen corner = new Pen(cornerColor, 1f))
            {
                DrawCrosshair(e.Graphics, corner, CornerOffset, CornerOffset);
                DrawCrosshair(e.Graphics, corner, ClientSize.Width - CornerOffset - CornerSize, CornerOffset);
                DrawCrosshair(e.Graphics, corner, CornerOffset, ClientSize.Height - CornerOffset - CornerSize);
                DrawCrosshair(
                    e.Graphics,
                    corner,
                    ClientSize.Width - CornerOffset - CornerSize,
                    ClientSize.Height - CornerOffset - CornerSize);
            }
        }

        private static void DrawCrosshair(Graphics graphics, Pen pen, int x, int y)
        {
            int center = CornerSize / 2;
            graphics.DrawLine(pen, x, y + center, x + CornerSize - 1, y + center);
            graphics.DrawLine(pen, x + center, y, x + center, y + CornerSize - 1);
        }
    }
}
