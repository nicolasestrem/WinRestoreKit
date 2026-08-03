using System;
using System.Drawing;
using System.Windows.Forms;

namespace WinRestoreKit
{
    internal sealed class SegmentedControl : Panel
    {
        private string[] options = Array.Empty<string>();
        private int selectedIndex = -1;

        internal SegmentedControl()
        {
            Height = 32;
            DoubleBuffered = true;
            Font = Ui.Body();
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.SupportsTransparentBackColor,
                true);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
        }

        internal SegmentedControl(string[] options)
            : this()
        {
            Options = options;
        }

        internal string[] Options
        {
            get => options;
            set
            {
                options = value ?? Array.Empty<string>();
                int newIndex = options.Length == 0 ? -1 : 0;
                bool changed = selectedIndex != newIndex;
                selectedIndex = newIndex;
                Invalidate();

                if (changed)
                    SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        internal int SelectedIndex
        {
            get => selectedIndex;
            set
            {
                int normalized = value >= 0 && value < options.Length ? value : -1;
                if (selectedIndex == normalized)
                    return;

                selectedIndex = normalized;
                Invalidate();
                SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        internal event EventHandler SelectedIndexChanged;

        protected override void OnPaintBackground(PaintEventArgs e)
        {
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Color surface = GetParentSurfaceColor();
            using (Brush background = new SolidBrush(surface))
            {
                e.Graphics.FillRectangle(background, ClientRectangle);
            }

            base.OnPaint(e);

            if (options.Length == 0 || ClientSize.Width <= 0 || ClientSize.Height <= 0)
                return;

            int width = ClientSize.Width;
            int height = ClientSize.Height;
            for (int index = 0; index < options.Length; index++)
            {
                int left = index * width / options.Length;
                int right = (index + 1) * width / options.Length;
                Rectangle segment = new Rectangle(left, 0, right - left, height);

                if (index == selectedIndex)
                {
                    using (Brush selected = new SolidBrush(Theme.Current.Accent))
                    {
                        e.Graphics.FillRectangle(selected, segment);
                    }
                }

                TextRenderer.DrawText(
                    e.Graphics,
                    options[index] ?? string.Empty,
                    Font,
                    segment,
                    index == selectedIndex ? Theme.Current.Bg : Theme.Current.Text,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
            }

            using (Pen border = new Pen(Theme.Current.Divider, 1f))
            {
                e.Graphics.DrawRectangle(border, 0, 0, width - 1, height - 1);
                for (int index = 1; index < options.Length; index++)
            {
                    int x = index * width / options.Length;
                    e.Graphics.DrawLine(border, x, 0, x, height - 1);
                }
            }
        }

        private Color GetParentSurfaceColor()
        {
            for (Control ancestor = Parent; ancestor != null; ancestor = ancestor.Parent)
            {
                if (ancestor.BackColor.A != 0)
                    return ancestor.BackColor;
            }

            return Theme.Current.Bg;
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);

            if (e.Button != MouseButtons.Left || options.Length == 0 || ClientSize.Width == 0)
                return;

            SelectedIndex = Math.Min(options.Length - 1, e.X * options.Length / ClientSize.Width);
        }
    }
}
