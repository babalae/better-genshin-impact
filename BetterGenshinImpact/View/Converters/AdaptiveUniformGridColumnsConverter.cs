using System;
using System.Globalization;
using System.Windows.Data;

namespace BetterGenshinImpact.View.Converters;

[ValueConversion(typeof(double), typeof(int))]
public sealed class AdaptiveUniformGridColumnsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not double width || double.IsNaN(width) || width <= 0)
        {
            return 1;
        }

        var minimumColumnWidth = 280d;
        var maxColumns = 4;

        if (parameter is string parameterText && !string.IsNullOrWhiteSpace(parameterText))
        {
            var parts = parameterText.Split([',', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            if (parts.Length > 0 &&
                double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedMinimumWidth) &&
                parsedMinimumWidth > 0)
            {
                minimumColumnWidth = parsedMinimumWidth;
            }

            if (parts.Length > 1 &&
                int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedMaxColumns) &&
                parsedMaxColumns > 0)
            {
                maxColumns = parsedMaxColumns;
            }
        }

        var columns = Math.Max(1, (int)Math.Floor(width / minimumColumnWidth));
        return Math.Min(columns, maxColumns);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
