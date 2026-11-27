using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace BetterGenshinImpact.View.Controls;

/// <summary>按实际字体测量首字和省略号；放不下有效文字时仅显示类型图标。</summary>
public sealed class AccountRecordsCategoryTextVisibilityConverter : IMultiValueConverter
{
    /// <summary>文字区包含与库存正文一致的左右各六像素间距。</summary>
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 4 || values[0] is not double width || values[1] is not string text ||
            string.IsNullOrEmpty(text) || values[2] is not FontFamily font || values[3] is not double size)
            return Visibility.Hidden;
        var first = StringInfo.GetNextTextElement(text);
        var minimum = new FormattedText(first.Length == text.Length ? first : first + "…", culture,
            FlowDirection.LeftToRight, new Typeface(font, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
            size, Brushes.Black, 1).WidthIncludingTrailingWhitespace;
        return width >= minimum + 12 ? Visibility.Visible : Visibility.Hidden;
    }

    /// <summary>纯展示绑定不回写。</summary>
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
