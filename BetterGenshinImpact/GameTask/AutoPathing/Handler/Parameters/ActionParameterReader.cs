using System;
using System.Collections.Generic;
using System.Globalization;

namespace BetterGenshinImpact.GameTask.AutoPathing.Handler.Parameters;

/// <summary>
/// 读取 action 参数中的 key=value 项和单独 flag，兼容旧字符串参数。
/// </summary>
public sealed class ActionParameterReader
{
    private static readonly char[] PairSeparators = [';', ',', '&'];
    private readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _flags = new(StringComparer.OrdinalIgnoreCase);

    private ActionParameterReader()
    {
    }

    public static ActionParameterReader Parse(string? raw)
    {
        var reader = new ActionParameterReader();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return reader;
        }

        foreach (var token in raw.Split(PairSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separatorIndex = token.IndexOf('=');
            if (separatorIndex < 0)
            {
                var flag = NormalizeKey(token);
                if (!string.IsNullOrEmpty(flag))
                {
                    reader._flags.Add(flag);
                }
                continue;
            }

            var key = NormalizeKey(token[..separatorIndex]);
            if (string.IsNullOrEmpty(key))
            {
                continue;
            }

            reader._values[key] = token[(separatorIndex + 1)..].Trim();
        }

        return reader;
    }

    public bool HasFlag(params string[] aliases)
    {
        foreach (var alias in aliases)
        {
            if (_flags.Contains(NormalizeKey(alias)))
            {
                return true;
            }
        }

        return false;
    }

    public bool TryGetString(out string value, params string[] aliases)
    {
        foreach (var alias in aliases)
        {
            if (_values.TryGetValue(NormalizeKey(alias), out value!))
            {
                return true;
            }
        }

        value = string.Empty;
        return false;
    }

    public bool TryGetDouble(out double value, params string[] aliases)
    {
        if (TryGetString(out var raw, aliases) && TryParseDouble(raw, out value))
        {
            return true;
        }

        value = default;
        return false;
    }

    public bool TryGetInt(out int value, params string[] aliases)
    {
        if (TryGetString(out var raw, aliases) && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
        {
            return true;
        }

        value = default;
        return false;
    }

    public bool TryGetBool(out bool value, params string[] aliases)
    {
        if (!TryGetString(out var raw, aliases))
        {
            value = default;
            return false;
        }

        return TryParseBool(raw, out value);
    }

    public static bool TryParseDouble(string? raw, out double value)
    {
        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
        {
            return true;
        }

        return double.TryParse(raw, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
    }

    public static bool TryParseBool(string? raw, out bool value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        switch (raw.Trim().ToLowerInvariant())
        {
            case "true":
            case "1":
            case "yes":
            case "y":
            case "on":
                value = true;
                return true;
            case "false":
            case "0":
            case "no":
            case "n":
            case "off":
                value = false;
                return true;
            default:
                return false;
        }
    }

    private static string NormalizeKey(string key)
    {
        return key.Trim().Replace('-', '_');
    }
}
