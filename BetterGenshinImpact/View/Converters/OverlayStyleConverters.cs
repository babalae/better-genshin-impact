using BetterGenshinImpact.Helpers.Ui;
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace BetterGenshinImpact.View.Converters;

public sealed class ColorStringToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var fallback = ParseFallback(parameter, Colors.Transparent);
        return OverlayStyleHelper.CreateBrush(value as string, fallback);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }

    private static Color ParseFallback(object? parameter, Color fallback)
    {
        return parameter is string text
            ? OverlayStyleHelper.ParseColorOrDefault(text, fallback)
            : fallback;
    }
}

public sealed class ColorStringToColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var fallback = parameter is string text
            ? OverlayStyleHelper.ParseColorOrDefault(text, Colors.Transparent)
            : Colors.Transparent;
        return OverlayStyleHelper.ParseColorOrDefault(value as string, fallback);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

public sealed class ShadowOpacityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        var enabled = values.Length > 0 && values[0] is true;
        if (!enabled)
        {
            return 0d;
        }

        if (values.Length > 1 && values[1] is double opacity)
        {
            return Math.Clamp(opacity, 0d, 1d);
        }

        return 0d;
    }

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

public sealed class EnabledColorBrushConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        var enabled = values.Length > 0 && values[0] is true;
        var disabledColor = values.Length > 1 ? values[1] as string : null;
        var enabledColor = values.Length > 2 ? values[2] as string : null;
        return OverlayStyleHelper.CreateBrush(enabled ? enabledColor : disabledColor, Colors.LightGray);
    }

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

public sealed class DoubleToGridLengthConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is double number && number > 0
            ? new GridLength(number)
            : new GridLength(1, GridUnitType.Star);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
