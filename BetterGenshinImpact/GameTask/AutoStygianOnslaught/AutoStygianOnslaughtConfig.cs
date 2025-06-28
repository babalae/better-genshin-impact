using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterGenshinImpact.GameTask.AutoStygianOnslaught;

[Serializable]
public partial class AutoStygianOnslaughtConfig : ObservableObject
{
    [ObservableProperty] 
    private string _strategyName = "";
    
    // 结束后是否自动分解圣遗物
    [ObservableProperty]
    private bool _autoArtifactSalvage = false;
    
    // 指定树脂的使用次数
    [ObservableProperty]
    private bool _specifyResinUse = false;
    
    // 自定义使用树脂优先级
    [ObservableProperty]
    private List<string> _resinPriorityList =
    [
        "浓缩树脂",
        "原粹树脂"
    ];
    
    // 使用原粹树脂刷取副本次数
    [ObservableProperty]
    private int _originalResinUseCount = 0;
    
    //使用浓缩树脂刷取副本次数
    [ObservableProperty]
    private int _condensedResinUseCount = 0;

    // 使用须臾树脂刷取副本次数
    [ObservableProperty]
    private int _transientResinUseCount = 0;
    
    // 使用脆弱树脂刷取副本次数
    [ObservableProperty]
    private int _fragileResinUseCount = 0;
}