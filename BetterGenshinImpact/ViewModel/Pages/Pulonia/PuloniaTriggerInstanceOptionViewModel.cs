using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterGenshinImpact.ViewModel.Pages.Pulonia;

/// <summary>
/// 定时触发器编辑区“执行实例”中的一项（统筹设计 9.5）：主实例、桌面分身或已建立的云原神实例，不要求在线。
/// 计划跟随当前账户时可多选（复选框），计划指定账户时只能选一个（单选按钮）。
/// </summary>
public sealed partial class PuloniaTriggerInstanceOptionViewModel : ObservableObject
{
    /// <summary>选择变化后通知所属编辑器，由它处理单选与校验。</summary>
    private readonly Action<PuloniaTriggerInstanceOptionViewModel> _selectionChanged;

    /// <summary>实例键。</summary>
    public string InstanceKey { get; }

    /// <summary>实例显示名，例如“主实例”“云原神 · 小号a”。</summary>
    public string InstanceName { get; }

    /// <summary>是否选为执行实例。</summary>
    [ObservableProperty] private bool _isSelected;

    /// <summary>是否允许多选；计划指定账户时只能选一个执行实例。</summary>
    [ObservableProperty] private bool _isMultiSelect = true;

    /// <summary>
    /// 建立选项。
    /// </summary>
    /// <param name="instanceKey">实例键</param>
    /// <param name="instanceName">实例显示名</param>
    /// <param name="selectionChanged">选择变化的回调</param>
    public PuloniaTriggerInstanceOptionViewModel(string instanceKey, string instanceName,
        Action<PuloniaTriggerInstanceOptionViewModel> selectionChanged)
    {
        InstanceKey = instanceKey;
        InstanceName = instanceName;
        _selectionChanged = selectionChanged;
    }

    /// <summary>选择变化后交给所属编辑器处理。</summary>
    partial void OnIsSelectedChanged(bool value) => _selectionChanged(this);
}
