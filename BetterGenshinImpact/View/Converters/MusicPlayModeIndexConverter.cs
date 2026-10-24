using System;
using System.Globalization;
using System.Windows.Data;

namespace BetterGenshinImpact.View.Converters;

/// <summary>
/// 「演奏方式」下拉栏用：0 = 自动演奏，1 = 引导式跟弹，与 ViewModel 上的布尔值互转。
/// </summary>
[ValueConversion(typeof(bool), typeof(int))]
public sealed class MusicPlayModeIndexConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true ? 1 : 0;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is int index && index == 1;
    }
}
