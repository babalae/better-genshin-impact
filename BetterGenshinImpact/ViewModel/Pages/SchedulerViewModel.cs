using System;
using System.Collections.ObjectModel;
using BetterGenshinImpact.Model;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BetterGenshinImpact.ViewModel.Pages;

public partial class SchedulerViewModel : ViewModel
{
    // 顺序触发相关属性
    [ObservableProperty] private bool _isSequentialEnabled;

    [ObservableProperty] private double _sequentialInterval = 5;

    // 定时触发相关属性
    [ObservableProperty] private bool _isScheduledEnabled;

    [ObservableProperty] private DateTime _scheduledTime = DateTime.Now;

    [ObservableProperty] private bool _isRepeatDaily;

    [ObservableProperty] private string _nextScheduledRunText = "下次执行: 未设置";

    // 热键触发相关属性
    [ObservableProperty] private bool _isHotkeyEnabled;

    [ObservableProperty] private string _hotkeyString = "";

    // 任务列表
    [ObservableProperty] private ObservableCollection<SchedulerTask> _tasks = new();
    
    [ObservableProperty] private ObservableCollection<OneDragonTaskItem> _taskList =
    [
        new("领取邮件"),
        new("合成树脂"),
        // new ("每日委托"),
        new("自动秘境"),
    ];


    [ObservableProperty] private OneDragonTaskItem? _selectedTask;
    
    [ObservableProperty] private ObservableCollection<OneDragonTaskItem> _taskList2 =
    [
        new("领取邮件"),
        new("合成树脂"),
        // new ("每日委托"),
        new("自动秘境"),
    ];

    // 命令
    [RelayCommand]
    private void StartSequential()
    {
        // 实现顺序触发逻辑
    }

    [RelayCommand]
    private void StartScheduled()
    {
        // 实现定时触发逻辑
    }

    [RelayCommand]
    private void StartHotkey()
    {
        // 实现热键触发逻辑
    }

    [RelayCommand]
    private void AddTask()
    {
        // 实现添加任务逻辑
    }

    [RelayCommand]
    private void EditTask(SchedulerTask task)
    {
        // 实现编辑任务逻辑
    }

    [RelayCommand]
    private void DuplicateTask(SchedulerTask task)
    {
        // 实现复制任务逻辑
    }

    [RelayCommand]
    private void DeleteTask(SchedulerTask task)
    {
        // 实现删除任务逻辑
    }

    [RelayCommand]
    private void ImportTasks()
    {
        // 实现导入任务逻辑
    }

    [RelayCommand]
    private void ExportTasks()
    {
        // 实现导出任务逻辑
    }

    [RelayCommand]
    private void Settings()
    {
        // 实现全局设置逻辑
    }
}

// 任务模型
public partial class SchedulerTask : ObservableObject
{
    public int Index { get; set; }

    [ObservableProperty] private string _name;

    [ObservableProperty] private string _typeDescription;

    [ObservableProperty] private bool _isEnabled = true;

    // 其他任务属性
}