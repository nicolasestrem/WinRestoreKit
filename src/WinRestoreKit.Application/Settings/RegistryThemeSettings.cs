using System;
using Microsoft.Win32;

namespace WinRestoreKit
{
    internal sealed class RegistryThemeSettings : IThemeSettings
    {
        private const string DefaultKeyPath = @"Software\WinRestoreKit";
        private const string ValueName = "ThemeMode";
        private readonly string keyPath;

        internal RegistryThemeSettings(string keyPath = DefaultKeyPath)
        {
            this.keyPath = string.IsNullOrWhiteSpace(keyPath) ? DefaultKeyPath : keyPath;
        }

        public ThemeMode ReadThemeMode()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(keyPath))
                {
                    return key?.GetValue(ValueName) is int value && Enum.IsDefined(typeof(ThemeMode), value)
                        ? (ThemeMode)value
                        : ThemeMode.FollowSystem;
                }
            }
            catch (Exception)
            {
                return ThemeMode.FollowSystem;
            }
        }

        public void WriteThemeMode(ThemeMode mode)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(keyPath))
                    key?.SetValue(ValueName, (int)mode, RegistryValueKind.DWord);
            }
            catch (Exception)
            {
            }
        }
    }
}
