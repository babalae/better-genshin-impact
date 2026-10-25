using System.Collections.Generic;

namespace BetterGenshinImpact.GameTask.Common.Resin;

/// <summary>任务支持的树脂使用范围，供参数校验及后续界面、编排层共用。</summary>
public sealed class ResinUsageCapability
{
    /// <summary>能力内部持有的默认设置，不向调用方暴露可变实例。</summary>
    private ResinUsage _defaults = new();

    /// <summary>支持的使用方式。</summary>
    public required IReadOnlySet<ResinUsageMode> Modes { get; init; }

    /// <summary>每类树脂的支持方式。</summary>
    public required ResinKindValues<ResinKindSupport> Kinds { get; init; }

    /// <summary>支持的原粹档位，按界面展示顺序排列。</summary>
    public required IReadOnlyList<OriginalResinTier> Tiers { get; init; }

    /// <summary>默认设置；写入和每次读取均深拷贝，调用方可以独立编辑返回值。</summary>
    public required ResinUsage Defaults
    {
        get => _defaults.Clone();
        init => _defaults = value.Clone();
    }

    /// <summary>任务专属说明；为空的项由使用方提供通用文案。</summary>
    public ResinCapabilityHints Hints { get; init; } = new();

    /// <summary>仅有一种树脂直接用于领奖时，返回必须锁定开启的种类。</summary>
    public ResinKind? LockedKind
    {
        get
        {
            ResinKind? locked = null;
            foreach (var kind in ResinKinds.InOrder)
            {
                if (Kinds[kind] != ResinKindSupport.Direct)
                    continue;
                if (locked is not null)
                    return null;
                locked = kind;
            }
            return locked;
        }
    }

    /// <summary>判断某类树脂是否支持直接领奖或战前补充。</summary>
    /// <param name="kind">树脂种类。</param>
    /// <returns>支持直接使用或补充时为 true。</returns>
    public bool IsSupported(ResinKind kind) => Kinds[kind] != ResinKindSupport.Unsupported;
}

/// <summary>树脂能力的任务专属说明，不包含界面行为。</summary>
public sealed class ResinCapabilityHints
{
    /// <summary>各树脂不支持原因；对应项为 null 时使用通用说明。</summary>
    public ResinKindValues<string?> UnsupportedReasons { get; init; } = new();

    /// <summary>不支持按树脂分别指定的原因。</summary>
    public string? PerTypeUnsupportedReason { get; init; }

    /// <summary>“默认”原粹树脂档位的具体含义。</summary>
    public string? DefaultTierDescription { get; init; }
}
