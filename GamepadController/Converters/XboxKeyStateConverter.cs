using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Media;

namespace GamepadController.Converters
{
    /// <summary>
    /// Xbox 按键状态
    /// </summary>
    public class XboxKeyStateConverter : IMultiValueConverter
    {
        /// <summary>
        /// 按键颜色转换器
        /// </summary>
        /// <param name="values">按键状态、默认颜色</param>
        /// <param name="targetType"></param>
        /// <param name="parameter"></param>
        /// <param name="culture"></param>
        /// <returns></returns>
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            var v1 = bool.Parse($"{values[0]}");
            Brush v2 = values[1] as Brush;

            return v1 ? Brushes.Orange : v2;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
