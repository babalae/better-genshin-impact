using System;
using System.Globalization;
using System.Windows.Data;

namespace MicaSetup.Design.Converters;

[ValueConversion(typeof(string), typeof(bool))]
public sealed class StringEqualityConverter : SingletonConverterBase<StringEqualityConverter>
{
    protected override object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string inputString)
        {
            return inputString == parameter as string;
        }
        return value;
    }

    protected override object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool isChecked = (bool)value;

        if (!isChecked)
        {
            return string.Empty;
        }
        return (parameter as string) ?? string.Empty;
    }
}
