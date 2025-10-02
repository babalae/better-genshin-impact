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
    private GearTaskDefinitionViewModel? _selectedTaskDefinition;

    public GearTriggerPageViewModel()
    {
    }

    public override void OnNavigatedTo()
    {
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
}