using System.Windows.Data;
using Property = System.Windows.DependencyProperty;

namespace MicaSetup.Design.Converters;

[ValueConversion(typeof(object), typeof(bool))]
public class ValueToBoolConverter<T> : ReversibleValueToBoolConverterBase<T, ValueToBoolConverter<T>>
{
    public override T TrueValue
    {
        get => (T)this.GetValue(TrueValueProperty);
        set => this.SetValue(TrueValueProperty, value);
    }

    public static readonly Property TrueValueProperty =
        PropertyHelper.Create<T, ValueToBoolConverter<T>>(nameof(TrueValue));

    public override T FalseValue
    {
        get => (T)this.GetValue(FalseValueProperty);
        set => this.SetValue(FalseValueProperty, value);
    }

    public static readonly Property FalseValueProperty =
        PropertyHelper.Create<T, ValueToBoolConverter<T>>(nameof(FalseValue));
}

[ValueConversion(typeof(object), typeof(bool))]
public class ValueToBoolConverter : ValueToBoolConverter<object>
{
}
