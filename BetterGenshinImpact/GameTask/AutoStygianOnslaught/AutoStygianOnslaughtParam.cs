using System.Collections.Generic;
using BetterGenshinImpact.GameTask.Model;

namespace BetterGenshinImpact.GameTask.AutoStygianOnslaught;

public class AutoStygianOnslaughtParam:BaseTaskParam<AutoStygianOnslaughtTask>
{
    public string StrategyName { get; set; }
    public int BossNum { get; set; }
    // 结束后是否自动分解圣遗物
    public bool AutoArtifactSalvage { get; set; }
    
    // 指定树脂的使用次数
    public bool SpecifyResinUse{ get; set; }
    
    // 自定义使用树脂优先级
    public List<string> ResinPriorityList{ get; set; }
    // 使用原粹树脂刷取副本次数
    public int OriginalResinUseCount { get; set; }
    
    //使用浓缩树脂刷取副本次数
    public int CondensedResinUseCount { get; set; }

    // 使用须臾树脂刷取副本次数
    public int TransientResinUseCount { get; set; }
    
    // 使用脆弱树脂刷取副本次数
    public int FragileResinUseCount { get; set; }
    // 指定战斗队伍
    public string FightTeamName { get; set; }
    // 战斗脚本包路径
    public string CombatScriptBagPath { get; set; }
    public void SetDefault()
    {
        var config = TaskContext.Instance().Config.AutoStygianOnslaughtConfig;
        SetAutoStygianOnslaughtConfig(config);
    }
    public void SetAutoStygianOnslaughtConfig(AutoStygianOnslaughtConfig config)
    {
        StrategyName = config.StrategyName;
        BossNum = config.BossNum;
        AutoArtifactSalvage = config.AutoArtifactSalvage;
        SpecifyResinUse = config.SpecifyResinUse;
        ResinPriorityList = config.ResinPriorityList;
        OriginalResinUseCount = config.OriginalResinUseCount;
        CondensedResinUseCount = config.CondensedResinUseCount;
        TransientResinUseCount = config.TransientResinUseCount;
        FragileResinUseCount = config.FragileResinUseCount;
        FightTeamName = config.FightTeamName;
    }
    public AutoStygianOnslaughtParam() : base(null, null)
    {
        SetDefault();
    }
    public AutoStygianOnslaughtParam(string combatScriptBagPath) : base(null, null)
    {
        SetDefault();
        CombatScriptBagPath = combatScriptBagPath;
    }
    public void SetResinPriorityList(params string[] priorities)
    {
        ResinPriorityList.Clear();
        ResinPriorityList.AddRange(priorities);
    }
}