using System;
using System.Globalization;
using System.Windows.Controls;
using System.Windows.Data;
using BetterGenshinImpact.Core.Script.Group;

namespace BetterGenshinImpact.View.Converters;

public class ItemIndexConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is ScriptGroupProject project)
        {
            return project.Order;
        }

        var item = value as ListViewItem;
        if (ItemsControl.ItemsControlFromItemContainer(item) is ListView listView && item != null)
        {
            var index = listView.ItemContainerGenerator.IndexFromContainer(item) + 1;
            return index.ToString();
        }
        return "";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
