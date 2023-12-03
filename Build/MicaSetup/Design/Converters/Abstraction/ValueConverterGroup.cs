using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Data;
using System.Windows.Markup;

namespace MicaSetup.Design.Converters;

[ContentProperty(nameof(Converters))]
[ValueConversion(typeof(object), typeof(object))]
public class ValueConverterGroup : SingletonConverterBase<ValueConverterGroup>
{
    public List<IValueConverter> Converters { get; set; } = new List<IValueConverter>();

    protected override object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (this.Converters is IEnumerable<IValueConverter> converters)
        {
            var language = culture;
            return converters.Aggregate(value, (current, converter) => converter.Convert(current, targetType, parameter, language));
        }

        return UnsetValue;
    }

    protected override object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (this.Converters is IEnumerable<IValueConverter> converters)
        {
            var language = culture;
            return converters.Reverse<IValueConverter>().Aggregate(value, (current, converter) => converter.Convert(current, targetType, parameter, language));
        }

        return UnsetValue;
    }
}
