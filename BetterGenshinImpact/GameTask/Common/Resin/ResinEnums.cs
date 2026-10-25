using System.Text.Json.Serialization;

namespace BetterGenshinImpact.GameTask.Common.Resin;

/// <summary>树脂种类，声明顺序即固定使用顺序。</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ResinKind>))]
public enum ResinKind
{
    /// <summary>浓缩树脂。</summary>
    Condensed,
    /// <summary>原粹树脂。</summary>
    Original,
    /// <summary>须臾树脂。</summary>
    Transient,
    /// <summary>脆弱树脂。</summary>
    Fragile
}

/// <summary>树脂使用的目标控制方式。</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ResinUsageMode>))]
public enum ResinUsageMode
{
    /// <summary>刷至允许的树脂耗尽。</summary>
    Exhaust,
    /// <summary>指定成功领奖次数。</summary>
    RunCount,
    /// <summary>按树脂分别指定领奖次数。</summary>
    PerType
}

/// <summary>原粹树脂的领奖档位策略。</summary>
[JsonConverter(typeof(JsonStringEnumConverter<OriginalResinTier>))]
public enum OriginalResinTier
{
    /// <summary>沿用任务的默认档位规则。</summary>
    Default,
    /// <summary>优先消耗 40 点，不足时使用 20 点。</summary>
    Prefer40,
    /// <summary>只使用 20 点档位。</summary>
    Only20
}

/// <summary>任务使用某类树脂的方式。</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ResinKindSupport>))]
public enum ResinKindSupport
{
    /// <summary>任务不支持此类树脂。</summary>
    Unsupported,
    /// <summary>领奖时直接使用。</summary>
    Direct,
    /// <summary>战前补充为原粹树脂。</summary>
    Replenish
}

/// <summary>因树脂规则结束领奖的原因。</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ResinStopReason>))]
public enum ResinStopReason
{
    /// <summary>已达到指定目标。</summary>
    TargetReached,
    /// <summary>允许的树脂已不足以继续领奖。</summary>
    ResinExhausted,
    /// <summary>界面未提供可用的树脂选项。</summary>
    NoUsableOption
}
