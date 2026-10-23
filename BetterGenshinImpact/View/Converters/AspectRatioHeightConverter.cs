using System;
using System.Globalization;
using System.Windows.Data;

namespace BetterGenshinImpact.View.Converters;

/// <summary>
/// 按给定宽度和宽高比计算高度，用于让内容区域在响应式布局中保持固定比例。
/// </summary>
[ValueConversion(typeof(double), typeof(double))]
public sealed class AspectRatioHeightConverter : IValueConverter
{
    /// <summary>
    /// 将宽度换算为高度；参数为宽高比，默认使用 16:9。
    /// </summary>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not double width || !double.IsFinite(width) || width <= 0)
        {
            return 0d;
        }

        var aspectRatio = 16d / 9d;
        if (parameter is string parameterText
            && double.TryParse(parameterText, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedRatio)
            && double.IsFinite(parsedRatio)
            && parsedRatio > 0)
        {
            aspectRatio = parsedRatio;
        }

        return width / aspectRatio;
    }

    /// <summary>
    /// 布局比例只用于单向显示，不支持反向换算。
    /// </summary>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
