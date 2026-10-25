using System.Linq;
using System.Diagnostics.CodeAnalysis;

namespace BetterGenshinImpact.GameTask.Common.Resin;

/// <summary>领奖任务共用的树脂设置；按值对象使用，保存时整体替换，不原地修改共享实例。</summary>
public sealed record ResinUsage
{
    /// <summary>允许的树脂；对 JSON null 使用默认开关。</summary>
    private ResinKindValues<bool> _allowed = new() { Condensed = true, Original = true };

    /// <summary>各类目标次数；对 JSON null 使用全 0。</summary>
    private ResinKindValues<int> _counts = new();

    /// <summary>使用方式。</summary>
    public ResinUsageMode Mode { get; set; } = ResinUsageMode.Exhaust;

    /// <summary>指定领奖次数时的成功领奖次数。</summary>
    public int RunCount { get; set; } = 1;

    /// <summary>刷至耗尽、指定次数时允许使用的树脂。</summary>
    [AllowNull]
    public ResinKindValues<bool> Allowed
    {
        get => _allowed;
        set => _allowed = value ?? new() { Condensed = true, Original = true };
    }

    /// <summary>按树脂分别指定时各类树脂的领奖次数。</summary>
    [AllowNull]
    public ResinKindValues<int> Counts
    {
        get => _counts;
        set => _counts = value ?? new();
    }

    /// <summary>原粹树脂档位。</summary>
    public OriginalResinTier OriginalTier { get; set; } = OriginalResinTier.Default;

    /// <summary>根据任务能力校验；当前方式不读取的字段保留原值且不参与校验。</summary>
    /// <param name="capability">任务能力声明。</param>
    /// <returns>面向用户的错误说明；合法时返回 null。</returns>
    public string? Validate(ResinUsageCapability capability)
    {
        // 所有方式都要检查使用方式和档位的支持范围。
        if (!capability.Modes.Contains(Mode))
            return "此任务不支持当前树脂使用方式。";
        if (!capability.Tiers.Contains(OriginalTier))
            return "此任务不支持当前原粹树脂档位。";

        if (Mode == ResinUsageMode.PerType)
        {
            // 分别指定只读取次数，不校验保留下来的开关和总次数。
            foreach (var kind in ResinKinds.InOrder)
            {
                if (Counts[kind] is < 0 or > 999)
                    return $"{ResinUsageText.DisplayName(kind)}领奖次数须为 0–999。";
                if (!capability.IsSupported(kind) && Counts[kind] != 0)
                    return $"此任务不能使用{ResinUsageText.DisplayName(kind)}，领奖次数须为 0。";
            }
            return ResinKinds.InOrder.Any(kind => Counts[kind] > 0) ? null : ResinUsageText.MissingCount;
        }

        // 耗尽和指定次数只读取开关；只有一种直接使用树脂时锁定开启。
        foreach (var kind in ResinKinds.InOrder)
        {
            if (Allowed[kind] && !capability.IsSupported(kind))
                return $"此任务不能使用{ResinUsageText.DisplayName(kind)}。";
        }
        if (capability.LockedKind is { } locked && !Allowed[locked])
            return $"此任务必须开启{ResinUsageText.DisplayName(locked)}。";
        if (!ResinKinds.InOrder.Any(kind => Allowed[kind]))
            return ResinUsageText.MissingAllowed;
        if (Mode == ResinUsageMode.RunCount && RunCount == 0)
            return ResinUsageText.MissingCount;
        if (Mode == ResinUsageMode.RunCount && RunCount is < 1 or > 999)
            return "领奖次数须为 1–999。";
        return null;
    }
}
