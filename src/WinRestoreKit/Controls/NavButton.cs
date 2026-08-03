using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;

namespace WinRestoreKit
{
    internal sealed class NavButton : Button
    {
        private const float IconSize = 17f;
        private string label = string.Empty;
        private string lucidePath = string.Empty;
        private GraphicsPath iconPath = new GraphicsPath();
        private bool isSelected;

        internal NavButton()
        {
            Height = 38;
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            FlatAppearance.MouseDownBackColor = Color.Transparent;
            FlatAppearance.MouseOverBackColor = Color.Transparent;
            TextAlign = ContentAlignment.MiddleLeft;
            Padding = new Padding(36, 0, 8, 0);
            Font = Ui.Body();
            UseVisualStyleBackColor = false;
            BackColor = Color.Transparent;
            TabStop = true;
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.SupportsTransparentBackColor,
                true);
            ApplyPalette();
        }

        internal string LucidePath
        {
            get => lucidePath;
            set
            {
                lucidePath = value ?? string.Empty;
                iconPath.Dispose();
                iconPath = SvgPathParser.Parse(lucidePath);
                Invalidate();
            }
        }

        internal bool IsSelected
        {
            get => isSelected;
            set
            {
                if (isSelected == value)
                    return;

                isSelected = value;
                ApplyPalette();
                Invalidate();
            }
        }

        internal string Label
        {
            get => label;
            set
            {
                label = value ?? string.Empty;
                AccessibleName = label;
                Invalidate();
            }
        }


        protected override void OnPaint(PaintEventArgs e)
        {
            ApplyPalette();
            base.OnPaint(e);

            DrawIcon(e.Graphics);
            TextRenderer.DrawText(
                e.Graphics,
                label,
                Font,
                new Rectangle(36, 0, Math.Max(0, ClientSize.Width - 44), ClientSize.Height),
                ForeColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                iconPath.Dispose();

            base.Dispose(disposing);
        }

        private void ApplyPalette()
        {
            if (isSelected)
            {
                BackColor = Theme.Current.Accent;
                ForeColor = Theme.Current.Bg;
            }
            else
            {
                BackColor = Color.Transparent;
                ForeColor = Color.FromArgb(190, Theme.Current.Text);
            }
        }

        private void DrawIcon(Graphics graphics)
        {
            if (iconPath.PointCount == 0 || ClientSize.Height <= 0)
                return;

            float scale = IconSize / 24f;
            float x = 10f;
            float y = (ClientSize.Height - IconSize) / 2f;
            GraphicsState state = graphics.Save();
            try
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.TranslateTransform(x, y);
                graphics.ScaleTransform(scale, scale);
                using (Pen pen = new Pen(ForeColor, 1.5f / scale))
                {
                    pen.LineJoin = LineJoin.Round;
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    graphics.DrawPath(pen, iconPath);
                }
            }
            finally
            {
                graphics.Restore(state);
            }
        }

        private static class SvgPathParser
        {
            internal static GraphicsPath Parse(string source)
            {
                GraphicsPath path = new GraphicsPath();
                if (string.IsNullOrWhiteSpace(source))
                    return path;

                int index = 0;
                char command = '\0';
                PointF current = PointF.Empty;
                PointF start = PointF.Empty;
                bool hasCurrent = false;

                while (index < source.Length)
                {
                    SkipSeparators(source, ref index);
                    if (index >= source.Length)
                        break;

                    if (char.IsLetter(source[index]))
                        command = source[index++];
                    else if (command == '\0')
                        break;

                    bool relative = char.IsLower(command);
                    switch (char.ToUpperInvariant(command))
                    {
                        case 'M':
                            if (!TryReadPoint(source, ref index, out PointF move))
                                return path;

                            current = relative ? Offset(current, move) : move;
                            start = current;
                            hasCurrent = true;
                            path.StartFigure();
                            command = relative ? 'l' : 'L';
                            break;

                        case 'L':
                            if (!hasCurrent || !TryReadPoint(source, ref index, out PointF line))
                                return path;

                            PointF lineEnd = relative ? Offset(current, line) : line;
                            path.AddLine(current, lineEnd);
                            current = lineEnd;
                            break;

                        case 'H':
                            if (!hasCurrent || !TryReadNumber(source, ref index, out float horizontal))
                                return path;

                            PointF horizontalEnd = new PointF(relative ? current.X + horizontal : horizontal, current.Y);
                            path.AddLine(current, horizontalEnd);
                            current = horizontalEnd;
                            break;

                        case 'V':
                            if (!hasCurrent || !TryReadNumber(source, ref index, out float vertical))
                                return path;

                            PointF verticalEnd = new PointF(current.X, relative ? current.Y + vertical : vertical);
                            path.AddLine(current, verticalEnd);
                            current = verticalEnd;
                            break;

                        case 'C':
                            if (!hasCurrent || !TryReadPoint(source, ref index, out PointF control1)
                                || !TryReadPoint(source, ref index, out PointF control2)
                                || !TryReadPoint(source, ref index, out PointF curveEnd))
                            {
                                return path;
                            }

                            PointF c1 = relative ? Offset(current, control1) : control1;
                            PointF c2 = relative ? Offset(current, control2) : control2;
                            PointF end = relative ? Offset(current, curveEnd) : curveEnd;
                            path.AddBezier(current, c1, c2, end);
                            current = end;
                            break;

                        case 'A':
                            if (!hasCurrent || !TryReadArcEnd(source, ref index, out PointF arcEnd))
                                return path;

                            PointF absoluteArcEnd = relative ? Offset(current, arcEnd) : arcEnd;
                            path.AddLine(current, absoluteArcEnd);
                            current = absoluteArcEnd;
                            break;

                        case 'Z':
                            if (hasCurrent)
                            {
                                path.CloseFigure();
                                current = start;
                            }
                            command = '\0';
                            break;

                        default:
                            return path;
                    }
                }

                return path;
            }

            private static bool TryReadArcEnd(string source, ref int index, out PointF end)
            {
                end = PointF.Empty;
                return TryReadNumber(source, ref index, out _)
                    && TryReadNumber(source, ref index, out _)
                    && TryReadNumber(source, ref index, out _)
                    && TryReadNumber(source, ref index, out _)
                    && TryReadNumber(source, ref index, out _)
                    && TryReadPoint(source, ref index, out end);
            }

            private static bool TryReadPoint(string source, ref int index, out PointF point)
            {
                point = PointF.Empty;
                return TryReadNumber(source, ref index, out float x)
                    && TryReadNumber(source, ref index, out float y)
                    && SetPoint(x, y, out point);
            }

            private static bool SetPoint(float x, float y, out PointF point)
            {
                point = new PointF(x, y);
                return true;
            }

            private static bool TryReadNumber(string source, ref int index, out float value)
            {
                value = 0f;
                SkipSeparators(source, ref index);
                if (index >= source.Length)
                    return false;

                int start = index;
                if (source[index] == '+' || source[index] == '-')
                    index++;

                bool sawDigit = false;
                while (index < source.Length && char.IsDigit(source[index]))
                {
                    sawDigit = true;
                    index++;
                }

                if (index < source.Length && source[index] == '.')
                {
                    index++;
                    while (index < source.Length && char.IsDigit(source[index]))
                    {
                        sawDigit = true;
                        index++;
                    }
                }

                if (!sawDigit)
                {
                    index = start;
                    return false;
                }

                if (index < source.Length && (source[index] == 'e' || source[index] == 'E'))
                {
                    int exponentStart = index++;
                    if (index < source.Length && (source[index] == '+' || source[index] == '-'))
                        index++;

                    int digitStart = index;
                    while (index < source.Length && char.IsDigit(source[index]))
                        index++;

                    if (digitStart == index)
                        index = exponentStart;
                }

                return float.TryParse(
                    source.Substring(start, index - start),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out value);
            }

            private static void SkipSeparators(string source, ref int index)
            {
                while (index < source.Length && (char.IsWhiteSpace(source[index]) || source[index] == ','))
                    index++;
            }

            private static PointF Offset(PointF point, PointF offset)
                => new PointF(point.X + offset.X, point.Y + offset.Y);
        }
    }
}
