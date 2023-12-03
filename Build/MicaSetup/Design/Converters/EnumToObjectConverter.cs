using System;
using System.Globalization;
using System.Windows.Data;

namespace MicaSetup.Design.Converters;

[ValueConversion(typeof(object), typeof(object))]
public class EnumToObjectConverter : StringToObjectConverter
{
    protected override object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value == null)
        {
            return UnsetValue;
        }

        var type = value.GetType();
        var key = Enum.GetName(type, value);
        return base.Convert(key, targetType, parameter, culture);
    }
}
