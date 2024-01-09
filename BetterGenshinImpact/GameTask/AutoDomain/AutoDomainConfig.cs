using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace BetterGenshinImpact.GameTask.AutoDomain;

[Serializable]
public partial class AutoDomainConfig : ObservableObject
{

    /// <summary>
    /// 战斗结束后延迟几秒再开始寻找石化古树
    /// </summary>
    [ObservableProperty] private int _fightEndDelay = 5000;
}