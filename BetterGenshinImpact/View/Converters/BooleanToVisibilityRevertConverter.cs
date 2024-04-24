using System;
using System.Windows;
using System.Windows.Data;

namespace BetterGenshinImpact.View.Converters;

[ValueConversion(typeof(bool),typeof(Visibility))]
public sealed class BooleanToVisibilityRevertConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        return (value is bool v && v) ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        return value is Visibility.Collapsed;
    }
}
