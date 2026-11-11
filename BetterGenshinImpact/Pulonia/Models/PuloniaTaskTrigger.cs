using System;
using System.Collections.Generic;
using BetterGenshinImpact.Model;
using Newtonsoft.Json;

namespace BetterGenshinImpact.Pulonia.Models;

/// <summary>
/// 计划内的定时或快捷键触发配置；只描述准入，不直接驱动游戏。
/// </summary>
public sealed class PuloniaTaskTrigger
{
    /// <summary>稳定触发器 ID，配置变化不改变去重身份。</summary>
    [JsonProperty("id", Required = Required.Always)]
    public string Id { get; set; } = PuloniaId.NewTriggerId();
    /// <summary>由实际触发配置派生的展示文本，不作为可编辑名称写入配置。</summary>
    [JsonIgnore]
    public string DisplayText => Kind == PuloniaTaskTriggerKind.Hotkey ? Hotkey : Cron;
    /// <summary>是否启用；新建配置不会未经确认启动自动任务。</summary>
    public bool Enabled { get; set; }
    /// <summary>触发入口。</summary>
    public PuloniaTaskTriggerKind Kind { get; set; }
    /// <summary>五段 Cron：分、时、日、月、星期；快捷表单也写入这一唯一表达式。</summary>
    public string Cron { get; set; } = "0 4 * * *";
    /// <summary>日程采用的原神服务器时区，默认使用 UTC+8。</summary>
    public PuloniaTaskServerTimeZone ServerTimeZone { get; set; } = PuloniaTaskServerTimeZone.UtcPlus8;
    /// <summary>本次启用或保存的起点，同时作为配置版本；不补跑配置生效以前的日程。</summary>
    public DateTimeOffset ActivatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    /// <summary>快捷键，复用现有热键文本格式，支持键盘键或鼠标侧键。</summary>
    public string Hotkey { get; set; } = "Ctrl + Alt + F8";
    /// <summary>复用软件的全局注册 / 键鼠监听类型。</summary>
    public HotKeyTypeEnum HotkeyType { get; set; } = HotKeyTypeEnum.GlobalRegister;
    /// <summary>可选编辑树节点 ID；为空运行整个计划，祖先配置仍生效。</summary>
    public string? TargetTaskId { get; set; }
    // 原“账号参数资料”（account_id）已删除：运行账户只由计划的运行账户设置和实例当前账户决定，触发器不能覆盖。
    /// <summary>到期后还能开始的时间窗，分钟。</summary>
    public int WindowMinutes { get; set; } = 360;
    /// <summary>错过触发时刻时，是否在允许延迟启动的范围内合并补一次。</summary>
    public bool CatchUp { get; set; } = true;
    /// <summary>同一计划繁忙时的行为。</summary>
    public PuloniaTaskBusyPolicy BusyPolicy { get; set; }

    /// <summary>
    /// 定时触发器的执行实例（实例键列表，统筹设计 6.4.2）：到点时由这些实例各自检查并在本地提交，游标存在各自的 state.json。
    /// 为空表示只在主实例执行。热键触发器必须为空：计划热键只在主实例注册和执行。
    /// </summary>
    public List<string> Instances { get; set; } = [];

    /// <summary>
    /// 本实例是否负责这个定时触发器：执行实例为空时只有主实例负责，否则执行实例包含本实例时负责。热键触发器总返回 false。
    /// </summary>
    /// <param name="instanceKey">本实例的实例键</param>
    public bool IsScheduledOn(string instanceKey)
        => Kind == PuloniaTaskTriggerKind.Schedule
           && (Instances.Count == 0 ? instanceKey == PuloniaInstanceKey.Primary : Instances.Contains(instanceKey));

    /// <summary>建立不依赖 JSON 序列化的独立配置副本。</summary>
    public PuloniaTaskTrigger Clone() => new()
    {
        Id = Id,
        Enabled = Enabled,
        Kind = Kind,
        Cron = Cron,
        ServerTimeZone = ServerTimeZone,
        ActivatedAtUtc = ActivatedAtUtc,
        Hotkey = Hotkey,
        HotkeyType = HotkeyType,
        TargetTaskId = TargetTaskId,
        WindowMinutes = WindowMinutes,
        CatchUp = CatchUp,
        BusyPolicy = BusyPolicy,
        Instances = [.. Instances]
    };
}

/// <summary>触发入口类型。</summary>
public enum PuloniaTaskTriggerKind
{
    /// <summary>定时日程。</summary>
    Schedule,
    /// <summary>全局热键或键鼠监听快捷键。</summary>
    Hotkey
}

/// <summary>原神各服务器使用的固定时区。</summary>
public enum PuloniaTaskServerTimeZone
{
    /// <summary>美洲服，UTC-5。</summary>
    America = -5,
    /// <summary>欧洲服，UTC+1。</summary>
    Europe = 1,
    /// <summary>天空岛、世界树、亚洲服和港澳台服，UTC+8。</summary>
    UtcPlus8 = 8
}

/// <summary>自动触发的忙碌处理方式。</summary>
public enum PuloniaTaskBusyPolicy
{
    /// <summary>同一计划已有未结束请求时跳过，其他计划仍串行排队。</summary>
    Skip,
    /// <summary>允许活动请求之后再排一次，不无界累积。</summary>
    QueueOnce,
    /// <summary>请求停止当前任务，确认退出后优先执行，不抢占仍未释放的输入。</summary>
    StopCurrent
}
