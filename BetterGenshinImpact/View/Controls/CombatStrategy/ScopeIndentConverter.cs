using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace BetterGenshinImpact.View.Controls.CombatStrategy;

/// <summary>将目录深度转换为范围行的左侧缩进，领域节点不依赖 WPF。</summary>
public sealed class ScopeIndentConverter : IValueConverter
{
    /// <summary>每层缩进 18 个设备无关像素。</summary>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => new Thickness(value is int depth ? Math.Max(0, depth) * 18 : 0, 0, 0, 0);

    /// <summary>显示转换不回写源值。</summary>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
