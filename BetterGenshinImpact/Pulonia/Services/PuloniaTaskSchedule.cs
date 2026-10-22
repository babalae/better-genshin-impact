using System;
using System.Collections.Generic;
using System.Linq;
using BetterGenshinImpact.Model;
using BetterGenshinImpact.Pulonia.Models;
using System.Windows.Input;

namespace BetterGenshinImpact.Pulonia.Services;

/// <summary>
/// Cron 日程计算和触发配置校验的纯函数入口，不依赖页面或计时器。
/// </summary>
public static class PuloniaTaskSchedule
{
    /// <summary>原神服务器时区对应的无夏令时固定时区。</summary>
    private static readonly IReadOnlyDictionary<PuloniaTaskServerTimeZone, TimeZoneInfo> ServerTimeZones
        = Enum.GetValues<PuloniaTaskServerTimeZone>().ToDictionary(
            value => value,
            value => TimeZoneInfo.CreateCustomTimeZone(
                value.ToString(), TimeSpan.FromHours((int)value), value.ToString(), value.ToString()));

    /// <summary>严格检查公共字段及当前入口实际使用的配置。</summary>
    public static void Validate(PuloniaTaskTrigger trigger)
    {
        PuloniaTaskValidator.ValidateId(trigger.Id, "trigger/id");
        if (!Enum.IsDefined(trigger.Kind) || !Enum.IsDefined(trigger.BusyPolicy)
            || trigger.WindowMinutes is < 1 or > 10080)
            throw new FormatException("触发器类型或允许延迟启动时间无效。");
        if (trigger.TargetTaskId is not null)
            PuloniaTaskValidator.ValidateId(trigger.TargetTaskId, "trigger/target_task_id");
        if (trigger.AccountId is not null)
            PuloniaTaskValidator.ValidateId(trigger.AccountId, "trigger/account_id");
        if (trigger.Kind == PuloniaTaskTriggerKind.Hotkey)
        {
            ValidateHotkey(trigger);
            return;
        }

        var zone = GetTimeZone(trigger.ServerTimeZone);
        var cron = new PuloniaCronExpression(trigger.Cron);
        if (cron.Next(trigger.ActivatedAtUtc.AddTicks(-1), zone) is null)
            throw new FormatException("该 Cron 在未来八年没有有效日期。");
    }

    /// <summary>计算严格晚于指定 UTC 时刻的下次执行。</summary>
    public static DateTimeOffset? Next(PuloniaTaskTrigger trigger, DateTimeOffset afterUtc)
    {
        if (trigger.Kind == PuloniaTaskTriggerKind.Hotkey)
            return null;
        return new PuloniaCronExpression(trigger.Cron).Next(afterUtc,
            GetTimeZone(trigger.ServerTimeZone));
    }

    /// <summary>只返回仍可延迟启动的最近一次遗漏，不能把关机多日漏掉的任务全部展开。</summary>
    public static DateTimeOffset? Latest(PuloniaTaskTrigger trigger, DateTimeOffset now, DateTimeOffset lowerExclusive)
    {
        if (trigger.Kind == PuloniaTaskTriggerKind.Hotkey)
            return null;
        var floor = now.AddMinutes(-trigger.WindowMinutes);
        if (lowerExclusive < floor)
            lowerExclusive = floor;
        return new PuloniaCronExpression(trigger.Cron).Latest(now, lowerExclusive,
            GetTimeZone(trigger.ServerTimeZone));
    }

    /// <summary>取得触发器支持的固定服务器时区，并拒绝未定义值。</summary>
    public static TimeZoneInfo GetTimeZone(PuloniaTaskServerTimeZone serverTimeZone)
    {
        if (!ServerTimeZones.TryGetValue(serverTimeZone, out var zone))
            throw new FormatException("请选择支持的原神服务器时区。");
        return zone;
    }

    /// <summary>校验热键模式和键值组合。</summary>
    private static void ValidateHotkey(PuloniaTaskTrigger trigger)
    {
        var hotkey = HotKey.FromString(trigger.Hotkey);
        if (!Enum.IsDefined(trigger.HotkeyType) || hotkey.IsEmpty || !Enum.IsDefined(hotkey.Key)
            || !Enum.IsDefined(hotkey.MouseButton)
            || (hotkey.Modifiers & ~(ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift | ModifierKeys.Windows)) != 0)
            throw new FormatException("请选择有效的热键类型并设置快捷键。");
        // 两种模式沿用现有热键系统的边界：监听只接受单键 / 侧键，全局注册接受组合键 / 功能键。
        if (trigger.HotkeyType == HotKeyTypeEnum.KeyboardMonitor)
        {
            if (hotkey.Modifiers != ModifierKeys.None
                || hotkey.MouseButton is not (MouseButton.Left or MouseButton.XButton1 or MouseButton.XButton2)
                || hotkey.MouseButton != MouseButton.Left && hotkey.Key != Key.None)
                throw new FormatException("键鼠监听只支持键盘单键或鼠标侧键，不支持组合键。");
            if (hotkey.MouseButton is MouseButton.XButton1 or MouseButton.XButton2) return;
        }
        else if (hotkey.MouseButton != MouseButton.Left || hotkey.Key == Key.F12
            || hotkey.Modifiers == ModifierKeys.None && hotkey.Key is not (>= Key.F1 and <= Key.F24))
            throw new FormatException("全局热键支持组合键或功能键；普通键需搭配 Ctrl / Alt / Win，F12 为系统保留。");
        if (hotkey.Key is Key.None or Key.System or Key.Clear or Key.OemClear or Key.Apps or Key.ImeProcessed or Key.DeadCharProcessed
                or Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
                or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
            throw new FormatException("请设置有效的键盘键，不可只使用修饰键。");
    }
}
