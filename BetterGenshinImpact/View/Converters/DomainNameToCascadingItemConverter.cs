using System;
using System.Globalization;
using System.Linq;
using System.Windows.Data;
using BetterGenshinImpact.Helpers;
using Wpf.Ui.Violeta.Controls;

namespace BetterGenshinImpact.View.Converters;

public class DomainNameToCascadingItemConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string name && !string.IsNullOrEmpty(name))
        {
            foreach (var country in DomainCascadingItems.Items)
            {
                var found = country.Children?.FirstOrDefault(d => d.Tag as string == name);
                if (found != null)
                    return found;
            }
        }

        return null;
    }

    public object? ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is ICascadingItem item)
            return item.Tag as string;
        return null;
    }
}
