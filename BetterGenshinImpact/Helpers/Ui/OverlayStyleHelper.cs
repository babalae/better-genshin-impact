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

    /// <summary>
    /// 解析 #RRGGBB 或 #RRGGBBAA（注意 Alpha 在末尾，与 WPF 的 #AARRGGBB 不同）。
    /// 准星、技能 CD 等配置项使用这种格式。解析失败返回 null
    /// </summary>
    public static Color? ParseRgbaHexColor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var hex = value.Trim().TrimStart('#');
        if ((hex.Length != 6 && hex.Length != 8)
            || !uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _))
        {
            return null;
        }

        var r = byte.Parse(hex[0..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var g = byte.Parse(hex[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var b = byte.Parse(hex[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var a = hex.Length == 8 ? byte.Parse(hex[6..8], NumberStyles.HexNumber, CultureInfo.InvariantCulture) : byte.MaxValue;
        return Color.FromArgb(a, r, g, b);
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
