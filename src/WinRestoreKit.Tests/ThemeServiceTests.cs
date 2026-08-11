using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
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

        [Fact]
        public void WpfThemeService_SystemThemeChangeIsReappliedOnDispatcher()
        {
            WpfTestHost.Run(() =>
            {
                Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
                ResourceDictionary resources = new ResourceDictionary();
                FakeThemeSettings settings = new FakeThemeSettings(ThemeMode.FollowSystem);
                MutableSystemThemeDetector detector = new MutableSystemThemeDetector(isDark: false);

                using (WpfThemeService service = new WpfThemeService(resources, settings, detector, dispatcher))
                {
                    Assert.Equal(ThemeMode.Light, service.EffectiveMode);

                    detector.IsDark = true;

                    // SystemEvents raises on its own thread; fire the handler off the dispatcher, then
                    // push a frame so the marshaled reapply runs on the UI thread before we assert.
                    Task.Run(() => service.SystemEvents_UserPreferenceChanged(null,
                        new UserPreferenceChangedEventArgs(UserPreferenceCategory.General))).Wait();
                    DispatcherFrame frame = new DispatcherFrame();
                    dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
                    Dispatcher.PushFrame(frame);

                    Assert.Equal(ThemeMode.Dark, service.EffectiveMode);
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

        private sealed class MutableSystemThemeDetector : ISystemThemeDetector
        {
            internal bool IsDark { get; set; }

            internal MutableSystemThemeDetector(bool isDark) => IsDark = isDark;

            public bool IsDarkAppsTheme() => IsDark;
        }
    }
}
