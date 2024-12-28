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

    /// <summary>
    /// 检测战斗结束
    /// </summary>
    [ObservableProperty]
    private bool _fightFinishDetectEnabled = true;

    /// <summary>
    /// 检测战斗结束
    /// </summary>
    [ObservableProperty]
    private bool _pickDropsAfterFightEnabled = true;

    /// <summary>
    /// 战斗超时，单位秒
    /// </summary>
    [ObservableProperty]
    private int _timeout = 120;

    /// <summary>
    /// 判断战斗结束读条颜色，不同帧率可能下会有些不同，默认为95,235,255
    /// </summary>
    [ObservableProperty]
    private string _battleEndProgressBarColor = "";

    /// <summary>
    /// 对于上方颜色地偏差值，即±某个值，例如 6或6,6,6，前者表示所有偏差值都一样，后者则可以分别设置
    /// </summary>
    [ObservableProperty]
    private string _battleEndProgressBarColorTolerance = "";
}
