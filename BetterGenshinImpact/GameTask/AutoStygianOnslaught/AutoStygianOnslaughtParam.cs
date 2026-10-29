using System;
using System.Text.Json.Serialization;
using System.Diagnostics.CodeAnalysis;
using BetterGenshinImpact.GameTask.Common.Resin;
using BetterGenshinImpact.GameTask.AutoFight.Strategy;
using System.Collections.Generic;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.Model;
using BetterGenshinImpact.Service;

namespace BetterGenshinImpact.GameTask.AutoStygianOnslaught;

public class AutoStygianOnslaughtParam:BaseTaskParam<AutoStygianOnslaughtTask>
{
    /// <summary>参数独立持有的树脂设置，默认值来自任务能力。</summary>
    private ResinUsage _resinUsage = ResinCapabilities.StygianOnslaught.Defaults;

    /// <summary>统一树脂设置；JSON null 回退为任务默认值，整体替换会清除待归一化的旧字段。</summary>
    [AllowNull]
    public ResinUsage ResinUsage
    {
        get => _resinUsage;
        set
        {
            _resinUsage = value ?? ResinCapabilities.StygianOnslaught.Defaults;
        }
    }


    /// <summary>
    /// 配置根。参数类由脚本和界面直接 new，没有创建方可以传入配置，暂时读配置根（见 task-context-removal.md 第 5 节）
    /// </summary>
    private static AllConfig Config => ConfigService.Config!;
   
    public int BossNum { get; set; }

    /// <summary>
    /// 危战勋望目标：每次领奖前读到勋望达到该值时停止，结束原因为达到目标；0 表示不限制。
    /// 体力计划在紊乱爆发期优先时传入 <see cref="StygianDirePrestigeReader.CommendationPrestige"/>。
    /// 勋望识别尚未实现（<see cref="StygianDirePrestigeReader.ReadAsync"/> 返回未识别），目前不会因此提前停止。
    /// </summary>
    public int DirePrestigeTarget { get; set; }

    // 结束后是否自动分解圣遗物
    public bool AutoArtifactSalvage { get; set; }

    /// <summary>Pulonia 快照中的分解最大星级；原调用为空时沿用旧全局设置。</summary>
    public string? MaxArtifactStar { get; set; }
    
    // 指定树脂的使用次数
    /// <summary>JS 兼容开关，直接在分别指定和耗尽方式之间切换，不清除隐藏字段。</summary>
    [JsonIgnore]
    public bool SpecifyResinUse
    {
        get => ResinUsageLegacyMapper.ToSpecifyResinUse(ResinUsage);
        set => ResinUsage.Mode = value ? ResinUsageMode.PerType : ResinUsageMode.Exhaust;
    }
    
    // 自定义使用树脂优先级
    /// <summary>JS 兼容列表，保留读写但不影响固定使用顺序，且不与全局配置共享。</summary>
    [JsonIgnore]
    public List<string> ResinPriorityList{ get; set; }=["浓缩树脂","原粹树脂"];
    // 使用原粹树脂刷取副本次数
    /// <summary>JS 兼容的原粹次数，直接读写统一设置中该类的领奖次数。</summary>
    [JsonIgnore]
    public int OriginalResinUseCount
    {
        get => ResinUsage.Counts.Original;
        set => ResinUsage.Counts.Original = value;
    }
    
    //使用浓缩树脂刷取副本次数
    /// <summary>JS 兼容的浓缩次数，直接读写统一设置中该类的领奖次数。</summary>
    [JsonIgnore]
    public int CondensedResinUseCount
    {
        get => ResinUsage.Counts.Condensed;
        set => ResinUsage.Counts.Condensed = value;
    }

    // 使用须臾树脂刷取副本次数
    /// <summary>JS 兼容的须臾次数，直接读写统一设置中该类的领奖次数。</summary>
    [JsonIgnore]
    public int TransientResinUseCount
    {
        get => ResinUsage.Counts.Transient;
        set => ResinUsage.Counts.Transient = value;
    }
    
    // 使用脆弱树脂刷取副本次数
    /// <summary>JS 兼容的脆弱次数，直接读写统一设置中该类的领奖次数。</summary>
    [JsonIgnore]
    public int FragileResinUseCount
    {
        get => ResinUsage.Counts.Fragile;
        set => ResinUsage.Counts.Fragile = value;
    }
    // 指定战斗队伍
    public string FightTeamName { get; set; }
    // 战斗脚本包路径
    public string CombatScriptBagPath { get; set; }

    /// <summary>
    /// 战斗策略名称（可为策略名、路径或“根据队伍自动选择”）。
    /// 仅当 <see cref="CombatScriptBagPath"/> 为空时，任务才按此名称解析策略路径；任务计划保存名称便于跨机器使用。
    /// </summary>
    public string? StrategyName { get; set; }

    public void SetDefault()
    {
        var config = Config.AutoStygianOnslaughtConfig;
        SetAutoStygianOnslaughtConfig(config);
    }
    public void SetAutoStygianOnslaughtConfig(AutoStygianOnslaughtConfig config)
    {
        BossNum = config.BossNum;
        AutoArtifactSalvage = config.AutoArtifactSalvage;
        ResinUsage = config.EnsureResinUsage().Clone();
        FightTeamName = config.FightTeamName;
        // 同时记录策略名，路径被清空时任务可按名称重新解析
        StrategyName = config.StrategyName;
        SetCombatStrategyPath(config.StrategyName);
    }
    public AutoStygianOnslaughtParam() : base(null, null)
    {
        SetDefault();
    }

    /// <summary>使用本次计划的独立配置创建参数，不加载全局业务默认值。</summary>
    internal AutoStygianOnslaughtParam(AutoStygianOnslaughtConfig config) : base(null, null)
    {
        SetAutoStygianOnslaughtConfig(config);
    }
    public AutoStygianOnslaughtParam(string combatScriptBagPath) : base(null, null)
    {
        SetDefault();
        CombatScriptBagPath=combatScriptBagPath;
    }
    /// <summary>JS 兼容入口；树脂使用顺序固定，传入的优先级不再生效。</summary>
    /// <param name="priorities">旧脚本传入的优先级。</param>
    [Obsolete("树脂使用顺序固定，此方法不再生效")]
    public void SetResinPriorityList(params string[] priorities)
    {
        // 保留旧方法供社区脚本调用。
    }
    
    
    /// <summary>  
    /// 设置战斗策略路径
    /// </summary>  
    /// <param name="strategyName">策略名称</param>  
    public void SetCombatStrategyPath(string? strategyName = null)
    {
        if (string.IsNullOrEmpty(strategyName))
        {
            strategyName = Config.AutoFightConfig.StrategyName;
        }

        CombatScriptBagPath = CombatStrategyResolver.Parse(strategyName).Path;
    }
}
