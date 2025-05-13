using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterGenshinImpact.Core.Config;

[Serializable]
public partial class PathingPartyTaskCycleConfig : ObservableObject
{
    //启用执行周期配置
    [ObservableProperty]
    private bool _enable = false;
    
    //周期分界时间点
    [ObservableProperty]
    private int _boundaryTime = 0;
    
    //不同材料有不同的周期，按需配置，如矿石类是3、突破材料是2，或按照自已想几天执行一次配置即可
    [ObservableProperty]
    private int _cycle = 1;
    
    
    
    //执行周期序号，按时间戳对应的天数，对周期求余值加1，得出的值和配置执一致就会执行，否则跳过任务。
    [ObservableProperty]
    private int _index = 1;
    
}