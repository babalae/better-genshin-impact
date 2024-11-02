using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.Model;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.ComponentModel;

namespace BetterGenshinImpact.ViewModel.Pages.View;

public partial class PathingConfigViewModel : ObservableObject, IViewModel
{
    public AllConfig Config { get; }

    public PathingConfigViewModel()
    {
        Config = TaskContext.Instance().Config;
        // 初始化默认条件配置
        var conditions = TaskContext.Instance().Config.PathingConditionConfig.AvatarConditions;
        if (conditions.Count == 0)
        {
            conditions.Add(new Condition
            {
                Subject = "队伍中角色",
                Object = ["绮良良", "莱依拉", "芭芭拉", "七七"],
                Result = "循环短E"
            });
            conditions.Add(new Condition
            {
                Subject = "队伍中角色",
                Object = ["钟离"],
                Result = "循环长E"
            });
        }
    }

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
