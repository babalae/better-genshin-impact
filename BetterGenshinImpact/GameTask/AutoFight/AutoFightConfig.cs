using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace BetterGenshinImpact.GameTask.AutoFight;

/// <summary>
/// 自动战斗配置
/// </summary>
[Serializable]
public partial class AutoFightConfig : ObservableObject
{
    [ObservableProperty] private string _strategyName = "";

    /// <summary>
    /// 英文逗号分割 强制指定队伍角色
    /// </summary>
    [ObservableProperty] private string _teamNames = "";
}