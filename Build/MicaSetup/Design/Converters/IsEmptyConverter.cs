using System;
using System.Collections;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace MicaSetup.Design.Converters;

[ValueConversion(typeof(IEnumerable), typeof(bool))]
[ValueConversion(typeof(object), typeof(bool))]
public class IsEmptyConverter : SingletonConverterBase<IsEmptyConverter>
{
    public static readonly DependencyProperty IsInvertedProperty = DependencyProperty.Register(
        nameof(IsInverted),
        typeof(bool),
        typeof(IsEmptyConverter),
        new PropertyMetadata(false));

    public bool IsInverted
    {
        get => (bool)this.GetValue(IsInvertedProperty);
        set => this.SetValue(IsInvertedProperty, value);
    }

    protected override object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is IEnumerable enumerable)
        {
            var hasAtLeastOne = enumerable.GetEnumerator().MoveNext();
            return (hasAtLeastOne == false) ^ this.IsInverted;
        }

        return true ^ this.IsInverted;
    }
}
