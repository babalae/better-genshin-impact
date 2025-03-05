using System;
using System.Collections.Generic;
using System.Text;
using System.Windows.Data;
using System.Windows;
using System.ComponentModel;
using BetterGenshinImpact.GameTask.AutoFishing;
using BetterGenshinImpact.GameTask.AutoFight.Script;
using System.Reflection;
using Vanara.Extensions.Reflection;

namespace BetterGenshinImpact.View.Converters
{
    [ValueConversion(typeof(KeyValuePair<Enum, string>), typeof(Enum))]
    public sealed class EnumToKVPConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            Enum e = (Enum)value;
            var name = Enum.GetName(value.GetType(), value);
            if (name != null)
            {
                var field = value.GetType().GetField(name);
                if (field != null && Attribute.GetCustomAttribute(field, typeof(DescriptionAttribute)) is DescriptionAttribute attr)
                    return new KeyValuePair<Enum, string>(e, attr.Description);
            }
            throw new NotSupportedException();
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            // 这里很难拆箱，干脆反射了
            var key = value.InvokeMethod<Enum>("get_Key");
            if (key != null)
            {
                return key;
            }
            throw new NotSupportedException();
        }
    }
}
