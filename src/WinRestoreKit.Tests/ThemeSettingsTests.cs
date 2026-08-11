using System;
using Microsoft.Win32;
using WinRestoreKit;
using Xunit;

namespace WinRestoreKit.Tests
{
    public class ThemeSettingsTests
    {
        [Fact]
        public void RegistryThemeSettings_RoundTripsLightDarkAndFollowSystem()
        {
            string keyPath = @"Software\WinRestoreKit.Tests\" + Guid.NewGuid().ToString("N");
            try
            {
                IThemeSettings settings = new RegistryThemeSettings(keyPath);

                settings.WriteThemeMode(ThemeMode.Dark);
                Assert.Equal(ThemeMode.Dark, settings.ReadThemeMode());
                settings.WriteThemeMode(ThemeMode.Light);
                Assert.Equal(ThemeMode.Light, settings.ReadThemeMode());
                settings.WriteThemeMode(ThemeMode.FollowSystem);
                Assert.Equal(ThemeMode.FollowSystem, settings.ReadThemeMode());
            }
            finally
            {
                Registry.CurrentUser.DeleteSubKeyTree(keyPath, false);
            }
        }
    }
}
