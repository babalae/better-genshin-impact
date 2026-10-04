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
            || trigger.TimeoutSeconds is { } timeout && (!double.IsFinite(timeout) || timeout is <= 0 or > 604800)
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
            if (!Enum.IsDefined(trigger.HotkeyType) || hotkey.IsEmpty || !Enum.IsDefined(hotkey.Key)
                || !Enum.IsDefined(hotkey.MouseButton)
                || (hotkey.Modifiers & ~(ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift | ModifierKeys.Windows)) != 0)
                throw new FormatException("请选择有效的热键类型并设置快捷键。" );
            // 两种模式沿用现有热键系统的边界：监听只接受单键 / 侧键，全局注册接受组合键 / 功能键。
            if (trigger.HotkeyType == HotKeyTypeEnum.KeyboardMonitor)
            {
                if (hotkey.Modifiers != ModifierKeys.None
                    || hotkey.MouseButton is not (MouseButton.Left or MouseButton.XButton1 or MouseButton.XButton2)
                    || hotkey.MouseButton != MouseButton.Left && hotkey.Key != Key.None)
                    throw new FormatException("键鼠监听只支持键盘单键或鼠标侧键，不支持组合键。" );
                if (hotkey.MouseButton is MouseButton.XButton1 or MouseButton.XButton2) return;
            }
            else if (hotkey.MouseButton != MouseButton.Left || hotkey.Key == Key.F12
                || hotkey.Modifiers == ModifierKeys.None && hotkey.Key is not (>= Key.F1 and <= Key.F24))
                throw new FormatException("全局热键支持组合键或功能键；普通键需搭配 Ctrl / Alt / Win，F12 为系统保留。" );
            // 旧配置中的 Shift 组合仍是合法系统注册；新输入按原捕获控件规则避免录入字符型 Shift 组合。
            if (hotkey.Key is Key.None or Key.System or Key.Clear or Key.OemClear or Key.Apps or Key.ImeProcessed or Key.DeadCharProcessed
                    or Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
                    or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
                throw new FormatException("请设置有效的键盘键，不可只使用修饰键。" );
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
