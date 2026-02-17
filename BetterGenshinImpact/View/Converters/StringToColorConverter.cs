using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace BetterGenshinImpact.View.Converters;

/// <summary>
/// 将颜色字符串（#RRGGBB 或 #RRGGBBAA）转换为Color对象
/// </summary>
public class StringToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string colorStr)
        {
            return ParseColor(colorStr) ?? Colors.White;
        }

        return Colors.White;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }

    private static Color? ParseColor(string colorStr)
    {
        if (string.IsNullOrWhiteSpace(colorStr))
        {
            return null;
        }

        try
        {
            string hex = colorStr.Trim().TrimStart('#');

            if (hex.Length == 6)
            {
                // RGB格式
                byte r = byte.Parse(hex.Substring(0, 2), NumberStyles.HexNumber);
                byte g = byte.Parse(hex.Substring(2, 2), NumberStyles.HexNumber);
                byte b = byte.Parse(hex.Substring(4, 2), NumberStyles.HexNumber);
                return Color.FromArgb(255, r, g, b);
            }
            else if (hex.Length == 8)
            {
                // RGBA格式
                byte r = byte.Parse(hex.Substring(0, 2), NumberStyles.HexNumber);
                byte g = byte.Parse(hex.Substring(2, 2), NumberStyles.HexNumber);
                byte b = byte.Parse(hex.Substring(4, 2), NumberStyles.HexNumber);
                byte a = byte.Parse(hex.Substring(6, 2), NumberStyles.HexNumber);
                return Color.FromArgb(a, r, g, b);
            }
        }
        catch (Exception)
        {
            // 解析失败，返回null
        }

        return null;
    }
}
