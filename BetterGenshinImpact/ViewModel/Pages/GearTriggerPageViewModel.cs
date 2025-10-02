using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BetterGenshinImpact.ViewModel.Pages.Component;
using BetterGenshinImpact.Model.Gear.Triggers;
using BetterGenshinImpact.Model;
using System.Linq;

namespace BetterGenshinImpact.ViewModel.Pages;

public partial class GearTriggerPageViewModel : ViewModel
{
    [ObservableProperty]
    private ObservableCollection<GearTriggerViewModel> _timedTriggers = new();

    [ObservableProperty]
    private ObservableCollection<GearTriggerViewModel> _hotkeyTriggers = new();

    [ObservableProperty]
    private GearTriggerViewModel? _selectedTrigger;

    [ObservableProperty]
    private ObservableCollection<GearTaskDefinitionViewModel> _availableTaskDefinitions = new();

    [ObservableProperty]
    private GearTaskDefinitionViewModel? _selectedTaskDefinition;

    [ObservableProperty]
    private ObservableCollection<GearTaskViewModel> _taskReferences = new();

    public GearTriggerPageViewModel()
    {
    }
    
    public override void OnNavigatedTo()
    {
        InitializeExampleData();
    }

    private void InitializeExampleData()
    {
        // 添加定时触发器示例数据
        TimedTriggers.Add(new GearTriggerViewModel("每日签到", TriggerType.Timed)
        {
            IsEnabled = true,
            TaskDefinitionName = "自动签到任务"
        });

        TimedTriggers.Add(new GearTriggerViewModel("周常清理", TriggerType.Timed)
        {
            IsEnabled = false,
            TaskDefinitionName = "背包清理任务"
        });

        // 添加快捷键触发器示例数据
        HotkeyTriggers.Add(new GearTriggerViewModel("快速战斗", TriggerType.Hotkey)
        {
            IsEnabled = true,
            TaskDefinitionName = "自动战斗任务",
            Hotkey = new HotKey { Key = System.Windows.Input.Key.F1 }
        });

        HotkeyTriggers.Add(new GearTriggerViewModel("快速采集", TriggerType.Hotkey)
        {
            IsEnabled = true,
            TaskDefinitionName = "自动采集任务",
            Hotkey = new HotKey { Key = System.Windows.Input.Key.F2 }
        });

        // 添加示例任务定义
        AvailableTaskDefinitions.Add(new GearTaskDefinitionViewModel("自动签到任务", "每日自动签到"));
        AvailableTaskDefinitions.Add(new GearTaskDefinitionViewModel("背包清理任务", "清理背包中的无用物品"));
        AvailableTaskDefinitions.Add(new GearTaskDefinitionViewModel("自动战斗任务", "自动进行战斗"));
        AvailableTaskDefinitions.Add(new GearTaskDefinitionViewModel("自动采集任务", "自动采集周围的物品"));
    }

    [RelayCommand]
    private void AddTimedTrigger()
    {
        var newTrigger = new GearTriggerViewModel($"定时触发器 {TimedTriggers.Count + 1}", TriggerType.Timed);
        TimedTriggers.Add(newTrigger);
        SelectedTrigger = newTrigger;
    }

    [RelayCommand]
    private void AddHotkeyTrigger()
    {
        var newTrigger = new GearTriggerViewModel($"快捷键触发器 {HotkeyTriggers.Count + 1}", TriggerType.Hotkey);
        HotkeyTriggers.Add(newTrigger);
        SelectedTrigger = newTrigger;
    }

    [RelayCommand]
    private void DeleteTrigger()
    {
        if (SelectedTrigger == null) return;

        switch (SelectedTrigger.TriggerType)
        {
            case TriggerType.Timed:
                TimedTriggers.Remove(SelectedTrigger);
                break;
            case TriggerType.Hotkey:
                HotkeyTriggers.Remove(SelectedTrigger);
                break;
        }

        SelectedTrigger = null;
    }

    [RelayCommand]
    private void SetTaskDefinition()
    {
        if (SelectedTrigger == null || SelectedTaskDefinition == null) return;

        SelectedTrigger.TaskDefinitionName = SelectedTaskDefinition.Name;
        UpdateTaskReferences();
    }

    [RelayCommand]
    private void ClearTaskDefinition()
    {
        if (SelectedTrigger == null) return;

        SelectedTrigger.TaskDefinitionName = string.Empty;
        TaskReferences.Clear();
    }

    partial void OnSelectedTriggerChanged(GearTriggerViewModel? value)
    {
        UpdateTaskReferences();
    }

    private void UpdateTaskReferences()
    {
        TaskReferences.Clear();

        if (SelectedTrigger == null || string.IsNullOrEmpty(SelectedTrigger.TaskDefinitionName))
            return;

        var taskDefinition = AvailableTaskDefinitions.FirstOrDefault(t => t.Name == SelectedTrigger.TaskDefinitionName);
        if (taskDefinition?.RootTask != null)
        {
            AddTaskReferences(taskDefinition.RootTask);
        }
    }

    private void AddTaskReferences(GearTaskViewModel task)
    {
        TaskReferences.Add(task);
        foreach (var child in task.Children)
        {
            AddTaskReferences(child);
        }
    }

    [RelayCommand]
    private void SaveConfiguration()
    {
        // TODO: 实现保存配置逻辑
    }

    [RelayCommand]
    private void LoadConfiguration()
    {
        // TODO: 实现加载配置逻辑
    }
}