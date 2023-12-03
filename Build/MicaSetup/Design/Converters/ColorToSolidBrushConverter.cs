using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace MicaSetup.Design.Converters;

[ValueConversion(typeof(string), typeof(SolidColorBrush))]
public sealed class ColorToSolidBrushConverter : ConverterBase
{
    public bool Freeze { get; set; } = true;

    protected override object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is Color color)
        {
            SolidColorBrush brush = new(color);

            if (Freeze)
            {
                brush.Freeze();
            }
            return brush;
        }
        return value;
    }

    protected override object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is SolidColorBrush brush)
        {
            return brush.Color;
        }
        return default(Color);
    }
}
