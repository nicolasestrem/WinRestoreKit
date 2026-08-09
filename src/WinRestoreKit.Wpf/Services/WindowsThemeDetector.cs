using System;
using Microsoft.Win32;

namespace WinRestoreKit.Wpf.Services
{
    internal sealed class WindowsThemeDetector : ISystemThemeDetector
    {
        private const string PersonalizeKeyPath =
            @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
        private const string AppsUseLightThemeValueName = "AppsUseLightTheme";

        public bool IsDarkAppsTheme()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(PersonalizeKeyPath))
                {
                    return key?.GetValue(AppsUseLightThemeValueName) is int value && value == 0;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
