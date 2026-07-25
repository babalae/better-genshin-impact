using System;
using System.Globalization;
using System.Linq;
using System.Windows.Data;
using BetterGenshinImpact.Helpers;
using Wpf.Ui.Violeta.Controls;

namespace BetterGenshinImpact.View.Converters;

/// <summary>
/// 在 AutoBoss 配置保存的 Boss 名称与级联下拉选中项之间互相转换。
/// </summary>
public class AutoBossNameToCascadingItemConverter : IValueConverter
{
    /// <summary>
    /// 将配置中的 Boss 名称转换为级联下拉的子项。
    /// </summary>
    /// <param name="value">Boss 名称字符串。</param>
    /// <param name="targetType">绑定目标类型。</param>
    /// <param name="parameter">绑定参数，当前未使用。</param>
    /// <param name="culture">区域性信息。</param>
    /// <returns>匹配的级联下拉项；未找到时返回 null。</returns>
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string name && !string.IsNullOrEmpty(name))
        {
            foreach (var country in AutoBossCascadingItems.Items)
            {
                var found = country.Children?.FirstOrDefault(boss => boss.Tag as string == name);
                if (found != null)
                {
                    return found;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// 将级联下拉选中的 Boss 子项转换回配置保存的 Boss 名称。
    /// </summary>
    /// <param name="value">级联下拉选中项。</param>
    /// <param name="targetType">绑定目标类型。</param>
    /// <param name="parameter">绑定参数，当前未使用。</param>
    /// <param name="culture">区域性信息。</param>
    /// <returns>Boss 名称；选中项无效时返回 <see cref="Binding.DoNothing"/>。</returns>
    public object? ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is ICascadingItem { Tag: string name } && !string.IsNullOrWhiteSpace(name))
        {
            return name;
        }

        return Binding.DoNothing;
    }
}
