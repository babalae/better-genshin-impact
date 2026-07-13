using System.Collections.Generic;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.Model;

namespace BetterGenshinImpact.GameTask.AutoStygianOnslaught;

public class AutoStygianOnslaughtParam:BaseTaskParam<AutoStygianOnslaughtTask>
{
   
    public int BossNum { get; set; }
    // 结束后是否自动分解圣遗物
    public bool AutoArtifactSalvage { get; set; }
    
    // 指定树脂的使用次数
    public bool SpecifyResinUse{ get; set; }
    
    // 自定义使用树脂优先级
    public List<string> ResinPriorityList{ get; set; }=["浓缩树脂","原粹树脂"];
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
        BossNum = config.BossNum;
        AutoArtifactSalvage = config.AutoArtifactSalvage;
        SpecifyResinUse = config.SpecifyResinUse;
        ResinPriorityList = config.ResinPriorityList == null ? new List<string> { "浓缩树脂", "原粹树脂" }: new List<string>(config.ResinPriorityList);
        OriginalResinUseCount = config.OriginalResinUseCount;
        CondensedResinUseCount = config.CondensedResinUseCount;
        TransientResinUseCount = config.TransientResinUseCount;
        FragileResinUseCount = config.FragileResinUseCount;
        FightTeamName = config.FightTeamName;
        SetCombatStrategyPath(config.StrategyName);
    }
    public AutoStygianOnslaughtParam() : base(null, null)
    {
        SetDefault();
    }
    public AutoStygianOnslaughtParam(string combatScriptBagPath) : base(null, null)
    {
        SetDefault();
        CombatScriptBagPath=combatScriptBagPath;
    }
    public void SetResinPriorityList(params string[] priorities)
    {
        ResinPriorityList.Clear();
        ResinPriorityList.AddRange(priorities);
    }
    
    
    /// <summary>  
    /// 设置战斗策略路径
    /// </summary>  
    /// <param name="strategyName">策略名称</param>  
    public void SetCombatStrategyPath(string? strategyName = null)
    {
        if (string.IsNullOrEmpty(strategyName))
        {
            strategyName = TaskContext.Instance().Config.AutoFightConfig.StrategyName;
        }

        if (string.IsNullOrWhiteSpace(strategyName) ||"根据队伍自动选择".Equals(strategyName))
        {
            CombatScriptBagPath= Global.Absolute(@"User\AutoFight\");
            return;
        }

        CombatScriptBagPath= Global.Absolute(@"User\AutoFight\" + strategyName + ".txt");
    }
}