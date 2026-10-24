using System;
using System.Globalization;
using System.Windows.Data;

namespace BetterGenshinImpact.View.Controls.CombatStrategy;

/// <summary>长名称从中间省略，保留目录前缀和策略名末尾。</summary>
public sealed class MiddleEllipsisConverter : IValueConverter
{
    /// <summary>按照给定字符上限省略显示文字，完整值仍在悬停提示中。</summary>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var text = value as string ?? "";
        var limit = int.TryParse(parameter as string, out var length) ? Math.Max(5, length) : 32;
        if (text.Length <= limit) return text;
        var head = (limit - 1) / 2;
        return text[..head] + "…" + text[^(limit - head - 1)..];
    }

    /// <summary>展示转换器不参与双向写回。</summary>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
