using System;
using System.ComponentModel;
using BetterGenshinImpact.Model;
using Newtonsoft.Json;

namespace BetterGenshinImpact.Pulonia.Models;

/// <summary>
/// 计划内的定时或快捷键触发配置；只描述准入，不直接驱动游戏。
/// </summary>
public sealed class PuloniaTaskTrigger
{
    /// <summary>稳定触发器 ID，修改名称不改变去重身份。</summary>
    [JsonProperty("id", Required = Required.Always)]
    public string Id { get; set; } = PuloniaId.NewTriggerId();
    /// <summary>用户可读名称。</summary>
    public string Name { get; set; } = "每日任务";
    /// <summary>是否启用；新建配置不会未经确认启动自动任务。</summary>
    public bool Enabled { get; set; }
    /// <summary>触发入口。</summary>
    public PuloniaTaskTriggerKind Kind { get; set; }
    /// <summary>定时日程种类。</summary>
    public PuloniaTaskScheduleKind ScheduleKind { get; set; }
    /// <summary>五段 Cron：分、时、日、月、星期；快捷表单也写入这一唯一表达式。</summary>
    public string Cron { get; set; } = "0 4 * * *";
    /// <summary>日程采用的 Windows 时区，不与 CD 服务器时区混淆。</summary>
    public string TimeZoneId { get; set; } = "China Standard Time";
    /// <summary>一次性时刻或固定间隔的 UTC 锚点，重启不能重置。</summary>
    public DateTimeOffset AnchorUtc { get; set; } = DateTimeOffset.UtcNow;
    /// <summary>固定间隔分钟数，不用 Cron 伪装跨日的 N 小时。</summary>
    public int IntervalMinutes { get; set; } = 24 * 60;
    /// <summary>本次启用/调整的起点，不补跑配置生效以前的日程。</summary>
    public DateTimeOffset ActivatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    /// <summary>快捷键，复用现有热键文本格式，支持键盘键或鼠标侧键。</summary>
    public string Hotkey { get; set; } = "Ctrl + Alt + F8";
    /// <summary>复用软件的全局注册 / 键鼠监听类型；旧配置缺省为全局注册，省略默认值以保持原配置签名。</summary>
    [DefaultValue(HotKeyTypeEnum.GlobalRegister)]
    [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
    public HotKeyTypeEnum HotkeyType { get; set; } = HotKeyTypeEnum.GlobalRegister;
    /// <summary>可选编辑树节点 ID；为空运行整个计划，祖先配置仍生效。</summary>
    public string? TargetTaskId { get; set; }
    /// <summary>当前已登录账号资料引用；不执行账号切换。</summary>
    public string? AccountId { get; set; }
    /// <summary>到期后还能开始的时间窗，分钟。</summary>
    public int WindowMinutes { get; set; } = 360;
    /// <summary>错过到期时刻时是否在有效窗口内合并补一次。</summary>
    public bool CatchUp { get; set; } = true;
    /// <summary>同计划同账号繁忙时的行为。</summary>
    public PuloniaTaskBusyPolicy BusyPolicy { get; set; }
    /// <summary>计划运行总预算，秒；默认 null 表示不限时，与开始截止时间分别计算，兼容旧配置的显式预算。</summary>
    public double? TimeoutSeconds { get; set; }
}

/// <summary>触发入口类型。</summary>
public enum PuloniaTaskTriggerKind
{
    /// <summary>定时日程。</summary>
    Schedule,
    /// <summary>全局热键或键鼠监听快捷键。</summary>
    Hotkey
}

/// <summary>日程的唯一时间计算方式。</summary>
public enum PuloniaTaskScheduleKind
{
    /// <summary>五段 Cron 周期。</summary>
    Cron,
    /// <summary>一次性 UTC 时刻。</summary>
    Once,
    /// <summary>以保存锚点为基准的固定间隔。</summary>
    Interval
}

/// <summary>自动触发的忙碌处理方式。</summary>
public enum PuloniaTaskBusyPolicy
{
    /// <summary>同计划同账号已有未结束请求时跳过，其他计划仍串行排队。</summary>
    Skip,
    /// <summary>允许活动请求之后再排一次，不无界累积。</summary>
    QueueOnce,
    /// <summary>请求停止当前任务，确认退出后优先执行，不抢占仍未释放的输入。</summary>
    StopCurrent
}
