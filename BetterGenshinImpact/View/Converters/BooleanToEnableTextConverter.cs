using System;
using System.Windows;
using System.Windows.Data;

namespace BetterGenshinImpact.View.Converters;

/// <summary>
/// ScriptGroupProjectExtensions
/// </summary>
[ValueConversion(typeof(bool), typeof(Visibility))]
public sealed class BooleanToEnableTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        return value is "Enabled";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        return value is false ? "Disabled" : "Enabled";
    }
}
