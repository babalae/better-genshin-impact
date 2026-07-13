using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Data;

namespace BetterGenshinImpact.View.Converters;

[ValueConversion(typeof(KeyValuePair<string, string>), typeof(string))]
class CultureInfoNameToKVPConverter : IValueConverter
{
    public static string GetDisplayName(string cultureInfoName)
    {
        return cultureInfoName switch
        {
            "zh-Hans" => "简体中文",
            "zh-Hant" => "繁體中文",
            "en" => "English",
            "ja" => "日本語",
            _ => cultureInfoName
        };
    }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value == null)
        {
            return null;
        }

        string? cultureInfoName = value as string;
        if (cultureInfoName is null)
        {
            throw new ArgumentException("Value must be a CultureInfoName");
        }

        var description = GetDisplayName(cultureInfoName);

        return new KeyValuePair<string, string>(cultureInfoName, description);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value == null)
        {
            return null;
        }

        var kvp = (KeyValuePair<string, string>)value;
        return kvp.Key;
    }
}
