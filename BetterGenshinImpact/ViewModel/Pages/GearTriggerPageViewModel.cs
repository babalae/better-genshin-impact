using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BetterGenshinImpact.ViewModel.Pages.Component;
using BetterGenshinImpact.Model.Gear.Triggers;
using BetterGenshinImpact.Model;
using System.Linq;
using BetterGenshinImpact.View.Windows.GearTask;
using BetterGenshinImpact.Service.GearTask;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace BetterGenshinImpact.ViewModel.Pages;

public partial class GearTriggerPageViewModel : ViewModel
{
    private readonly ILogger<GearTriggerPageViewModel> _logger;
    private readonly GearTriggerStorageService _storageService;

    [ObservableProperty]
    private ObservableCollection<GearTriggerViewModel> _timedTriggers = new();

    [ObservableProperty]
    private ObservableCollection<GearTriggerViewModel> _hotkeyTriggers = new();

    [ObservableProperty]
    private GearTriggerViewModel? _selectedTrigger;

    [ObservableProperty]
    private GearTaskDefinitionViewModel? _selectedTaskDefinition;
    
    [ObservableProperty]
    private ObservableCollection<TriggerExecutionRecord> _executionHistory = new();

    private readonly ITriggerHistoryService _historyService;
    private readonly LogReaderService _logReaderService;

    public GearTriggerPageViewModel(ILogger<GearTriggerPageViewModel> logger, GearTriggerStorageService storageService, ITriggerHistoryService historyService, LogReaderService logReaderService)
    {
        _logger = logger;
        _storageService = storageService;
        _historyService = historyService;
        _logReaderService = logReaderService;
        
        _historyService.HistoryChanged += OnHistoryChanged;
    }
    
    private void OnHistoryChanged(object? sender, EventArgs e)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(async () =>
        {
            await LoadHistoryAsync();
            UpdateTriggerStatus();
        });
    }
    
    private async Task LoadHistoryAsync()
    {
        try 
        {
            var history = await _historyService.GetHistoryAsync();
            ExecutionHistory.Clear();
            foreach (var record in history)
            {
                ExecutionHistory.Add(record);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "加载历史记录失败");
        }
    }
    
    private void UpdateTriggerStatus()
    {
        var history = ExecutionHistory.ToList();
        
        foreach (var trigger in TimedTriggers)
        {
            var lastRun = history.FirstOrDefault(x => x.TriggerName == trigger.Name);
            if (lastRun != null)
            {
                trigger.LastRunTime = lastRun.StartTime;
                trigger.LastRunStatus = lastRun.Status;
            }
            // 确保更新下次运行时间
            trigger.UpdateNextRunTime();
        }
    }

    public override void OnNavigatedTo()
    {
        _ = LoadTriggersAsync();
        _ = LoadHistoryAsync();
    }

    /// <summary>
    /// 异步加载触发器数据
    /// </summary>
    private async Task LoadTriggersAsync()
    {
        try
        {
            var (timedTriggers, hotkeyTriggers) = await _storageService.LoadTriggersAsync();
            
            TimedTriggers.Clear();
            HotkeyTriggers.Clear();
            
            foreach (var trigger in timedTriggers)
            {
                trigger.UpdateNextRunTime();
                TimedTriggers.Add(trigger);
            }
            
            foreach (var trigger in hotkeyTriggers)
            {
                HotkeyTriggers.Add(trigger);
            }
            
            // 加载完触发器后更新状态
            UpdateTriggerStatus();
            
            _logger.LogInformation("已加载 {TimedCount} 个定时触发器和 {HotkeyCount} 个快捷键触发器", 
                TimedTriggers.Count, HotkeyTriggers.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "加载触发器数据时发生错误");
        }
    }
    
    [RelayCommand]
    private async Task ClearHistory()
    {
        await _historyService.ClearHistoryAsync();
    }

    [RelayCommand]
    private async Task ViewHistoryDetails(TriggerExecutionRecord? record)
    {
        if (record == null) return;
        
        var logContent = "正在加载日志...";
        if (!string.IsNullOrEmpty(record.CorrelationId))
        {
            try
            {
                logContent = await _logReaderService.GetLogsForCorrelationIdAsync(record.CorrelationId, record.StartTime);
            }
            catch (Exception ex)
            {
                logContent = $"读取日志失败: {ex.Message}";
            }
        }
        else
        {
            logContent = string.IsNullOrEmpty(record.LogDetails) ? "无关联的日志记录 (可能是旧版本数据)" : record.LogDetails;
        }

        var message = $"""
            触发器: {record.TriggerName}
            任务: {record.TaskName}
            开始时间: {record.StartTime}
            结束时间: {record.EndTime}
            耗时: {record.Duration.TotalSeconds:F2} 秒
            状态: {record.Status}
            CorrelationId: {record.CorrelationId}
            
            简述: {record.Message}
            
            === 详细日志 ===
            {logContent}
            """;
            
        // 这里可以使用更高级的弹窗，目前先用 MessageBox
        // 为了更好的体验，建议后续改用专门的日志查看窗口
        System.Windows.MessageBox.Show(message, "执行详情");
    }


    /// <summary>
    /// 保存触发器数据
    /// </summary>
    private async Task SaveTriggersAsync()
    {
        try
        {
            await _storageService.SaveTriggersAsync(TimedTriggers, HotkeyTriggers);
            _logger.LogInformation("触发器数据已保存");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "保存触发器数据时发生错误");
        }
    }


    [RelayCommand]
    private void AddTimedTrigger()
    {
        var dialog = AddTriggerDialog.ShowAddTriggerDialog(TriggerType.Timed);
        if (dialog != null)
        {
            var newTrigger = new GearTriggerViewModel(dialog.TriggerName, TriggerType.Timed)
            {
                CronExpression = dialog.CronExpression,
                TaskDefinitionName = dialog.SelectedTaskDefinitionName,
                IsEnabled = true
            };
            TimedTriggers.Add(newTrigger);
            SelectedTrigger = newTrigger;
            
            // 保存数据
            _ = SaveTriggersAsync();
        }
    }

    [RelayCommand]
    private void AddHotkeyTrigger()
    {
        var dialog = AddTriggerDialog.ShowAddTriggerDialog(TriggerType.Hotkey);
        if (dialog != null)
        {
            var newTrigger = new GearTriggerViewModel(dialog.TriggerName, TriggerType.Hotkey)
            {
                Hotkey = dialog.SelectedHotkey,
                TaskDefinitionName = dialog.SelectedTaskDefinitionName,
                IsEnabled = true
            };
            HotkeyTriggers.Add(newTrigger);
            SelectedTrigger = newTrigger;
            
            // 保存数据
            _ = SaveTriggersAsync();
        }
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
        
        // 保存数据
        _ = SaveTriggersAsync();
    }
}