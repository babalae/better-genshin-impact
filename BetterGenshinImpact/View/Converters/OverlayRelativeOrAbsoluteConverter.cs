using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace BetterGenshinImpact.View.Converters
{
    public class OverlayRelativeOrAbsoluteConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
        {
            if (values.Length < 2)
            {
                return DependencyProperty.UnsetValue;
            }

            if (values.Length == 2)
            {
                var ratio = ToDouble(values[0]);
                var baseSize = ToDouble(values[1]);

                if (double.IsNaN(ratio) || double.IsNaN(baseSize) || baseSize <= 0)
                {
                    return DependencyProperty.UnsetValue;
                }

                return ratio * baseSize;
            }

            var ratio3 = ToDouble(values[0]);
            var absolute = ToDouble(values[1]);
            var baseSize3 = ToDouble(values[2]);

            if (double.IsNaN(baseSize3) || baseSize3 <= 0)
            {
                return absolute;
            }

            if (!double.IsNaN(ratio3))
            {
                return ratio3 * baseSize3;
            }

            return absolute;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }

        private static double ToDouble(object value)
        {
            if (value == DependencyProperty.UnsetValue)
            {
                return double.NaN;
            }

            return value switch
            {
                double d => d,
                float f => f,
                int i => i,
                long l => l,
                _ => double.NaN
            };
        }
    }
}
