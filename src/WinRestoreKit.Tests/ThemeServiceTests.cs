using System;
using System.Windows;
using WinRestoreKit;
using WinRestoreKit.Wpf.Services;
using Xunit;

namespace WinRestoreKit.Tests
{
    public class ThemeServiceTests
    {
        [Fact]
        public void WpfThemeService_UsesSystemDarkOnlyWhenModeFollowsSystem()
        {
            WpfTestHost.Run(() =>
            {
                ResourceDictionary resources = new ResourceDictionary();
                FakeThemeSettings settings = new FakeThemeSettings(ThemeMode.FollowSystem);
                using (WpfThemeService service = new WpfThemeService(resources, settings,
                           new FakeSystemThemeDetector(isDark: true)))
                {
                    Assert.Equal(ThemeMode.Dark, service.EffectiveMode);
                    service.SetMode(ThemeMode.Light);
                    Assert.Equal(ThemeMode.Light, service.EffectiveMode);
                    Assert.Equal(ThemeMode.Light, settings.ReadThemeMode());
                }
            });
        }

        private sealed class FakeThemeSettings : IThemeSettings
        {
            private ThemeMode mode;

            internal FakeThemeSettings(ThemeMode mode) => this.mode = mode;

            public ThemeMode ReadThemeMode() => mode;

            public void WriteThemeMode(ThemeMode mode) => this.mode = mode;
        }

        private sealed class FakeSystemThemeDetector : ISystemThemeDetector
        {
            private readonly bool isDark;

            internal FakeSystemThemeDetector(bool isDark) => this.isDark = isDark;

            public bool IsDarkAppsTheme() => isDark;
        }
    }
}
