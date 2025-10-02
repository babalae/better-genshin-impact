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

    public GearTriggerPageViewModel(ILogger<GearTriggerPageViewModel> logger, GearTriggerStorageService storageService)
    {
        _logger = logger;
        _storageService = storageService;
    }

    public override void OnNavigatedTo()
    {
        _ = LoadTriggersAsync();
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
                TimedTriggers.Add(trigger);
            }
            
            foreach (var trigger in hotkeyTriggers)
            {
                HotkeyTriggers.Add(trigger);
            }
            
            _logger.LogInformation("已加载 {TimedCount} 个定时触发器和 {HotkeyCount} 个快捷键触发器", 
                TimedTriggers.Count, HotkeyTriggers.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "加载触发器数据时发生错误");
        }
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