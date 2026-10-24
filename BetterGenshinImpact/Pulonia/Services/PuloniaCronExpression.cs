using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace BetterGenshinImpact.Pulonia.Services;

/// <summary>
/// 有界的五段数字 Cron 解析器，支持通配、列表、范围与步长；不接受 Quartz 的秒段和 L/W/#/?。
/// 日与星期同时限制时采用 AND，不暗中迁移其他 Cron 方言。
/// 只按固定 UTC 偏移计算（原神服务器时区都没有夏令时），因此不存在缺失或重复的本地时刻。
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
    /// <param name="afterUtc">起点（不含）。</param>
    /// <param name="utcOffset">日程所在时区的固定 UTC 偏移。</param>
    public DateTimeOffset? Next(DateTimeOffset afterUtc, TimeSpan utcOffset)
    {
        var day = afterUtc.ToOffset(utcOffset).Date;
        for (var index = 0; index < 8 * 366 && day.Year < 9999; index++, day = day.AddDays(1))
        {
            if (!MatchesDay(day))
                continue;
            DateTimeOffset? earliest = null;
            foreach (var hour in _hours)
            foreach (var minute in _minutes)
            {
                var candidate = ToUtc(day.AddHours(hour).AddMinutes(minute), utcOffset);
                if (candidate > afterUtc && (earliest is null || candidate < earliest))
                    earliest = candidate;
            }
            if (earliest is not null)
                return earliest;
        }
        return null;
    }

    /// <summary>查找有效回看窗口内最后一次发生，合并遗漏而非逐分钟补跑。</summary>
    /// <param name="nowUtc">当前时刻（含）。</param>
    /// <param name="lowerExclusive">回看下限（不含）。</param>
    /// <param name="utcOffset">日程所在时区的固定 UTC 偏移。</param>
    public DateTimeOffset? Latest(DateTimeOffset nowUtc, DateTimeOffset lowerExclusive, TimeSpan utcOffset)
    {
        var day = nowUtc.ToOffset(utcOffset).Date;
        var lastDay = lowerExclusive.ToOffset(utcOffset).Date.AddDays(-1);
        for (; day >= lastDay; day = day.AddDays(-1))
        {
            if (!MatchesDay(day))
                continue;
            DateTimeOffset? latest = null;
            foreach (var hour in _hours)
            foreach (var minute in _minutes)
            {
                var candidate = ToUtc(day.AddHours(hour).AddMinutes(minute), utcOffset);
                if (candidate <= nowUtc && candidate > lowerExclusive && (latest is null || candidate > latest))
                    latest = candidate;
            }
            if (latest is not null)
                return latest;
        }
        return null;
    }

    /// <summary>把固定偏移下的本地日程时刻换算为 UTC；固定偏移没有缺失或重复时刻。</summary>
    /// <param name="local">不带时区种类的本地时刻。</param>
    /// <param name="utcOffset">固定 UTC 偏移。</param>
    private static DateTimeOffset ToUtc(DateTime local, TimeSpan utcOffset)
        => new DateTimeOffset(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), utcOffset).ToUniversalTime();

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
