using Microsoft.Win32;
using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace WinRestoreKit
{
    /// <summary>User-selectable palette mode. Persisted under HKCU\Software\WinRestoreKit\PaletteMode.</summary>
    internal enum PaletteMode
    {
        FollowSystem = 0,
        Voltage = 1,
        Flux = 2
    }

    /// <summary>Industry design-system colour tokens for one palette (Voltage or Flux).</summary>
    internal sealed class PaletteV2
    {
        internal Color Bg;
        internal Color Surface;
        internal Color Text;
        internal Color Accent;
        internal Color Accent2;
        internal Color Divider;

        internal Color Neutral100;
        internal Color Neutral200;
        internal Color Neutral300;
        internal Color Neutral400;
        internal Color Neutral500;
        internal Color Neutral600;
        internal Color Neutral700;
        internal Color Neutral800;
        internal Color Neutral900;

        internal Color Accent100;
        internal Color Accent200;
        internal Color Accent300;
        internal Color Accent400;
        internal Color Accent500;
        internal Color Accent600;
        internal Color Accent700;
        internal Color Accent800;
        internal Color Accent900;

        internal Color Accent2_100;
        internal Color Accent2_200;
        internal Color Accent2_300;
        internal Color Accent2_400;
        internal Color Accent2_500;
        internal Color Accent2_600;
        internal Color Accent2_700;
        internal Color Accent2_800;
        internal Color Accent2_900;

        /// <summary>Muted body text. Voltage Neutral600 / Flux Neutral600.</summary>
        internal Color TextMuted => Neutral600;

        /// <summary>Voltage (light) palette. Exact hex from ds-industry-volt.css.</summary>
        internal static PaletteV2 Voltage() => new PaletteV2
        {
            Bg = Hex("#f4f6f8"),
            Surface = Hex("#e9edf1"),
            Text = Hex("#1d1f20"),
            Accent = Hex("#1f6fff"),
            Accent2 = Hex("#f78f00"),
            // Text at ~16% over Bg, solid approximation.
            Divider = Hex("#cbd2d8"),

            Neutral100 = Hex("#f5f5f8"),
            Neutral200 = Hex("#e7e7ea"),
            Neutral300 = Hex("#d4d4d7"),
            Neutral400 = Hex("#b7b7ba"),
            Neutral500 = Hex("#98989b"),
            Neutral600 = Hex("#7a7a7d"),
            Neutral700 = Hex("#5d5d60"),
            Neutral800 = Hex("#424244"),
            Neutral900 = Hex("#2b2b2d"),

            Accent100 = Hex("#eaf1ff"),
            Accent200 = Hex("#cfe0ff"),
            Accent300 = Hex("#a8c6ff"),
            Accent400 = Hex("#76a2ff"),
            Accent500 = Hex("#3d7dff"),
            Accent600 = Hex("#0b53e8"),
            Accent700 = Hex("#0a3cae"),
            Accent800 = Hex("#0a2b7a"),
            Accent900 = Hex("#0d1f4d"),

            Accent2_100 = Hex("#fff4e0"),
            Accent2_200 = Hex("#ffe2b3"),
            Accent2_300 = Hex("#ffcb7a"),
            Accent2_400 = Hex("#ffae38"),
            Accent2_500 = Hex("#f78f00"),
            Accent2_600 = Hex("#cc7000"),
            Accent2_700 = Hex("#9c5400"),
            Accent2_800 = Hex("#6f3c00"),
            Accent2_900 = Hex("#452700"),
        };

        /// <summary>Flux (dark) palette. Exact hex from ds-industry-flux.css.</summary>
        internal static PaletteV2 Flux() => new PaletteV2
        {
            Bg = Hex("#0f1418"),
            Surface = Hex("#161d23"),
            Text = Hex("#e6edf3"),
            Accent = Hex("#22d3ee"),
            Accent2 = Hex("#e0388a"),
            // white at ~24% over Bg, solid approximation.
            Divider = Hex("#3c4148"),

            Neutral100 = Hex("#1b232a"),
            Neutral200 = Hex("#232c34"),
            Neutral300 = Hex("#2c3740"),
            Neutral400 = Hex("#465360"),
            Neutral500 = Hex("#697787"),
            Neutral600 = Hex("#8b98a6"),
            Neutral700 = Hex("#adb8c4"),
            Neutral800 = Hex("#cfd7e0"),
            Neutral900 = Hex("#e8eef4"),

            Accent100 = Hex("#0b2b33"),
            Accent200 = Hex("#0f3b47"),
            Accent300 = Hex("#155e6e"),
            Accent400 = Hex("#1b8399"),
            Accent500 = Hex("#22b8d6"),
            Accent600 = Hex("#4ee0f5"),
            Accent700 = Hex("#7ce9f8"),
            Accent800 = Hex("#a9f2fb"),
            Accent900 = Hex("#d7f9fd"),

            Accent2_100 = Hex("#330f22"),
            Accent2_200 = Hex("#4d1531"),
            Accent2_300 = Hex("#73204a"),
            Accent2_400 = Hex("#a02a66"),
            Accent2_500 = Hex("#e0388a"),
            Accent2_600 = Hex("#f45fa4"),
            Accent2_700 = Hex("#fb8fc0"),
            Accent2_800 = Hex("#ffb8d6"),
            Accent2_900 = Hex("#ffdbea"),
        };

        internal static PaletteV2 FromMode(PaletteMode mode)
        {
            if (mode == PaletteMode.FollowSystem)
                return Theme.IsDarkOs() ? Flux() : Voltage();

            return mode == PaletteMode.Flux ? Flux() : Voltage();
        }

        private static Color Hex(string hex)
        {
            string h = hex.TrimStart('#');
            int r = Convert.ToInt32(h.Substring(0, 2), 16);
            int g = Convert.ToInt32(h.Substring(2, 2), 16);
            int b = Convert.ToInt32(h.Substring(4, 2), 16);
            return Color.FromArgb(r, g, b);
        }
    }

    /// <summary>
    /// Controls that paint themselves - state chips, backup cards, the primary action button - and
    /// which <see cref="Theme.Apply"/> therefore steps over.
    /// </summary>
    internal sealed class AccentLabel : Label { }

    /// <inheritdoc cref="AccentLabel"/>
    internal sealed class AccentButton : Button { }

    /// <inheritdoc cref="AccentLabel"/>
    internal sealed class AccentPanel : TableLayoutPanel { }

    /// <summary>
    /// Voltage / Flux / FollowSystem palettes plus the control-tree walker that applies them.
    /// </summary>
    internal static class Theme
    {
        private const string RegistryKeyPath = @"Software\WinRestoreKit";
        private const string RegistryValueName = "PaletteMode";

        internal static PaletteV2 Current { get; private set; } = PaletteV2.Voltage();

        internal static PaletteMode Mode { get; private set; } = PaletteMode.FollowSystem;

        /// <summary>True when the active palette is the dark (Flux) one.</summary>
        internal static bool IsDark => Current.Bg.GetBrightness() < 0.5f;

        /// <summary>
        /// Reads the persisted palette mode from the registry and sets <see cref="Current"/>.
        /// Call from Program.Main before constructing MainForm.
        /// </summary>
        internal static void Initialize()
        {
            Mode = ReadMode();
            Current = PaletteV2.FromMode(Mode);
        }

        /// <summary>Switches the active palette and persists the choice.</summary>
        internal static void SetMode(PaletteMode mode)
        {
            Mode = mode;
            Current = PaletteV2.FromMode(mode);
            WriteMode(mode);
        }

        /// <summary>Recomputes Current from the current Mode (e.g. after an OS light/dark flip).</summary>
        internal static void RefreshFromMode()
        {
            Current = PaletteV2.FromMode(Mode);
        }

        /// <summary>
        /// Whether Windows is in dark app mode. Any failure reads as light.
        /// </summary>
        internal static bool IsDarkOs()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(
                           @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    if (key == null)
                        return false;

                    return key.GetValue("AppsUseLightTheme") is int light && light == 0;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static PaletteMode ReadMode()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath))
                {
                    if (key == null)
                        return PaletteMode.FollowSystem;

                    object raw = key.GetValue(RegistryValueName);
                    if (raw is int i && Enum.IsDefined(typeof(PaletteMode), i))
                        return (PaletteMode)i;
                }
            }
            catch (Exception)
            {
                // Fall through to default.
            }

            return PaletteMode.FollowSystem;
        }

        private static void WriteMode(PaletteMode mode)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RegistryKeyPath))
                {
                    if (key != null)
                        key.SetValue(RegistryValueName, (int)mode, RegistryValueKind.DWord);
                }
            }
            catch (Exception)
            {
                // Persistence is best-effort.
            }
        }

        /// <summary>
        /// Paints a control tree with the current palette, by control kind.
        /// </summary>
        internal static void Apply(Control root)
        {
            if (root == null)
                return;

            // Paints itself: step over it but still theme what it contains.
            if (root is AccentLabel || root is AccentButton || root is AccentPanel)
            {
                foreach (Control accentChild in root.Controls)
                    Apply(accentChild);

                return;
            }

            // Blueprint frames and Keyed marks paint themselves.
            string typeName = root.GetType().Name;
            if (typeName == "BlueprintFrame" || typeName == "KeyedMark" || typeName == "NavButton"
                || typeName == "CustomCheckbox" || typeName == "SegmentedControl" || typeName == "TagChip")
            {
                foreach (Control child in root.Controls)
                    Apply(child);
                return;
            }

            PaletteV2 p = Current;

            switch (root)
            {
                case TextBox textBox:
                    textBox.BackColor = textBox.ReadOnly ? p.Bg : p.Surface;
                    textBox.ForeColor = RemapSemantic(textBox.ForeColor, p);
                    break;

                case RichTextBox richTextBox:
                    richTextBox.BackColor = p.Bg;
                    richTextBox.ForeColor = p.TextMuted;
                    break;

                case TreeView tree:
                    tree.BackColor = p.Bg;
                    tree.ForeColor = p.Text;
                    ApplyExplorerTheme(tree);
                    break;

                case ListBox list:
                    list.BackColor = p.Surface;
                    list.ForeColor = p.Text;
                    ApplyExplorerTheme(list);
                    break;

                case ComboBox combo:
                    combo.BackColor = p.Surface;
                    combo.ForeColor = p.Text;
                    break;

                case LinkLabel link:
                    link.BackColor = Color.Transparent;
                    link.LinkColor = RemapSemantic(link.LinkColor, p);
                    link.ActiveLinkColor = link.LinkColor;
                    break;

                case Button button:
                    button.ForeColor = p.Text;
                    button.BackColor = p.Surface;
                    button.FlatAppearance.BorderColor = p.Divider;
                    break;

                case CheckBox check:
                    check.BackColor = Color.Transparent;
                    check.ForeColor = RemapSemantic(check.ForeColor, p);
                    break;

                case RadioButton radio:
                    radio.BackColor = Color.Transparent;
                    radio.ForeColor = p.Text;
                    break;

                case Label label:
                    label.BackColor = Color.Transparent;
                    label.ForeColor = RemapSemantic(label.ForeColor, p);
                    break;

                case Panel panel:
                    // A hairline separator is a Divider-coloured Panel.
                    panel.BackColor = IsDividerColor(panel.BackColor)
                        ? p.Divider
                        : p.Bg;
                    panel.ForeColor = p.Text;
                    break;

                case Form form:
                    form.BackColor = p.Bg;
                    form.ForeColor = p.Text;
                    break;

                default:
                    root.BackColor = p.Bg;
                    root.ForeColor = p.Text;
                    break;
            }

            foreach (Control child in root.Controls)
                Apply(child);
        }

        /// <summary>
        /// Carries a label's MEANING across a palette change: muted stays muted, caution stays
        /// caution, danger stays danger, and anything else becomes ordinary body text.
        /// </summary>
        private static Color RemapSemantic(Color current, PaletteV2 p)
        {
            PaletteV2 v = PaletteV2.Voltage();
            PaletteV2 f = PaletteV2.Flux();

            if (current == v.TextMuted || current == f.TextMuted
                || current == v.Neutral600 || current == f.Neutral600)
                return p.TextMuted;

            // Caution / danger both map to Accent2-600 in the new system.
            if (current == v.Accent2_600 || current == f.Accent2_600
                || current == Color.FromArgb(150, 92, 0) || current == Color.FromArgb(240, 190, 90)
                || current == Color.FromArgb(168, 34, 34) || current == Color.FromArgb(255, 120, 120))
                return p.Accent2_600;

            if (current == v.Accent700 || current == f.Accent700)
                return p.Accent700;

            if (current == v.Accent || current == f.Accent)
                return p.Accent;

            return p.Text;
        }

        private static bool IsDividerColor(Color c)
        {
            PaletteV2 v = PaletteV2.Voltage();
            PaletteV2 f = PaletteV2.Flux();
            return c == v.Divider || c == f.Divider
                || c == Color.FromArgb(220, 220, 220)
                || c == Color.FromArgb(110, 110, 110);
        }

        // ---------------------------------------------------------------------------------------------
        //  Cosmetic P/Invokes.
        // ---------------------------------------------------------------------------------------------

        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hwnd, string subAppName, string subIdList);

        /// <summary>Paints the title bar to match the theme.</summary>
        internal static void ApplyTitleBar(Form form)
        {
            if (form == null || !form.IsHandleCreated)
                return;

            try
            {
                int on = IsDark ? 1 : 0;
                DwmSetWindowAttribute(form.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref on, sizeof(int));
            }
            catch (Exception)
            {
                // Cosmetic only.
            }
        }

        /// <summary>
        /// Dark scrollbars on a TreeView/ListBox.
        /// </summary>
        private static void ApplyExplorerTheme(Control control)
        {
            if (control == null || !control.IsHandleCreated)
                return;

            try
            {
                SetWindowTheme(control.Handle, IsDark ? "DarkMode_Explorer" : "Explorer", null);
            }
            catch (Exception)
            {
                // Cosmetic only.
            }
        }
    }
}
