namespace BetterGenshinImpact.GameTask.Common.Resin;

/// <summary>一次运行的树脂使用报告，不包含任务结果种类或游戏操作。</summary>
public sealed class ResinUsageReport
{
    /// <summary>本次使用方式。</summary>
    public ResinUsageMode Mode { get; set; }

    /// <summary>成功领奖次数。</summary>
    public int ClaimCount { get; set; }

    /// <summary>目标次数：指定次数为总目标，分别指定为各类之和，耗尽为 null。</summary>
    public int? TargetCount { get; set; }

    /// <summary>分别指定时各类目标的快照，供输出“原粹 3/4”等文案；其他方式为 null。</summary>
    public ResinKindValues<int>? TargetCounts { get; set; }

    /// <summary>各类树脂用于领奖的次数；首领只有原粹。</summary>
    public ResinKindValues<int> Claims { get; set; } = new();

    /// <summary>消耗量：原粹为点数，其余为个数；战前补充只计入这里，不计入领奖次数。</summary>
    public ResinKindValues<int> Consumed { get; set; } = new();

    /// <summary>结束原因。</summary>
    public ResinStopReason StopReason { get; set; }
}
