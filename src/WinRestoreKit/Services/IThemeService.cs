using System;
using WinRestoreKit;

namespace WinRestoreKit.Wpf.Services
{
    internal interface IThemeService : IDisposable
    {
        ThemeMode Mode { get; }
        ThemeMode EffectiveMode { get; }
        event EventHandler ThemeChanged;
        void SetMode(ThemeMode mode);
    }
}
