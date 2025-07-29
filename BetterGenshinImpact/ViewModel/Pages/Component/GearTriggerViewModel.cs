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
    private string _description = string.Empty;

    [ObservableProperty]
    private bool _isEnabled = true;

    [ObservableProperty]
    private bool _isSelected = false;

    [ObservableProperty]
    private DateTime _createdTime = DateTime.Now;

    [ObservableProperty]
    private DateTime _modifiedTime = DateTime.Now;

    /// <summary>
    /// 触发器类型
    /// </summary>
    [ObservableProperty]
    private TriggerType _triggerType = TriggerType.Sequential;

    /// <summary>
    /// 任务引用列表
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<GearTaskRefence> _taskReferences = new();

    // 顺序触发器属性（无额外属性）

    // 定时触发器属性
    [ObservableProperty]
    private int _intervalMs = 5000;

    [ObservableProperty]
    private bool _isRepeating = true;

    [ObservableProperty]
    private int _delayMs = 0;

    [ObservableProperty]
    private int _maxExecutions = 0;

    // 热键触发器属性
    [ObservableProperty]
    private HotKey? _hotkey;

    [ObservableProperty]
    private int _cooldownMs = 1000;

    public GearTriggerViewModel()
    {
    }

    public GearTriggerViewModel(string name, TriggerType triggerType)
    {
        Name = name;
        TriggerType = triggerType;
        Description = GetDefaultDescription(triggerType);
    }

    /// <summary>
    /// 获取触发器类型的默认描述
    /// </summary>
    private string GetDefaultDescription(TriggerType triggerType)
    {
        return triggerType switch
        {
            TriggerType.Sequential => "按顺序执行任务",
            TriggerType.Timed => "定时执行任务",
            TriggerType.Hotkey => "热键触发执行任务",
            _ => "未知触发器类型"
        };
    }

    /// <summary>
    /// 获取触发器类型显示名称
    /// </summary>
    public string TriggerTypeDisplayName => TriggerType switch
    {
        TriggerType.Sequential => "顺序触发",
        TriggerType.Timed => "定时触发",
        TriggerType.Hotkey => "热键触发",
        _ => "未知类型"
    };

    /// <summary>
    /// 获取热键显示文本
    /// </summary>
    public string HotkeyDisplayText => Hotkey?.ToString() ?? "未设置";

    /// <summary>
    /// 获取定时器配置显示文本
    /// </summary>
    public string TimerDisplayText
    {
        get
        {
            if (TriggerType != TriggerType.Timed) return "";
            
            var text = $"间隔: {IntervalMs}ms";
            if (DelayMs > 0) text += $", 延迟: {DelayMs}ms";
            if (MaxExecutions > 0) text += $", 最大次数: {MaxExecutions}";
            if (!IsRepeating) text += ", 单次执行";
            
            return text;
        }
    }

    /// <summary>
    /// 转换为对应的触发器实例
    /// </summary>
    public GearBaseTrigger ToTrigger()
    {
        GearBaseTrigger trigger = TriggerType switch
        {
            TriggerType.Sequential => new SequentialGearTrigger(),
            TriggerType.Timed => new TimedGearTrigger
            {
                IntervalMs = IntervalMs,
                IsRepeating = IsRepeating,
                DelayMs = DelayMs,
                MaxExecutions = MaxExecutions
            },
            TriggerType.Hotkey => new HotkeyGearTrigger
            {
                Hotkey = Hotkey,
                CooldownMs = CooldownMs,
                IsEnabled = IsEnabled
            },
            _ => new SequentialGearTrigger()
        };

        trigger.Name = Name;
        trigger.GearTaskRefenceList = new ObservableCollection<GearTaskRefence>(TaskReferences);
        
        return trigger;
    }
}

/// <summary>
/// 触发器类型枚举
/// </summary>
public enum TriggerType
{
    /// <summary>
    /// 顺序触发
    /// </summary>
    Sequential,
    
    /// <summary>
    /// 定时触发
    /// </summary>
    Timed,
    
    /// <summary>
    /// 热键触发
    /// </summary>
    Hotkey
}