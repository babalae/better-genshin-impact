using System;

namespace BetterGenshinImpact.GameTask.AutoPathing.Handler.Parameters;

public enum UseGadgetMode
{
    WaitForAvailable,
    Once
}

public sealed class UseGadgetOptions
{
    public const double DefaultMaxWaitSeconds = 100;

    public UseGadgetMode Mode { get; init; } = UseGadgetMode.WaitForAvailable;

    public double MaxWaitSeconds { get; init; } = DefaultMaxWaitSeconds;

    public static UseGadgetOptions Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new UseGadgetOptions();
        }

        var trimmed = raw.Trim();
        var reader = ActionParameterReader.Parse(trimmed);
        var mode = UseGadgetMode.WaitForAvailable;
        var maxWaitSeconds = DefaultMaxWaitSeconds;

        if (reader.HasFlag("not_wait", "no_wait", "once"))
        {
            mode = UseGadgetMode.Once;
        }

        if (reader.TryGetBool(out var notWait, "not_wait", "no_wait") && notWait)
        {
            mode = UseGadgetMode.Once;
        }

        if (reader.TryGetBool(out var wait, "wait", "wait_cd", "wait_for_cd"))
        {
            mode = wait ? UseGadgetMode.WaitForAvailable : UseGadgetMode.Once;
        }

        if (reader.TryGetString(out var modeValue, "mode"))
        {
            mode = ParseMode(modeValue, mode);
        }

        if (reader.TryGetDouble(out var parsedMaxWaitSeconds, "max_wait", "max_wait_seconds", "timeout", "timeout_seconds", "max"))
        {
            maxWaitSeconds = NormalizeMaxWaitSeconds(parsedMaxWaitSeconds);
        }
        else if (!trimmed.Contains('=') && ActionParameterReader.TryParseDouble(trimmed, out parsedMaxWaitSeconds))
        {
            maxWaitSeconds = NormalizeMaxWaitSeconds(parsedMaxWaitSeconds);
        }

        return new UseGadgetOptions
        {
            Mode = mode,
            MaxWaitSeconds = maxWaitSeconds
        };
    }

    private static UseGadgetMode ParseMode(string raw, UseGadgetMode fallback)
    {
        return raw.Trim().ToLowerInvariant() switch
        {
            "once" or "not_wait" or "no_wait" or "fire_and_forget" => UseGadgetMode.Once,
            "wait" or "wait_cd" or "wait_for_cd" or "default" => UseGadgetMode.WaitForAvailable,
            _ => fallback
        };
    }

    private static double NormalizeMaxWaitSeconds(double value)
    {
        return value > 0 ? value : DefaultMaxWaitSeconds;
    }
}
