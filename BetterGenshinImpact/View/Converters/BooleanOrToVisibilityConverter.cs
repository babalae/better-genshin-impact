using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Data;

namespace BetterGenshinImpact.View.Converters;

public sealed class BooleanOrToVisibilityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        return values.Any(value => value is true) ? Visibility.Visible : Visibility.Collapsed;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
