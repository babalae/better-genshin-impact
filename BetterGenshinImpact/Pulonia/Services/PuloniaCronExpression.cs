using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace BetterGenshinImpact.Pulonia.Services;

/// <summary>
/// 有界的五段数字 Cron 解析器，支持通配、列表、范围与步长；不接受 Quartz 的秒段和 L/W/#/?。
/// 日与星期同时限制时采用 AND，不暗中迁移其他 Cron 方言。
/// </summary>
public sealed class PuloniaCronExpression
{
    /// <summary>分钟集合。</summary>
    private readonly int[] _minutes;
    /// <summary>小时集合。</summary>
    private readonly int[] _hours;
    /// <summary>月内日期集合。</summary>
    private readonly int[] _days;
    /// <summary>月份集合。</summary>
    private readonly int[] _months;
    /// <summary>星期集合，0 为周日。</summary>
    private readonly int[] _weekdays;

    /// <summary>解析并固定各时间字段，错误不会被解释为默认每日任务。</summary>
    public PuloniaCronExpression(string expression)
    {
        var fields = expression.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length != 5 || expression.Length > 256)
            throw new FormatException("Cron 必须是五段：分 时 日 月 星期，例如 0 4 * * *；不支持秒段。" );
        _minutes = ParseField(fields[0], 0, 59);
        _hours = ParseField(fields[1], 0, 23);
        _days = ParseField(fields[2], 1, 31);
        _months = ParseField(fields[3], 1, 12);
        _weekdays = ParseField(fields[4], 0, 7).Select(value => value % 7).Distinct().Order().ToArray();
    }

    /// <summary>查找严格晚于给定 UTC 时刻的下一次发生，最多扫描八个日历年。</summary>
    public DateTimeOffset? Next(DateTimeOffset afterUtc, TimeZoneInfo zone)
    {
        var day = TimeZoneInfo.ConvertTime(afterUtc, zone).Date;
        for (var offset = 0; offset < 8 * 366 && day.Year < 9999; offset++, day = day.AddDays(1))
        {
            if (!MatchesDay(day))
                continue;
            DateTimeOffset? earliest = null;
            foreach (var hour in _hours)
            foreach (var minute in _minutes)
            {
                var candidate = ResolveLocal(day.AddHours(hour).AddMinutes(minute), zone);
                if (candidate > afterUtc && (earliest is null || candidate < earliest))
                    earliest = candidate;
            }
            if (earliest is not null)
                return earliest;
        }
        return null;
    }

    /// <summary>查找有效回看窗口内最后一次发生，合并遗漏而非逐分钟补跑。</summary>
    public DateTimeOffset? Latest(DateTimeOffset nowUtc, DateTimeOffset lowerExclusive, TimeZoneInfo zone)
    {
        var day = TimeZoneInfo.ConvertTime(nowUtc, zone).Date;
        var lastDay = TimeZoneInfo.ConvertTime(lowerExclusive, zone).Date.AddDays(-1);
        for (; day >= lastDay; day = day.AddDays(-1))
        {
            if (!MatchesDay(day))
                continue;
            DateTimeOffset? latest = null;
            foreach (var hour in _hours)
            foreach (var minute in _minutes)
            {
                var candidate = ResolveLocal(day.AddHours(hour).AddMinutes(minute), zone);
                if (candidate <= nowUtc && candidate > lowerExclusive && (latest is null || candidate > latest))
                    latest = candidate;
            }
            if (latest is not null)
                return latest;
        }
        return null;
    }

    /// <summary>缺失时刻顺延到第一个有效分钟；重复时刻固定为首次 UTC 发生，不能触发两次。</summary>
    public static DateTimeOffset ResolveLocal(DateTime local, TimeZoneInfo zone)
    {
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        var attempts = 0;
        while (zone.IsInvalidTime(local))
        {
            if (++attempts > 2 * 24 * 60)
                throw new FormatException("时区缺失时间超过两天，无法安全安排。" );
            local = local.AddMinutes(1);
        }
        var utcOffset = zone.IsAmbiguousTime(local)
            ? zone.GetAmbiguousTimeOffsets(local).Max()
            : zone.GetUtcOffset(local);
        return new DateTimeOffset(local, utcOffset).ToUniversalTime();
    }

    /// <summary>不存在的月内日期自然跳过，日和星期限制同时生效。</summary>
    private bool MatchesDay(DateTime day) => Array.BinarySearch(_months, day.Month) >= 0
        && Array.BinarySearch(_days, day.Day) >= 0 && Array.BinarySearch(_weekdays, (int)day.DayOfWeek) >= 0;

    /// <summary>严格解析一个数字字段；拒绝空项、倒置范围、零步长及特殊方言符号。</summary>
    private static int[] ParseField(string field, int min, int max)
    {
        var values = new SortedSet<int>();
        foreach (var item in field.Split(','))
        {
            var stepParts = item.Split('/');
            if (stepParts.Length > 2)
                throw new FormatException("Cron 步长格式错误。" );
            var step = stepParts.Length == 2 ? Number(stepParts[1], 1, max - min + 1) : 1;
            var range = stepParts[0].Split('-');
            int from, to;
            if (stepParts[0] == "*")
            {
                from = min;
                to = max;
            }
            else if (range.Length == 2)
            {
                from = Number(range[0], min, max);
                to = Number(range[1], min, max);
            }
            else if (range.Length == 1)
            {
                from = Number(range[0], min, max);
                to = stepParts.Length == 2 ? max : from;
            }
            else
                throw new FormatException("Cron 范围格式错误。" );
            if (from > to)
                throw new FormatException("Cron 范围不能倒置。" );
            for (var value = from; value <= to; value += step)
                values.Add(value);
        }
        return values.ToArray();
    }

    /// <summary>解析有界非负十进制数字，不接受名称或区域性格式。</summary>
    private static int Number(string value, int min, int max)
    {
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var result) || result < min || result > max)
            throw new FormatException($"Cron 字段应为 {min}—{max} 的数字；仅支持 * , - /，不支持 ? L W # 或名称。" );
        return result;
    }
}
