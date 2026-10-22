using System;
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
    /// <summary>类型、启用状态和时间规则。</summary>
    public string Summary => (Trigger.Enabled ? "已启用 · " : "未启用 · ") + (Trigger.Kind == PuloniaTaskTriggerKind.Hotkey
        ? Trigger.HotkeyType.ToLocalizedName() + " · " + Trigger.Hotkey : Trigger.Cron);
    /// <summary>游标状态和下次执行时间。</summary>
    [ObservableProperty] private string _runtimeText = "等待调度检查。";
    /// <summary>建立不影响编辑模型的列表项。</summary>
    public PuloniaTaskTriggerItemViewModel(PuloniaTaskTrigger trigger)
        => Trigger = trigger.Clone();
    /// <summary>确认取消或校验失败后让单向绑定恢复真实开关值，不留下误导性的选中外观。</summary>
    public void RefreshEnabledBinding() => OnPropertyChanged(nameof(Enabled));
    /// <summary>只显示与当前配置版本一致的游标，禁用配置不伪称将执行。</summary>
    public void Update(PuloniaTaskTriggerState? state)
    {
        if (!Trigger.Enabled) { RuntimeText = "已停用，可使用卡片开关启用。"; return; }
        if (state is null || state.TriggerActivatedAtUtc != Trigger.ActivatedAtUtc) { RuntimeText = "配置已保存，等待调度器接入。"; return; }
        var next = state.NextOccurrenceUtc is { } utc
            ? "\n下次：" + TimeZoneInfo.ConvertTime(utc, PuloniaTaskSchedule.GetTimeZone(Trigger.ServerTimeZone)).ToString("MM-dd HH:mm zzz") : "";
        RuntimeText = state.Status + " · " + state.Message + next;
    }
}
