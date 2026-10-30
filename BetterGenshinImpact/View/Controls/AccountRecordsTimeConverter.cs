using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace BetterGenshinImpact.View.Controls;

/// <summary>按时间单元格的可用宽度逐步展示秒和年份；宽度单位为 WPF 逻辑像素。</summary>
public sealed class AccountRecordsTimeConverter : IMultiValueConverter
{
    /// <summary>统一使用本地时区，默认精确到分钟，拖宽后依次补充秒、年份。</summary>
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2 || values[0] is not DateTimeOffset time) return "未记录";
        var width = values[1] is double actualWidth ? actualWidth : 0;
        var format = width >= 184 ? "yyyy-MM-dd HH:mm:ss" : width >= 142 ? "MM-dd HH:mm:ss" : "MM-dd HH:mm";
        return time.ToLocalTime().ToString(format, CultureInfo.InvariantCulture);
    }

    /// <summary>显示转换不回写库存时间。</summary>
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => [Binding.DoNothing, Binding.DoNothing];
}
