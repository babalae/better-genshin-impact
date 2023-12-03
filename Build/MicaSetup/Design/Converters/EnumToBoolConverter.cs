using System;
using System.Globalization;
using System.Windows.Data;

namespace MicaSetup.Design.Converters;

[ValueConversion(typeof(object), typeof(bool))]
public class EnumToBoolConverter : SingletonConverterBase<EnumToBoolConverter>
{
    protected override object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (parameter is string parameterString)
        {
            if (Enum.IsDefined(value.GetType(), value) == false)
            {
                return UnsetValue;
            }

            var parameterValue = Enum.Parse(value.GetType(), parameterString);

            return parameterValue.Equals(value);
        }

        return UnsetValue;
    }

    protected override object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (parameter is string parameterString)
        {
            return Enum.Parse(targetType, parameterString);
        }

        return UnsetValue;
    }
}
