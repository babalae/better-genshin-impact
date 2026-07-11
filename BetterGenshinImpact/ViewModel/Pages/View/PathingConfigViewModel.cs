using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.Model;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace BetterGenshinImpact.ViewModel.Pages.View;

public partial class PathingConfigViewModel : ObservableObject, IViewModel
{
    public AllConfig Config { get; } = TaskContext.Instance().Config;

    [ObservableProperty]
    private ObservableCollection<KeyValuePair<string, RecoverTiming>> _recoverTimingSource = new()
    {
        new KeyValuePair<string, RecoverTiming>("任何路径点", RecoverTiming.AnyWaypoint),
        new KeyValuePair<string, RecoverTiming>("只在传送点", RecoverTiming.OnlyTeleport),
        new KeyValuePair<string, RecoverTiming>("不回复", RecoverTiming.Never),
    };

    [RelayCommand]
    public void OnAddPartyConditionConfig()
    {
        Config.PathingConditionConfig.PartyConditions.Add(new Condition());
    }

    [RelayCommand]
    public void OnRemovePartyConditionConfig(object? item)
    {
        if (item is Condition condition)
        {
            Config.PathingConditionConfig.PartyConditions.Remove(condition);
        }
    }

    [RelayCommand]
    public void OnAddAvatarConditionConfig()
    {
        Config.PathingConditionConfig.AvatarConditions.Add(new Condition
        {
            Subject = "队伍中角色"
        });
    }

    [RelayCommand]
    public void OnRemoveAvatarConditionConfig(object? item)
    {
        if (item is Condition condition)
        {
            Config.PathingConditionConfig.AvatarConditions.Remove(condition);
        }
    }

    [RelayCommand]
    private void OnClosing(CancelEventArgs e)
    {
        TaskContext.Instance().Config.OnAnyChangedAction?.Invoke();
    }
}
