using System;
using System.Globalization;
using System.Windows.Media;

namespace BetterGenshinImpact.Helpers.Ui;

public static class OverlayStyleHelper
{
    public static Color ParseColorOrDefault(string? value, Color fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var text = value.Trim();
        if (TryParseHexColor(text, out var hexColor))
        {
            return hexColor;
        }

        try
        {
            var converted = ColorConverter.ConvertFromString(text);
            if (converted is Color color)
            {
                return color;
            }
        }
        catch
        {
            // Fall back below.
        }

        return fallback;
    }

    public static SolidColorBrush CreateBrush(string? value, Color fallback)
    {
        var brush = new SolidColorBrush(ParseColorOrDefault(value, fallback));
        brush.Freeze();
        return brush;
    }

    private static bool TryParseHexColor(string text, out Color color)
    {
        color = default;
        var hex = text.StartsWith("#", StringComparison.Ordinal) ? text[1..] : text;

        if (hex.Length != 6 && hex.Length != 8)
        {
            return false;
        }

        if (!int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _))
        {
            return false;
        }

        var start = 0;
        var a = byte.MaxValue;
        if (hex.Length == 8)
        {
            a = byte.Parse(hex[0..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            start = 2;
        }

        var r = byte.Parse(hex[start..(start + 2)], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var g = byte.Parse(hex[(start + 2)..(start + 4)], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var b = byte.Parse(hex[(start + 4)..(start + 6)], NumberStyles.HexNumber, CultureInfo.InvariantCulture);

        color = Color.FromArgb(a, r, g, b);
        return true;
    }
}
