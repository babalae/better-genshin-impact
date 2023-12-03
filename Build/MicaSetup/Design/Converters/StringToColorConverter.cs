using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace MicaSetup.Design.Converters;

[ValueConversion(typeof(string), typeof(Color))]
public sealed class StringToColorConverter : SingletonConverterBase<StringToColorConverter>
{
    protected override object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string inputString)
        {
            return ColorConverter.ConvertFromString(inputString);
        }
        return value;
    }

    protected override object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is Color color)
        {
            return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        }
        return string.Empty;
    }
}
