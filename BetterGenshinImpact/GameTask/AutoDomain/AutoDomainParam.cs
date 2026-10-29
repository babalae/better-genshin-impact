using System;
using System.Text.Json.Serialization;
using System.Diagnostics.CodeAnalysis;
using BetterGenshinImpact.GameTask.Common.Resin;
using BetterGenshinImpact.GameTask.AutoFight.Strategy;
using System.Collections.Generic;
using BetterGenshinImpact.GameTask.AutoDomain.TrainingGuide;
using BetterGenshinImpact.GameTask.Model;
using BetterGenshinImpact.GameTask.AutoFight;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Service;
using BetterGenshinImpact.GameTask.Common.Reward;

namespace BetterGenshinImpact.GameTask.AutoDomain;

/// <summary>
/// 自动秘境任务参数。也是 JS 宿主类型，现有构造函数与属性是社区脚本的对外合同，只允许新增。
/// </summary>
public class AutoDomainParam : BaseTaskParam<AutoDomainTask>
{
    /// <summary>参数独立持有的树脂设置，默认值来自任务能力。</summary>
    private ResinUsage _resinUsage = ResinCapabilities.Domain.Defaults;

    /// <summary>JS 已赋值的不分档位原粹次数；null 表示尚未赋值或已归一化。</summary>
    private int? _legacyOriginal;

    /// <summary>JS 已赋值的20 点档位原粹次数；null 表示尚未赋值或已归一化。</summary>
    private int? _legacyOriginal20;

    /// <summary>JS 已赋值的40 点档位原粹次数；null 表示尚未赋值或已归一化。</summary>
    private int? _legacyOriginal40;

    /// <summary>统一树脂设置；JSON null 回退为任务默认值，整体替换会清除待归一化的旧字段。</summary>
    [AllowNull]
    public ResinUsage ResinUsage
    {
        get => _resinUsage;
        set
        {
            _resinUsage = value ?? ResinCapabilities.Domain.Defaults;
            _legacyOriginal = null;
            _legacyOriginal20 = null;
            _legacyOriginal40 = null;
        }
    }


    /// <summary>
    /// 配置根。参数类由脚本和界面直接 new，没有创建方可以传入配置，暂时读配置根（见 task-context-removal.md 第 5 节）
    /// </summary>
    private static AllConfig Config => ConfigService.Config!;

    /// <summary>仅本次运行使用。null 沿用游戏培养计划；JSON 数组元素为 material 和 target（目标库存）。</summary>
    public string? TrainingTargetsJson { get; set; }

    /// <summary>JS 使用 JSON.stringify([{material: "材料完整名称", target: 28}]) 传入。</summary>
    /// <param name="json">自定义培养目标 JSON，格式错误时抛出异常且不修改当前值。</param>
    public void SetTrainingTargets(string json)
    {
        TrainingGuideCustomTargets.Parse(json);
        TrainingTargetsJson = json;
    }

    /// <summary>JS 兼容的额外轮数上限；构造参数 0 按原语义转换为 9999，不进入树脂设置。</summary>
    [JsonIgnore]
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

    /// <summary>JS 兼容开关，直接在分别指定和耗尽方式之间切换，不清除隐藏字段。</summary>
    [JsonIgnore]
    public bool SpecifyResinUse
    {
        get => ResinUsageLegacyMapper.ToSpecifyResinUse(ResinUsage);
        set => ResinUsage.Mode = value ? ResinUsageMode.PerType : ResinUsageMode.Exhaust;
    }

    // 使用树脂优先级
    /// <summary>JS 兼容列表，保留读写但不影响固定使用顺序，且不与全局配置共享。</summary>
    [JsonIgnore]
    public List<string> ResinPriorityList { get; set; } =
    [
        "浓缩树脂",
        "原粹树脂"
    ];
    // 使用原粹树脂刷取副本次数
    /// <summary>JS 兼容次数，已赋值时读取暂存值，否则按当前原粹档位投影；统一由 ResolveResinUsage 合并。</summary>
    [JsonIgnore]
    public int OriginalResinUseCount
    {
        get => _legacyOriginal ?? (ResinUsage.OriginalTier == OriginalResinTier.Default ? ResinUsage.Counts.Original : 0);
        set => _legacyOriginal = value;
    }
    // 使用原粹树脂(20)刷取副本次数
    /// <summary>JS 兼容的 20 点档位次数，已赋值时读取暂存值，否则按当前档位投影原粹目标。</summary>
    [JsonIgnore]
    public int OriginalResin20UseCount
    {
        get => _legacyOriginal20 ?? (ResinUsage.OriginalTier == OriginalResinTier.Only20 ? ResinUsage.Counts.Original : 0);
        set => _legacyOriginal20 = value;
    }

    // 使用原粹树脂(40)刷取副本次数
    /// <summary>JS 兼容的 40 点档位次数，已赋值时读取暂存值，否则按当前档位投影原粹目标。</summary>
    [JsonIgnore]
    public int OriginalResin40UseCount
    {
        get => _legacyOriginal40 ?? (ResinUsage.OriginalTier == OriginalResinTier.Prefer40 ? ResinUsage.Counts.Original : 0);
        set => _legacyOriginal40 = value;
    }

    // 使用浓缩树脂刷取副本次数
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
    /// 材料目标列表。
    /// </summary>
    public List<TargetItem> TargetItems { get; set; } = [];

    /// <summary>
    /// 使用全局秘境配置创建参数，轮数为无限（9999），战斗策略取全局自动战斗策略。
    /// 行为与原来的 <c>new AutoDomainParam()</c>（即 <c>AutoDomainParam(0)</c>）一致，供 ClearScript 与任务框架的无参构造使用。
    /// </summary>
    public AutoDomainParam() : this(0)
    {
    }

    /// <summary>Training guide options inherit task settings at construction; JS may override any subset for this run.</summary>
    public bool TrainingGuideCalculateRunsEnabled { get; set; }

    /// <summary>0: reserve crafting bonuses; 1: ignore crafting bonuses. Other values normalize to 0.</summary>
    public int TrainingGuideRunPreference
    {
        get => _trainingGuideRunPreference;
        set => _trainingGuideRunPreference = value is 0 or 1 ? value : 0;
    }

    /// <summary>培养计划刷取偏好的存储字段，只保存 0 或 1。</summary>
    private int _trainingGuideRunPreference;

    /// <summary>Crafting bonus reserve percentage, clamped to 0 through 20.</summary>
    public int TrainingGuideCraftingBonusReservePercent
    {
        get => _trainingGuideCraftingBonusReservePercent;
        set => _trainingGuideCraftingBonusReservePercent = Math.Clamp(value, 0,
            TrainingGuideRunCalculator.MaxCraftingBonusReservePercent);
    }

    /// <summary>合成收益预留百分比的存储字段，范围 0～20。</summary>
    private int _trainingGuideCraftingBonusReservePercent;

    /// <summary>Use recognized rewards to update the remaining training plan.</summary>
    public bool TrainingGuideRewardRecognitionEnabled { get; set; }

    /// <summary>培养奖励识别失败后是否按当前入口的初始预算继续；关闭时识别失败即结束任务。</summary>
    public bool TrainingGuideRewardFailureBudgetEnabled { get; set; }

    /// <summary>是否保存培养计划的详细诊断记录与截图。</summary>
    public bool TrainingGuideDiagnosticsEnabled { get; set; }

    /// <summary>是否启用低世界等级适配：选择入口时检查解锁状态并自动降低难度。</summary>
    public bool TrainingGuideLowARAdaptationEnabled { get; set; }

    /// <summary>Fallback domain after training targets are complete. Empty disables fallback.</summary>
    public string TrainingGuideFallbackDomainName { get; set; } = string.Empty;

    /// <summary>Sunday or limited-time reward selection for the fallback domain.</summary>
    public string TrainingGuideFallbackSundaySelectedValue { get; set; } = string.Empty;

    /// <summary>
    /// 体力计划盛材移涌期间领取双倍的精通或炼武秘境；空字符串表示跟随培养计划（按提升指南选择秘境）。
    /// 只在体力计划中生效，普通自动秘境不读取。
    /// </summary>
    public string MaterialOverflowDomainName { get; set; } = string.Empty;

    /// <summary>
    /// 体力计划祝圣移涌期间领取双倍的祝圣秘境；空字符串表示跟随培养计划（提升指南中养成角色推荐圣遗物所在的秘境）。
    /// 只在体力计划中生效，普通自动秘境不读取。
    /// </summary>
    public string BlessingOverflowDomainName { get; set; } = string.Empty;

    /// <summary>
    /// 本次运行是否需要识别奖励：常规奖励识别、材料目标或培养计划奖励识别任一需要即识别。
    /// </summary>
    /// <returns>需要识别奖励时返回 true。</returns>
    public bool ShouldRecognizeRewards() => RewardRecognitionEnabled || TargetItems.Count > 0 ||
        (TrainingTargetsJson != null && TrainingGuideRewardRecognitionEnabled) ||
        (DomainName == AutoDomainTask.TrainingGuideOption &&
         TrainingGuideCalculateRunsEnabled && TrainingGuideRewardRecognitionEnabled);

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
        SetAutoDomainConfig(Config.AutoDomainConfig);
        MaxArtifactStar = Config.AutoArtifactSalvageConfig.MaxArtifactStar;
    }

    /// <summary>从指定秘境配置复制参数，树脂设置深拷贝后由参数独立持有。</summary>
    /// <param name="config">秘境业务配置。</param>
    public void SetAutoDomainConfig(AutoDomainConfig config)
    {
        PartyName = config.PartyName;
        DomainName = config.DomainName;
        SundaySelectedValue = config.SundaySelectedValue;
        AutoArtifactSalvage = config.AutoArtifactSalvage;
        ResinUsage = config.EnsureResinUsage().Clone();
        RewardRecognitionEnabled = config.RewardRecognitionEnabled;
        // 以下业务设置原来由任务直接读取 AutoDomainConfig，现在随参数传入
        FightEndDelay = config.FightEndDelay;
        ShortMovement = config.ShortMovement;
        WalkToF = config.WalkToF;
        LeftRightMoveTimes = config.LeftRightMoveTimes;
        AutoEat = config.AutoEat;
        ReviveRetryCount = config.ReviveRetryCount;
        TargetItems = TargetItem.CloneAll(config.TargetItems);
        CopyTrainingGuideSettings(config);
    }

    /// <summary>从秘境配置复制培养计划相关设置；Pulonia 导入旧配置时也使用此方法。</summary>
    /// <param name="config">秘境业务配置。</param>
    internal void CopyTrainingGuideSettings(AutoDomainConfig config)
    {
        TrainingGuideCalculateRunsEnabled = config.TrainingGuideCalculateRunsEnabled;
        TrainingGuideRunPreference = config.TrainingGuideRunPreference;
        TrainingGuideCraftingBonusReservePercent = config.TrainingGuideCraftingBonusReservePercent;
        TrainingGuideRewardRecognitionEnabled = config.TrainingGuideRewardRecognitionEnabled;
        TrainingGuideRewardFailureBudgetEnabled = config.TrainingGuideRewardFailureBudgetEnabled;
        TrainingGuideDiagnosticsEnabled = config.TrainingGuideDiagnosticsEnabled;
        TrainingGuideLowARAdaptationEnabled = config.TrainingGuideLowARAdaptationEnabled;
        TrainingGuideFallbackDomainName = config.TrainingGuideFallbackDomainName;
        TrainingGuideFallbackSundaySelectedValue = config.TrainingGuideFallbackSundaySelectedValue;
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

    /// <summary>将 JS 已赋值的原粹/20/40 次数组合归一化一次，不覆盖其他树脂及使用方式。</summary>
    public void ResolveResinUsage()
    {
        if (_legacyOriginal is null && _legacyOriginal20 is null && _legacyOriginal40 is null) return;
        var mapped = ResinUsageLegacyMapper.FromDomain(SpecifyResinUse, OriginalResinUseCount,
            CondensedResinUseCount, TransientResinUseCount, FragileResinUseCount,
            OriginalResin20UseCount, OriginalResin40UseCount, out var warning);
        var next = ResinUsage.Clone();
        next.Counts.Original = mapped.Counts.Original;
        next.OriginalTier = mapped.OriginalTier;
        ResinUsage = next;
        if (warning is not null) Serilog.Log.Warning("{Warning}", warning);
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

        return CombatStrategyResolver.Parse(strategyName).Path;
    }

    /// <summary>JS 兼容入口；树脂使用顺序固定，传入的优先级不再生效。</summary>
    /// <param name="priorities">旧脚本传入的优先级。</param>
    [Obsolete("树脂使用顺序固定，此方法不再生效")]
    public void SetResinPriorityList(params string[] priorities)
    {
        // 保留旧方法供社区脚本调用。
    }
}
