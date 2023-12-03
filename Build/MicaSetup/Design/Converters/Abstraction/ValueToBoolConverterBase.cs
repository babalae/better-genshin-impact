using System;
using System.Globalization;
using Property = System.Windows.DependencyProperty;

namespace MicaSetup.Design.Converters;

public abstract class ValueToBoolConverterBase<T, TConverter> : ConverterBase
    where TConverter : new()
{
    public abstract T TrueValue { get; set; }

    public bool IsInverted
    {
        get { return (bool)this.GetValue(IsInvertedProperty); }
        set { this.SetValue(IsInvertedProperty, value); }
    }

    protected override object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var trueValue = this.TrueValue;
        return Equals(value, trueValue) ^ this.IsInverted;
    }

    public static readonly Property IsInvertedProperty = PropertyHelper.Create<bool, ValueToBoolConverterBase<T, TConverter>>(nameof(IsInverted));
}
