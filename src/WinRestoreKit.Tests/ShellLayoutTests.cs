using System;
using System.Linq;
using System.Drawing;
using System.Windows.Forms;
using WinRestoreKit;
using Xunit;

namespace WinRestoreKit.Tests
{
    public class ShellLayoutTests
    {
        [Fact]
        public void TitleWordmark_UsesNonOverlappingLabels()
        {
            using (MainForm form = new MainForm())
            {
                Label prefix = form.Controls.Find("lblWordmarkPrefix", true).OfType<Label>().Single();
                Label suffix = form.Controls.Find("lblWordmarkSuffix", true).OfType<Label>().Single();
                Label version = form.Controls.Find("lblVersion", true).OfType<Label>().Single();

                Assert.True(prefix.Right <= suffix.Left, "The Win and RestoreKit labels overlap.");
                Assert.True(suffix.Right <= version.Left, "The RestoreKit and version labels overlap.");
            }
        }

        [Fact]
        public void SegmentedControl_PaintsUnselectedSegmentsOverParentSurface()
        {
            Color parentSurface = Color.FromArgb(17, 24, 31);
            using (Panel parent = new Panel { BackColor = parentSurface, Size = new Size(120, 32) })
            using (SegmentedControl control = new SegmentedControl(new[] { "NONE", "FAST", "MAX" })
            {
                Location = Point.Empty,
                Size = new Size(120, 32)
            })
            using (Bitmap bitmap = new Bitmap(120, 32))
            {
                parent.Controls.Add(control);
                control.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                Assert.Equal(parentSurface.ToArgb(), bitmap.GetPixel(60, 2).ToArgb());
            }
        }
    }
}
