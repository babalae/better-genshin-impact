using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Data;

namespace BetterGenshinImpact.View.Converters;

/// <summary>
/// 把 MultiBinding 的多个绑定值组装为一个 object[]，用于需要同时传递多个值的 CommandParameter。
/// 尚未就绪的绑定值（<see cref="DependencyProperty.UnsetValue"/>）转为 null，命令端只需按类型判断。
/// </summary>
public sealed class MultiValueArrayConverter : IMultiValueConverter
{
    /// <summary>
    /// 复制绑定值数组。WPF 会复用传入的数组，直接返回会在后续求值时被改写，所以必须返回副本。
    /// </summary>
    /// <param name="values">各子绑定的当前值。</param>
    /// <param name="targetType">目标属性类型。</param>
    /// <param name="parameter">未使用。</param>
    /// <param name="culture">未使用。</param>
    /// <returns>与子绑定顺序一致的新数组。</returns>
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
        => values.Select(value => value == DependencyProperty.UnsetValue ? null : value).ToArray();

    /// <summary>
    /// 只用于单向组装参数，不支持反向转换。
    /// </summary>
    /// <param name="value">目标值。</param>
    /// <param name="targetTypes">各子绑定的源类型。</param>
    /// <param name="parameter">未使用。</param>
    /// <param name="culture">未使用。</param>
    /// <returns>不返回。</returns>
    /// <exception cref="NotSupportedException">始终抛出。</exception>
    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
