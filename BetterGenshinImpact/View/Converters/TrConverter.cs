using System;
using System.Globalization;
using System.Windows.Data;
using BetterGenshinImpact.Service.Interface;

namespace BetterGenshinImpact.View.Converters;

[ValueConversion(typeof(string), typeof(string))]
public sealed class TrConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string s || string.IsNullOrEmpty(s))
        {
            return value ?? string.Empty;
        }

        var translator = App.GetService<ITranslationService>();
        return translator?.Translate(s, MissingTextSource.UiDynamicBinding) ?? s;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value ?? string.Empty;
    }
}
