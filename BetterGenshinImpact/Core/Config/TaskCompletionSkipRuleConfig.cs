using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterGenshinImpact.Core.Config;

public partial class TaskCompletionSkipRuleConfig:ObservableObject
{
    //启用跳过完成任务
    [ObservableProperty]
    private bool _enable = false;
    
    //跳过策略
    //GroupPhysicalPathSkipPolicy:  配置组且物理路径相同跳过
    //PhysicalPathSkipPolicy:  物理路径相同跳过        
    //SameNameSkipPolicy:   同类型同名跳过
    [ObservableProperty]
    private string _skipPolicy = "GroupPhysicalPathSkipPolicy";
    
    
    //周期分界时间点，如果负数则不启用，主要适用于固定时间的刷新物品适用
    [ObservableProperty]
    private int _boundaryTime = 4;
    // 分界时间是否基于服务器时间（否则基于本地时间）
    [ObservableProperty]
    private bool _isBoundaryTimeBasedOnServerTime = false;
    
    //上一次执行间隔时间，出于精度考虑，这里使用秒为单位
    [ObservableProperty]
    private int _lastRunGapSeconds = -1;  
    //间隔时间计算参照，开始时间(StartTime)和结束时间(EndTime)
    [ObservableProperty]
    private string _referencePoint = "EndTime";
    
}