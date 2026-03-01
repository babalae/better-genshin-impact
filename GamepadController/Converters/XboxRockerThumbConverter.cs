using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;

namespace GamepadController.Converters
{
    /// <summary>
    /// Xbox 摇杆坐标
    /// </summary>
    public class XboxRockerThumbConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            var v1 = decimal.Parse($"{values[0]}");
            var v2 = decimal.Parse($"{values[1]}");
            return new Thickness()
            {
                Left = (double)(v1 / 32767 * 30),
                Top = -(double)(v2 / 32767 * 30),
                Right = 0,
                Bottom = 0
            };
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
