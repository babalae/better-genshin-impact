using System;
using System.Collections.Generic;
using System.Linq;

namespace BetterGenshinImpact.GameTask.Common.Resin;

/// <summary>树脂设置摘要与执行报告的统一纯文本格式化，不依赖界面。</summary>
public static class ResinUsageText
{
    /// <summary>开始时树脂不足一次领奖的跳过文案。</summary>
    public const string Skipped = "树脂不足，未开始领奖。";

    /// <summary>尚未填写领奖次数的统一提示。</summary>
    public const string MissingCount = "尚未填写次数";

    /// <summary>尚未选择可用树脂的统一提示。</summary>
    public const string MissingAllowed = "尚未选择可用树脂";

    /// <summary>领奖次数行的说明。</summary>
    public const string RunCountDescription = "只统计成功领取奖励的次数";

    /// <summary>格式化合成任务的保留设置；旧的非正数均表示默认合成。</summary>
    public static string CraftSummary(int minResinToKeep)
        => minResinToKeep <= 0 ? "按默认数量合成" : $"合成后保留 {minResinToKeep} 原粹树脂";

    /// <summary>使用方式的说明，不支持的方式优先显示能力给出的原因。</summary>
    public static string ModeDescription(ResinUsageMode mode, ResinUsageCapability capability)
    {
        if (!capability.Modes.Contains(mode))
            return mode == ResinUsageMode.PerType
                ? capability.Hints.PerTypeUnsupportedReason ?? "此任务不支持按树脂分别指定"
                : "此任务不支持此使用方式";
        return mode switch
        {
            ResinUsageMode.Exhaust => "按顺序使用允许的树脂，不足一次领奖时结束",
            ResinUsageMode.RunCount => "成功领奖达到次数后结束，树脂不足时提前结束",
            _ => "每种树脂各领奖指定次数"
        };
    }

    /// <summary>树脂分组标题。</summary>
    public static string KindsHeader(ResinUsageMode mode)
        => mode == ResinUsageMode.PerType ? "各树脂领奖次数" : "可用树脂";

    /// <summary>树脂分组的固定使用顺序及零值说明。</summary>
    public static string KindsDescription(ResinUsageMode mode)
        => mode == ResinUsageMode.PerType ? "0 表示不使用此类树脂；某类不足时使用下一类" : "按从上到下的顺序使用";

    /// <summary>树脂行说明，区分直接领奖、战前补充、分别指定和任务不支持。</summary>
    public static string KindDescription(ResinKind kind, ResinUsageMode mode, ResinUsageCapability capability)
    {
        if (!capability.IsSupported(kind))
            return capability.Hints.UnsupportedReasons[kind] ?? $"此任务不能使用{DisplayName(kind)}";
        if (mode == ResinUsageMode.PerType)
            return kind switch
            {
                ResinKind.Condensed => "最先使用",
                ResinKind.Original => "浓缩完成后使用",
                ResinKind.Transient => "原粹完成后使用",
                _ => "须臾完成后使用"
            };
        if (capability.Kinds[kind] == ResinKindSupport.Replenish)
            return kind == ResinKind.Transient ? "原粹不足 40 时，战前补充为原粹树脂" : "须臾不可用时，战前补充为原粹树脂";
        if (capability.LockedKind == ResinKind.Original && kind == ResinKind.Original)
            return "首领奖励固定消耗 40 原粹树脂";
        var description = kind switch
        {
            ResinKind.Condensed => "最先使用",
            ResinKind.Original => "浓缩不足时使用",
            ResinKind.Transient => "默认关闭，开启后浓缩和原粹不足时使用",
            _ => "默认关闭，开启后须臾不可用时使用"
        };
        return mode == ResinUsageMode.Exhaust && kind is ResinKind.Transient or ResinKind.Fragile
            ? description + "，直到用完" : description;
    }

    /// <summary>刷至耗尽时开启单类补充树脂的确认正文。</summary>
    public static string EnableSupplementMessage(ResinKind kind, ResinKindSupport support)
        => support == ResinKindSupport.Replenish
            ? $"刷至树脂耗尽时，原粹不足 40 会持续用{DisplayName(kind)}补充，直到用完。\n确定开启吗？"
            : $"刷至树脂耗尽时，浓缩和原粹不足后会持续使用{DisplayName(kind)}，直到用完。\n确定开启吗？";

    /// <summary>已开启补充树脂时切换到耗尽方式的确认正文。</summary>
    public static string SwitchToExhaustMessage(IEnumerable<ResinKind> kinds)
        => $"已开启{string.Join("、", kinds.Select(DisplayName))}。刷至树脂耗尽会持续使用它们，直到用完。\n是否继续使用？";

    /// <summary>按传入任务能力校验并格式化设置摘要；无效时返回对应提示。</summary>
    /// <param name="usage">树脂设置。</param>
    /// <param name="capability">当前任务能力，用于判断设置是否有效。</param>
    /// <returns>统一摘要或无效提示。</returns>
    public static string Summary(ResinUsage usage, ResinUsageCapability capability)
    {
        if (Problem(usage, capability) is { } problem)
            return problem;

        var kinds = ResinKinds.InOrder.Where(kind => usage.Mode == ResinUsageMode.PerType
            ? usage.Counts[kind] > 0 : usage.Allowed[kind]);
        var sequence = string.Join(" → ", kinds.Select(kind => usage.Mode == ResinUsageMode.PerType
            ? $"{ShortName(kind)} {usage.Counts[kind]} 次" : ShortName(kind)));
        var summary = usage.Mode switch
        {
            ResinUsageMode.Exhaust => $"刷至树脂耗尽 · {sequence}",
            ResinUsageMode.RunCount => $"领奖 {usage.RunCount} 次 · {sequence}",
            _ => sequence
        };
        return usage.OriginalTier == OriginalResinTier.Default
            ? summary : $"{summary} · 原粹{TierDisplayName(usage.OriginalTier)}";
    }

    /// <summary>转发能力校验的无效原因，所有提示由 Validate 统一产生。</summary>
    /// <param name="usage">树脂设置。</param>
    /// <param name="capability">任务能力。</param>
    /// <returns>无效原因；有效时为 null。</returns>
    public static string? Problem(ResinUsage usage, ResinUsageCapability capability)
        => usage.Validate(capability);

    /// <summary>格式化执行报告，包含进度、实际消耗及结束原因，末尾带句号。</summary>
    /// <param name="report">树脂使用报告。</param>
    /// <returns>可接在任务结束语之后的报告文本。</returns>
    public static string Report(ResinUsageReport report)
    {
        string progress;
        if (report.Mode == ResinUsageMode.PerType && report.TargetCounts is { } targets)
        {
            // 分别指定展示各类领奖进度，原粹分子也是领奖次数，不是消耗点数。
            progress = string.Join("、", ResinKinds.InOrder.Where(kind => targets[kind] > 0)
                .Select(kind => $"{ShortName(kind)} {report.Claims[kind]}/{targets[kind]}"));
        }
        else
        {
            progress = report.TargetCount is { } target
                ? $"领奖 {report.ClaimCount}/{target} 次" : $"领奖 {report.ClaimCount} 次";
            var consumption = FormatConsumption(report);
            if (consumption.Length > 0)
                progress += $"（{consumption}）";
        }

        return progress + (report.StopReason switch
        {
            ResinStopReason.TargetReached => "。",
            ResinStopReason.ResinExhausted when report.Mode == ResinUsageMode.Exhaust => "，树脂已用完。",
            ResinStopReason.ResinExhausted => "，树脂不足，提前结束。",
            ResinStopReason.NoUsableOption => "，未找到可用的树脂选项，提前结束。",
            _ => throw new ArgumentOutOfRangeException(nameof(report))
        });
    }

    /// <summary>格式化消耗；须臾、脆弱仅有消耗而无领奖记录时单列为补充。</summary>
    /// <param name="report">树脂报告。</param>
    /// <returns>普通消耗以顿号连接，补充部分以逗号分隔。</returns>
    private static string FormatConsumption(ResinUsageReport report)
    {
        var direct = new List<string>();
        var replenished = new List<string>();
        foreach (var kind in ResinKinds.InOrder)
        {
            if (report.Consumed[kind] <= 0)
                continue;
            if (kind is ResinKind.Transient or ResinKind.Fragile && report.Claims[kind] == 0)
                replenished.Add($"补充{ShortName(kind)} {report.Consumed[kind]} 个");
            else
                direct.Add($"{ShortName(kind)} {report.Consumed[kind]}");
        }
        var parts = new List<string>();
        if (direct.Count > 0)
            parts.Add(string.Join("、", direct));
        parts.AddRange(replenished);
        return string.Join("，", parts);
    }

    /// <summary>获取树脂全称。</summary>
    /// <param name="kind">树脂种类。</param>
    /// <returns>统一全称。</returns>
    public static string DisplayName(ResinKind kind) => ShortName(kind) + "树脂";

    /// <summary>获取摘要、报告使用的树脂简称。</summary>
    /// <param name="kind">树脂种类。</param>
    /// <returns>统一简称。</returns>
    public static string ShortName(ResinKind kind) => kind switch
    {
        ResinKind.Condensed => "浓缩",
        ResinKind.Original => "原粹",
        ResinKind.Transient => "须臾",
        ResinKind.Fragile => "脆弱",
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    /// <summary>获取原粹档位的展示名称。</summary>
    /// <param name="tier">原粹档位。</param>
    /// <returns>统一档位名。</returns>
    public static string TierDisplayName(OriginalResinTier tier) => tier switch
    {
        OriginalResinTier.Default => "默认",
        OriginalResinTier.Prefer40 => "优先 40",
        OriginalResinTier.Only20 => "只用 20",
        _ => throw new ArgumentOutOfRangeException(nameof(tier))
    };

    /// <summary>获取使用方式的展示名称。</summary>
    /// <param name="mode">使用方式。</param>
    /// <returns>统一方式名。</returns>
    public static string ModeDisplayName(ResinUsageMode mode) => mode switch
    {
        ResinUsageMode.Exhaust => "刷至树脂耗尽",
        ResinUsageMode.RunCount => "指定领奖次数",
        ResinUsageMode.PerType => "按树脂分别指定",
        _ => throw new ArgumentOutOfRangeException(nameof(mode))
    };
}
