using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using System.Diagnostics.CodeAnalysis;
using BetterGenshinImpact.GameTask.Common.Resin;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.Common.Reward;
using BetterGenshinImpact.GameTask.Model;
using BetterGenshinImpact.Service;

namespace BetterGenshinImpact.GameTask.AutoLeyLineOutcrop;

public class AutoLeyLineOutcropParam:BaseTaskParam<AutoLeyLineOutcropTask>
{
    /// <summary>参数独立持有的树脂设置，默认值来自任务能力。</summary>
    private ResinUsage _resinUsage = ResinCapabilities.LeyLineOutcrop.Defaults;

    /// <summary>JS 已赋值的耗尽开关；null 表示尚未赋值或已归一化。</summary>
    private bool? _legacyExhaustion;

    /// <summary>JS 已赋值的取小值开关；null 表示尚未赋值或已归一化。</summary>
    private bool? _legacyMin;

    /// <summary>JS 已赋值的领奖次数；null 表示尚未赋值或已归一化。</summary>
    private int? _legacyCount;

    /// <summary>JS 已赋值的须臾开关；null 表示尚未赋值或已归一化。</summary>
    private bool? _legacyTransient;

    /// <summary>JS 已赋值的脆弱开关；null 表示尚未赋值或已归一化。</summary>
    private bool? _legacyFragile;

    /// <summary>统一树脂设置；JSON null 回退为任务默认值，整体替换会清除待归一化的旧字段。</summary>
    [AllowNull]
    public ResinUsage ResinUsage
    {
        get => _resinUsage;
        set
        {
            _resinUsage = value ?? ResinCapabilities.LeyLineOutcrop.Defaults;
            _legacyExhaustion = null;
            _legacyMin = null;
            _legacyCount = null;
            _legacyTransient = null;
            _legacyFragile = null;
        }
    }


    /// <summary>
    /// 配置根。参数类由脚本和界面直接 new，没有创建方可以传入配置，暂时读配置根（见 task-context-removal.md 第 5 节）
    /// </summary>
    private static AllConfig Config => ConfigService.Config!;

    // 刷取次数
    /// <summary>JS 兼容属性，已赋值时读取暂存值，否则投影统一设置，调用 ResolveResinUsage 后归一化。</summary>
    [JsonIgnore]
    public int Count
    {
        get => _legacyCount ?? (ResinUsage.Mode == ResinUsageMode.PerType ? ResinKinds.InOrder.Sum(kind => ResinUsage.Counts[kind]) : ResinUsage.RunCount);
        set => _legacyCount = value;
    }
    // 地区
    public string Country { get; set; }
    //地脉花类型
    public string LeyLineOutcropType { get; set; }
    // 开启取小值模式
    /// <summary>JS 兼容属性，已赋值时读取暂存值，否则投影统一设置，调用 ResolveResinUsage 后归一化。</summary>
    [JsonIgnore]
    public bool OpenModeCountMin
    {
        get => _legacyMin ?? (ResinUsageLegacyMapper.ToLeyLineOpenModeCountMin(ResinUsage));
        set => _legacyMin = value;
    }
    // 是否开启树脂耗尽模式
    /// <summary>JS 兼容属性，已赋值时读取暂存值，否则投影统一设置，调用 ResolveResinUsage 后归一化。</summary>
    [JsonIgnore]
    public bool IsResinExhaustionMode
    {
        get => _legacyExhaustion ?? (ResinUsageLegacyMapper.ToLeyLineIsResinExhaustionMode(ResinUsage));
        set => _legacyExhaustion = value;
    }
    //好感队名称
    public string FriendshipTeam { get; set; }
    //战斗的队伍名称
    public string Team { get; set; }
    //战斗超时时间
    public int Timeout { get; set; }
    //地脉花独立战斗配置
    public AutoLeyLineOutcropFightConfig FightConfig { get; set; } = new();
    //是否前往合成台合成浓缩树脂
    /// <summary>JS 兼容的无效属性，保留读写但不影响执行。</summary>
    [JsonIgnore]
    public bool IsGoToSynthesizer { get; set; }
    //是否使用脆弱树脂
    /// <summary>JS 兼容属性，已赋值时读取暂存值，否则投影统一设置，调用 ResolveResinUsage 后归一化。</summary>
    [JsonIgnore]
    public bool UseFragileResin
    {
        get => _legacyFragile ?? (ResinUsage.Allowed.Fragile);
        set => _legacyFragile = value;
    }
    //是否使用须臾树脂
    /// <summary>JS 兼容属性，已赋值时读取暂存值，否则投影统一设置，调用 ResolveResinUsage 后归一化。</summary>
    [JsonIgnore]
    public bool UseTransientResin
    {
        get => _legacyTransient ?? (ResinUsage.Allowed.Transient);
        set => _legacyTransient = value;
    }
    /// <summary>
    /// 是否在领取奖励后扫描掉落物光柱。
    /// </summary>
    public bool ScanDropsAfterRewardEnabled { get; set; }

    /// <summary>
    /// 领取奖励后扫描掉落物光柱的最长时长，单位为秒。
    /// </summary>
    public int ScanDropsAfterRewardSeconds { get; set; }

    /// <summary>
    /// 是否为一条龙模式：为 true 时开始前不先传送到七天神像。默认 false（原任务构造参数 oneDragonMode）。
    /// </summary>
    public bool OneDragonMode { get; set; }

    /// <summary>
    /// 材料目标列表：按物品名与数量提前停止。
    /// </summary>
    public List<TargetItem> TargetItems { get; set; } = [];
    
    /// <summary>归一化 JS 已赋值的组合字段；未赋值的模式、补充开关及新模型专属字段保持原值。</summary>
    public void ResolveResinUsage()
    {
        if (_legacyExhaustion is null && _legacyMin is null && _legacyCount is null
            && _legacyTransient is null && _legacyFragile is null) return;
        var mapped = ResinUsageLegacyMapper.FromLeyLine(IsResinExhaustionMode, OpenModeCountMin, Count,
            UseTransientResin, UseFragileResin);
        var next = ResinUsage.Clone();
        // 仅旧模式开关参与方式换算；单独设置补充开关不会抹掉新模型的 PerType 方式。
        if (_legacyExhaustion is not null || _legacyMin is not null)
        {
            // 旧一条龙总会写两个 false；未显式指定 Count 时不能覆盖新界面的分别指定目标。
            if (!(mapped.Mode == ResinUsageMode.RunCount && _legacyCount is null
                && ResinUsage.Mode == ResinUsageMode.PerType)) next.Mode = mapped.Mode;
        }
        if (_legacyCount is not null) next.RunCount = mapped.RunCount;
        if (_legacyTransient is not null) next.Allowed.Transient = mapped.Allowed.Transient;
        if (_legacyFragile is not null) next.Allowed.Fragile = mapped.Allowed.Fragile;
        ResinUsage = next;
    }

    public void SetDefault()
    {
        var config = Config.AutoLeyLineOutcropConfig;
        SetAutoLeyLineOutcropConfig(config);
    }

    public void SetAutoLeyLineOutcropConfig(AutoLeyLineOutcropConfig config)
    {
        ResinUsage = config.EnsureResinUsage().Clone();
        FriendshipTeam= config.FriendshipTeam;
        Team= config.Team;
        Timeout= config.Timeout;
        FightConfig = config.FightConfig ?? new AutoLeyLineOutcropFightConfig();
        ScanDropsAfterRewardEnabled = config.ScanDropsAfterRewardEnabled;
        ScanDropsAfterRewardSeconds = config.ScanDropsAfterRewardSeconds;
        Country = config.Country;
        LeyLineOutcropType = config.LeyLineOutcropType;
        TargetItems = TargetItem.CloneAll(config.TargetItems);
    }

    public AutoLeyLineOutcropParam() : base(null, null)
    {
        SetDefault();
    }

    /// <summary>使用快照配置创建参数，避免构造期间读取独立任务页面的业务配置。</summary>
    internal AutoLeyLineOutcropParam(AutoLeyLineOutcropConfig config) : base(null, null)
    {
        SetAutoLeyLineOutcropConfig(config);
    }
    
    public AutoLeyLineOutcropParam(int count, string country, string leyLineOutcropType) : base(null, null)
    {
        SetDefault();
        this.Count = count;
        this.Country = country;
        this.LeyLineOutcropType = leyLineOutcropType;
    }
}
