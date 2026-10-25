using System;
using System.Collections.Generic;
using System.Linq;

namespace BetterGenshinImpact.GameTask.Common.Resin;

/// <summary>按设置给出领奖候选并累计进度；纯计算，不识图、不操作游戏。</summary>
public sealed class ResinClaimSelector
{
    /// <summary>创建时深拷贝的本次设置。</summary>
    private readonly ResinUsage _usage;

    /// <summary>创建时复制的各类支持方式。</summary>
    private readonly ResinKindValues<ResinKindSupport> _supports;

    /// <summary>各类树脂的成功领奖次数。</summary>
    private readonly ResinKindValues<int> _claims = new();

    /// <summary>包括战前补充在内的各类消耗量。</summary>
    private readonly ResinKindValues<int> _consumed = new();

    /// <summary>按本次设置和任务能力创建选择器，并复制设置，隔离外部后续修改。</summary>
    /// <param name="usage">本次设置，须通过任务能力校验。</param>
    /// <param name="capability">任务能力声明。</param>
    public ResinClaimSelector(ResinUsage usage, ResinUsageCapability capability)
    {
        if (usage.Validate(capability) is { } problem)
            throw new ArgumentException(problem, nameof(usage));
        _usage = usage.Clone();
        _supports = capability.Kinds.Clone();
    }

    /// <summary>已成功领奖的总次数。</summary>
    public int ClaimCount { get; private set; }

    /// <summary>各类剩余领奖次数的独立快照；非分别指定方式没有各类目标，返回全 0。</summary>
    public ResinKindValues<int> RemainingCounts
    {
        get
        {
            var remaining = new ResinKindValues<int>();
            if (_usage.Mode == ResinUsageMode.PerType)
            {
                foreach (var kind in ResinKinds.InOrder)
                    remaining[kind] = Math.Max(0, _usage.Counts[kind] - _claims[kind]);
            }
            return remaining;
        }
    }

    /// <summary>是否已达到目标；刷至耗尽时恒为 false。</summary>
    public bool IsTargetReached => _usage.Mode switch
    {
        ResinUsageMode.RunCount => ClaimCount >= _usage.RunCount,
        ResinUsageMode.PerType => ResinKinds.InOrder.All(kind => _claims[kind] >= _usage.Counts[kind]),
        _ => false
    };

    /// <summary>返回界面提供、能力为直接使用且本次允许的候选，按固定顺序排列；达标后为空。</summary>
    /// <param name="offered">任务识别到的界面可用树脂。</param>
    /// <returns>按顺序尝试的候选树脂。</returns>
    public IReadOnlyList<ResinKind> Candidates(IReadOnlySet<ResinKind> offered)
        => IsTargetReached ? Array.Empty<ResinKind>() : ResinKinds.InOrder
            .Where(kind => offered.Contains(kind) && _supports[kind] == ResinKindSupport.Direct
                && (_usage.Mode == ResinUsageMode.PerType
                    ? _claims[kind] < _usage.Counts[kind]
                    : _usage.Allowed[kind]))
            .ToArray();

    /// <summary>按顺序返回能力为战前补充且本次允许的种类；分别指定时按该类目标大于 0 判断。</summary>
    public IReadOnlyList<ResinKind> ReplenishKinds => ResinKinds.InOrder
        .Where(kind => _supports[kind] == ResinKindSupport.Replenish
            && (_usage.Mode == ResinUsageMode.PerType ? _usage.Counts[kind] > 0 : _usage.Allowed[kind]))
        .ToArray();

    /// <summary>记录成功领奖；原粹计入实际点数，其余种类每次消耗一个。</summary>
    /// <param name="kind">用于领奖的树脂。</param>
    /// <param name="originalSpent">本次实际消耗原粹点数；非原粹传 0。</param>
    public void RecordClaim(ResinKind kind, int originalSpent)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(originalSpent);
        // 只有直接使用的树脂参与领奖；补充消耗由 RecordConsumed 单独记录。
        if (_supports[kind] != ResinKindSupport.Direct)
            throw new ArgumentException("此类树脂不能直接用于领奖。", nameof(kind));
        _claims[kind]++;
        ClaimCount++;
        _consumed[kind] += kind == ResinKind.Original ? originalSpent : 1;
    }

    /// <summary>记录不属于领奖的消耗，例如首领战前补充；不增加领奖次数。</summary>
    /// <param name="kind">消耗的种类。</param>
    /// <param name="amount">消耗量，原粹为点数，其余为个数。</param>
    public void RecordConsumed(ResinKind kind, int amount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amount);
        _consumed[kind] += amount;
    }

    /// <summary>生成独立报告快照；继续记录或修改报告不会相互影响。</summary>
    /// <param name="reason">任务判断的结束原因。</param>
    /// <returns>本次运行的树脂报告。</returns>
    public ResinUsageReport BuildReport(ResinStopReason reason) => new()
    {
        Mode = _usage.Mode,
        ClaimCount = ClaimCount,
        TargetCount = _usage.Mode switch
        {
            ResinUsageMode.RunCount => _usage.RunCount,
            ResinUsageMode.PerType => ResinKinds.InOrder.Sum(kind => _usage.Counts[kind]),
            _ => null
        },
        TargetCounts = _usage.Mode == ResinUsageMode.PerType ? _usage.Counts.Clone() : null,
        Claims = _claims.Clone(),
        Consumed = _consumed.Clone(),
        StopReason = reason
    };
}
