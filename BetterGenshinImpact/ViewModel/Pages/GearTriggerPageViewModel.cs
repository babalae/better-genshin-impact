using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BetterGenshinImpact.Model.Gear.Triggers;
using BetterGenshinImpact.Service;
using BetterGenshinImpact.Service.GearTask;
using BetterGenshinImpact.View.Windows;
using BetterGenshinImpact.View.Windows.GearTask;
using BetterGenshinImpact.ViewModel.Pages.Component;
using Microsoft.Extensions.Logging;
using Quartz;

namespace BetterGenshinImpact.ViewModel.Pages;

public partial class GearTriggerPageViewModel : ViewModel
{
    private readonly ILogger<GearTriggerPageViewModel> _logger;
    private readonly GearTriggerStorageService _storageService;
    private readonly QuartzSchedulerService _quartzSchedulerService;

    [ObservableProperty]
    private ObservableCollection<GearTriggerViewModel> _timedTriggers = new();

    [ObservableProperty]
    private ObservableCollection<GearTriggerViewModel> _hotkeyTriggers = new();

    public GearTriggerPageViewModel(
        ILogger<GearTriggerPageViewModel> logger,
        GearTriggerStorageService storageService,
        QuartzSchedulerService quartzSchedulerService)
    {
        _logger = logger;
        _storageService = storageService;
        _quartzSchedulerService = quartzSchedulerService;
    }

    public override void OnNavigatedTo()
    {
        _ = LoadTriggersAsync();
    }

    private void UpdateTimedTriggersNextRunTime()
    {
        foreach (var trigger in TimedTriggers)
        {
            trigger.UpdateNextRunTime();
        }
    }

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

            UpdateTimedTriggersNextRunTime();
            _logger.LogInformation("已加载 {TimedCount} 个定时触发器和 {HotkeyCount} 个热键触发器", TimedTriggers.Count, HotkeyTriggers.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "加载触发器数据时发生错误");
        }
    }

    private async Task<bool> SaveTriggersAsync()
    {
        if (!ValidateTimedTriggers())
        {
            return false;
        }

        try
        {
            await _storageService.SaveTriggersAsync(TimedTriggers, HotkeyTriggers);
            await _quartzSchedulerService.SyncTimedTriggersAsync(TimedTriggers);
            _logger.LogInformation("触发器数据已保存，并已同步到 Quartz 调度器");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "保存触发器数据时发生错误");
            ThemedMessageBox.Error($"保存触发器失败：{ex.Message}", "保存失败");
            return false;
        }
    }

    private bool ValidateTimedTriggers()
    {
        var invalidTrigger = TimedTriggers.FirstOrDefault(t =>
            t.TriggerType == TriggerType.Timed &&
            t.IsEnabled &&
            !string.IsNullOrWhiteSpace(t.CronExpression) &&
            !IsValidCronExpression(t.CronExpression, out _));

        if (invalidTrigger == null)
        {
            return true;
        }

        IsValidCronExpression(invalidTrigger.CronExpression, out var errorMessage);
        ThemedMessageBox.Error($"触发器“{invalidTrigger.Name}”的 Cron 表达式无效。\n{errorMessage}", "保存失败");
        return false;
    }

    private static bool IsValidCronExpression(string? cronExpression, out string errorMessage)
    {
        errorMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(cronExpression))
        {
            errorMessage = "Cron 表达式不能为空";
            return false;
        }

        try
        {
            _ = new CronExpression(cronExpression);
            return true;
        }
        catch (FormatException ex)
        {
            errorMessage = ex.Message;
            return false;
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            return false;
        }
    }

    [RelayCommand]
    private async Task AddTimedTrigger()
    {
        var dialog = AddTriggerDialog.ShowAddTriggerDialog(TriggerType.Timed);
        if (dialog == null)
        {
            return;
        }

        var newTrigger = new GearTriggerViewModel(dialog.TriggerName, TriggerType.Timed)
        {
            CronExpression = dialog.CronExpression,
            TaskDefinitionName = dialog.SelectedTaskDefinitionName,
            IsEnabled = dialog.IsEnabled
        };

        TimedTriggers.Add(newTrigger);
        newTrigger.UpdateNextRunTime();

        await SaveTriggersAsync();
    }

    [RelayCommand]
    private async Task AddHotkeyTrigger()
    {
        var dialog = AddTriggerDialog.ShowAddTriggerDialog(TriggerType.Hotkey);
        if (dialog == null)
        {
            return;
        }

        var newTrigger = new GearTriggerViewModel(dialog.TriggerName, TriggerType.Hotkey)
        {
            Hotkey = dialog.SelectedHotkey,
            HotkeyType = dialog.HotkeyType,
            TaskDefinitionName = dialog.SelectedTaskDefinitionName,
            IsEnabled = dialog.IsEnabled
        };

        HotkeyTriggers.Add(newTrigger);

        await SaveTriggersAsync();
    }

    [RelayCommand]
    private async Task DeleteTriggerItem(GearTriggerViewModel? trigger)
    {
        if (trigger == null)
        {
            return;
        }

        var triggerTypeName = trigger.TriggerType == TriggerType.Timed ? "定时触发器" : "快捷键触发器";
        var result = await ThemedMessageBox.ShowAsync(
            $"确定要删除{triggerTypeName}「{trigger.Name}」吗？",
            "确认删除",
            System.Windows.MessageBoxButton.YesNo,
            ThemedMessageBox.MessageBoxIcon.Question);
        if (result != System.Windows.MessageBoxResult.Yes)
        {
            return;
        }

        switch (trigger.TriggerType)
        {
            case TriggerType.Timed:
                TimedTriggers.Remove(trigger);
                break;
            case TriggerType.Hotkey:
                HotkeyTriggers.Remove(trigger);
                break;
        }

        await SaveTriggersAsync();
    }

    [RelayCommand]
    private async Task EditTriggerItem(GearTriggerViewModel? trigger)
    {
        if (trigger == null)
        {
            return;
        }

        var dialog = AddTriggerDialog.ShowEditTriggerDialog(trigger);
        if (dialog == null)
        {
            return;
        }

        trigger.Name = dialog.TriggerName;
        trigger.IsEnabled = dialog.IsEnabled;
        trigger.TaskDefinitionName = dialog.SelectedTaskDefinitionName;

        if (trigger.TriggerType == TriggerType.Timed)
        {
            trigger.CronExpression = dialog.CronExpression;
            trigger.Hotkey = null;
        }
        else if (trigger.TriggerType == TriggerType.Hotkey)
        {
            trigger.Hotkey = dialog.SelectedHotkey;
            trigger.HotkeyType = dialog.HotkeyType;
            trigger.CronExpression = null;
        }

        trigger.ModifiedTime = DateTime.Now;
        trigger.UpdateNextRunTime();

        await SaveTriggersAsync();
    }
}
