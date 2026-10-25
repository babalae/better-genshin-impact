using BetterGenshinImpact.GameTask.Common.Resin;
using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterGenshinImpact.GameTask.AutoStygianOnslaught;

[Serializable]
public partial class AutoStygianOnslaughtConfig : ObservableObject
{
    /// <summary>统一树脂设置；为空时在读取配置后由旧字段迁移。</summary>
    [ObservableProperty]
    private ResinUsage? _resinUsage;

    [ObservableProperty] 
    private string _strategyName = "";
    
    // boss 名字 1~3
    [ObservableProperty]
    private int _bossNum = 1;
    
    // 结束后是否自动分解圣遗物
    [ObservableProperty]
    private bool _autoArtifactSalvage = false;
    
    // 指定树脂的使用次数
    /// <summary>仅用于迁移旧树脂设置；界面与执行层使用 ResinUsage。</summary>
    [ObservableProperty]
    private bool _specifyResinUse = false;
    
    // 自定义使用树脂优先级
    /// <summary>仅用于迁移旧树脂设置；界面与执行层使用 ResinUsage。</summary>
    [ObservableProperty]
    private List<string> _resinPriorityList =
    [
        "浓缩树脂",
        "原粹树脂"
    ];
    
    // 使用原粹树脂刷取副本次数
    /// <summary>仅用于迁移旧树脂设置；界面与执行层使用 ResinUsage。</summary>
    [ObservableProperty]
    private int _originalResinUseCount = 0;
    
    //使用浓缩树脂刷取副本次数
    /// <summary>仅用于迁移旧树脂设置；界面与执行层使用 ResinUsage。</summary>
    [ObservableProperty]
    private int _condensedResinUseCount = 0;

    // 使用须臾树脂刷取副本次数
    /// <summary>仅用于迁移旧树脂设置；界面与执行层使用 ResinUsage。</summary>
    [ObservableProperty]
    private int _transientResinUseCount = 0;
    
    // 使用脆弱树脂刷取副本次数
    /// <summary>仅用于迁移旧树脂设置；界面与执行层使用 ResinUsage。</summary>
    [ObservableProperty]
    private int _fragileResinUseCount = 0;
    
    // 指定战斗队伍
    [ObservableProperty]
    private string _fightTeamName = "";

    /// <summary>仅在尚无统一设置时，从旧字段迁移一次；已有新设置始终优先。</summary>
    public void MigrateLegacyResinUsage()
    {
        if (ResinUsage is not null) return;
        ResinUsage = ResinUsageLegacyMapper.FromStygian(SpecifyResinUse, OriginalResinUseCount, CondensedResinUseCount,
            TransientResinUseCount, FragileResinUseCount);
    }

    /// <summary>确保完成旧字段迁移并返回配置持有的设置；参数类读取后必须 Clone。</summary>
    /// <returns>配置持有的非空树脂设置。</returns>
    public ResinUsage EnsureResinUsage()
    {
        MigrateLegacyResinUsage();
        return ResinUsage!;
    }
}