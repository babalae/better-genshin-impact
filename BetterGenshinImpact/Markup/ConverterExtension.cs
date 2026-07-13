using System;
using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;

namespace BetterGenshinImpact.Markup;

[MarkupExtensionReturnType(typeof(object))]
public class ConverterExtension : MarkupExtension
{
    public object? Value { get; set; } = null;
    public IValueConverter Converter { get; set; } = null!;
    public object? Parameter { get; set; } = null;
    public CultureInfo Culture { get; set; } = null!;

    public ConverterExtension()
    {
    }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        if (Converter == null)
        {
            return Value!;
        }
        else
        {
            Type targetType = typeof(object);

            if (serviceProvider.GetService(typeof(IProvideValueTarget)) is IProvideValueTarget provideValueTarget)
            {
                var targetProperty = provideValueTarget.TargetProperty;

                if (targetProperty is DependencyProperty dp)
                {
                    targetType = dp.PropertyType;
                }
                else if (targetProperty is PropertyInfo prop)
                {
                    targetType = prop.PropertyType;
                }
            }

            return Converter.Convert(Value, targetType, Parameter, Culture);
        }
    }
}
