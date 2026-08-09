using System;
using System.Windows;
using Microsoft.Win32;
using WinRestoreKit;

namespace WinRestoreKit.Wpf.Services
{
    internal sealed class WpfThemeService : IThemeService
    {
        private readonly ResourceDictionary resources;
        private readonly IThemeSettings settings;
        private readonly ISystemThemeDetector systemTheme;
        private ResourceDictionary activeThemeDictionary;
        private bool disposed;

        internal WpfThemeService(ResourceDictionary resources, IThemeSettings settings,
                                 ISystemThemeDetector systemTheme)
        {
            this.resources = resources ?? throw new ArgumentNullException(nameof(resources));
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            this.systemTheme = systemTheme ?? throw new ArgumentNullException(nameof(systemTheme));

            Mode = settings.ReadThemeMode();
            SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;
            ApplyEffectiveMode();
        }

        public ThemeMode Mode { get; private set; }

        public ThemeMode EffectiveMode { get; private set; }

        public event EventHandler ThemeChanged;

        public void SetMode(ThemeMode mode)
        {
            Mode = mode;
            settings.WriteThemeMode(mode);
            ApplyEffectiveMode();
        }

        public void Dispose()
        {
            if (disposed)
                return;

            SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;
            disposed = true;
        }

        private void SystemEvents_UserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (Mode == ThemeMode.FollowSystem)
                ApplyEffectiveMode();
        }

        private void ApplyEffectiveMode()
        {
            ThemeMode effectiveMode = Mode == ThemeMode.FollowSystem
                ? (systemTheme.IsDarkAppsTheme() ? ThemeMode.Dark : ThemeMode.Light)
                : Mode;

            if (effectiveMode != ThemeMode.Dark)
                effectiveMode = ThemeMode.Light;

            ResourceDictionary nextThemeDictionary = (ResourceDictionary)Application.LoadComponent(
                new Uri(
                    effectiveMode == ThemeMode.Dark
                        ? "/WinRestoreKit.Wpf;component/Themes/Dark.xaml"
                        : "/WinRestoreKit.Wpf;component/Themes/Light.xaml",
                    UriKind.Relative));

            if (activeThemeDictionary != null)
                resources.MergedDictionaries.Remove(activeThemeDictionary);

            resources.MergedDictionaries.Add(nextThemeDictionary);
            activeThemeDictionary = nextThemeDictionary;
            EffectiveMode = effectiveMode;
            ThemeChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
