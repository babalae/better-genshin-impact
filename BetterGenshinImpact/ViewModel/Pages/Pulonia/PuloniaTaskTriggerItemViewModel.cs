using System;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterGenshinImpact.ViewModel.Pages.Pulonia;

/// <summary>触发器列表项，将已保存配置与最新调度反馈分开展示。</summary>
public partial class PuloniaTaskTriggerItemViewModel : ObservableObject
{
    /// <summary>配置的独立副本。</summary>
    public PuloniaTaskTrigger Trigger { get; }
    /// <summary>用户名称。</summary>
    public string Name => Trigger.Name;
    /// <summary>类型、启用状态和时间规则。</summary>
    public string Summary => (Trigger.Enabled ? "已启用 · " : "未启用 · ") + (Trigger.Kind == PuloniaTaskTriggerKind.Hotkey
        ? Trigger.Hotkey : Trigger.ScheduleKind == PuloniaTaskScheduleKind.Cron ? Trigger.Cron
        : Trigger.ScheduleKind == PuloniaTaskScheduleKind.Once ? "仅一次" : $"每 {Trigger.IntervalMinutes} 分钟");
    /// <summary>游标状态和下次执行时间。</summary>
    [ObservableProperty] private string _runtimeText = "等待调度检查。";
    /// <summary>建立不影响编辑模型的列表项。</summary>
    public PuloniaTaskTriggerItemViewModel(PuloniaTaskTrigger trigger)
        => Trigger = PuloniaTaskJson.Read<PuloniaTaskTrigger>(PuloniaTaskJson.Write(trigger));
    /// <summary>只显示与当前配置签名一致的游标，禁用配置不伪称将执行。</summary>
    public void Update(PuloniaTaskTriggerState? state)
    {
        if (!Trigger.Enabled) { RuntimeText = "保存并启用后才会执行。"; return; }
        if (state is null || state.Signature != PuloniaTaskSchedule.Signature(Trigger)) { RuntimeText = "配置已保存，等待调度器接入。"; return; }
        var next = state.NextOccurrenceUtc is { } utc
            ? "\n下次：" + TimeZoneInfo.ConvertTime(utc, TimeZoneInfo.FindSystemTimeZoneById(Trigger.TimeZoneId)).ToString("MM-dd HH:mm zzz") : "";
        RuntimeText = state.Status + " · " + state.Message + next;
    }
}
