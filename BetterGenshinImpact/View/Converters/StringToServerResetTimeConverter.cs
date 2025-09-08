using System;
using System.Globalization;
using System.Windows.Data;
using BetterGenshinImpact.Model;

namespace BetterGenshinImpact.View.Converters;

/// <summary>
/// Converts between ServerResetTime and string for WPF binding.
/// </summary>
[ValueConversion(typeof(string), typeof(ServerResetTime))]
public class StringToServerResetTimeConverter : IValueConverter
{
    /// <summary>
    /// Converts a ServerResetTime struct to a string for display in the ComboBox text box.
    /// </summary>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is ServerResetTime resetTime)
        {
            return resetTime.ToString();
        }

        return string.Empty;
    }

    /// <summary>
    /// Converts a string from manual entry back to a ServerResetTime struct.
    /// </summary>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string stringValue)
        {
            // Default CN server reset time
            return ServerResetTime.Default;
        }

        try
        {
            return ServerResetTime.Parse(stringValue);
        }
        catch
        {
            // Returns a default value on parsing failure
            return ServerResetTime.Default;
        }

        return Binding.DoNothing;
    }
}