using Microsoft.Win32;
using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace WinRestoreKit
{
    /// <summary>One theme's colour tokens. Two instances exist: <see cref="Theme.Light"/> and Dark.</summary>
    internal sealed class Palette
    {
        internal Color Surface;
        internal Color RailSurface;
        internal Color CardSurface;
        internal Color TextPrimary;
        internal Color TextMuted;
        internal Color Border;
        internal Color Danger;
        internal Color Caution;
        internal Color ChipSucceededBack;
        internal Color ChipSucceededFore;
        internal Color ChipSkippedBack;
        internal Color ChipSkippedFore;
        internal Color ChipFailedBack;
        internal Color ChipFailedFore;
        internal Color InputBack;
    }

    /// <summary>
    /// Controls that paint themselves - state chips, backup cards, the primary action button - and
    /// which <see cref="Theme.Apply"/> therefore steps over.
    /// </summary>
    /// <remarks>
    /// A marker TYPE rather than a registry of instances: chips and cards are rebuilt on every
    /// render, so a HashSet of opted-out controls would grow without bound and hold disposed
    /// controls alive. The walker still recurses into their children.
    /// </remarks>
    internal sealed class AccentLabel : Label { }

    /// <inheritdoc cref="AccentLabel"/>
    internal sealed class AccentButton : Button { }

    /// <inheritdoc cref="AccentLabel"/>
    internal sealed class AccentPanel : TableLayoutPanel { }

    /// <summary>
    /// Light and dark colour tokens plus the control-tree walker that applies them.
    /// </summary>
    /// <remarks>
    /// Hand-rolled on purpose. <c>Application.SetColorMode</c> is .NET 9+ and experimental
    /// (WFO5001); it is not available on net8.0-windows. .NET 8 reaches end of life in November
    /// 2026, and a later retarget would let SetColorMode replace most of this - so this class is
    /// deliberately thin and disposable rather than a framework to grow.
    ///
    /// Light is today's values moved, not chosen, so switching to it is a no-op against the
    /// pre-Phase-4 look. Skipped is amber in BOTH palettes and never green: the styling has to keep
    /// the distinction the engine's three-state result fought for.
    ///
    /// MessageBoxes and common dialogs stay light no matter what. That is disclosed, not chased -
    /// owner-drawing our way out of it is a budget this phase does not have, and Path D already cut
    /// the remaining MessageBoxes down to the consent-class prompts.
    /// </remarks>
    internal static class Theme
    {
        internal static readonly Palette Light = new Palette
        {
            Surface = Color.FromArgb(243, 243, 243),
            RailSurface = Color.FromArgb(245, 241, 249),
            CardSurface = Color.FromArgb(250, 250, 250),
            TextPrimary = Color.Black,
            TextMuted = Color.DimGray,
            Border = Color.FromArgb(220, 220, 220),
            Danger = Color.FromArgb(168, 34, 34),
            Caution = Color.FromArgb(150, 92, 0),
            ChipSucceededBack = Color.FromArgb(39, 124, 74),
            ChipSucceededFore = Color.White,
            ChipSkippedBack = Color.FromArgb(150, 92, 0),
            ChipSkippedFore = Color.White,
            ChipFailedBack = Color.FromArgb(168, 34, 34),
            ChipFailedFore = Color.White,
            InputBack = Color.FromArgb(250, 250, 250),
        };

        internal static readonly Palette Dark = new Palette
        {
            Surface = Color.FromArgb(32, 32, 32),
            RailSurface = Color.FromArgb(50, 50, 50),
            CardSurface = Color.FromArgb(56, 56, 56),
            TextPrimary = Color.FromArgb(240, 240, 240),
            TextMuted = Color.FromArgb(170, 170, 170),
            // 3.2:1 against Surface. The first pass used (65,65,65) - 1.6:1 - which is invisible,
            // and since CardSurface was 1.15:1 against Surface as well, nothing on a dark screen had
            // an edge: backup cards, result rows and text boxes all dissolved into the background.
            // The text was never the problem; every foreground token here clears 6:1. Borders are
            // what make a surface a surface, and 3:1 is the floor for a boundary that carries
            // meaning rather than decoration.
            Border = Color.FromArgb(110, 110, 110),
            Danger = Color.FromArgb(255, 120, 120),
            Caution = Color.FromArgb(240, 190, 90),
            ChipSucceededBack = Color.FromArgb(38, 104, 66),
            ChipSucceededFore = Color.White,
            ChipSkippedBack = Color.FromArgb(140, 96, 20),
            ChipSkippedFore = Color.White,
            ChipFailedBack = Color.FromArgb(150, 48, 48),
            ChipFailedFore = Color.White,
            InputBack = Color.FromArgb(56, 56, 56),
        };

        internal static Palette Current { get; private set; } = Light;

        internal static bool IsDark { get; private set; }

        /// <summary>Switches the active palette. Callers re-apply afterwards.</summary>
        internal static void Use(bool dark)
        {
            IsDark = dark;
            Current = dark ? Dark : Light;
        }

        /// <summary>
        /// Whether Windows is in dark app mode. Any failure reads as light.
        /// </summary>
        /// <remarks>
        /// AppsUseLightTheme (0 = dark) rather than SystemUsesLightTheme: the former is the app
        /// setting, the latter is the taskbar/Start setting, and they are set independently.
        /// </remarks>
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

        /// <summary>
        /// Paints a control tree with the current palette, by control kind.
        /// </summary>
        /// <remarks>
        /// Called by MainForm for the whole shell and by the two dialogs on themselves, since they
        /// are constructed after startup and are not in the shell's tree.
        /// </remarks>
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

            Palette p = Current;

            switch (root)
            {
                case TextBox textBox:
                    textBox.BackColor = textBox.ReadOnly ? p.Surface : p.InputBack;
                    // Remapped, not flattened: the wizard's inline warnings and the results panel's
                    // reasons are read-only TextBoxes precisely so they stay selectable, and they
                    // carry Caution and Muted. Assigning TextPrimary here would drop the distinction
                    // on a live theme switch.
                    textBox.ForeColor = RemapSemantic(textBox.ForeColor, p);
                    break;

                case RichTextBox richTextBox:
                    richTextBox.BackColor = p.Surface;
                    richTextBox.ForeColor = p.TextMuted;
                    break;

                case TreeView tree:
                    tree.BackColor = p.Surface;
                    tree.ForeColor = p.TextPrimary;
                    ApplyExplorerTheme(tree);
                    break;

                case ListBox list:
                    list.BackColor = p.InputBack;
                    list.ForeColor = p.TextPrimary;
                    ApplyExplorerTheme(list);
                    break;

                case ComboBox combo:
                    combo.BackColor = p.InputBack;
                    combo.ForeColor = p.TextPrimary;
                    break;

                case LinkLabel link:
                    link.BackColor = Color.Transparent;
                    // Remapped for the same reason labels are: History's "Open folder" is muted so
                    // it reads as secondary to "Restore from this backup" beside it. Flattening both
                    // to TextPrimary here collapsed that hierarchy on every refresh.
                    link.LinkColor = RemapSemantic(link.LinkColor, p);
                    link.ActiveLinkColor = link.LinkColor;
                    break;

                case Button button:
                    // Chips and the primary action button paint themselves; leave any control that
                    // has opted out of the palette alone rather than flattening it.
                    button.ForeColor = p.TextPrimary;
                    button.BackColor = p.CardSurface;
                    button.FlatAppearance.BorderColor = p.Border;
                    break;

                case CheckBox check:
                    check.BackColor = Color.Transparent;
                    // Muted here means the row is inert - "(nothing in this backup)" in restore
                    // step 2 - so it has to survive a repaint like any other semantic colour.
                    check.ForeColor = RemapSemantic(check.ForeColor, p);
                    break;

                case RadioButton radio:
                    radio.BackColor = Color.Transparent;
                    radio.ForeColor = p.TextPrimary;
                    break;

                case Label label:
                    label.BackColor = Color.Transparent;
                    label.ForeColor = RemapSemantic(label.ForeColor, p);
                    break;

                case Panel panel:
                    // A hairline separator is a Border-coloured Panel, so it has to survive the walk
                    // like any other semantic colour - the default below would repaint it Surface
                    // and leave an invisible 1px gap where a divider should be.
                    panel.BackColor = panel.BackColor == Light.Border || panel.BackColor == Dark.Border
                        ? p.Border
                        : p.Surface;
                    panel.ForeColor = p.TextPrimary;
                    break;

                default:
                    root.BackColor = p.Surface;
                    root.ForeColor = p.TextPrimary;
                    break;
            }

            foreach (Control child in root.Controls)
                Apply(child);
        }

        /// <summary>
        /// Carries a label's MEANING across a palette change: muted stays muted, caution stays
        /// caution, danger stays danger, and anything else becomes ordinary body text.
        /// </summary>
        /// <remarks>
        /// This used to skip semantic labels instead of remapping them, on the assumption that the
        /// view would re-apply its own colours afterwards. No view does - every <c>Ui.Caution</c> is
        /// read once in a constructor and the views are built once and reused. So on a live OS
        /// switch a banner built in light mode kept <see cref="Light"/>'s dark amber (150,92,0) and
        /// sat on dark mode's (32,32,32) surface, which is the one combination the amber was chosen
        /// to avoid.
        ///
        /// Both palettes are checked for each token because this runs AFTER <c>Current</c> has
        /// already flipped, so the colour being matched is the outgoing one.
        /// </remarks>
        private static Color RemapSemantic(Color current, Palette p)
        {
            if (current == Light.TextMuted || current == Dark.TextMuted)
                return p.TextMuted;

            if (current == Light.Caution || current == Dark.Caution)
                return p.Caution;

            if (current == Light.Danger || current == Dark.Danger)
                return p.Danger;

            return p.TextPrimary;
        }

        // ---------------------------------------------------------------------------------------------
        //  Cosmetic P/Invokes. BOTH are wrapped: these are decoration in an elevated process and must
        //  never be able to throw. If a future Windows build breaks them, deleting the calls is safe -
        //  nothing depends on them.
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
        /// Dark scrollbars on a TreeView/ListBox. Undocumented but stable, and used by essentially
        /// every dark WinForms app.
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
