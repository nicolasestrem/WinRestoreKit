using System.Drawing;

namespace WinRestoreKit
{
    /// <summary>
    /// Shared spacing and typography for the rebuilt views.
    /// </summary>
    /// <remarks>
    /// Deliberately small and deliberately not a theme. Colour tokens, the light/dark palettes and the
    /// system-preference walker are PR 9's job; putting a half-built Theme here would mean PR 9 either
    /// inherits an API it did not design or deletes one that already has callers. What lives here is
    /// only what the shell and Home need to avoid hard-coding the same four numbers in two files.
    ///
    /// The fonts are the pairing RestoreConfirmForm already uses, so the new screens do not introduce a
    /// third typographic voice into an app that is mid-revamp.
    ///
    /// Colours below are today's light values verbatim, moved rather than chosen - the shell has to
    /// paint something, and inventing a palette here would make PR 9's diff a redesign instead of a
    /// theming pass.
    /// </remarks>
    internal static class Ui
    {
        internal const int SpaceXs = 4;
        internal const int SpaceS = 8;
        internal const int SpaceM = 12;
        internal const int SpaceL = 24;

        internal const string BodyFamily = "Segoe UI Variable Text";
        internal const string DisplayFamily = "Segoe UI Variable Display";

        /// <summary>Glyph font. A font, therefore DPI-free - which is why glyphs are not images.</summary>
        internal const string IconFamily = "Segoe Fluent Icons";

        internal static Font Body() => new Font(BodyFamily, 9.75f);

        internal static Font BodyBold() => new Font(BodyFamily, 9.75f, FontStyle.Bold);

        internal static Font Title() => new Font(DisplayFamily, 16f, FontStyle.Bold);

        internal static Font Heading() => new Font(DisplayFamily, 12f);

        internal static Font Icon() => new Font(IconFamily, 12f);

        // ---------------------------------------------------------------------------------------------
        //  Colour tokens. These forward to the active palette in Theme, so every existing Ui.* call
        //  site became theme-aware without changing. Fonts and spacing above stay here; palettes and
        //  the control-tree walker live in Theme (PR 9).
        // ---------------------------------------------------------------------------------------------

        internal static Color Surface => Theme.Current.Surface;

        internal static Color RailSurface => Theme.Current.RailSurface;

        internal static Color CardSurface => Theme.Current.CardSurface;

        /// <summary>Primary text. Replaces the inline Color.Black the views used to hardcode.</summary>
        internal static Color TextPrimary => Theme.Current.TextPrimary;

        internal static Color Muted => Theme.Current.TextMuted;

        internal static Color Border => Theme.Current.Border;

        internal static Color InputBack => Theme.Current.InputBack;

        /// <summary>Failure text.</summary>
        internal static Color Danger => Theme.Current.Danger;

        /// <summary>
        /// An outcome that is not a success and not a failure either - skipped, or not recorded.
        /// </summary>
        /// <remarks>
        /// Amber and deliberately never green, in BOTH palettes: the styling has to keep the
        /// distinction the engine's three-state result fought for, and an item with no recorded
        /// outcome must not read as one that went fine.
        /// </remarks>
        internal static Color Caution => Theme.Current.Caution;

        internal static Color ChipSucceededBack => Theme.Current.ChipSucceededBack;

        internal static Color ChipSucceededFore => Theme.Current.ChipSucceededFore;

        internal static Color ChipSkippedBack => Theme.Current.ChipSkippedBack;

        internal static Color ChipSkippedFore => Theme.Current.ChipSkippedFore;

        internal static Color ChipFailedBack => Theme.Current.ChipFailedBack;

        internal static Color ChipFailedFore => Theme.Current.ChipFailedFore;
    }
}
