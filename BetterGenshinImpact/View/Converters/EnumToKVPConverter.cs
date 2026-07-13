using System;
using System.Collections.Generic;
using System.Text;
using System.Windows.Data;
using System.Windows;
using System.ComponentModel;
using System.Globalization;
using System.Reflection;

namespace BetterGenshinImpact.View.Converters;

[ValueConversion(typeof(KeyValuePair<Enum, string>), typeof(Enum))]
public class EnumToKVPConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value == null)
        {
            return null;
        }

        var enumType = value.GetType();
        if (!enumType.IsEnum)
        {
            throw new ArgumentException("Value must be an enum");
        }

        var name = Enum.GetName(enumType, value) ?? throw new NullReferenceException();
        var field = enumType.GetField(name);
        var descriptionAttribute = field?.GetCustomAttribute<DescriptionAttribute>();

        var description = descriptionAttribute != null ? descriptionAttribute.Description : name;
        return new KeyValuePair<Enum, string>((Enum)value, description);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value == null)
        {
            return null;
        }

        var kvp = (KeyValuePair<Enum, string>)value;
        return kvp.Key;
    }
}