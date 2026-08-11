namespace WinRestoreKit
{
    internal interface IThemeSettings
    {
        ThemeMode ReadThemeMode();
        void WriteThemeMode(ThemeMode mode);
    }
}
