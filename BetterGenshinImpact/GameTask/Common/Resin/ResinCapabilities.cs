using System;
using System.Collections.Frozen;

namespace BetterGenshinImpact.GameTask.Common.Resin;

/// <summary>四类领奖任务的树脂能力声明，后续任务类可直接引用。</summary>
public static class ResinCapabilities
{
    /// <summary>秘境：三种方式、四类直接使用、三种原粹档位，默认刷至耗尽。</summary>
    public static ResinUsageCapability Domain { get; } = CreateRewardPromptCapability(
        new ResinUsage(), "默认使用领奖界面当前档位");

    /// <summary>幽境危战：支持范围及默认设置与秘境一致。</summary>
    public static ResinUsageCapability StygianOnslaught { get; } = CreateRewardPromptCapability(
        new ResinUsage(), "默认使用领奖界面当前档位");

    /// <summary>地脉花：三种方式、四类直接使用、三种档位，默认领奖 6 次。</summary>
    public static ResinUsageCapability LeyLineOutcrop { get; } = CreateRewardPromptCapability(
        new ResinUsage { Mode = ResinUsageMode.RunCount, RunCount = 6 },
        "双倍活动时切到 40，其余使用界面当前档位");

    /// <summary>首领：原粹直接领奖、须臾及脆弱战前补充，默认只用原粹刷至耗尽。</summary>
    public static ResinUsageCapability Boss { get; } = new()
    {
        Modes = new[] { ResinUsageMode.Exhaust, ResinUsageMode.RunCount }.ToFrozenSet(),
        Kinds = new()
        {
            Condensed = ResinKindSupport.Unsupported,
            Original = ResinKindSupport.Direct,
            Transient = ResinKindSupport.Replenish,
            Fragile = ResinKindSupport.Replenish
        },
        Tiers = Array.AsReadOnly(new[] { OriginalResinTier.Default }),
        Defaults = new ResinUsage { Allowed = new() { Original = true } },
        Hints = new()
        {
            UnsupportedReasons = new() { Condensed = "首领讨伐暂不支持浓缩树脂" },
            PerTypeUnsupportedReason = "首领讨伐只按领奖次数控制",
            DefaultTierDescription = "首领固定使用 40 原粹树脂"
        }
    };

    /// <summary>创建领奖弹窗类任务的独立能力对象，避免不同任务共享可变的种类声明。</summary>
    /// <param name="defaults">任务默认设置。</param>
    /// <param name="defaultTierDescription">默认档位含义。</param>
    /// <returns>支持三种方式、三种档位、四类直接使用树脂的能力。</returns>
    private static ResinUsageCapability CreateRewardPromptCapability(ResinUsage defaults, string defaultTierDescription) => new()
    {
        Modes = new[] { ResinUsageMode.Exhaust, ResinUsageMode.RunCount, ResinUsageMode.PerType }.ToFrozenSet(),
        Kinds = new()
        {
            Condensed = ResinKindSupport.Direct,
            Original = ResinKindSupport.Direct,
            Transient = ResinKindSupport.Direct,
            Fragile = ResinKindSupport.Direct
        },
        Tiers = Array.AsReadOnly(new[] { OriginalResinTier.Default, OriginalResinTier.Prefer40, OriginalResinTier.Only20 }),
        Defaults = defaults,
        Hints = new() { DefaultTierDescription = defaultTierDescription }
    };
}
