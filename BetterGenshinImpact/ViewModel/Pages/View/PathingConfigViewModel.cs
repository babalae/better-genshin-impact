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
    public AllConfig Config { get; }

    /// <summary>
    /// 创建地图追踪设置 ViewModel（DI 单例）
    /// </summary>
    /// <param name="config">配置根（DI 注入）</param>
    public PathingConfigViewModel(AllConfig config)
    {
        Config = config;
    }

    [ObservableProperty]
    private ObservableCollection<KeyValuePair<RecoverTiming, string>> _recoverTimingSource = new()
    {
        new KeyValuePair<RecoverTiming, string>(RecoverTiming.AnyWaypoint, "任何路径点"),
        new KeyValuePair<RecoverTiming, string>(RecoverTiming.OnlyTeleport, "只在传送点"),
        new KeyValuePair<RecoverTiming, string>(RecoverTiming.Never, "不回复"),
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
        Config.OnAnyChangedAction?.Invoke();
    }
}
