using System;
using System.Globalization;
using System.Windows.Data;

namespace BetterGenshinImpact.View.Converters;

public sealed class MillisecondsToTimeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not double milliseconds || double.IsNaN(milliseconds) || double.IsInfinity(milliseconds))
        {
            return "00:00";
        }

        var time = TimeSpan.FromMilliseconds(Math.Max(0, milliseconds));
        return time.TotalHours >= 1
            ? $"{(int)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00}"
            : $"{time.Minutes:00}:{time.Seconds:00}";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
