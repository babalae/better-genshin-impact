using System.Windows;
using System.Windows.Data;

namespace MicaSetup.Design.Converters;

[ValueConversion(typeof(bool), typeof(object))]
public class BoolToValueConverter<T> : BoolToValueConverterBase<T, BoolToValueConverter<T>>
{
    public static readonly DependencyProperty TrueValueProperty = DependencyProperty.Register(
        nameof(TrueValue),
        typeof(T),
        typeof(BoolToValueConverter<T>),
        new PropertyMetadata(default(T)));

    public static readonly DependencyProperty FalseValueProperty = DependencyProperty.Register(
        nameof(FalseValue),
        typeof(T),
        typeof(BoolToValueConverter<T>),
        new PropertyMetadata(default(T)));

    public static readonly DependencyProperty IsInvertedProperty = DependencyProperty.Register(
        nameof(IsInverted),
        typeof(bool),
        typeof(BoolToValueConverter<T>),
        new PropertyMetadata(false));

    public override T TrueValue
    {
        get => (T)this.GetValue(TrueValueProperty);
        set => this.SetValue(TrueValueProperty, value);
    }

    public override T FalseValue
    {
        get => (T)this.GetValue(FalseValueProperty);
        set => this.SetValue(FalseValueProperty, value);
    }

    public override bool IsInverted
    {
        get => (bool)this.GetValue(IsInvertedProperty);
        set => this.SetValue(IsInvertedProperty, value);
    }
}
