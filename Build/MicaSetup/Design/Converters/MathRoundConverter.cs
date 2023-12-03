using System;
using System.Globalization;
using System.Windows.Data;

namespace MicaSetup.Design.Converters;

internal sealed class MathRoundConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        double? valueNullable = default;

        if (value is float @float)
        {
            valueNullable = @float;
        }
        else if (value is double @double)
        {
            valueNullable = @double;
        }
        else if (value is int @int)
        {
            valueNullable = @int;
        }

        if (valueNullable != null)
        {
            int dec = default;

            if (parameter is null)
            {
                dec = default;
            }
            else if (parameter is int decFromInt)
            {
                dec = decFromInt;
            }
            else if (parameter is string decString)
            {
                if (int.TryParse(decString, out int decFromString))
                {
                    dec = decFromString;
                }
            }
            return Math.Round(valueNullable.Value, dec).ToString();
        }
        return value?.ToString()!;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
