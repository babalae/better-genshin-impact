using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using BetterGenshinImpact.Model.Gear.Triggers;
using BetterGenshinImpact.Model.Gear.Tasks;
using BetterGenshinImpact.Model;

namespace BetterGenshinImpact.ViewModel.Pages.Component;

/// <summary>
/// 触发器ViewModel
/// </summary>
public partial class GearTriggerViewModel : ObservableObject
{
    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private bool _isEnabled = true;

    [ObservableProperty]
    private bool _isSelected = false;
    
    [ObservableProperty]
    private string? _cronExpression;

    [ObservableProperty]
    private DateTime _createdTime = DateTime.Now;

    [ObservableProperty]
    private DateTime _modifiedTime = DateTime.Now;

    /// <summary>
    /// 触发器类型
    /// </summary>
    [ObservableProperty]
    private TriggerType _triggerType = TriggerType.Timed;

    /// <summary>
    /// 任务引用名称
    /// </summary>
    [ObservableProperty]
    private string _taskDefinitionName = string.Empty;
    

    // 热键触发器属性
    [ObservableProperty]
    private HotKey? _hotkey;

    public GearTriggerViewModel()
    {
    }

    public GearTriggerViewModel(string name, TriggerType triggerType)
    {
        Name = name;
        TriggerType = triggerType;
    }
    
    /// <summary>
    /// 获取触发器类型显示名称
    /// </summary>
    public string TriggerTypeDisplayName => TriggerType switch
    {
        TriggerType.Timed => "定时触发",
        TriggerType.Hotkey => "热键触发",
        _ => "未知类型"
    };

    /// <summary>
    /// 获取热键显示文本
    /// </summary>
    public string HotkeyDisplayText => Hotkey?.ToString() ?? "未设置";
    

    /// <summary>
    /// 转换为对应的触发器实例
    /// </summary>
    public GearBaseTrigger ToTrigger()
    {
        GearBaseTrigger trigger = TriggerType switch
        {
            TriggerType.Timed => new QuartzCronGearTrigger
            {
                CronExpression = CronExpression
            },
            TriggerType.Hotkey => new HotkeyGearTrigger
            {
                Hotkey = Hotkey
            },
            _ => throw new ArgumentOutOfRangeException()
        };

        trigger.Name = Name;
        trigger.IsEnabled = IsEnabled;
        trigger.TaskDefinitionName = TaskDefinitionName;
        
        return trigger;
    }
}

/// <summary>
/// 触发器类型枚举
/// </summary>
public enum TriggerType
{
    /// <summary>
    /// 定时触发
    /// </summary>
    Timed,
    
    /// <summary>
    /// 热键触发
    /// </summary>
    Hotkey
}