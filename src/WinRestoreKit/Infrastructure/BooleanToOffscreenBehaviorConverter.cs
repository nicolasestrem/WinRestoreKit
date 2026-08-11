using System;
using System.Globalization;
using System.Windows.Automation;
using System.Windows.Data;

namespace WinRestoreKit.Wpf.Infrastructure
{
    public sealed class BooleanToOffscreenBehaviorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool isOnscreen = value is bool flag && flag;
            if (string.Equals(parameter as string, "Inverse", StringComparison.OrdinalIgnoreCase))
                isOnscreen = !isOnscreen;

            return isOnscreen ? IsOffscreenBehavior.Onscreen : IsOffscreenBehavior.Offscreen;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }
}
