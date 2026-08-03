using System.Drawing;

namespace WinRestoreKit
{
    internal static class Ui
    {
        internal const int SpaceXs = 4;
        internal const int SpaceS = 8;
        internal const int SpaceM = 12;
        internal const int SpaceL = 24;

        internal const string FontBody = "Barlow";
        internal const string FontHeading = "Barlow Condensed";
        internal const string FontMono = "IBM Plex Mono";
        internal const string IconFamily = "Segoe Fluent Icons";

        internal static Font Heading() => FontLoader.Load(FontHeading, 30f, FontStyle.Bold);

        internal static Font Heading2() => FontLoader.Load(FontHeading, 20f, FontStyle.Bold);

        internal static Font Kicker() => FontLoader.Load(FontBody, 10f, FontStyle.Bold);

        internal static Font Body() => FontLoader.Load(FontBody, 14f);

        internal static Font BodyBold() => FontLoader.Load(FontBody, 14f, FontStyle.Bold);

        internal static Font Mono() => FontLoader.Load(FontMono, 12.5f);

        internal static Font MonoSmall() => FontLoader.Load(FontMono, 11f);

        internal static Font Figure() => FontLoader.Load(FontHeading, 30f, FontStyle.Bold);

        internal static Font Pct() => FontLoader.Load(FontHeading, 62f, FontStyle.Bold);

        internal static Font Title() => Heading2();

        internal static Font Icon() => new Font(IconFamily, 12f, FontStyle.Regular, GraphicsUnit.Point);

        internal static Color Surface => Theme.Current.Bg;

        internal static Color RailSurface => Theme.Current.Surface;

        internal static Color CardSurface => Theme.Current.Surface;

        internal static Color TextPrimary => Theme.Current.Text;

        internal static Color Muted => Theme.Current.TextMuted;

        internal static Color Border => Theme.Current.Divider;

        internal static Color InputBack => Theme.Current.Surface;

        internal static Color Danger => Theme.Current.Accent2_600;

        internal static Color Caution => Theme.Current.Accent2_600;

        internal static Color ChipSucceededBack => Theme.Current.Accent100;

        internal static Color ChipSucceededFore => Theme.Current.Accent800;

        internal static Color ChipFailedBack => Theme.Current.Accent2_100;

        internal static Color ChipFailedFore => Theme.Current.Accent2_800;

        internal static Color ChipSkippedBack => Theme.Current.Accent2_100;

        internal static Color ChipSkippedFore => Theme.Current.Accent2_800;

        internal static Color Accent => Theme.Current.Accent;

        internal static Color Accent600 => Theme.Current.Accent600;

        internal static Color Accent700 => Theme.Current.Accent700;

        internal static Color Accent2 => Theme.Current.Accent2;

        internal static Color Accent2400 => Theme.Current.Accent2_400;

        internal static Color Accent2600 => Theme.Current.Accent2_600;

        internal static Color Bg => Theme.Current.Bg;
    }
}
