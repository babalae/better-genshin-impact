using BetterGenshinImpact.GameTask.AutoFight;
using System;
using System.Globalization;

namespace BetterGenshinImpact.GameTask.AutoDomain;

public readonly record struct AutoDomainFightSeekOptions(
    bool Enabled,
    TimeSpan Interval,
    TimeSpan InitialDelay,
    int RotaryFactor)
{
    private static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan MinInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaxInterval = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan DefaultInitialDelay = TimeSpan.FromSeconds(2);

    public static AutoDomainFightSeekOptions FromAutoFightConfig(AutoFightConfig config)
    {
        var finishDetectConfig = config.FinishDetectConfig;
        var rotaryFactor = Math.Clamp(finishDetectConfig.RotaryFactor, 1, 13);

        if (!finishDetectConfig.RotateFindEnemyEnabled)
        {
            return new AutoDomainFightSeekOptions(false, DefaultInterval, DefaultInitialDelay, rotaryFactor);
        }

        var interval = finishDetectConfig.FastCheckEnabled
            ? ParseInterval(finishDetectConfig.FastCheckParams) ?? DefaultInterval
            : DefaultInterval;

        return new AutoDomainFightSeekOptions(
            true,
            ClampInterval(interval),
            finishDetectConfig.IsFirstCheck ? TimeSpan.Zero : DefaultInitialDelay,
            rotaryFactor);
    }

    private static TimeSpan ClampInterval(TimeSpan interval)
    {
        var seconds = Math.Clamp(interval.TotalSeconds, MinInterval.TotalSeconds, MaxInterval.TotalSeconds);
        return TimeSpan.FromSeconds(seconds);
    }

    private static TimeSpan? ParseInterval(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        foreach (var segment in input.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            if (double.TryParse(segment.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
                && seconds > 0)
            {
                return TimeSpan.FromSeconds(seconds);
            }
        }

        return null;
    }
}
