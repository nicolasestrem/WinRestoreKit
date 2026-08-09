using System;
using System.Collections.Generic;
using WinRestoreKit;
using WinRestoreKit.Wpf.Infrastructure;
using WinRestoreKit.Wpf.Services;

namespace WinRestoreKit.Wpf.ViewModels
{
    internal sealed class SettingsViewModel : ObservableObject
    {
        private readonly IThemeService themes;
        private readonly IReadOnlyList<ThemeMode> availableModes = new[]
        {
            ThemeMode.FollowSystem,
            ThemeMode.Light,
            ThemeMode.Dark
        };
        private ThemeMode selectedTheme;

        internal SettingsViewModel(IThemeService themes)
        {
            this.themes = themes ?? throw new ArgumentNullException(nameof(themes));
            selectedTheme = themes.Mode;
        }

        public IReadOnlyList<ThemeMode> AvailableModes => availableModes;

        public ThemeMode SelectedTheme
        {
            get => selectedTheme;
            set
            {
                if (selectedTheme == value)
                    return;

                themes.SetMode(value);
                SetProperty(ref selectedTheme, value, nameof(SelectedTheme));
            }
        }
    }
}
