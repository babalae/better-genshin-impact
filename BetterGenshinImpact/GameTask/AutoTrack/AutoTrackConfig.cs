using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace BetterGenshinImpact.GameTask.AutoTrack;

/// <summary>
/// 自动跟踪任务配置
/// </summary>
[Serializable]
public partial class AutoTrackConfig : ObservableObject
{
    /// <summary>
    /// 启用智能路径规划
    /// </summary>
    [ObservableProperty]
    private bool _enableSmartPathfinding = true;

    /// <summary>
    /// 自动切换队伍
    /// </summary>
    [ObservableProperty]
    private bool _enableAutoTeamSwitch = false;

    /// <summary>
    /// 跳过对话
    /// </summary>
    [ObservableProperty]
    private bool _skipDialogue = true;

    /// <summary>
    /// 选中的队伍配置名称
    /// </summary>
    [ObservableProperty]
    private string _selectedTeamConfiguration = "默认队伍";
}