using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace MicaSetup.Design.Converters;

public abstract class ConverterBase : DependencyObject, IValueConverter
{
    public PreferredCulture PreferredCulture { get; set; } = ValueConvertersConfig.DefaultPreferredCulture;

    protected abstract object Convert(object value, Type targetType, object parameter, CultureInfo culture);

    protected virtual object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException($"Converter '{this.GetType().Name}' does not support backward conversion.");
    }

    object IValueConverter.Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return this.Convert(value, targetType, parameter, this.SelectCulture(() => culture));
    }

    object IValueConverter.ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return this.ConvertBack(value, targetType, parameter, this.SelectCulture(() => culture));
    }

    private CultureInfo SelectCulture(Func<CultureInfo> converterCulture)
    {
        return PreferredCulture switch
        {
            PreferredCulture.CurrentCulture => CultureInfo.CurrentCulture,
            PreferredCulture.CurrentUICulture => CultureInfo.CurrentUICulture,
            _ => converterCulture(),
        };
    }

    public static readonly object UnsetValue = DependencyProperty.UnsetValue;
}
