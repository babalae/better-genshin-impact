using System;
using System.Security.Cryptography;
using System.Text;
using BetterGenshinImpact.Model;
using BetterGenshinImpact.Pulonia.Models;
using System.Windows.Input;

namespace BetterGenshinImpact.Pulonia.Services;

/// <summary>
/// 日程计算、配置签名和校验的纯函数入口，不依赖页面或计时器。
/// </summary>
public static class PuloniaTaskSchedule
{
    /// <summary>用于排队后复核与配置变更重置的内容签名。</summary>
    public static string Signature(PuloniaTaskTrigger trigger) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(PuloniaTaskJson.Write(trigger))));

    /// <summary>严格检查全部准入字段，即使禁用的配置也不能藏有无效时间或危险热键。</summary>
    public static void Validate(PuloniaTaskTrigger trigger)
    {
        PuloniaTaskValidator.ValidateId(trigger.Id, "trigger/id");
        if (string.IsNullOrWhiteSpace(trigger.Name) || trigger.Name.Length > 128
            || !Enum.IsDefined(trigger.Kind) || !Enum.IsDefined(trigger.ScheduleKind) || !Enum.IsDefined(trigger.BusyPolicy)
            || trigger.WindowMinutes is < 1 or > 10080
            || !double.IsFinite(trigger.TimeoutSeconds) || trigger.TimeoutSeconds is <= 0 or > 604800
            || trigger.IntervalMinutes is < 1 or > 525600)
            throw new FormatException("触发器名称、类型、有效窗口或总预算无效。" );
        if (trigger.TargetTaskId is not null)
            PuloniaTaskValidator.ValidateId(trigger.TargetTaskId, "trigger/target_task_id");
        if (trigger.AccountId is not null)
            PuloniaTaskValidator.ValidateId(trigger.AccountId, "trigger/account_id");
        _ = TimeZoneInfo.FindSystemTimeZoneById(trigger.TimeZoneId);
        if (trigger.Kind == PuloniaTaskTriggerKind.Hotkey)
        {
            var hotkey = HotKey.FromString(trigger.Hotkey);
            if (hotkey.IsEmpty || hotkey.MouseButton != MouseButton.Left || hotkey.Modifiers == ModifierKeys.None
                || hotkey.Key is Key.None or Key.System or Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
                    or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin || hotkey.Key == Key.F12)
                throw new FormatException("请设置带 Ctrl/Alt/Shift/Win 的键盘组合键（F12 为系统保留，不能使用）。" );
        }
        else if (trigger.ScheduleKind == PuloniaTaskScheduleKind.Cron)
        {
            var cron = new PuloniaCronExpression(trigger.Cron);
            if (cron.Next(trigger.ActivatedAtUtc.AddTicks(-1), TimeZoneInfo.FindSystemTimeZoneById(trigger.TimeZoneId)) is null)
                throw new FormatException("该 Cron 在未来八年没有有效日期。" );
        }
    }

    /// <summary>计算严格晚于指定 UTC 时刻的下次执行。</summary>
    public static DateTimeOffset? Next(PuloniaTaskTrigger trigger, DateTimeOffset afterUtc)
    {
        if (trigger.Kind == PuloniaTaskTriggerKind.Hotkey)
            return null;
        return trigger.ScheduleKind switch
        {
            PuloniaTaskScheduleKind.Once => trigger.AnchorUtc > afterUtc ? trigger.AnchorUtc : null,
            PuloniaTaskScheduleKind.Interval => NextInterval(trigger, afterUtc),
            _ => new PuloniaCronExpression(trigger.Cron).Next(afterUtc, TimeZoneInfo.FindSystemTimeZoneById(trigger.TimeZoneId))
        };
    }

    /// <summary>只返回有效窗口内最近一次遗漏，不能把关机多日漏掉的任务全部展开。</summary>
    public static DateTimeOffset? Latest(PuloniaTaskTrigger trigger, DateTimeOffset now, DateTimeOffset lowerExclusive)
    {
        var floor = now.AddMinutes(-trigger.WindowMinutes);
        if (lowerExclusive < floor)
            lowerExclusive = floor;
        if (trigger.ScheduleKind == PuloniaTaskScheduleKind.Once)
            return trigger.AnchorUtc > lowerExclusive && trigger.AnchorUtc <= now ? trigger.AnchorUtc : null;
        if (trigger.ScheduleKind == PuloniaTaskScheduleKind.Interval)
        {
            if (now < trigger.AnchorUtc)
                return null;
            var steps = (now - trigger.AnchorUtc).Ticks / TimeSpan.FromMinutes(trigger.IntervalMinutes).Ticks;
            var latest = trigger.AnchorUtc.AddMinutes(steps * (double)trigger.IntervalMinutes);
            return latest > lowerExclusive ? latest : null;
        }
        return new PuloniaCronExpression(trigger.Cron).Latest(now, lowerExclusive,
            TimeZoneInfo.FindSystemTimeZoneById(trigger.TimeZoneId));
    }

    /// <summary>间隔基于固定锚点推进，与程序启动时刻无关。</summary>
    private static DateTimeOffset NextInterval(PuloniaTaskTrigger trigger, DateTimeOffset afterUtc)
    {
        if (afterUtc < trigger.AnchorUtc)
            return trigger.AnchorUtc;
        var steps = (afterUtc - trigger.AnchorUtc).Ticks / TimeSpan.FromMinutes(trigger.IntervalMinutes).Ticks + 1;
        return trigger.AnchorUtc.AddMinutes(steps * (double)trigger.IntervalMinutes);
    }
}
