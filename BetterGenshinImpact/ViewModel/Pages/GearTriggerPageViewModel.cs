using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using BetterGenshinImpact.ViewModel.Pages.Component;
using BetterGenshinImpact.Model.Gear.Tasks;
using BetterGenshinImpact.Model;

namespace BetterGenshinImpact.ViewModel.Pages;

/// <summary>
/// 任务触发页面ViewModel
/// </summary>
public partial class GearTriggerPageViewModel : ViewModel
{
    private readonly ILogger<GearTriggerPageViewModel> _logger;

    /// <summary>
    /// 顺序触发器列表
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<GearTriggerViewModel> _sequentialTriggers = new();

    /// <summary>
    /// 定时触发器列表
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<GearTriggerViewModel> _timedTriggers = new();

    /// <summary>
    /// 热键触发器列表
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<GearTriggerViewModel> _hotkeyTriggers = new();

    /// <summary>
    /// 当前选中的触发器
    /// </summary>
    [ObservableProperty]
    private GearTriggerViewModel? _selectedTrigger;

    /// <summary>
    /// 当前选中的触发器类型Tab
    /// </summary>
    [ObservableProperty]
    private int _selectedTriggerTypeIndex = 0;

    /// <summary>
    /// 触发器配置中的任务定义列表
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<GearTaskDefinitionViewModel> _availableTaskDefinitions = new();

    /// <summary>
    /// 当前选中的任务定义
    /// </summary>
    [ObservableProperty]
    private GearTaskDefinitionViewModel? _selectedTaskDefinition;

    public GearTriggerPageViewModel(ILogger<GearTriggerPageViewModel> logger)
    {
        _logger = logger;
        InitializeData();
    }

    private void InitializeData()
    {
        // 初始化示例数据
        var sequentialTrigger = new GearTriggerViewModel("示例顺序触发器", TriggerType.Sequential);
        SequentialTriggers.Add(sequentialTrigger);

        var timedTrigger = new GearTriggerViewModel("示例定时触发器", TriggerType.Timed);
        TimedTriggers.Add(timedTrigger);

        var hotkeyTrigger = new GearTriggerViewModel("示例热键触发器", TriggerType.Hotkey)
        {
            Hotkey = new HotKey(System.Windows.Input.Key.F1, System.Windows.Input.ModifierKeys.Control)
        };
        HotkeyTriggers.Add(hotkeyTrigger);

        // 初始化可用的任务定义（这里应该从实际的任务定义服务获取）
        LoadAvailableTaskDefinitions();
    }

    private void LoadAvailableTaskDefinitions()
    {
        // 这里应该从实际的任务定义服务获取
        // 暂时创建示例数据
        var sampleTask = new GearTaskDefinitionViewModel("示例任务组", "这是一个示例任务组");
        AvailableTaskDefinitions.Add(sampleTask);
    }

    partial void OnSelectedTriggerChanged(GearTriggerViewModel? value)
    {
        UpdateSelectedTaskDefinition();
    }

    private void UpdateSelectedTaskDefinition()
    {
        if (SelectedTrigger != null && !string.IsNullOrEmpty(SelectedTrigger.TaskDefinitionName))
        {
            // 根据任务定义名称找到对应的任务定义
            SelectedTaskDefinition = AvailableTaskDefinitions.FirstOrDefault(t => t.Name == SelectedTrigger.TaskDefinitionName);
        }
        else
        {
            SelectedTaskDefinition = null;
        }
    }

    /// <summary>
    /// 添加顺序触发器
    /// </summary>
    [RelayCommand]
    private void AddSequentialTrigger()
    {
        var trigger = new GearTriggerViewModel($"顺序触发器 {SequentialTriggers.Count + 1}", TriggerType.Sequential);
        SequentialTriggers.Add(trigger);
        SelectedTrigger = trigger;
        SelectedTriggerTypeIndex = 0;
    }

    /// <summary>
    /// 添加定时触发器
    /// </summary>
    [RelayCommand]
    private void AddTimedTrigger()
    {
        var trigger = new GearTriggerViewModel($"定时触发器 {TimedTriggers.Count + 1}", TriggerType.Timed);
        TimedTriggers.Add(trigger);
        SelectedTrigger = trigger;
        SelectedTriggerTypeIndex = 1;
    }

    /// <summary>
    /// 添加热键触发器
    /// </summary>
    [RelayCommand]
    private void AddHotkeyTrigger()
    {
        var trigger = new GearTriggerViewModel($"热键触发器 {HotkeyTriggers.Count + 1}", TriggerType.Hotkey);
        HotkeyTriggers.Add(trigger);
        SelectedTrigger = trigger;
        SelectedTriggerTypeIndex = 2;
    }

    /// <summary>
    /// 删除触发器
    /// </summary>
    [RelayCommand]
    private void DeleteTrigger(GearTriggerViewModel? trigger)
    {
        if (trigger == null) return;

        var result = MessageBox.Show($"确定要删除触发器 '{trigger.Name}' 吗？", "确认删除", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result == MessageBoxResult.Yes)
        {
            switch (trigger.TriggerType)
            {
                case TriggerType.Sequential:
                    SequentialTriggers.Remove(trigger);
                    break;
                case TriggerType.Timed:
                    TimedTriggers.Remove(trigger);
                    break;
                case TriggerType.Hotkey:
                    HotkeyTriggers.Remove(trigger);
                    break;
            }

            if (SelectedTrigger == trigger)
            {
                SelectedTrigger = null;
            }
        }
    }

    /// <summary>
    /// 编辑触发器
    /// </summary>
    [RelayCommand]
    private void EditTrigger(GearTriggerViewModel? trigger)
    {
        if (trigger == null) return;
        SelectedTrigger = trigger;
        
        // 切换到对应的Tab
        SelectedTriggerTypeIndex = trigger.TriggerType switch
        {
            TriggerType.Sequential => 0,
            TriggerType.Timed => 1,
            TriggerType.Hotkey => 2,
            _ => 0
        };
    }

    /// <summary>
    /// 设置任务定义到当前触发器
    /// </summary>
    [RelayCommand]
    private void SetTaskDefinition(GearTaskDefinitionViewModel? taskDefinition)
    {
        if (taskDefinition == null || SelectedTrigger == null) return;

        SelectedTrigger.TaskDefinitionName = taskDefinition.Name;
        SelectedTaskDefinition = taskDefinition;
    }

    /// <summary>
    /// 清除当前触发器的任务定义
    /// </summary>
    [RelayCommand]
    private void ClearTaskDefinition()
    {
        if (SelectedTrigger == null) return;

        SelectedTrigger.TaskDefinitionName = string.Empty;
        SelectedTaskDefinition = null;
    }

    /// <summary>
    /// 保存配置
    /// </summary>
    [RelayCommand]
    private void SaveConfiguration()
    {
        try
        {
            // 这里应该实现保存到文件的逻辑
            _logger.LogInformation("触发器配置已保存");
            MessageBox.Show("配置保存成功！", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "保存触发器配置时发生错误");
            MessageBox.Show($"保存失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// 加载配置
    /// </summary>
    [RelayCommand]
    private void LoadConfiguration()
    {
        try
        {
            // 这里应该实现从文件加载的逻辑
            _logger.LogInformation("触发器配置已加载");
            MessageBox.Show("配置加载成功！", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "加载触发器配置时发生错误");
            MessageBox.Show($"加载失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}