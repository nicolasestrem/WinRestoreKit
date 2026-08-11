using System;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using WinRestoreKit;

namespace WinRestoreKit.Wpf.Services
{
    internal sealed class WpfThemeService : IThemeService
    {
        private readonly ResourceDictionary resources;
        private readonly IThemeSettings settings;
        private readonly ISystemThemeDetector systemTheme;
        private readonly Dispatcher uiDispatcher;
        private ResourceDictionary activeThemeDictionary;
        private bool disposed;

        internal WpfThemeService(ResourceDictionary resources, IThemeSettings settings,
                                 ISystemThemeDetector systemTheme)
            : this(resources, settings, systemTheme, Application.Current?.Dispatcher)
        {
        }

        internal WpfThemeService(ResourceDictionary resources, IThemeSettings settings,
                                 ISystemThemeDetector systemTheme, Dispatcher uiDispatcher)
        {
            this.resources = resources ?? throw new ArgumentNullException(nameof(resources));
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            this.systemTheme = systemTheme ?? throw new ArgumentNullException(nameof(systemTheme));
            this.uiDispatcher = uiDispatcher;

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

        // SystemEvents raises UserPreferenceChanged on its own thread, not the UI dispatcher. WPF
        // resource dictionaries belong to the UI thread, so applying a theme there is a cross-thread
        // access that can terminate the process. Marshal the reapply onto the application dispatcher,
        // mirroring the WinForms handler's BeginInvoke.
        internal void SystemEvents_UserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (Mode != ThemeMode.FollowSystem)
                return;

            Dispatcher dispatcher = uiDispatcher;
            if (dispatcher == null)
                return;

            dispatcher.BeginInvoke(new Action(ApplyEffectiveMode));
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
                        ? "/WinRestoreKit;component/Themes/Dark.xaml"
                        : "/WinRestoreKit;component/Themes/Light.xaml",
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
