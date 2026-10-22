using System.Collections.Generic;
using BetterGenshinImpact.GameTask.Model;
using BetterGenshinImpact.GameTask.AutoFight;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Service;

namespace BetterGenshinImpact.GameTask.AutoDomain;

/// <summary>
/// 自动秘境任务参数。也是 JS 宿主类型，现有构造函数与属性是社区脚本的对外合同，只允许新增。
/// </summary>
public class AutoDomainParam : BaseTaskParam<AutoDomainTask>
{
    /// <summary>
    /// 配置根。参数类由脚本和界面直接 new，没有创建方可以传入配置，暂时读配置根（见 task-context-removal.md 第 5 节）
    /// </summary>
    private static AllConfig Config => ConfigService.Config!;

    public int DomainRoundNum { get; set; }

    public string CombatStrategyPath { get; set; }

    // 刷副本使用的队伍名称
    public string PartyName { get; set; } = string.Empty;

    // 需要刷取的副本名称
    public string DomainName { get; set; } = string.Empty;

    // 需要刷取的副本名称
    public string SundaySelectedValue { get; set; } = string.Empty;

    // 结束后是否自动分解圣遗物
    public bool AutoArtifactSalvage { get; set; } = false;

    // 分解圣遗物的最大星级
    // 1~4
    public string MaxArtifactStar { get; set; } = "4";

    public bool SpecifyResinUse { get; set; } = false;

    // 使用树脂优先级
    public List<string> ResinPriorityList { get; set; } =
    [
        "浓缩树脂",
        "原粹树脂"
    ];
    // 使用原粹树脂刷取副本次数
    public int OriginalResinUseCount { get; set; } = 0;
    // 使用原粹树脂(20)刷取副本次数
    public int OriginalResin20UseCount { get; set; } = 0;

    // 使用原粹树脂(40)刷取副本次数
    public int OriginalResin40UseCount { get; set; } = 0;

    // 使用浓缩树脂刷取副本次数
    public int CondensedResinUseCount { get; set; } = 0;

    // 使用须臾树脂刷取副本次数
    public int TransientResinUseCount { get; set; } = 0;

    // 使用脆弱树脂刷取副本次数
    public int FragileResinUseCount { get; set; } = 0;

    /// <summary>
    /// 是否启用奖励名称识别。默认关闭。
    /// </summary>
    public bool RewardRecognitionEnabled { get; set; } = false;

    /// <summary>
    /// 战斗结束后延迟几秒再开始寻找石化古树，单位秒。对应 <see cref="AutoDomainConfig.FightEndDelay"/>。
    /// </summary>
    public double FightEndDelay { get; set; } = 5;

    /// <summary>
    /// 寻找石化古树时是否只使用短距离移动，用于识别速度过慢的计算机。对应 <see cref="AutoDomainConfig.ShortMovement"/>。
    /// </summary>
    public bool ShortMovement { get; set; } = false;

    /// <summary>
    /// 走向钥匙或石化古树时是否只步行不冲刺。对应 <see cref="AutoDomainConfig.WalkToF"/>。
    /// </summary>
    public bool WalkToF { get; set; } = true;

    /// <summary>
    /// 寻找石化古树时左右来回小碎步的次数达到该值即视为已对准。对应 <see cref="AutoDomainConfig.LeftRightMoveTimes"/>。
    /// </summary>
    public int LeftRightMoveTimes { get; set; } = 3;

    /// <summary>
    /// 是否在本次秘境期间启用自动吃药。对应 <see cref="AutoDomainConfig.AutoEat"/>。
    /// </summary>
    public bool AutoEat { get; set; } = false;

    /// <summary>
    /// 战斗死亡后复活重试秘境的次数（同时也是秘境流程的最大执行次数）。对应 <see cref="AutoDomainConfig.ReviveRetryCount"/>。
    /// </summary>
    public int ReviveRetryCount { get; set; } = 3;

    /// <summary>
    /// 使用全局秘境配置创建参数，轮数为无限（9999），战斗策略取全局自动战斗策略。
    /// 行为与原来的 <c>new AutoDomainParam()</c>（即 <c>AutoDomainParam(0)</c>）一致，供 ClearScript 与任务框架的无参构造使用。
    /// </summary>
    public AutoDomainParam() : this(0)
    {
    }

    public AutoDomainParam(int domainRoundNum, string path) : base(null, null)
    {
        DomainRoundNum = domainRoundNum;
        if (domainRoundNum == 0)
        {
            DomainRoundNum = 9999;
        }

        CombatStrategyPath = path;
        SetDefault();
    }

    /// <summary>为 Pulonia 创建不读取全局业务配置的参数，全部配置由运行快照填充。</summary>
    internal AutoDomainParam(string path) : base(null, null)
    {
        DomainRoundNum = 9999;
        CombatStrategyPath = path;
    }

    public void SetDefault()
    {
        var config = Config.AutoDomainConfig;
        PartyName = config.PartyName;
        DomainName = config.DomainName;
        SundaySelectedValue = config.SundaySelectedValue;
        AutoArtifactSalvage = config.AutoArtifactSalvage;
        MaxArtifactStar = Config.AutoArtifactSalvageConfig.MaxArtifactStar;
        ResinPriorityList = config.ResinPriorityList;
        OriginalResinUseCount = config.OriginalResinUseCount;
        CondensedResinUseCount = config.CondensedResinUseCount;
        TransientResinUseCount = config.TransientResinUseCount;
        FragileResinUseCount = config.FragileResinUseCount;
        SpecifyResinUse = config.SpecifyResinUse;
        OriginalResin20UseCount = config.OriginalResin20UseCount;
        OriginalResin40UseCount = config.OriginalResin40UseCount;
        RewardRecognitionEnabled = config.RewardRecognitionEnabled;
        // 以下业务设置原来由任务直接读取 AutoDomainConfig，现在随参数传入
        FightEndDelay = config.FightEndDelay;
        ShortMovement = config.ShortMovement;
        WalkToF = config.WalkToF;
        LeftRightMoveTimes = config.LeftRightMoveTimes;
        AutoEat = config.AutoEat;
        ReviveRetryCount = config.ReviveRetryCount;
    }

    public AutoDomainParam(int domainRoundNum = 0) : base(null, null)
    {
        DomainRoundNum = domainRoundNum;
        if (domainRoundNum == 0)
        {
            DomainRoundNum = 9999;
        }

        CombatStrategyPath = SetCombatStrategyPath();
        SetDefault();
    }

    /// <summary>  
    /// 设置战斗策略路径
    /// </summary>  
    /// <param name="strategyName">策略名称</param>  
    public string SetCombatStrategyPath(string? strategyName = null)
    {
        if (string.IsNullOrEmpty(strategyName))
        {
            strategyName = Config.AutoFightConfig.StrategyName;
        }

        if ("根据队伍自动选择".Equals(strategyName))
        {
            return Global.Absolute(@"User\AutoFight\");
        }
        else if (AutoFightParam.ComboStrategyName.Equals(strategyName))
        {
            return AutoFightParam.ComboStrategyName;
        }
        else
        {
            return Global.Absolute(@"User\AutoFight\" + strategyName + ".txt");
        }
    }

    public void SetResinPriorityList(params string[] priorities)
    {
        ResinPriorityList.Clear();
        ResinPriorityList.AddRange(priorities);
    }
}
