using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Data;

namespace MicaSetup.Design.Converters;

[ValueConversion(typeof(Image), typeof(Bitmap))]
public sealed class ImageToBitmapConverter : SingletonConverterBase<ImageToBitmapConverter>
{
    protected override object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is Image image)
        {
            return new Bitmap(image);
        }
        else if (value is Bitmap)
        {
            return value;
        }
        return null!;
    }
}
