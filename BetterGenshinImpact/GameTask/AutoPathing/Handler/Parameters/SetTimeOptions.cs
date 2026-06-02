using System;

namespace BetterGenshinImpact.GameTask.AutoPathing.Handler.Parameters;

public sealed class SetTimeOptions
{
    public int Hour { get; init; }

    public int Minute { get; init; }

    public bool SkipAnimation { get; init; } = true;

    public static bool TryParse(string? raw, out SetTimeOptions? options)
    {
        options = null;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var trimmed = raw.Trim();
        if (TryParseKeyValue(trimmed, out options))
        {
            return true;
        }

        return TryParseLegacy(trimmed, out options);
    }

    private static bool TryParseKeyValue(string raw, out SetTimeOptions? options)
    {
        options = null;
        if (!raw.Contains('='))
        {
            return false;
        }

        var reader = ActionParameterReader.Parse(raw);
        int? hour = null;
        int? minute = null;

        if (reader.TryGetString(out var timeValue, "time", "at"))
        {
            if (!TryParseHourMinute(timeValue, out var parsedHour, out var parsedMinute))
            {
                return false;
            }

            hour = parsedHour;
            minute = parsedMinute;
        }

        if (reader.TryGetInt(out var hourValue, "hour", "h"))
        {
            hour = hourValue;
        }

        if (reader.TryGetInt(out var minuteValue, "minute", "min", "m"))
        {
            minute = minuteValue;
        }

        var skipAnimation = true;
        if (reader.TryGetBool(out var parsedSkipAnimation, "skip_animation", "skip", "skip_anim"))
        {
            skipAnimation = parsedSkipAnimation;
        }

        if (hour == null || minute == null)
        {
            return false;
        }

        options = new SetTimeOptions
        {
            Hour = hour.Value,
            Minute = minute.Value,
            SkipAnimation = skipAnimation
        };
        return true;
    }

    private static bool TryParseLegacy(string raw, out SetTimeOptions? options)
    {
        options = null;
        var firstColon = raw.IndexOf(':');
        if (firstColon < 0)
        {
            return false;
        }

        var secondColon = raw.IndexOf(':', firstColon + 1);
        var hourStr = raw[..firstColon];
        var minuteStr = secondColon < 0
            ? raw[(firstColon + 1)..]
            : raw.Substring(firstColon + 1, secondColon - firstColon - 1);

        if (!int.TryParse(hourStr, out var hour) || !int.TryParse(minuteStr, out var minute))
        {
            return false;
        }

        var skipAnimation = true;
        if (secondColon >= 0)
        {
            var skipStr = raw[(secondColon + 1)..];
            if (ActionParameterReader.TryParseBool(skipStr, out var parsedSkipAnimation))
            {
                skipAnimation = parsedSkipAnimation;
            }
        }

        options = new SetTimeOptions
        {
            Hour = hour,
            Minute = minute,
            SkipAnimation = skipAnimation
        };
        return true;
    }

    private static bool TryParseHourMinute(string raw, out int hour, out int minute)
    {
        hour = default;
        minute = default;
        var firstColon = raw.IndexOf(':');
        if (firstColon < 0)
        {
            return false;
        }

        return int.TryParse(raw[..firstColon], out hour)
               && int.TryParse(raw[(firstColon + 1)..], out minute);
    }
}
