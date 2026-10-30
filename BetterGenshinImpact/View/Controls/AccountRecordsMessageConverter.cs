using System;
using System.Globalization;
using System.Windows.Data;
using BetterGenshinImpact.Service.AccountRecords;

namespace BetterGenshinImpact.View.Controls;

/// <summary>将事件原始说明转换为当前展示文案，不修改事件或历史证据。</summary>
public sealed class AccountRecordsMessageConverter : IValueConverter
{
    /// <summary>与库存通知和事件详情使用相同的文案转换。</summary>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is string message ? AccountRecordsErrorMessages.ForDisplay(message) : "";

    /// <summary>展示文案不回写原始说明。</summary>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}
