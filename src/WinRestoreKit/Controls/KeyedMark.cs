using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace WinRestoreKit
{
    internal sealed class KeyedMark : Control
    {
        private const float GridSize = 96f;

        internal KeyedMark()
        {
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

            float xScale = ClientSize.Width / GridSize;
            float yScale = ClientSize.Height / GridSize;
            float smallestSide = Math.Min(ClientSize.Width, ClientSize.Height);
            float strokeScale = Math.Min(xScale, yScale);

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            e.Graphics.ScaleTransform(xScale, yScale);

            using (Pen plate = new Pen(Theme.Current.Text, StrokeWidth(smallestSide) / strokeScale))
            using (Brush tab = new SolidBrush(Theme.Current.Accent))
            {
                e.Graphics.DrawRectangle(plate, 21f, 27f, 54f, 42f);
                e.Graphics.FillRectangle(tab, 40f, 17f, 14f, 10f);
            }
        }

        private static float StrokeWidth(float smallestSide)
        {
            if (smallestSide <= 16f)
                return 5f;

            if (smallestSide <= 24f)
                return 4f;

            return 2f;
        }
    }
}
