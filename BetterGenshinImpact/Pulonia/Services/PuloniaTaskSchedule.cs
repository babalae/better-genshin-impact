using System;
using BetterGenshinImpact.Pulonia.Models;

namespace BetterGenshinImpact.Pulonia.Services;

/// <summary>
/// Cron 日程计算和触发配置校验的纯函数入口，不依赖页面、计时器或 WPF 输入类型。
/// 热键键位组合由热键入口侧的 <see cref="PuloniaTaskHotkeyValidator"/> 校验。
/// </summary>
public static class PuloniaTaskSchedule
{
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
            // 计划校验只确认热键配置存在；键位是否可注册由编辑器保存前和宿主注册前检查，失败时在界面提示。
            if (!Enum.IsDefined(trigger.HotkeyType) || string.IsNullOrWhiteSpace(trigger.Hotkey))
                throw new FormatException("请选择有效的热键类型并设置快捷键。");
            return;
        }

        var utcOffset = GetUtcOffset(trigger.ServerTimeZone);
        var cron = new PuloniaCronExpression(trigger.Cron);
        if (cron.Next(trigger.ActivatedAtUtc.AddTicks(-1), utcOffset) is null)
            throw new FormatException("该 Cron 在未来八年没有有效日期。");
    }

    /// <summary>计算严格晚于指定 UTC 时刻的下次执行。</summary>
    public static DateTimeOffset? Next(PuloniaTaskTrigger trigger, DateTimeOffset afterUtc)
    {
        if (trigger.Kind == PuloniaTaskTriggerKind.Hotkey)
            return null;
        return new PuloniaCronExpression(trigger.Cron).Next(afterUtc,
            GetUtcOffset(trigger.ServerTimeZone));
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
            GetUtcOffset(trigger.ServerTimeZone));
    }

    /// <summary>取得触发器支持的原神服务器时区的固定 UTC 偏移（枚举值即小时数），并拒绝未定义值。</summary>
    public static TimeSpan GetUtcOffset(PuloniaTaskServerTimeZone serverTimeZone)
    {
        if (!Enum.IsDefined(serverTimeZone))
            throw new FormatException("请选择支持的原神服务器时区。");
        return TimeSpan.FromHours((int)serverTimeZone);
    }
}
