using System;
using System.Collections.Generic;
using System.Linq;
using BetterGenshinImpact.Model;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterGenshinImpact.ViewModel.Pages.Pulonia;

/// <summary>触发器列表项，将已保存配置与最新调度反馈分开展示。</summary>
public partial class PuloniaTaskTriggerItemViewModel : ObservableObject
{
    /// <summary>配置的独立副本。</summary>
    public PuloniaTaskTrigger Trigger { get; }
    /// <summary>卡片的已保存启用状态；开关通过页面命令独立提交，不双向回写编辑草稿。</summary>
    public bool Enabled => Trigger.Enabled;
    /// <summary>类型、启用状态、时间规则，以及定时触发器选了主实例以外的执行实例时的执行实例。</summary>
    public string Summary => (Trigger.Enabled ? "已启用 · " : "未启用 · ") + (Trigger.Kind == PuloniaTaskTriggerKind.Hotkey
        ? Trigger.HotkeyType.ToLocalizedName() + " · " + Trigger.Hotkey : Trigger.Cron)
        + (Trigger.Instances.Count > 0 ? " · " + string.Join("、", Trigger.Instances.Select(PuloniaInstanceKey.Describe)) : "");
    /// <summary>游标状态和下次执行时间。</summary>
    [ObservableProperty] private string _runtimeText = "正在读取运行状态…";
    /// <summary>建立不影响编辑模型的列表项。</summary>
    public PuloniaTaskTriggerItemViewModel(PuloniaTaskTrigger trigger)
        => Trigger = trigger.Clone();
    /// <summary>确认取消或校验失败后让单向绑定恢复真实开关值，不留下误导性的选中外观。</summary>
    public void RefreshEnabledBinding() => OnPropertyChanged(nameof(Enabled));
    /// <summary>
    /// 只显示与当前配置版本一致的反馈，禁用配置不伪称将执行（统筹设计 9.5）：
    /// 本实例负责的部分显示实时状态；其他执行实例只显示游标记录的上次处理时刻和是否在线，由调用方逐行给出。
    /// </summary>
    /// <param name="status">本实例的运行期反馈；本实例不负责时忽略</param>
    /// <param name="handledHere">本实例是否负责这个触发器（本实例负责的定时，或主实例上的热键）</param>
    /// <param name="otherInstances">其他执行实例的状态行，例如“云原神 · 小号a：在线，已处理到 04:30”</param>
    public void Update(PuloniaTaskTriggerStatus? status, bool handledHere = true, IReadOnlyList<string>? otherInstances = null)
    {
        if (!Trigger.Enabled) { RuntimeText = "已停用，可使用卡片开关启用。"; return; }
        var lines = new List<string>();
        // 1. 本实例负责的部分：实时反馈
        if (handledHere)
        {
            if (status is null || status.TriggerActivatedAtUtc != Trigger.ActivatedAtUtc)
                lines.Add("已保存，稍后开始生效。");
            else
            {
                var next = status.NextOccurrenceUtc is { } utc
                    ? "\n下次：" + utc.ToOffset(PuloniaTaskSchedule.GetUtcOffset(Trigger.ServerTimeZone)).ToString("MM-dd HH:mm zzz") : "";
                lines.Add((otherInstances is { Count: > 0 } ? "本实例：" : "") + GetStatusText(status) + " · " + status.Message + next);
            }
        }
        // 2. 其他执行实例：上次处理时刻与是否在线
        if (otherInstances is { Count: > 0 })
            lines.AddRange(otherInstances);
        if (lines.Count == 0)
            lines.Add(Trigger.Kind == PuloniaTaskTriggerKind.Hotkey
                ? "计划热键只在主实例生效，本实例不响应。"
                : "执行实例不含本实例，本实例不检查这个定时。");
        RuntimeText = string.Join("\n", lines);
    }

    /// <summary>把结果类别或关联运行状态映射为界面文案；文案只用于展示，不参与逻辑判断。</summary>
    private static string GetStatusText(PuloniaTaskTriggerStatus status) => status.RunStatus switch
    {
        PuloniaTaskRunStatus.Queued => "已排队",
        PuloniaTaskRunStatus.Running => "运行中",
        PuloniaTaskRunStatus.Cancelling => "停止中",
        PuloniaTaskRunStatus.Succeeded => "已完成",
        PuloniaTaskRunStatus.Failed => "已失败",
        PuloniaTaskRunStatus.Cancelled => "已取消",
        PuloniaTaskRunStatus.TimedOut => "已超时",
        PuloniaTaskRunStatus.Expired => "已过期",
        PuloniaTaskRunStatus.Interrupted => "已中断",
        PuloniaTaskRunStatus.NeedsAttention => "待处理",
        _ => status.Outcome switch
        {
            PuloniaTaskTriggerOutcome.Waiting => "等待",
            PuloniaTaskTriggerOutcome.Submitted => "已排队",
            PuloniaTaskTriggerOutcome.Missed => "已过期",
            PuloniaTaskTriggerOutcome.Merged => "已合并",
            _ => "准备失败"
        }
    };
}
