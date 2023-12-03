using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;

namespace MicaSetup.Design.Converters;

[ValueConversion(typeof(string), typeof(object))]
[ContentProperty(nameof(Items))]
public class StringToObjectConverter : SingletonConverterBase<StringToObjectConverter>
{
    public ResourceDictionary Items { get; set; } = null!;

    protected override object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string key)
        {
            if (this.Items != null && ContainsKey(this.Items, key))
            {
                return this.Items[key];
            }
        }

        return UnsetValue;
    }

    private static bool ContainsKey(ResourceDictionary dict, string key)
    {
        return dict.Contains(key);
    }
}
