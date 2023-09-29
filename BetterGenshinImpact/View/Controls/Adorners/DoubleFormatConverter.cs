using System;
using System.Globalization;
using System.Windows.Data;

namespace BetterGenshinImpact.View.Controls.Adorners;

public class DoubleFormatConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        double d = (double)value;
        return Math.Round(d);
    }

    public object? ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return default;
    }
}
