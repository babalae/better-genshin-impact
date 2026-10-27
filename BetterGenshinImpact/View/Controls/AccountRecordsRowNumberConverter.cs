using System;
using System.Globalization;
using System.Windows.Data;

namespace BetterGenshinImpact.View.Controls;

/// <summary>将列表的零起始显示序号转换为带句点的行号。</summary>
public sealed class AccountRecordsRowNumberConverter : IValueConverter
{
    /// <summary>行号仅用于缺少图标时的占位，不作为物品标识。</summary>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is int index && index >= 0 ? (index + 1L).ToString(CultureInfo.InvariantCulture) + "." : "";

    /// <summary>不回写行号。</summary>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
